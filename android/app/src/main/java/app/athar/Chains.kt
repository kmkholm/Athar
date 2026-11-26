package app.athar

import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import org.json.JSONArray
import org.json.JSONObject
import java.math.BigDecimal
import java.net.URLEncoder

interface ChainApi {
    suspend fun info(address: String): AddrInfo
    /** asc=true: oldest first from fromTime; otherwise newest first up to toTime. */
    suspend fun transfers(address: String, max: Int, asc: Boolean, fromTime: Long = 0, toTime: Long = 0): TransferSet
    suspend fun tx(hash: String): List<Xfer>
}

class Chain(val key: String, val name: String, val symbol: String, val family: String, val explorer: String, val chainId: Int = 0, val needsKey: Boolean = false, private val make: (Chain) -> ChainApi) {
    private var cached: ChainApi? = null
    val api: ChainApi get() = cached ?: make(this).also { cached = it }
    fun reset() { cached = null }
    val ready: Boolean get() = !needsKey || Chains.etherscanKey.isNotBlank()
    fun addrUrl(a: String) = if (family == "tron") "$explorer/#/address/$a" else "$explorer/address/$a"
    fun txUrl(h: String) = if (family == "tron") "$explorer/#/transaction/$h" else "$explorer/tx/$h"
}

object Chains {
    var etherscanKey = ""
    private fun v2Base(c: Chain) = "https://api.etherscan.io/v2/api?chainid=${c.chainId}&apikey=${URLEncoder.encode(etherscanKey, "UTF-8")}&"
    private val v2: (Chain) -> ChainApi = { c -> EtherscanApi(c, v2Base(c)) }
    val all: List<Chain> = listOf(
        Chain("eth", "Ethereum", "ETH", "evm", "https://etherscan.io", 1) { c -> BlockscoutApi(c, "https://eth.blockscout.com", if (etherscanKey.isBlank()) "https://api.routescan.io/v2/network/mainnet/evm/1/etherscan/api?" else v2Base(c)) },
        Chain("btc", "Bitcoin", "BTC", "btc", "https://mempool.space") { c -> BtcApi(c) },
        Chain("tron", "Tron", "TRX", "tron", "https://tronscan.org") { c -> TronApi(c) },
        Chain("avax", "Avalanche C", "AVAX", "evm", "https://snowtrace.io", 43114) { c -> EtherscanApi(c, "https://api.routescan.io/v2/network/mainnet/evm/43114/etherscan/api?") },
        Chain("op", "Optimism", "ETH", "evm", "https://optimistic.etherscan.io", 10) { c -> BlockscoutApi(c, "https://explorer.optimism.io", null) },
        Chain("zksync", "zkSync Era", "ETH", "evm", "https://explorer.zksync.io", 324) { c -> BlockscoutApi(c, "https://zksync.blockscout.com", null) },
        Chain("scroll", "Scroll", "ETH", "evm", "https://scrollscan.com", 534352) { c -> BlockscoutApi(c, "https://scroll.blockscout.com", null) },
        Chain("unichain", "Unichain", "ETH", "evm", "https://uniscan.xyz", 130) { c -> BlockscoutApi(c, "https://unichain.blockscout.com", null) },
        Chain("celo", "Celo", "CELO", "evm", "https://celoscan.io", 42220) { c -> BlockscoutApi(c, "https://celo.blockscout.com", null) },
        Chain("mantle", "Mantle", "MNT", "evm", "https://mantlescan.xyz", 5000) { c -> EtherscanApi(c, "https://api.routescan.io/v2/network/mainnet/evm/5000/etherscan/api?") },
        Chain("base", "Base", "ETH", "evm", "https://basescan.org", 8453, true, v2),
        Chain("arb", "Arbitrum One", "ETH", "evm", "https://arbiscan.io", 42161, true, v2),
        Chain("polygon", "Polygon", "POL", "evm", "https://polygonscan.com", 137, true, v2),
        Chain("bsc", "BNB Chain", "BNB", "evm", "https://bscscan.com", 56, true, v2),
        Chain("demo", "Demo · offline", "ETH", "evm", "") { _ -> Demo },
    )
    fun get(key: String): Chain = all.firstOrNull { it.key == key } ?: throw ApiException("Unknown chain: $key")
    fun resetAll() = all.forEach { it.reset() }

    val stable = setOf("0xdac17f958d2ee523a2206206994597c13d831ec7", "0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48", "0x6b175474e89094c44da98b954eedeac495271d0f", "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", "TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8")
    val knownTokens = mapOf(
        "0xdac17f958d2ee523a2206206994597c13d831ec7" to ("USDT" to 6), "0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48" to ("USDC" to 6),
        "0x6b175474e89094c44da98b954eedeac495271d0f" to ("DAI" to 18), "0xc02aaa39b223fe8d0a0e5c4f27ead9083c756cc2" to ("WETH" to 18),
        "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t" to ("USDT" to 6), "TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8" to ("USDC" to 6),
    )
}

/** Etherscan-compatible list endpoints shared by Routescan and Etherscan V2. */
abstract class EvmListApi(protected val c: Chain) : ChainApi, FreezeCheck {
    protected abstract fun url(query: String): String
    private val issuers = listOf(Triple("0xdac17f958d2ee523a2206206994597c13d831ec7", "0xe47d6060", "USDT"), Triple("0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48", "0xfe575a87", "USDC"))

    /** USDT isBlackListed(address) and USDC isBlacklisted(address) on Ethereum. */
    override suspend fun frozen(address: String): List<String> {
        if (c.key != "eth") return emptyList()
        val arg = "000000000000000000000000" + address.lowercase().substring(2)
        return issuers.filter { (token, sel, _) -> val r = Http.obj(url("module=proxy&action=eth_call&to=$token&data=$sel$arg&tag=latest"), 600, tries = 2, timeoutSec = 15)?.s("result").orEmpty(); r.startsWith("0x") && r.length >= 66 && r.endsWith("1") }.map { it.third }
    }
    override suspend fun frozenAmount(address: String, tokens: List<String>): BigDecimal {
        val arg = "000000000000000000000000" + address.lowercase().substring(2)
        return issuers.filter { it.third in tokens }.map { (token, _, _) -> Units.fromHex(Http.obj(url("module=proxy&action=eth_call&to=$token&data=0x70a08231$arg&tag=latest"), 600, tries = 2, timeoutSec = 15)?.s("result"), 6) }.total()
    }

    protected suspend fun list(query: String): List<JSONObject> {
        val j = Http.obj(url(query)) ?: return emptyList()
        val r = j.opt("result")
        if (r is JSONArray) return r.objs()
        val msg = (j.s("message") + " " + (r as? String ?: "")).trim()
        if (msg.contains("No ", true) && msg.contains("found", true)) return emptyList()
        if (msg.isEmpty() || msg == "OK") return emptyList()
        throw ApiException("${c.name}: $msg")
    }
    private suspend fun safeList(q: String) = try { list(q) } catch (_: ApiException) { emptyList() }

    protected suspend fun blockAt(time: Long, before: Boolean): Long = try {
        val j = Http.obj(url("module=block&action=getblocknobytime&timestamp=$time&closest=${if (before) "before" else "after"}"), 86400)
        when (val r = j?.opt("result")) { is String -> r.toLongOrNull() ?: -1; is JSONObject -> r.l("blockNumber"); else -> -1 }
    } catch (_: ApiException) { -1 }

    override suspend fun transfers(address: String, max: Int, asc: Boolean, fromTime: Long, toTime: Long): TransferSet = coroutineScope {
        val a = address.lowercase()
        var range = ""
        if (fromTime > 0) blockAt(fromTime, false).let { if (it > 0) range += "&startblock=$it" }
        if (toTime > 0) blockAt(toTime, true).let { if (it > 0) range += "&endblock=$it" }
        val tail = "&address=$a$range&page=1&offset=$max&sort=${if (asc) "asc" else "desc"}"
        val dn = async { list("module=account&action=txlist$tail") }
        val dt = async { list("module=account&action=tokentx$tail") }
        val di = async { safeList("module=account&action=txlistinternal$tail") }
        val n = dn.await(); val t = dt.await(); val i = di.await()
        val items = ArrayList<Xfer>()
        for (e in n) {
            if (e.s("isError") == "1") continue
            val v = Units.from(e.s("value"), 18); if (v.signum() == 0) continue
            val to = e.s("to").ifEmpty { e.s("contractAddress") }
            items += Xfer(c.key, e.s("hash"), e.s("from").lowercase(), to.lowercase(), c.symbol, v, e.l("timeStamp"), block = e.l("blockNumber"))
        }
        for (e in t) {
            val raw = e.s("value"); if (raw.isEmpty()) continue
            val v = Units.from(raw, e.l("tokenDecimal").toInt()); if (v.signum() == 0) continue
            val sym = e.s("tokenSymbol").ifEmpty { "TOKEN" }.take(14)
            items += Xfer(c.key, e.s("hash"), e.s("from").lowercase(), e.s("to").lowercase(), sym, v, e.l("timeStamp"), e.s("contractAddress").lowercase(), "token", e.l("blockNumber"))
        }
        for (e in i) {
            if (e.s("isError") == "1") continue
            val v = Units.from(e.s("value"), 18); if (v.signum() == 0) continue
            items += Xfer(c.key, e.s("transactionHash").ifEmpty { e.s("hash") }, e.s("from").lowercase(), e.s("to").lowercase(), c.symbol, v, e.l("timeStamp"), kind = "internal", block = e.l("blockNumber"))
        }
        var out = items.filter { (fromTime <= 0 || it.time >= fromTime) && (toTime <= 0 || it.time <= toTime) }
        out = if (asc) out.sortedBy { it.time } else out.sortedByDescending { it.time }
        TransferSet(out, n.size >= max || t.size >= max || i.size >= max)
    }
}

/** Labels, address info and single transactions from Blockscout's REST API; transfer lists from an Etherscan-compatible endpoint. */
class BlockscoutApi(c: Chain, private val host: String, private val listBase: String?) : EvmListApi(c) {
    override fun url(query: String) = (listBase ?: "$host/api?") + query

    /** One list of Blockscout's REST API, newest first, following next_page_params. */
    private suspend fun pages(address: String, path: String, max: Int, fromTime: Long): Pair<List<JSONObject>, Boolean> {
        val all = ArrayList<JSONObject>(); var q = ""; val maxPages = (max / 50 + 1).coerceIn(1, 40)
        for (page in 0 until maxPages) {
            val url = "$host/api/v2/addresses/$address/$path" + (if (q.isEmpty()) "" else (if (path.contains('?')) "&" else "?") + q)
            val j = try { Http.obj(url, tries = 2, timeoutSec = 20) } catch (e: ApiException) { if (page > 0 || path.startsWith("internal")) return all to true else throw e } ?: return all to false
            val items = j.list("items"); all += items
            val np = j.o("next_page_params")
            if (np == null || items.isEmpty()) return all to false
            if (fromTime > 0 && Fmt.parseDate(items.last().s("timestamp")) < fromTime) return all to false
            q = np.keys().asSequence().filter { !np.isNull(it) }.joinToString("&") { it + "=" + java.net.URLEncoder.encode(np.get(it).toString(), "UTF-8") }
        }
        return all to true
    }

    override suspend fun transfers(address: String, max: Int, asc: Boolean, fromTime: Long, toTime: Long): TransferSet {
        if (listBase != null) return super.transfers(address, max, asc, fromTime, toTime)
        val a = address.lowercase(); val items = ArrayList<Xfer>()
        fun h(e: JSONObject, k: String) = e.o(k)?.s("hash")?.lowercase().orEmpty()
        val (tx, t1) = pages(a, "transactions", max, fromTime)
        for (e in tx) {
            if (e.s("status") == "error") continue
            val v = Units.from(e.s("value"), 18); if (v.signum() == 0) continue
            items += Xfer(c.key, e.s("hash"), h(e, "from"), h(e, "to"), c.symbol, v, Fmt.parseDate(e.s("timestamp")), block = e.l("block_number"))
        }
        val (tok, t2) = pages(a, "token-transfers?type=ERC-20", max, fromTime)
        for (e in tok) {
            val t = e.o("token") ?: continue; val tot = e.o("total") ?: continue
            val dec = tot.l("decimals").toInt().let { if (it == 0) t.l("decimals").toInt() else it }
            val v = Units.from(tot.s("value"), dec); if (v.signum() == 0) continue
            items += Xfer(c.key, e.s("transaction_hash"), h(e, "from"), h(e, "to"), t.s("symbol").ifEmpty { "TOKEN" }.take(14), v, Fmt.parseDate(e.s("timestamp")), t.s("address_hash").ifEmpty { t.s("address") }.lowercase(), "token", e.l("block_number"))
        }
        val (itx, t3) = pages(a, "internal-transactions", minOf(max, 500), fromTime)
        for (e in itx) {
            if (e.has("success") && !e.b("success")) continue
            val type = e.s("type"); if (type.isNotEmpty() && type != "call" && type != "create" && type != "selfdestruct") continue
            val v = Units.from(e.s("value"), 18); if (v.signum() == 0) continue
            items += Xfer(c.key, e.s("transaction_hash"), h(e, "from"), h(e, "to"), c.symbol, v, Fmt.parseDate(e.s("timestamp")), kind = "internal", block = e.l("block_number"))
        }
        var out = items.filter { (fromTime <= 0 || it.time >= fromTime) && (toTime <= 0 || it.time <= toTime) }
        out = if (asc) out.sortedBy { it.time } else out.sortedByDescending { it.time }
        return TransferSet(out, t1 || t2 || t3)
    }

    override suspend fun info(address: String): AddrInfo {
        val info = AddrInfo(c.key, address.lowercase())
        val a = Http.obj("$host/api/v2/addresses/$address", tries = 3, timeoutSec = 15)
        if (a == null) { info.note = "No activity recorded for this address."; return info }
        if (a.s("name").isNotEmpty()) info.name = a.s("name")
        if (a.s("ens_domain_name").isNotEmpty()) info.tags += a.s("ens_domain_name")
        for (t in a.list("public_tags")) t.s("display_name").let { if (it.isNotEmpty()) info.tags += it }
        a.o("metadata")?.list("tags")?.sortedByDescending { it.l("ordinal") }?.forEach { t ->
            val type = t.s("tagType"); val n = t.s("name")
            if (type == "note" || n.isEmpty() || n.startsWith("Proxy_")) return@forEach
            if (type == "name" && info.name.isEmpty()) info.name = n else if (n !in info.tags) info.tags += n
        }
        info.isContract = a.b("is_contract"); info.isScam = a.b("is_scam")
        info.balance = Units.from(a.s("coin_balance"), 18)
        info.usd = a.s("exchange_rate").toBigDecimalOrNull() ?: BigDecimal.ZERO
        try { Http.obj("$host/api/v2/addresses/$address/counters", tries = 1, timeoutSec = 8)?.let { info.txCount = it.l("transactions_count") + it.l("token_transfers_count") } } catch (_: ApiException) { }
        return info
    }

    override suspend fun tx(hash: String): List<Xfer> {
        val list = ArrayList<Xfer>()
        val t = Http.obj("$host/api/v2/transactions/$hash") ?: throw ApiException("Transaction not found on ${c.name}")
        val time = Fmt.parseDate(t.s("timestamp")); val block = t.l("block_number")
        val from = t.o("from")?.s("hash")?.lowercase().orEmpty(); val to = t.o("to")?.s("hash")?.lowercase().orEmpty()
        val v = Units.from(t.s("value"), 18)
        if (v.signum() > 0) list += Xfer(c.key, hash, from, to, c.symbol, v, time, block = block)
        Http.obj("$host/api/v2/transactions/$hash/token-transfers")?.list("items")?.forEach { e ->
            val tok = e.o("token") ?: return@forEach; val tot = e.o("total") ?: return@forEach
            val dec = tot.l("decimals").toInt().let { if (it == 0) tok.l("decimals").toInt() else it }
            val amt = Units.from(tot.s("value"), dec); if (amt.signum() == 0) return@forEach
            list += Xfer(c.key, hash, e.o("from")?.s("hash")?.lowercase().orEmpty(), e.o("to")?.s("hash")?.lowercase().orEmpty(), tok.s("symbol").ifEmpty { "TOKEN" }, amt, time, tok.s("address_hash").ifEmpty { tok.s("address") }.lowercase(), "token", block)
        }
        try {
            Http.obj("$host/api/v2/transactions/$hash/internal-transactions")?.list("items")?.forEach { e ->
                val amt = Units.from(e.s("value"), 18)
                if (amt.signum() == 0 || (e.has("success") && !e.b("success"))) return@forEach
                list += Xfer(c.key, hash, e.o("from")?.s("hash")?.lowercase().orEmpty(), e.o("to")?.s("hash")?.lowercase().orEmpty(), c.symbol, amt, time, kind = "internal", block = block)
            }
        } catch (_: ApiException) { }
        if (list.isEmpty() && from.isNotEmpty()) list += Xfer(c.key, hash, from, to, c.symbol, BigDecimal.ZERO, time, block = block)
        return list
    }
}

class EtherscanApi(c: Chain, private val base: String) : EvmListApi(c) {
    override fun url(query: String) = base + query
    private suspend fun proxy(q: String): Any? = Http.obj(url("module=proxy&$q"))?.opt("result")
    private fun needKey() { if (c.needsKey && !c.ready) throw ApiException("${c.name}: add an Etherscan API key in Settings to use this chain.") }

    override suspend fun info(address: String): AddrInfo {
        needKey()
        val info = AddrInfo(c.key, address.lowercase())
        (Http.obj(url("module=account&action=balance&address=$address&tag=latest"))?.opt("result") as? String)?.let { info.balance = Units.from(it, 18) }
        try {
            info.isContract = ((proxy("action=eth_getCode&address=$address&tag=latest") as? String)?.length ?: 0) > 2
            (proxy("action=eth_getTransactionCount&address=$address&tag=latest") as? String)?.let { info.txCount = Units.hexLong(it) }
        } catch (_: ApiException) { }
        return info
    }

    override suspend fun tx(hash: String): List<Xfer> {
        needKey()
        val list = ArrayList<Xfer>()
        val t = proxy("action=eth_getTransactionByHash&txhash=$hash") as? JSONObject ?: throw ApiException("Transaction not found on ${c.name}")
        val bn = t.s("blockNumber"); val block = Units.hexLong(bn)
        val time = (proxy("action=eth_getBlockByNumber&tag=$bn&boolean=false") as? JSONObject)?.let { Units.hexLong(it.s("timestamp")) } ?: 0
        val from = t.s("from").lowercase(); val to = t.s("to").lowercase()
        val v = Units.fromHex(t.s("value"), 18)
        if (v.signum() > 0) list += Xfer(c.key, hash, from, to, c.symbol, v, time, block = block)
        (proxy("action=eth_getTransactionReceipt&txhash=$hash") as? JSONObject)?.list("logs")?.forEach { lg ->
            val tp = lg.optJSONArray("topics") ?: return@forEach
            if (tp.length() != 3 || tp.optString(0) != "0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef") return@forEach
            val ct = lg.s("address").lowercase(); val k = Chains.knownTokens[ct]
            val amt = Units.fromHex(lg.s("data"), k?.second ?: 18); if (amt.signum() == 0) return@forEach
            list += Xfer(c.key, hash, "0x" + tp.optString(1).substring(26), "0x" + tp.optString(2).substring(26), k?.first ?: ("token " + Fmt.short(ct)), amt, time, ct, "token", block)
        }
        if (list.isEmpty()) list += Xfer(c.key, hash, from, to, c.symbol, BigDecimal.ZERO, time, block = block)
        return list
    }
}

class BtcApi(private val c: Chain) : ChainApi {
    private val h = "https://mempool.space/api"

    override suspend fun info(address: String): AddrInfo {
        val info = AddrInfo(c.key, address)
        val a = Http.obj("$h/address/$address") ?: throw ApiException("Bitcoin address not found")
        val cs = a.o("chain_stats"); val ms = a.o("mempool_stats")
        if (cs != null) {
            info.balance = Units.sats(cs.l("funded_txo_sum") - cs.l("spent_txo_sum") + (ms?.l("funded_txo_sum") ?: 0) - (ms?.l("spent_txo_sum") ?: 0))
            info.txCount = cs.l("tx_count")
        }
        try { Http.obj("$h/v1/prices", 900)?.let { info.usd = BigDecimal(it.l("USD")) } } catch (_: ApiException) { }
        return info
    }

    private fun normalize(tx: JSONObject, focus: String?): List<Xfer> {
        val res = ArrayList<Xfer>()
        val ins = LinkedHashMap<String, Long>(); var coinbase = false
        for (vin in tx.list("vin")) {
            if (vin.b("is_coinbase")) { coinbase = true; continue }
            val po = vin.o("prevout") ?: continue
            val a = po.s("scriptpubkey_address"); if (a.isEmpty()) continue
            ins[a] = (ins[a] ?: 0) + po.l("value")
        }
        val st = tx.o("status")
        val time = (st?.l("block_time") ?: 0).let { if (it == 0L) System.currentTimeMillis() / 1000 else it }
        val block = st?.l("block_height") ?: 0
        val hash = tx.s("txid")
        val mainIn = if (coinbase) "coinbase" else ins.maxByOrNull { it.value }?.key ?: "unknown"
        val outs = LinkedHashMap<String, Long>()
        for (vo in tx.list("vout")) { val a = vo.s("scriptpubkey_address"); if (a.isEmpty()) continue; outs[a] = (outs[a] ?: 0) + vo.l("value") }
        if (focus == null || ins.containsKey(focus)) {
            for ((a, v) in outs) if (!ins.containsKey(a)) res += Xfer(c.key, hash, focus ?: mainIn, a, "BTC", Units.sats(v), time, block = block)
        } else outs[focus]?.let { res += Xfer(c.key, hash, mainIn, focus, "BTC", Units.sats(it), time, block = block) }
        return res
    }

    override suspend fun transfers(address: String, max: Int, asc: Boolean, fromTime: Long, toTime: Long): TransferSet {
        val items = ArrayList<Xfer>(); var last: String? = null; var pages = 0; var truncated = false
        val maxPages = (max / 25 + 1).coerceIn(1, 40)
        while (pages < maxPages) {
            val arr = Http.arr(if (last == null) "$h/address/$address/txs" else "$h/address/$address/txs/chain/$last").objs()
            if (arr.isEmpty()) break
            var oldest = Long.MAX_VALUE
            for (tx in arr) { for (x in normalize(tx, address)) { items += x; oldest = minOf(oldest, x.time) }; last = tx.s("txid") }
            pages++
            if (arr.size < 25 || (fromTime > 0 && oldest < fromTime)) break
            if (pages == maxPages) truncated = true
        }
        var out = items.filter { (fromTime <= 0 || it.time >= fromTime) && (toTime <= 0 || it.time <= toTime) }
        out = if (asc) out.sortedBy { it.time } else out.sortedByDescending { it.time }
        return TransferSet(out, truncated, if (truncated) "Bitcoin history is read newest-first; older transactions beyond the page limit were not loaded." else "")
    }

    override suspend fun tx(hash: String): List<Xfer> = normalize(Http.obj("$h/tx/$hash") ?: throw ApiException("Transaction not found on Bitcoin"), null)
}

class TronApi(private val c: Chain) : ChainApi, FreezeCheck {
    private val h = "https://api.trongrid.io"
    private suspend fun call(address: String, fn: String): String {
        val hex = Addr.tronToHex(address); if (hex.isEmpty()) return ""
        val body = "{\"owner_address\":\"$address\",\"contract_address\":\"TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t\",\"function_selector\":\"$fn(address)\",\"parameter\":\"000000000000000000000000$hex\",\"visible\":true}"
        return Http.obj("$h/wallet/triggerconstantcontract", 600, body, 2, 15)?.optJSONArray("constant_result")?.optString(0).orEmpty()
    }
    override suspend fun frozen(address: String): List<String> = call(address, "isBlackListed").let { if (it.length >= 64 && it.endsWith("1")) listOf("USDT") else emptyList() }
    override suspend fun frozenAmount(address: String, tokens: List<String>): BigDecimal = Units.fromHex(call(address, "balanceOf"), 6)

    override suspend fun info(address: String): AddrInfo {
        val info = AddrInfo(c.key, address)
        val d = Http.obj("$h/v1/accounts/$address")?.list("data")?.firstOrNull()
        if (d != null) info.balance = Units.from(d.l("balance").toString(), 6) else info.note = "Account not activated or no data."
        Chains.knownTokens[address]?.let { info.name = it.first + " token contract"; info.isContract = true }
        try { Http.obj("https://api.coingecko.com/api/v3/simple/price?ids=tron&vs_currencies=usd", 1800, tries = 1, timeoutSec = 10)?.o("tron")?.s("usd")?.toBigDecimalOrNull()?.let { info.usd = it } } catch (_: ApiException) { }
        return info
    }

    override suspend fun transfers(address: String, max: Int, asc: Boolean, fromTime: Long, toTime: Long): TransferSet {
        val items = ArrayList<Xfer>(); var truncated = false
        val q = "limit=200&only_confirmed=true&order_by=block_timestamp,${if (asc) "asc" else "desc"}" + (if (fromTime > 0) "&min_timestamp=${fromTime * 1000}" else "") + (if (toTime > 0) "&max_timestamp=${toTime * 1000}" else "")
        suspend fun page(path: String, each: (JSONObject) -> Unit) {
            var fp: String? = null; var got = 0
            while (got < max) {
                val j = Http.obj("$h/v1/accounts/$address/$path?$q" + (if (fp != null) "&fingerprint=$fp" else "")) ?: return
                val data = j.list("data"); data.forEach(each); got += data.size
                fp = j.o("meta")?.s("fingerprint")
                if (data.isEmpty() || fp.isNullOrEmpty()) return
            }
            truncated = true
        }
        page("transactions/trc20") { e ->
            if (e.s("type").isNotEmpty() && e.s("type") != "Transfer") return@page
            val ti = e.o("token_info")
            val v = Units.from(e.s("value"), (ti?.l("decimals") ?: 6).toInt()); if (v.signum() == 0) return@page
            items += Xfer(c.key, e.s("transaction_id"), e.s("from"), e.s("to"), (ti?.s("symbol") ?: "").ifEmpty { "TOKEN" }.take(14), v, e.l("block_timestamp") / 1000, ti?.s("address").orEmpty(), "token")
        }
        page("transactions") { e ->
            val ct = e.o("raw_data")?.list("contract")?.firstOrNull() ?: return@page
            if (ct.s("type") != "TransferContract") return@page
            val ret = e.list("ret").firstOrNull()?.s("contractRet").orEmpty(); if (ret.isNotEmpty() && ret != "SUCCESS") return@page
            val v = ct.o("parameter")?.o("value") ?: return@page
            val amt = Units.from(v.l("amount").toString(), 6); if (amt.signum() == 0) return@page
            items += Xfer(c.key, e.s("txID"), Addr.tronFromHex(v.s("owner_address")), Addr.tronFromHex(v.s("to_address")), "TRX", amt, e.l("block_timestamp") / 1000, block = e.l("blockNumber"))
        }
        return TransferSet(if (asc) items.sortedBy { it.time } else items.sortedByDescending { it.time }, truncated)
    }

    override suspend fun tx(hash: String): List<Xfer> {
        val list = ArrayList<Xfer>()
        Http.obj("$h/wallet/gettransactionbyid", post = "{\"value\":\"$hash\"}")?.o("raw_data")?.let { rd ->
            val ct = rd.list("contract").firstOrNull()
            val v = ct?.o("parameter")?.o("value")
            if (ct != null && ct.s("type") == "TransferContract" && v != null)
                list += Xfer(c.key, hash, Addr.tronFromHex(v.s("owner_address")), Addr.tronFromHex(v.s("to_address")), "TRX", Units.from(v.l("amount").toString(), 6), rd.l("timestamp") / 1000)
        }
        Http.obj("$h/v1/transactions/$hash/events")?.list("data")?.forEach { e ->
            if (e.s("event_name") != "Transfer") return@forEach
            val r = e.o("result") ?: return@forEach
            val ct = e.s("contract_address"); val k = Chains.knownTokens[ct]
            val amt = Units.from(r.s("value"), k?.second ?: 6); if (amt.signum() == 0) return@forEach
            list += Xfer(c.key, hash, Addr.tronFromHex(r.s("from")), Addr.tronFromHex(r.s("to")), k?.first ?: ("token " + Fmt.short(ct)), amt, e.l("block_timestamp") / 1000, ct, "token")
        }
        if (list.isEmpty()) throw ApiException("Transaction not found on Tron (or it moved no value)")
        return list
    }
}
