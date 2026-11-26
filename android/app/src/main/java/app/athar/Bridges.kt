package app.athar

import org.json.JSONObject
import java.math.BigDecimal

/** The other side of a cross-chain transaction: where the value came out (or, for a payout, where it came from). */
class BridgeHop(val service: String, val chain: String, val address: String, val asset: String, val amount: BigDecimal, val tx: String, val time: Long, val outbound: Boolean)

/** Resolves bridge and cross-chain swap transactions through the services' own public lookups (THORChain Midgard, Relay, LI.FI). */
object Bridges {
    private const val MIDGARD = "https://gateway.liquify.com/chain/thorchain_midgard/v2/actions"
    private fun chainFromId(id: Long) = when (id) { 1L -> "eth"; 10L -> "op"; 56L -> "bsc"; 137L -> "polygon"; 324L -> "zksync"; 5000L -> "mantle"; 8453L -> "base"; 42161L -> "arb"; 43114L -> "avax"; 534352L -> "scroll"; 130L -> "unichain"; 42220L -> "celo"; 728126428L -> "tron"; 8253038L -> "btc"; 792703809L, 1151111081099710L -> "solana"; else -> "chain-$id" }
    private fun chainFromThor(asset: String) = when (val c = asset.split('.', '~', '/', '-')[0].uppercase()) { "BTC" -> "btc"; "ETH" -> "eth"; "TRON" -> "tron"; "BSC" -> "bsc"; "AVAX" -> "avax"; "BASE" -> "base"; "XRP" -> "xrp"; "DOGE" -> "doge"; "LTC" -> "ltc"; "BCH" -> "bch"; "GAIA" -> "cosmos"; "THOR" -> "thorchain"; "SOL" -> "solana"; else -> c.lowercase() }
    fun nameOf(key: String): String = Chains.all.firstOrNull { it.key == key }?.name ?: when (key) { "solana" -> "Solana"; "xrp" -> "XRP Ledger"; "doge" -> "Dogecoin"; "ltc" -> "Litecoin"; "bch" -> "Bitcoin Cash"; "thorchain" -> "THORChain"; "cosmos" -> "Cosmos"; else -> key }

    suspend fun resolve(chainKey: String, hash: String): List<BridgeHop> {
        for (f in listOf<suspend (String, String) -> List<BridgeHop>>(::thor, ::relay, ::lifi)) {
            try { val r = f(chainKey, hash); if (r.isNotEmpty()) return r } catch (_: ApiException) { } catch (_: NumberFormatException) { } catch (_: org.json.JSONException) { }
        }
        return emptyList()
    }

    private fun sides(a: JSONObject, isIn: Boolean): List<BridgeHop> {
        val time = (a.s("date").toBigDecimalOrNull() ?: BigDecimal.ZERO).divideToIntegralValue(BigDecimal(1_000_000_000)).toLong()
        return a.list(if (isIn) "out" else "in").mapNotNull { side ->
            val coin = side.list("coins").firstOrNull() ?: return@mapNotNull null
            val asset = coin.s("asset")
            if (asset.startsWith("THOR.") && isIn) return@mapNotNull null   // affiliate / fee legs paid in RUNE
            val ck = chainFromThor(asset); val tx = side.s("txID")
            BridgeHop("THORChain", ck, side.s("address"), asset.split('.', '~').last().split('-')[0], Units.from(coin.s("amount"), 8), if (ck in setOf("eth", "bsc", "avax", "base")) "0x" + tx.lowercase() else tx.lowercase(), time, isIn)
        }
    }

    /** Every THORChain swap a depositor made (up to 400), so that hundreds of small deposits resolve in a few calls. */
    suspend fun thorAll(depositor: String, fromTime: Long): List<BridgeHop> {
        val res = ArrayList<BridgeHop>(); var token = ""
        for (page in 0 until 8) {
            val j = Http.obj("$MIDGARD?address=$depositor&type=swap&limit=50" + (if (token.isNotEmpty()) "&nextPageToken=$token" else ""), 1800, tries = 2, timeoutSec = 20) ?: break
            val acts = j.list("actions")
            for (a in acts) if (a.list("in").any { it.s("address").equals(depositor, true) }) res += sides(a, true)
            token = j.o("meta")?.s("nextPageToken").orEmpty()
            if (acts.size < 50 || token.isEmpty() || (fromTime > 0 && res.isNotEmpty() && res.last().time < fromTime)) break
        }
        return res.filter { !it.address.equals(depositor, true) && (fromTime <= 0 || it.time >= fromTime) }
    }

    private suspend fun thor(chainKey: String, hash: String): List<BridgeHop> {
        val id = hash.removePrefix("0x")
        val j = Http.obj("$MIDGARD?txid=" + id.uppercase(), 3600, tries = 2, timeoutSec = 15) ?: return emptyList()
        return j.list("actions").flatMap { a -> sides(a, a.list("in").any { it.s("txID").equals(id, true) }) }
    }

    /** Relay's request lookup. Without a key only the older v2 endpoint answers (rate-limited and being retired); with a key from Settings v3 is used. */
    private val relayBase get() = "https://api.relay.link/requests/" + (if (Http.hostHeaders["api.relay.link"]?.second.isNullOrBlank()) "v2" else "v3")

    private fun relayHop(r: JSONObject, isIn: Boolean): BridgeHop? {
        val d = r.o("data") ?: return null
        val other = d.list(if (isIn) "outTxs" else "inTxs").firstOrNull() ?: return null
        val cur = d.o("metadata")?.o(if (isIn) "currencyOut" else "currencyIn")
        return BridgeHop("Relay", chainFromId(other.l("chainId")), if (isIn) r.s("recipient") else r.s("user"), cur?.o("currency")?.s("symbol").orEmpty(), cur?.s("amountFormatted")?.toBigDecimalOrNull() ?: BigDecimal.ZERO, other.s("hash"), other.l("timestamp"), isIn)
    }

    /** Every Relay request of a depositor (up to 300), so that dozens of deposits resolve in a few calls. */
    suspend fun relayAll(user: String, fromTime: Long): List<BridgeHop> {
        val res = ArrayList<BridgeHop>(); var cont = ""
        for (page in 0 until 6) {
            val j = Http.obj("$relayBase?user=$user&limit=50" + (if (cont.isNotEmpty()) "&continuation=" + java.net.URLEncoder.encode(cont, "UTF-8") else ""), 1800, tries = 3, timeoutSec = 20) ?: break
            val reqs = j.list("requests")
            for (r in reqs) if (r.s("user").equals(user, true) && r.s("status") == "success") relayHop(r, true)?.let { if (it.address.isNotEmpty()) res += it }
            cont = j.s("continuation")
            if (reqs.size < 50 || cont.isEmpty()) break
        }
        return res.filter { !(fromTime > 0 && it.time > 0 && it.time < fromTime) }
    }

    private suspend fun relay(chainKey: String, hash: String): List<BridgeHop> {
        val j = Http.obj("$relayBase?hash=$hash", 3600, tries = if (relayBase.endsWith("v2")) 1 else 3, timeoutSec = 15) ?: return emptyList()
        return j.list("requests").mapNotNull { r ->
            val d = r.o("data") ?: return@mapNotNull null
            relayHop(r, d.list("inTxs").any { it.s("hash").equals(hash, true) || ("0x" + it.s("hash")).equals(hash, true) })
        }
    }

    private suspend fun lifi(chainKey: String, hash: String): List<BridgeHop> {
        if (!hash.startsWith("0x")) return emptyList()
        val j = Http.obj("https://li.quest/v1/status?txHash=$hash", 3600, tries = 1, timeoutSec = 15) ?: return emptyList()
        val send = j.o("sending") ?: return emptyList(); val recv = j.o("receiving") ?: return emptyList()
        if (recv.s("txHash").isEmpty()) return emptyList()
        val isIn = send.s("txHash").equals(hash, true)
        val side = if (isIn) recv else send; val tok = side.o("token"); val ck = chainFromId(side.l("chainId"))
        if (isIn && ck == chainKey && recv.s("txHash").equals(hash, true)) return emptyList()   // same-chain swap, not a bridge
        return listOf(BridgeHop("LI.FI" + (if (j.s("tool").isNotEmpty()) " (" + j.s("tool") + ")" else ""), ck, if (isIn) j.s("toAddress") else j.s("fromAddress"), tok?.s("symbol").orEmpty(), Units.from(side.s("amount"), (tok?.l("decimals") ?: 18).toInt()), side.s("txHash"), side.l("timestamp"), isIn))
    }
}
