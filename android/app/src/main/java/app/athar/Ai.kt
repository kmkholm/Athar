// Optional AI analysis through the Claude API, using the official Anthropic Java SDK (anthropic-java).
// The user types the key in Settings; it is kept encrypted with a key held in the Android Keystore.
package app.athar

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import com.anthropic.client.AnthropicClient
import com.anthropic.client.okhttp.AnthropicOkHttpClient
import com.anthropic.core.JsonValue
import com.anthropic.errors.AnthropicServiceException
import com.anthropic.errors.BadRequestException
import com.anthropic.errors.RateLimitException
import com.anthropic.errors.UnauthorizedException
import com.anthropic.models.messages.Message
import com.anthropic.models.messages.MessageCreateParams
import com.anthropic.models.messages.OutputConfig
import com.anthropic.models.messages.StopReason
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

object SecureKey {
    private const val ALIAS = "athar_claude_key_v1"
    private const val PREFS = "athar_secure"

    private fun secret(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getEntry(ALIAS, null) as? KeyStore.SecretKeyEntry)?.let { return it.secretKey }
        val gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        gen.init(KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).setKeySize(256).build())
        return gen.generateKey()
    }
    private fun b64(b: ByteArray) = Base64.encodeToString(b, Base64.NO_WRAP)

    fun save(ctx: Context, key: String) {
        val p = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
        if (key.isBlank()) { p.remove("k").apply(); return }
        val c = Cipher.getInstance("AES/GCM/NoPadding"); c.init(Cipher.ENCRYPT_MODE, secret())
        p.putString("k", b64(c.iv) + ":" + b64(c.doFinal(key.trim().toByteArray()))).apply()
    }

    fun load(ctx: Context): String {
        val blob = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString("k", null) ?: return ""
        return runCatching {
            val (iv, ct) = blob.split(":").let { Base64.decode(it[0], Base64.NO_WRAP) to Base64.decode(it[1], Base64.NO_WRAP) }
            val c = Cipher.getInstance("AES/GCM/NoPadding"); c.init(Cipher.DECRYPT_MODE, secret(), GCMParameterSpec(128, iv))
            String(c.doFinal(ct))
        }.getOrDefault("")
    }
}

object Ai {
    val models = listOf("claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5")
    private var cachedKey = ""
    private var client: AnthropicClient? = null

    private fun client(key: String): AnthropicClient {
        if (client == null || key != cachedKey) { client = AnthropicOkHttpClient.builder().apiKey(key).maxRetries(2).build(); cachedKey = key }
        return client!!
    }

    fun system(arabic: Boolean): String =
        "You assist a blockchain investigator who uses a tracing tool. You receive public on-chain data that the tool collected: address labels, transfers, heuristic findings and fund-flow traces.\n" +
            "Explain what the data shows and what to check next. Rules:\n" +
            "- Rely only on the data given. Separate what the data shows from what is a hypothesis, and say how strong each point is.\n" +
            "- An address is not a person. Never name or guess who owns an unlabelled address, and never state that someone committed a crime; describe behaviour and patterns instead.\n" +
            "- Labels come from explorers and may be wrong; say so when a conclusion depends on one.\n" +
            "- Quote addresses and transaction hashes exactly as given, in their shortened form when that is how they appear.\n" +
            "- If the data is partial (truncated history, limited hops), say what that leaves unknown.\n" +
            "- Give concrete next steps the investigator can do in the tool (trace forward or backward from an address, link addresses, match deposits and payouts of a bridge or mixer, watch an address, contact the exchange that received funds).\n" +
            "Write in " + (if (arabic) "Arabic (Modern Standard, clear and plain)" else "English") + ". Plain text only: short headed sections and short lines starting with '- '. No Markdown tables, no bold markers, no code fences. Keep it under 350 words unless the data clearly needs more."

    suspend fun ask(key: String, model0: String, user: String, arabic: Boolean): String = withContext(Dispatchers.IO) {
        if (key.isBlank()) throw ApiException(t("Add your Claude API key in Settings to use AI analysis."))
        val model = if (model0 in models) model0 else models[0]
        fun build(fallback: Boolean): MessageCreateParams {
            val b = MessageCreateParams.builder().model(model).maxTokens(16000L).system(system(arabic))
                .outputConfig(OutputConfig.builder().effort(OutputConfig.Effort.MEDIUM).build()).addUserMessage(user)
            if (fallback) {
                // Server-side refusal fallback (beta): a declined request is re-served by a suitable model inside the same call.
                b.putAdditionalHeader("anthropic-beta", "server-side-fallback-2026-07-01")
                b.putAdditionalBodyProperty("fallbacks", JsonValue.from("default"))
            }
            return b.build()
        }
        val c = client(key); val fb = model != "claude-haiku-5-5"
        val msg: Message = try {
            try { c.messages().create(build(fb)) }
            catch (e: BadRequestException) { if (fb && (e.message ?: "").contains("fallback", true)) c.messages().create(build(false)) else throw e }
        } catch (e: UnauthorizedException) { throw ApiException(t("The Claude API key was rejected. Check it in Settings."))
        } catch (e: RateLimitException) { throw ApiException(t("Claude API rate limit reached. Try again in a minute."))
        } catch (e: AnthropicServiceException) { throw ApiException("Claude API (" + e.statusCode() + "): " + (e.message ?: ""))
        } catch (e: ApiException) { throw e
        } catch (e: kotlinx.coroutines.CancellationException) { throw e
        } catch (e: Exception) { throw ApiException("Claude API: " + (e.message ?: e.javaClass.simpleName)) }
        if (msg.stopReason().orElse(null) == StopReason.REFUSAL) throw ApiException(t("The model declined to answer this request."))
        val text = msg.content().mapNotNull { it.text().orElse(null)?.text() }.joinToString("").trim()
        if (text.isBlank()) throw ApiException(t("The model returned an empty answer."))
        text
    }

    private fun lab(a: String): String { val l = Labels.find(a); return if (l != null && l.name.isNotEmpty()) "${Fmt.short(a)} [${l.name}; ${l.cat}]" else Fmt.short(a) }

    fun addressPrompt(chain: Chain, addr: String, info: AddrInfo, full: TransferSet, firstFunding: Xfer? = null): String {
        val cap = 160; val sb = StringBuilder()
        val set = TransferSet(Tokens.clean(full.items), full.truncated, full.note); val spam = full.items.size - set.items.size
        sb.appendLine("TASK: Profile this address. Summarise its behaviour, list what looks unusual and why, and propose next steps.")
        sb.appendLine("NETWORK: ${chain.name}")
        sb.appendLine("ADDRESS: $addr  (shown below as ${Fmt.short(addr)})")
        val own = Labels.find(addr)
        sb.appendLine("LABEL: ${if (own != null) own.name + " (" + own.cat + ", source: " + own.source + ")" else "none"}; contract: ${info.isContract}; explorer scam flag: ${info.isScam}; tags: ${info.tags.joinToString(", ")}")
        sb.appendLine("BALANCE: ${Fmt.amt(info.balance)} ${chain.symbol}; total transactions reported by explorer: ${if (info.txCount >= 0) info.txCount.toString() else "unknown"}")
        sb.appendLine("TRANSFERS LOADED: ${set.items.size}${if (set.truncated) " (history is truncated: only the most recent transfers per type were loaded)" else " (complete for the loaded window)"}")
        sb.appendLine("\nTOOL FINDINGS (heuristics, to be verified):")
        for (f in Analysis.findings(addr, info, full, firstFunding)) sb.appendLine("- [${f.level}] ${f.title}: ${f.detail}")
        if (spam > 0) sb.appendLine("NOTE: $spam transfers of spam or look-alike tokens were removed from everything below.")
        sb.appendLine("\nTOP COUNTERPARTIES (counterparty | asset | received from it | sent to it | transfers | first..last UTC):")
        for (c in Analysis.counterparties(addr, set.items).take(25)) sb.appendLine("- ${lab(c.address)} | ${c.asset} | ${Fmt.amt(c.inn)} | ${Fmt.amt(c.out)} | ${c.count} | ${Fmt.day(c.first)}..${Fmt.day(c.last)}")
        sb.appendLine("\nOUTGOING TRANSACTIONS BY UTC HOUR (00..23): " + Analysis.hours(addr, set.items).joinToString(" "))
        val shown = set.items.sortedByDescending { it.time }.take(cap)
        sb.appendLine("\nTRANSFERS, newest first (${shown.size} of ${set.items.size} loaded${if (set.items.size > cap) "; the rest are omitted from this message" else ""}) (time UTC | direction | counterparty | amount asset | type):")
        for (x in shown) sb.appendLine("- ${Fmt.time(x.time)} | ${if (x.from == addr) "OUT" else "IN"} | ${lab(if (x.from == addr) x.to else x.from)} | ${Fmt.amt(x.amount)} ${x.asset} | ${x.kind}")
        return sb.toString()
    }

    fun tracePrompt(g: Graph): String {
        val sb = StringBuilder()
        sb.appendLine("TASK: Explain this fund-flow trace: the main path of the money, where it ended (exchanges, bridges, mixers, unexplored wallets), which endpoints matter most, how reliable each branch is, and the next steps.")
        sb.appendLine("NETWORK: ${Chains.get(g.chain).name}; DIRECTION: ${if (g.forward) "forward (where the money went)" else "backward (where the money came from)"}; START: ${g.nodes.filter { it.depth == 0 }.joinToString(", ") { lab(it.id) }}; traced ${Fmt.time(g.created)} UTC")
        sb.appendLine("PARAMETERS: ${g.options}")
        sb.appendLine("METHOD: at each hop only the largest transfers made after the funds arrived are followed; the trace stops at known services, contracts and very active wallets. Later hops may include funds unrelated to the start.")
        sb.appendLine("\nADDRESSES (hop | address [label; category] | why the trace stopped there):")
        for (n in g.nodes.sortedBy { it.depth }) sb.appendLine("- ${n.depth} | ${lab(n.id)} | status: ${g.status(n)}${if (n.frozen.isNotEmpty()) " (blacklisted by " + n.frozen + ", frozen balance " + money(n.frozenUsd) + ")" else ""}; stop reason: ${n.stop.ifEmpty { if (n.expanded) "expanded" else "not expanded" }}; balance now: ${Fmt.amt(n.balance)}; value reached: ${money(g.usdIn(n))}")
        sb.appendLine("\nFLOWS (from -> to | amount asset | transfers | first..last UTC):")
        for (e in g.edges.sortedWith(compareBy<GEdge> { g.node(it.from)?.depth ?: 0 }.thenByDescending { it.amount })) sb.appendLine("- ${Fmt.short(e.from)} -> ${Fmt.short(e.to)} | ${Fmt.amt(e.amount)} ${e.asset}${if (e.bridge) " (payout of a bridge / cross-chain swap on " + Bridges.nameOf(e.chainKey) + ")" else ""} | ${e.count} | ${Fmt.time(e.first)}..${Fmt.time(e.last)}")
        if (g.notes.isNotEmpty()) sb.appendLine("\nTOOL NOTES: " + g.notes.filter { it != "nolabel" }.distinct().joinToString(" | ") { if (it == "limit") "address limit reached, some branches not followed" else if (it == "recent") "start address is very active and no start date was given: only its most recent transfers were used" else it })
        return sb.toString()
    }

    fun casePrompt(c: CaseFile): String {
        val sb = StringBuilder()
        sb.appendLine("TASK: Draft the summary section of an investigation report for this case: what happened, the money trail, what is established and what is still open, and recommended next steps. Use only the material below.")
        sb.appendLine("CASE: ${c.name}; opened ${Fmt.day(c.created)}")
        if (c.notes.isNotBlank()) sb.appendLine("\nINVESTIGATOR'S NOTES:\n" + c.notes)
        if (c.findings.isNotEmpty()) { sb.appendLine("\nINVESTIGATOR'S FINDINGS:"); c.findings.forEach { sb.appendLine("- $it") } }
        if (c.items.isNotEmpty()) { sb.appendLine("\nITEMS (kind | network | value | role | note):"); c.items.forEach { sb.appendLine("- ${it.kind} | ${it.chain} | ${if (it.kind == "address") lab(it.value) else Fmt.short(it.value)} | ${it.role} | ${it.note}") } }
        c.traces.forEachIndexed { i, g -> sb.appendLine("\n===== TRACE ${i + 1} ====="); sb.appendLine(tracePrompt(g)) }
        return sb.toString()
    }
}
