package app.athar

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import org.json.JSONTokener
import java.io.File
import java.math.BigDecimal
import java.math.BigInteger
import java.math.RoundingMode
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest
import java.text.DecimalFormat
import java.text.DecimalFormatSymbols
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import java.util.concurrent.ConcurrentHashMap

/** One value movement, normalised across chains. */
data class Xfer(
    val chain: String, val hash: String, val from: String, val to: String, val asset: String,
    val amount: BigDecimal, val time: Long, val contract: String = "", val kind: String = "native", val block: Long = 0,
)

class TransferSet(var items: List<Xfer> = emptyList(), var truncated: Boolean = false, var note: String = "")

class AddrInfo(val chain: String, val address: String) {
    var name = ""; val tags = ArrayList<String>(); var isContract = false; var isScam = false
    var balance: BigDecimal = BigDecimal.ZERO; var txCount = -1L; var usd: BigDecimal = BigDecimal.ZERO; var note = ""
}

class ApiException(m: String) : Exception(m)

object Fmt {
    private val sym = DecimalFormatSymbols(Locale.US)
    private fun df(p: String) = DecimalFormat(p, sym).apply { roundingMode = RoundingMode.HALF_UP }
    fun short(a: String?): String {
        if (a.isNullOrEmpty()) return "—"
        val c = a.indexOf(':')
        if (c in 1..11) return a.substring(0, c + 1) + short(a.substring(c + 1))
        return if (a.length <= 16) a else a.take(8) + "…" + a.takeLast(6)
    }
    fun amt(v: BigDecimal): String {
        val a = v.abs()
        return when {
            a.signum() == 0 -> "0"
            a >= BigDecimal(1000) -> df("#,##0.##").format(v)
            a >= BigDecimal.ONE -> df("0.####").format(v)
            a >= BigDecimal("0.0001") -> df("0.######").format(v)
            else -> df("0.########").format(v)
        }
    }
    private fun sdf(p: String) = SimpleDateFormat(p, Locale.US).apply { timeZone = TimeZone.getTimeZone("UTC") }
    fun time(u: Long): String = if (u <= 0) "—" else sdf("yyyy-MM-dd HH:mm:ss").format(Date(u * 1000))
    fun day(u: Long): String = if (u <= 0) "—" else sdf("yyyy-MM-dd").format(Date(u * 1000))
    fun dur(s0: Long): String {
        val s = kotlin.math.abs(s0)
        return when {
            s < 60 -> "${s}s"
            s < 3600 -> "${s / 60}m ${s % 60}s"
            s < 86400 -> "${s / 3600}h ${s % 3600 / 60}m"
            else -> "${s / 86400}d ${s % 86400 / 3600}h"
        }
    }
    fun parseDate(s0: String?): Long {
        val s = s0?.trim().orEmpty()
        if (s.isEmpty()) return 0
        s.toLongOrNull()?.let { if (it > 1_000_000_000) return it }
        val c = s.replace('T', ' ').removeSuffix("Z").substringBefore('.')
        for (p in listOf("yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"))
            try { val f = sdf(p); f.isLenient = false; if (c.length == p.length) return f.parse(c)!!.time / 1000 } catch (_: Exception) { }
        return 0
    }
    fun html(s: String?): String = (s ?: "").replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("\"", "&quot;").replace("'", "&#39;")
}

object Units {
    fun from(raw: String?, dec: Int): BigDecimal {
        if (raw.isNullOrEmpty()) return BigDecimal.ZERO
        return try { BigDecimal(BigInteger(raw), maxOf(dec, 0)).stripTrailingZeros().let { if (it.scale() < 0) it.setScale(0) else it } } catch (_: Exception) { BigDecimal.ZERO }
    }
    fun fromHex(hex: String?, dec: Int): BigDecimal {
        val h = hex.orEmpty().removePrefix("0x")
        if (h.isEmpty()) return BigDecimal.ZERO
        return try { from(BigInteger(h, 16).toString(), dec) } catch (_: Exception) { BigDecimal.ZERO }
    }
    fun hexLong(hex: String?): Long = hex.orEmpty().removePrefix("0x").let { if (it.isEmpty()) 0 else it.toLongOrNull(16) ?: 0 }
    fun sats(v: Long): BigDecimal = from(v.toString(), 8)
}

/** Separates real assets from spam: look-alike symbols, empty symbols, advertising tokens and impersonations of the coin or of major stablecoins. */
object Tokens {
    private val majors = setOf("USDT", "USDC", "DAI", "WETH", "WBTC", "BUSD", "TUSD", "USDD")
    fun suspicious(x: Xfer): Boolean {
        if (x.kind != "token") return false
        val s = x.asset
        if (s.isEmpty() || s == "TOKEN") return true
        if (s.startsWith("token 0x") || s.startsWith("token T")) return false
        if (s.any { it.code > 0x7E || it.code < 0x21 }) return true
        val u = s.uppercase()
        if (listOf("HTTP", "WWW", ".COM", ".IO", ".ORG", ".NET", "CLAIM", "VISIT", "REWARD", "AIRDROP").any { u.contains(it) }) return true
        Chains.all.firstOrNull { it.key == x.chain }?.let { if (u == it.symbol.uppercase()) return true }
        if ((x.chain == "eth" || x.chain == "tron") && Chains.knownTokens.values.any { it.first == u } && !Chains.knownTokens.containsKey(x.contract)) return true
        return false
    }
    fun major(x: Xfer): Boolean = x.kind != "token" || Chains.knownTokens.containsKey(x.contract) || (x.chain != "eth" && x.chain != "tron" && x.asset.uppercase() in majors)
    fun clean(xs: List<Xfer>): List<Xfer> = xs.filter { !suspicious(it) }
}

object Addr {
    private const val B58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz"
    private fun isHex(s: String) = s.all { it in '0'..'9' || it in 'a'..'f' || it in 'A'..'F' }
    private fun isB58(s: String) = s.all { B58.indexOf(it) >= 0 }

    /** kind = address | tx | unknown; family = evm | btc | tron | btc-or-tron */
    fun detect(s0: String?): Pair<String, String> {
        val s = s0?.trim().orEmpty()
        if (s.startsWith("0x") || s.startsWith("0X")) {
            val h = s.substring(2)
            if (h.length == 40 && isHex(h)) return "address" to "evm"
            if (h.length == 64 && isHex(h)) return "tx" to "evm"
            return "unknown" to ""
        }
        if (s.length == 64 && isHex(s)) return "tx" to "btc-or-tron"
        if (s.length == 34 && s[0] == 'T' && isB58(s)) return "address" to "tron"
        if (s.startsWith("bc1", true) && s.length in 14..74) return "address" to "btc"
        if (s.length in 26..35 && (s[0] == '1' || s[0] == '3') && isB58(s)) return "address" to "btc"
        return "unknown" to ""
    }

    fun norm(family: String, a0: String?): String {
        val a = a0?.trim().orEmpty()
        if (family == "evm") return a.lowercase()
        if (family == "btc" && a.startsWith("bc1", true)) return a.lowercase()
        return a
    }

    /** Node ids of another network are written "network:address"; this returns the address part. */
    fun bare(id: String): String { val c = id.indexOf(':'); return if (c in 1..11) id.substring(c + 1) else id }

    /** Tron base58 address to its 20-byte hex body (without the 41 prefix). */
    fun tronToHex(b58: String): String {
        var n = BigInteger.ZERO
        for (ch in b58) { val d = B58.indexOf(ch); if (d < 0) return ""; n = n * BigInteger.valueOf(58) + BigInteger.valueOf(d.toLong()) }
        var bytes = n.toByteArray(); if (bytes.size > 25 && bytes[0].toInt() == 0) bytes = bytes.copyOfRange(1, bytes.size)
        if (bytes.size < 25) return ""
        return bytes.copyOfRange(bytes.size - 24, bytes.size - 4).joinToString("") { "%02x".format(it) }
    }

    fun tronFromHex(hex0: String?): String {
        var hex = hex0.orEmpty().removePrefix("0x")
        if (hex.isEmpty()) return ""
        if (hex.length == 40) hex = "41$hex"
        if (hex.length != 42) return hex
        val raw = ByteArray(21) { hex.substring(it * 2, it * 2 + 2).toInt(16).toByte() }
        val md = MessageDigest.getInstance("SHA-256")
        val chk = md.digest(md.digest(raw))
        val all = raw + chk.copyOf(4)
        var n = BigInteger(1, all)
        val sb = StringBuilder()
        val b58 = BigInteger.valueOf(58)
        while (n.signum() > 0) { val qr = n.divideAndRemainder(b58); sb.insert(0, B58[qr[1].toInt()]); n = qr[0] }
        for (b in all) { if (b.toInt() == 0) sb.insert(0, '1') else break }
        return sb.toString()
    }
}

/** HTTP with a disk cache, per-host pacing and retries. */
object Http {
    var cacheDir: File? = null
    var cacheSec = 900
    var gapMs = 0
    @Volatile var fresh = false
    @Volatile var calls = 0
    val sources = ConcurrentHashMap<String, Long>()
    /** Extra header per host (for services that want an API key), e.g. api.relay.link -> x-api-key. */
    val hostHeaders = ConcurrentHashMap<String, Pair<String, String>>()
    private val gates = ConcurrentHashMap<String, Mutex>()
    private val next = ConcurrentHashMap<String, Long>()

    private fun gap(host: String) = maxOf(gapMs, when { host.contains("routescan") -> 600; host.contains("etherscan") -> 380; host.contains("trongrid") -> 400; host.contains("relay.link") -> 1300; else -> 250 })
    private fun cacheFile(key: String): File? = cacheDir?.let { File(it, MessageDigest.getInstance("SHA-256").digest(key.toByteArray()).joinToString("") { b -> "%02x".format(b) }.take(40) + ".json") }

    suspend fun get(url: String, ttl: Int = cacheSec, post: String? = null, tries: Int = 5, timeoutSec: Int = 25): String = withContext(Dispatchers.IO) {
        val cf = cacheFile(url + "\n" + post)
        if (cf != null && ttl > 0 && !fresh && cf.exists() && System.currentTimeMillis() - cf.lastModified() < ttl * 1000L) return@withContext cf.readText()
        val u = URL(url); val host = u.host
        var last: Exception? = null
        var timeouts = 0
        for (attempt in 0 until tries) {
            gates.getOrPut(host) { Mutex() }.withLock {
                val wait = (next[host] ?: 0) - System.currentTimeMillis()
                if (wait > 0) delay(wait)
                next[host] = System.currentTimeMillis() + gap(host)
            }
            try {
                calls++
                val c = u.openConnection() as HttpURLConnection
                c.connectTimeout = 15000; c.readTimeout = timeoutSec * 1000
                c.setRequestProperty("User-Agent", "Athar/1.0"); c.setRequestProperty("Accept", "application/json")
                hostHeaders[host]?.let { if (it.second.isNotEmpty()) c.setRequestProperty(it.first, it.second) }
                if (post != null) { c.requestMethod = "POST"; c.doOutput = true; c.setRequestProperty("Content-Type", "application/json"); c.outputStream.use { it.write(post.toByteArray()) } }
                val code = c.responseCode
                val body = (if (code >= 400) c.errorStream else c.inputStream)?.bufferedReader()?.use { it.readText() } ?: ""
                c.disconnect()
                if (code == 429 || code >= 500) { last = ApiException("$host: HTTP $code"); delay(1200L * (attempt + 1)); continue }
                if (code == 404) return@withContext ""
                if (code >= 400) throw ApiException("$host: HTTP $code")
                sources[host] = System.currentTimeMillis()
                if (cf != null && body.isNotEmpty() && !body.contains("rate limit", true)) try { cf.writeText(body) } catch (_: Exception) { }
                return@withContext body
            } catch (e: ApiException) { throw e
            } catch (e: kotlinx.coroutines.CancellationException) { throw e
            } catch (e: java.net.SocketTimeoutException) { last = ApiException("$host: no answer within $timeoutSec seconds"); if (++timeouts >= 2) break
            } catch (e: Exception) { last = ApiException("$host: ${e.message ?: e.javaClass.simpleName}"); delay(900L * (attempt + 1)) }
        }
        throw last ?: ApiException("$host: request failed")
    }

    suspend fun any(url: String, ttl: Int = cacheSec, post: String? = null, tries: Int = 5, timeoutSec: Int = 25): Any? {
        val s = get(url, ttl, post, tries, timeoutSec)
        if (s.isBlank()) return null
        return try { JSONTokener(s).nextValue() } catch (_: Exception) { throw ApiException(URL(url).host + ": unexpected (non-JSON) reply") }
    }
    suspend fun obj(url: String, ttl: Int = cacheSec, post: String? = null, tries: Int = 5, timeoutSec: Int = 25): JSONObject? = any(url, ttl, post, tries, timeoutSec) as? JSONObject
    suspend fun arr(url: String, ttl: Int = cacheSec): JSONArray? = any(url, ttl) as? JSONArray
}

fun JSONObject.s(k: String): String = if (!has(k) || isNull(k)) "" else opt(k).let { if (it is JSONObject || it is JSONArray) "" else it.toString() }
fun JSONObject.l(k: String): Long { val v = s(k); return v.toLongOrNull() ?: v.toBigDecimalOrNull()?.toLong() ?: 0 }
fun JSONObject.b(k: String): Boolean = optBoolean(k, false)
fun JSONObject.o(k: String): JSONObject? = optJSONObject(k)
fun JSONObject.list(k: String): List<JSONObject> = optJSONArray(k).objs()
fun JSONArray?.objs(): List<JSONObject> = if (this == null) emptyList() else (0 until length()).mapNotNull { optJSONObject(it) }
fun Iterable<BigDecimal>.total(): BigDecimal = fold(BigDecimal.ZERO) { a, b -> a + b }
