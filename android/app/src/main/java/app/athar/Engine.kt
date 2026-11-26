package app.athar

import org.json.JSONArray
import org.json.JSONObject
import java.math.BigDecimal
import java.util.concurrent.ConcurrentHashMap
import kotlin.coroutines.coroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit

class Label(val name: String, val cat: String, val source: String)
class UserLabel(val address: String, val name: String, val cat: String = "user", val note: String = "")

/** Address labels: the user's own, a small built-in list, and tags returned by the explorer. */
object Labels {
    val cats = listOf("exchange", "mixer", "bridge", "dex", "threat", "contract", "user", "other")
    val builtin = LinkedHashMap<String, Label>()
    val seen = ConcurrentHashMap<String, Label>()
    val user = ArrayList<UserLabel>()

    private fun b(cat: String, name: String, addr: String) { builtin[addr.lowercase()] = Label(name, cat, "built-in") }
    init {
        b("mixer", "Tornado Cash: 0.1 ETH", "0x12D66f87A04A9E220743712cE6d9bB1B5616B8Fc")
        b("mixer", "Tornado Cash: 1 ETH", "0x47CE0C6eD5B0Ce3d3A51fdb1C52DC66a7c3c2936")
        b("mixer", "Tornado Cash: 10 ETH", "0x910Cbd523D972eb0a6f4cAe4618aD62622b39DbF")
        b("mixer", "Tornado Cash: 100 ETH", "0xA160cdAB225685dA1d56aa342Ad8841c3b53f291")
        b("mixer", "Tornado Cash: Router", "0xd90e2f925DA726b50C4Ed8D0Fb90Ad053324F31b")
        b("mixer", "Railgun: Relay", "0xFA7093CDD9EE6932B4eb2c9e1cde7CE00B1FA4b9")
        b("exchange", "Binance 7", "0xBE0eB53F46cd790Cd13851d5EFf43D12404d33E8")
        b("exchange", "Binance 8", "0xF977814e90dA44bFA03b6295A0616a897441aceC")
        b("exchange", "Binance 14", "0x28C6c06298d514Db089934071355E5743bf21d60")
        b("exchange", "Binance 15", "0x21a31Ee1afC51d94C2eFcCAa2092aD1028285549")
        b("exchange", "Binance 16", "0xDFd5293D8e347dFe59E90eFd55b2956a1343963d")
        b("exchange", "Coinbase 1", "0x71660c4005BA85c37ccec55d0C4493E66Fe775d3")
        b("exchange", "Kraken 4", "0x267be1C1D684F78cb4F6a176C4911b741E4Ffdc0")
        b("exchange", "OKX", "0x6cC5F688a315f3dC28A7781717a9A798a59fDA7b")
        b("exchange", "Bybit: Hot Wallet", "0xf89d7b9c864f589bbF53a82105107622B35EaA40")
        b("exchange", "KuCoin", "0x2B5634C42055806a59e9107ED44D43c426E58258")
        b("bridge", "Relay: Depository (Tron)", "TXtEs6t2oUWQsNos7m68gbHdE9Q5n6x2oN")
        b("bridge", "THORChain: Router", "0xD37BbE5744D730a1d98d8DC97c42F0Ca46aD7146")
        b("bridge", "Wormhole: Token Bridge", "0x3ee18B2214AFF97000D974cf647E7C347E8fa585")
        b("bridge", "Polygon: ERC20 Bridge", "0x40ec5B33f54e0E8A33A975908C5BA1c14e5BbbDF")
        b("bridge", "Arbitrum One: Bridge", "0x8315177aB297bA92A06054cE80a67Ed4DBd7ed3a")
        b("bridge", "Optimism: Gateway", "0x99C9fc46f92E8a1c0deC1b1747d010903E884bE1")
        b("dex", "Uniswap V2: Router", "0x7a250d5630B4cF539739dF2C5dAcb4c659F2488D")
        b("dex", "Uniswap V3: Router", "0xE592427A0AEce92De3Edee1F18E0157C05861564")
        b("dex", "Uniswap V3: Router 2", "0x68b3465833fb72A70ecDF485E0e4C7bD8665Fc45")
        b("dex", "1inch v5: Router", "0x1111111254EEB25477B68fb85Ed929f73A960582")
        b("contract", "Wrapped Ether (WETH)", "0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2")
        b("contract", "Beacon Deposit Contract", "0x00000000219ab540356cBB839Cbe05303d7705Fa")
        b("other", "Null address (burn / mint)", "0x0000000000000000000000000000000000000000")
        b("other", "Burn address", "0x000000000000000000000000000000000000dEaD")
        b("threat", "Bybit Exploiter (Feb 2025)", "0x47666Fab8bd0Ac7003bce3f5C3585383F09486E2")
    }

    fun isService(cat: String) = cat == "exchange" || cat == "mixer" || cat == "bridge" || cat == "dex"

    fun catFromText(text: String?, scam: Boolean, contract: Boolean): String {
        val t = (text ?: "").lowercase()
        fun has(vararg w: String) = w.any { t.contains(it) }
        return when {
            scam || has("exploit", "hack", "phish", "scam", "sanction", "ofac", "lazarus", "drainer", "heist", "stolen") -> "threat"
            has("tornado", "mixer", "railgun", "sinbad", "blender", "wasabi") -> "mixer"
            has("bridge", "wormhole", "stargate", "across", "thorchain", "synapse", "hop protocol") -> "bridge"
            has("exchange", "hot wallet", "cex", "binance", "coinbase", "kraken", "okx", "bybit", "kucoin", "huobi", "htx", "gate.io", "bitfinex", "bitget", "mexc", "crypto.com") -> "exchange"
            has("dex", "uniswap", "1inch", "router", "swap", "aggregator", "curve", "sushi", "pancake", "cow protocol", "0x protocol") -> "dex"
            contract -> "contract"
            t.isNotBlank() -> "other"
            else -> ""
        }
    }

    fun learn(i: AddrInfo?) {
        if (i == null) return
        val cat = catFromText(i.name + " " + i.tags.joinToString(" "), i.isScam, i.isContract)
        if (cat.isEmpty()) return
        val name = i.name.ifEmpty { i.tags.firstOrNull() ?: if (i.isScam) "Flagged as scam" else if (i.isContract) "Contract" else "" }
        seen[i.address] = Label(name, cat, "explorer")
    }

    fun find(addr0: String?): Label? {
        if (addr0.isNullOrEmpty()) return null
        val addr = Addr.bare(addr0)
        user.firstOrNull { it.address.equals(addr, true) }?.let { return Label(it.name, it.cat.ifEmpty { "user" }, "user") }
        builtin[addr.lowercase()]?.let { return it }
        return seen[addr]
    }
    /** Label, or the shortened address. Generic names the explorer gives to contracts keep the address next to them. */
    fun show(addr: String?): String {
        val l = find(addr)
        if (l == null || l.name.isEmpty()) return Fmt.short(addr)
        if (addr != null && addr != Addr.bare(addr)) return addr.substring(0, addr.indexOf(':') + 1) + l.name
        return if (l.source == "explorer" && (l.cat == "contract" || l.cat == "other" || l.cat == "dex")) l.name + " · " + Fmt.short(addr) else l.name
    }
}

class TraceOptions(
    var chain: String = "eth", var start: String = "", var forward: Boolean = true, var maxHops: Int = 3, var maxNodes: Int = 120, var branch: Int = 25,
    var minSharePct: Double = 3.0, var fromTime: Long = 0, var asset: String = "", var stopAtServices: Boolean = true, var perNode: Int = 1000, var rootPerNode: Int = 5000, var windowDays: Int = 0, var busyTx: Long = 5000, var allTokens: Boolean = false, var followBridges: Boolean = true,
)

class GNode(val id: String, var depth: Int = 0, var arrival: Long = 0) {
    var label = ""; var cat = ""; var stop = ""; var expanded = false; var isContract = false; var txCount = -1L; var x = 0.0; var y = 0.0
    var balance: BigDecimal = BigDecimal.ZERO; var frozen = ""; var frozenUsd: BigDecimal = BigDecimal.ZERO; var swap = false; var bx = 0.0; var by = 0.0; var r = 20.0
    fun json() = JSONObject().put("id", id).put("depth", depth).put("arrival", arrival).put("label", label).put("cat", cat).put("stop", stop).put("expanded", expanded).put("x", x).put("y", y).put("tx", txCount)
        .put("bal", balance.toPlainString()).put("frozen", frozen).put("fusd", frozenUsd.toPlainString()).put("bx", bx).put("by", by).put("r", r)
    companion object { fun of(j: JSONObject) = GNode(j.s("id"), j.optInt("depth"), j.l("arrival")).apply { label = j.s("label"); cat = j.s("cat"); stop = j.s("stop"); expanded = j.b("expanded"); x = j.optDouble("x", 0.0); y = j.optDouble("y", 0.0); txCount = j.optLong("tx", -1)
        balance = j.s("bal").toBigDecimalOrNull() ?: BigDecimal.ZERO; frozen = j.s("frozen"); frozenUsd = j.s("fusd").toBigDecimalOrNull() ?: BigDecimal.ZERO; bx = j.optDouble("bx", 0.0); by = j.optDouble("by", 0.0); r = j.optDouble("r", 20.0) } }
}

class GEdge(val from: String, val to: String, val asset: String, val contract: String, var amount: BigDecimal, var count: Int, val first: Long, var last: Long, hashes0: List<String>, val chainKey: String = "", val bridge: Boolean = false) {
    val hashes = ArrayList(hashes0)
    fun json() = JSONObject().put("from", from).put("to", to).put("asset", asset).put("contract", contract).put("amount", amount.toPlainString()).put("count", count).put("first", first).put("last", last).put("hashes", JSONArray(hashes)).put("ck", chainKey).put("bridge", bridge)
    companion object { fun of(j: JSONObject) = GEdge(j.s("from"), j.s("to"), j.s("asset"), j.s("contract"), j.s("amount").toBigDecimalOrNull() ?: BigDecimal.ZERO, j.optInt("count"), j.l("first"), j.l("last"), j.optJSONArray("hashes")?.let { a -> (0 until a.length()).map { a.optString(it) } } ?: emptyList(), j.s("ck"), j.b("bridge")) }
}

class Graph(val chain: String, val start: String, val forward: Boolean) {
    var created = 0L; var usdRate: BigDecimal = BigDecimal.ZERO; var options = ""
    val nodes = ArrayList<GNode>(); val edges = ArrayList<GEdge>(); val notes = ArrayList<String>()
    val rates = HashMap<String, BigDecimal>()
    fun node(id: String) = nodes.firstOrNull { it.id == id }
    /** Network key of a node id ("btc:bc1…" -> btc; a bare id is on the graph's own network). */
    fun chainOf(id: String): String { val c = id.indexOf(':'); return if (c in 1..11) id.substring(0, c) else chain }
    fun idFor(chainKey: String, address: String) = if (chainKey == chain) address else "$chainKey:$address"
    private fun rate(chainKey: String): BigDecimal = rates[chainKey.ifEmpty { chain }] ?: if (chainKey.isEmpty() || chainKey == chain) usdRate else BigDecimal.ZERO
    /** start, frozen, holding, exchange, mixer, bridge, dex, unexplored, burn or passed (moved on). */
    fun status(n: GNode): String = when {
        n.frozen.isNotEmpty() -> "frozen"
        n.depth == 0 -> "start"
        n.stop == "service" -> n.cat
        n.stop == "end" -> "holding"
        n.stop.isEmpty() -> if (n.expanded) "passed" else "unexplored"
        n.stop == "burn" -> "burn"
        n.stop == "otherchain" -> "otherchain"
        else -> "unexplored"
    }
    fun usdIn(n: GNode): BigDecimal = edges.filter { (if (forward) it.to else it.from) == n.id }.map { usd(it) ?: BigDecimal.ZERO }.total()
    fun usdOut(n: GNode): BigDecimal = edges.filter { (if (forward) it.from else it.to) == n.id }.map { usd(it) ?: BigDecimal.ZERO }.total()
    fun usd(e: GEdge): BigDecimal? = if (e.contract in Chains.stable || (e.bridge && e.asset in setOf("USDT", "USDC", "DAI"))) e.amount else if (e.contract.isEmpty() && rate(e.chainKey).signum() > 0) e.amount * rate(e.chainKey) else null
    fun json() = JSONObject().put("chain", chain).put("start", start).put("forward", forward).put("created", created).put("usd", usdRate.toPlainString()).put("options", options)
        .put("nodes", JSONArray(nodes.map { it.json() })).put("edges", JSONArray(edges.map { it.json() })).put("notes", JSONArray(notes))
    companion object {
        fun of(j: JSONObject) = Graph(j.s("chain"), j.s("start"), j.b("forward")).apply {
            created = j.l("created"); usdRate = j.s("usd").toBigDecimalOrNull() ?: BigDecimal.ZERO; options = j.s("options")
            nodes += j.list("nodes").map { GNode.of(it) }; edges += j.list("edges").map { GEdge.of(it) }
            j.optJSONArray("notes")?.let { a -> for (i in 0 until a.length()) notes += a.optString(i) }
        }
    }
}

/** Optional capability: tells whether an address is blacklisted (frozen) by a stablecoin issuer, and how much sits there. */
interface FreezeCheck {
    suspend fun frozen(address: String): List<String>
    suspend fun frozenAmount(address: String, tokens: List<String>): BigDecimal
}

/** Fund-flow tracing, level by level: from each address follow the largest transfers made after the funds arrived. */
object Tracer {
    fun starts(chain: Chain, text: String?): List<String> = (text ?: "").split('\n', '\r', ',', ';', ' ', '\t').filter { val d = Addr.detect(it); d.first == "address" && d.second == chain.family }.map { Addr.norm(chain.family, it) }.distinct()

    suspend fun run(o: TraceOptions, progress: ((String) -> Unit)? = null): Graph {
        val chain = Chains.get(o.chain)
        val st = starts(chain, o.start)
        if (st.isEmpty()) throw ApiException("No valid start address for ${chain.name}")
        val g = Graph(o.chain, st.joinToString("\n"), o.forward)
        g.created = System.currentTimeMillis() / 1000
        g.options = "hops=${o.maxHops}; branch=${o.branch}; min share=${o.minSharePct}%; max nodes=${o.maxNodes}; asset=${o.asset.ifEmpty { if (o.allTokens) "all tokens" else "coin + major stablecoins" }}; from=${Fmt.time(o.fromTime)}"
        st.forEach { g.nodes += GNode(it, 0, o.fromTime) }
        expand(g, g.nodes.toList(), o, o.maxHops, progress, null)
        return g
    }

    suspend fun more(g: Graph, from: GNode, o: TraceOptions, extraHops: Int, progress: ((String) -> Unit)? = null) {
        from.stop = ""
        o.maxNodes = g.nodes.size + maxOf(10, o.maxNodes / 2)
        expand(g, listOf(from), o, from.depth + extraHops, progress, from)
    }

    private suspend fun expand(g: Graph, seeds: List<GNode>, o: TraceOptions, maxDepth: Int, progress: ((String) -> Unit)?, force: GNode?) = coroutineScope {
        fun api(n: GNode): ChainApi? = Chains.all.firstOrNull { it.key == g.chainOf(n.id) }?.takeIf { it.ready }?.api
        fun value(ck: String, contract: String, sum: BigDecimal): BigDecimal = if (contract in Chains.stable) sum else if (contract.isEmpty()) sum * (g.rates[ck] ?: BigDecimal.ZERO) else BigDecimal.ZERO
        var level = seeds
        var done = 0
        val gate = Semaphore(4)
        while (level.isNotEmpty()) {
            coroutineContext.ensureActive()
            progress?.invoke("$done ✓  ·  ${level.size} …  ·  ${Fmt.short(level[0].id)}")
            val infos = level.map { n -> async { gate.withPermit { val a = api(n) ?: return@withPermit null; try { a.info(Addr.bare(n.id)).also { Labels.learn(it) } } catch (e: ApiException) { synchronized(g.notes) { g.notes += "nolabel" }; null } } } }.awaitAll()
            val todo = ArrayList<GNode>()
            val next = ArrayList<GNode>()
            level.forEachIndexed { k, n ->
                val info = infos[k]
                val ck = g.chainOf(n.id)
                if (api(n) == null) { n.stop = "otherchain"; if (n.label.isEmpty()) n.label = Bridges.nameOf(ck); return@forEachIndexed }
                if (info != null) { n.isContract = info.isContract; n.txCount = info.txCount; n.balance = info.balance; if (info.usd.signum() > 0) { g.rates[ck] = info.usd; if (ck == g.chain) g.usdRate = info.usd } }
                val lab = Labels.find(n.id)
                if (lab != null) { n.label = lab.name; n.cat = lab.cat } else if (n.isContract) { n.cat = "contract"; n.label = "Contract" }
                if (n.swap && n.isContract && (n.cat == "contract" || n.cat == "other" || n.cat.isEmpty())) n.cat = "dex"
                val root = n.depth == 0 || n === force
                if (!root) {
                    if (o.stopAtServices && Labels.isService(n.cat)) { n.stop = "service"; return@forEachIndexed }
                    if (n.cat == "other" && lab?.source == "built-in") { n.stop = "burn"; return@forEachIndexed }
                    if (n.isContract) { n.stop = "contract"; return@forEachIndexed }
                    if (n.txCount > o.busyTx) { n.stop = "busy"; return@forEachIndexed }
                    // dormant stash: the coin that arrived is still there, no need to read its history
                    val arrived = g.edges.filter { (if (g.forward) it.to else it.from) == n.id && it.contract.isEmpty() }.map { it.amount }.total()
                    if (g.forward && info != null && arrived.signum() > 0 && info.balance >= arrived * BigDecimal("0.95")) { n.stop = "end"; return@forEachIndexed }
                }
                if (n.depth >= maxDepth) { n.stop = "hops"; return@forEachIndexed }
                todo += n
            }
            // did the value leave this network here? ask the bridges themselves about the transactions that entered a contract or a hub wallet
            if (g.forward && o.followBridges && Chains.all.any { it.key == g.chain && it.explorer.isNotEmpty() }) {
                level.filter { n -> n.depth > 0 && !n.swap && n.cat != "mixer" && n.cat != "exchange" && n.stop !in setOf("end", "otherchain", "burn") }.map { n ->
                    async {
                        gate.withPermit {
                            val ck = g.chainOf(n.id)
                            for (e in g.edges.filter { it.to == n.id && !it.bridge }) {
                                val hub = n.stop in setOf("contract", "busy", "service")
                                val u = g.usd(e); if (u != null && u < BigDecimal(if (hub) 20000 else 100000)) continue
                                var bulk = false
                                for (h in e.hashes.take(if (hub) 3 else 1)) {
                                    if (bulk) break
                                    var hops = try { Bridges.resolve(ck, h) } catch (_: Exception) { emptyList() }.filter { it.outbound && it.address.isNotEmpty() }
                                    if (hops.isNotEmpty() && hops[0].service == "THORChain" && e.count > 3) try { val all = Bridges.thorAll(Addr.bare(e.from), e.first - 3600); if (all.isNotEmpty()) { hops = all; bulk = true } } catch (_: ApiException) { }
                                    else if (hops.isNotEmpty() && hops[0].service == "Relay" && e.count > 1) try { val all = Bridges.relayAll(Addr.bare(e.from), e.first - 3600); if (all.isNotEmpty()) { hops = all; bulk = true } } catch (_: ApiException) { }
                                    hops = hops.filter { !(it.chain == ck && it.address.equals(Addr.bare(e.from), true)) }
                                    if (hops.isEmpty()) continue
                                    synchronized(g) {
                                        n.cat = "bridge"; n.stop = "service"
                                        if (n.label.isEmpty() || n.label == "Contract") n.label = hops[0].service
                                        for (hop in hops) {
                                            val dc = Chains.all.firstOrNull { it.key == hop.chain }
                                            val id = g.idFor(hop.chain, if (dc != null) Addr.norm(dc.family, hop.address) else hop.address)
                                            var child = g.node(id)
                                            if (child == null) {
                                                if (g.nodes.size >= o.maxNodes) { if ("limit" !in g.notes) g.notes += "limit"; continue }
                                                child = GNode(id, n.depth + 1, hop.time); g.nodes += child; next += child
                                            }
                                            if (g.edges.any { it.from == n.id && it.to == id && hop.tx in it.hashes }) continue
                                            val same = g.edges.firstOrNull { it.from == n.id && it.to == id && it.bridge && it.asset == hop.asset }
                                            if (same != null) { same.amount += hop.amount; same.count++; same.last = maxOf(same.last, hop.time); same.hashes += hop.tx }
                                            else g.edges += GEdge(n.id, id, hop.asset, "", hop.amount, 1, hop.time, hop.time, listOf(hop.tx), hop.chain, true)
                                        }
                                    }
                                }
                                if (e.count > 3 && n.cat == "bridge" && !bulk && g.edges.any { it.from == n.id && it.bridge }) synchronized(g.notes) { g.notes += "${Fmt.short(e.from)} → ${n.label}: ${e.count} deposits, only the 3 largest were resolved." }
                            }
                        }
                    }
                }.awaitAll()
            }
            todo.removeAll { it.stop == "service" }
            if (level.any { n -> n.label.startsWith("Relay") && n.cat == "bridge" && g.edges.none { it.from == n.id && it.bridge } } && "relaykey" !in g.notes) g.notes += "relaykey"
            level.filter { n -> api(n) is FreezeCheck && (n.depth == 0 || g.edges.any { it.to == n.id && (it.contract in Chains.stable || it.asset == "USDT" || it.asset == "USDC") }) }.map { n ->
                async { gate.withPermit { val fz = api(n) as FreezeCheck; try { val f = fz.frozen(Addr.bare(n.id)); if (f.isNotEmpty()) { n.frozen = f.joinToString(", "); n.frozenUsd = fz.frozenAmount(Addr.bare(n.id), f) } } catch (_: ApiException) { } } }
            }.awaitAll()
            val sets = todo.map { n ->
                async {
                    gate.withPermit {
                        try {
                            var from = 0L; var to = 0L
                            if (o.forward) { from = n.arrival; if (o.windowDays > 0 && n.arrival > 0) to = n.arrival + o.windowDays * 86400L }
                            else { to = n.arrival; if (o.windowDays > 0 && n.arrival > 0) from = n.arrival - o.windowDays * 86400L }
                            // with no start date the most recent activity is the meaningful window, not the oldest
                            api(n)!!.transfers(Addr.bare(n.id), if (n.depth == 0) o.rootPerNode else o.perNode, o.forward && n.arrival > 0, from, to)
                        } catch (e: ApiException) { n.stop = "error"; synchronized(g.notes) { g.notes += "${Fmt.short(n.id)}: ${e.message}" }; null }
                    }
                }
            }.awaitAll()
            todo.forEachIndexed { k, n ->
                val set = sets[k] ?: return@forEachIndexed
                val ck = g.chainOf(n.id); val bare = Addr.bare(n.id)
                done++
                n.expanded = true
                if (set.truncated && n.depth == 0 && n.arrival == 0L) { if ("recent" !in g.notes) g.notes += "recent" }
                else if (set.truncated) g.notes += "${Fmt.short(n.id)}: only part of its history was read (it is very active)."
                val moves = set.items.filter { (if (o.forward) it.from == bare else it.to == bare) && it.from != it.to && !Tokens.suspicious(it) && (o.allTokens || o.asset.isNotEmpty() || Tokens.major(it)) && (o.asset.isEmpty() || it.asset.equals(o.asset, true)) }
                val groups = moves.groupBy { Triple(if (o.forward) it.to else it.from, it.asset, it.contract) }.filter { it.key.first.isNotEmpty() }
                val totals = HashMap<Pair<String, String>, BigDecimal>()
                for ((key, v) in groups) totals[key.second to key.third] = (totals[key.second to key.third] ?: BigDecimal.ZERO) + v.map { it.amount }.total()
                fun value(key: Triple<String, String, String>, sum: BigDecimal) = value(ck, key.third, sum)
                val picked = groups.map { (key, v) ->
                    val sum = v.map { it.amount }.total(); val tot = totals[key.second to key.third]!!
                    Triple(key, v, if (tot.signum() > 0) sum.toDouble() / tot.toDouble() * 100 else 0.0)
                }.filter { it.third >= o.minSharePct || value(it.first, it.second.map { x -> x.amount }.total()) >= BigDecimal(100000) }.sortedWith(compareByDescending<Triple<Triple<String, String, String>, List<Xfer>, Double>> { value(it.first, it.second.map { x -> x.amount }.total()) }.thenByDescending { it.third }.thenByDescending { it.second.map { x -> x.amount }.total() }).take(o.branch)
                if (picked.isEmpty()) { n.stop = if (groups.isEmpty()) "end" else "small"; return@forEachIndexed }
                // swap signature: in the same transaction the address gives one asset and gets a different one back
                val swapHashes = if (!o.forward) emptySet() else set.items.filter { !Tokens.suspicious(it) }.groupBy { it.hash }.filter { (_, h) -> h.any { x -> x.to == bare && h.any { y -> y.from == bare && (y.asset != x.asset || y.contract != x.contract) } } }.keys
                for ((key, v, _) in picked) {
                    val cp = g.idFor(ck, key.first)
                    var child = g.node(cp)
                    val t = if (o.forward) v.minOf { it.time } else v.maxOf { it.time }
                    if (child == null) {
                        if (g.nodes.size >= o.maxNodes) { if ("limit" !in g.notes) g.notes += "limit"; continue }
                        child = GNode(cp, n.depth + 1, t)
                        Labels.find(cp)?.let { child.label = it.name; child.cat = it.cat }
                        child.swap = v.count { it.hash in swapHashes } * 2 > v.size
                        g.nodes += child; next += child
                    } else if (!child.expanded && child.stop.isEmpty() && child.depth > n.depth) {
                        child.arrival = if (o.forward) minOf(child.arrival, t) else maxOf(child.arrival, t)
                    }
                    val ef = if (o.forward) n.id else cp; val et = if (o.forward) cp else n.id
                    if (g.edges.any { it.from == ef && it.to == et && it.asset == key.second && it.contract == key.third }) continue
                    g.edges += GEdge(ef, et, key.second, key.third, v.map { it.amount }.total(), v.size, v.minOf { it.time }, v.maxOf { it.time }, v.sortedByDescending { it.amount }.take(5).map { it.hash }, if (ck == g.chain) "" else ck)
                }
            }
            level = next
        }
        layout(g)
        bubbles(g)
    }

    /** Bubble map: radius follows the value that reached the address; children sit in rows around their parent, then overlaps are pushed apart. */
    fun bubbles(g: Graph) {
        val max = g.nodes.maxOf { maxOf(g.usdIn(it), g.usdOut(it)) }.max(BigDecimal.ONE).toDouble()
        g.nodes.forEach { it.r = 16 + 54 * Math.sqrt(maxOf(g.usdIn(it), g.usdOut(it)).toDouble() / max) }
        val kids = g.nodes.associate { it.id to ArrayList<GNode>() }
        for (n in g.nodes.filter { it.depth > 0 }.sortedBy { it.depth }) {
            val p = g.edges.filter { (if (g.forward) it.to else it.from) == n.id }.mapNotNull { g.node(if (g.forward) it.from else it.to) }.firstOrNull { it.depth < n.depth }
            if (p != null) kids[p.id]!!.add(n)
        }
        val placed = HashSet<String>()
        fun place(n: GNode, x: Double, y: Double, a0: Double, a1: Double) {
            n.bx = x; n.by = y; placed += n.id
            val ch = kids[n.id]!!.sortedByDescending { it.r }; if (ch.isEmpty()) return
            val span = a1 - a0; var ring = n.r + ch[0].r + 14; var used = 0.0; var rowMax = ch[0].r
            for (c in ch) {
                var need = (2 * c.r + 8) / ring
                if (used + need > span && used > 0) { ring += rowMax + c.r + 10; used = 0.0; rowMax = c.r; need = (2 * c.r + 8) / ring }
                val a = a0 + used + need / 2; used += need
                val sub = minOf(1.3, maxOf(need, 0.5))
                place(c, x + ring * Math.cos(a), y + ring * Math.sin(a), a - sub / 2, a + sub / 2)
            }
        }
        val roots = g.nodes.filter { it.depth == 0 }
        roots.forEachIndexed { i, r -> if (roots.size == 1) place(r, 0.0, 0.0, 0.0, 2 * Math.PI) else place(r, i * 900.0, 0.0, -1.2, 1.2) }
        g.nodes.filter { it.id !in placed }.forEach { it.bx = 0.0; it.by = 400.0 + it.depth * 120 }
        for (iter in 0 until 250) {
            var moved = false
            for (i in g.nodes.indices) for (j in i + 1 until g.nodes.size) {
                val a = g.nodes[i]; val b = g.nodes[j]
                var dx = b.bx - a.bx; var dy = b.by - a.by; var d = Math.sqrt(dx * dx + dy * dy); val min = a.r + b.r + 8
                if (d >= min) continue
                if (d < 0.01) { dx = 1.0; dy = 0.3 * (j - i); d = 1.0 }
                val push = (min - d) / 2; val ux = dx / d; val uy = dy / d
                when {
                    a.depth == 0 -> { b.bx += ux * push * 2; b.by += uy * push * 2 }
                    b.depth == 0 -> { a.bx -= ux * push * 2; a.by -= uy * push * 2 }
                    else -> { a.bx -= ux * push; a.by -= uy * push; b.bx += ux * push; b.by += uy * push }
                }
                moved = true
            }
            if (!moved) break
        }
    }

    /** Column per hop; inside a column nodes follow the order of their parents. */
    fun layout(g: Graph) {
        val dx = 290.0; val dy = 78.0
        val byDepth = g.nodes.groupBy { it.depth }.toSortedMap()
        val tallest = byDepth.values.maxOfOrNull { it.size } ?: 1
        for ((d, col) in byDepth) {
            val ordered = if (d == 0) col else col.sortedBy { n ->
                val parents = g.edges.filter { (if (g.forward) it.to else it.from) == n.id }.mapNotNull { g.node(if (g.forward) it.from else it.to) }.filter { it.depth < n.depth }
                if (parents.isEmpty()) 0.0 else parents.map { it.y }.average()
            }
            val off = (tallest - ordered.size) * dy / 2
            ordered.forEachIndexed { i, n -> n.x = (if (g.forward) d else byDepth.size - 1 - d) * dx; n.y = off + i * dy }
        }
    }
}

class Counterparty(val address: String, val label: String, val cat: String, val asset: String, val inn: BigDecimal, val out: BigDecimal, val count: Int, val first: Long, val last: Long)
class Finding(val level: String, val title: String, val detail: String, val hash: String = "", val address: String = "")
class MatchRow(val dep: Xfer, val wd: Xfer, val candidates: Int, val delay: Long, val diffPct: Double, val confidence: String)
class LinkResult(val direct: List<Xfer>, val shared: List<Triple<String, List<String>, Int>>, val funders: List<Pair<String, List<String>>>)

/** Heuristics over an address's transfers. Each one yields leads to verify, never proof. */
object Analysis {
    fun counterparties(addr: String, xs: List<Xfer>): List<Counterparty> =
        xs.filter { it.from != it.to && (it.from == addr || it.to == addr) }.groupBy { (if (it.from == addr) it.to else it.from) to it.asset }.map { (k, v) ->
            val l = Labels.find(k.first)
            Counterparty(k.first, l?.name ?: "", l?.cat ?: "", k.second, v.filter { it.to == addr }.map { it.amount }.total(), v.filter { it.from == addr }.map { it.amount }.total(), v.size, v.minOf { it.time }, v.maxOf { it.time })
        }.sortedByDescending { it.count }

    fun firstFunder(addr: String, xs: List<Xfer>): Xfer? = xs.filter { it.to == addr && it.from != addr && it.kind != "token" }.minByOrNull { it.time }

    fun testTxs(addr: String, xs: List<Xfer>, ratio: BigDecimal = BigDecimal(20), windowSec: Long = 172800): List<Pair<Xfer, Xfer>> {
        val res = ArrayList<Pair<Xfer, Xfer>>()
        for ((_, grp) in xs.filter { it.from == addr && it.to != addr }.groupBy { Triple(it.to, it.asset, it.contract) }) {
            val list = grp.sortedBy { it.time }
            if (list.size > 6) continue   // many payments to the same destination are routine, not a test
            for (j in 1 until list.size) {
                if (list[j].time - list[0].time > windowSec) break
                if (list[0].amount.signum() > 0 && list[j].amount >= list[0].amount * ratio) { res += list[0] to list[j]; break }
            }
        }
        return res.sortedBy { it.first.time }
    }

    fun passThrough(addr: String, xs: List<Xfer>, maxSec: Long = 1800, keep: BigDecimal = BigDecimal("0.9")): List<Pair<Xfer, Xfer>> {
        val res = ArrayList<Pair<Xfer, Xfer>>()
        val outs = xs.filter { it.from == addr && it.to != addr }.sortedBy { it.time }; val used = HashSet<Xfer>()
        for (i in xs.filter { it.to == addr && it.from != addr }.sortedBy { it.time }) {
            val o = outs.firstOrNull { it !in used && it.asset == i.asset && it.contract == i.contract && it.time >= i.time && it.time - i.time <= maxSec && it.amount <= i.amount && it.amount >= i.amount * keep } ?: continue
            used += o; res += i to o
        }
        return res
    }

    fun hours(addr: String, xs: List<Xfer>): IntArray {
        val h = IntArray(24)
        xs.filter { it.from == addr }.map { it.hash to it.time }.distinct().forEach { h[(it.second % 86400 / 3600).toInt()]++ }
        return h
    }

    fun findings(addr: String, info: AddrInfo?, set: TransferSet, firstFunding: Xfer? = null, f: (String, Array<out Any>) -> String = { k, a -> a.foldIndexed(k) { i, s, v -> s.replace("{$i}", v.toString()) } }): List<Finding> {
        val out = ArrayList<Finding>(); val xs = Tokens.clean(set.items); val spam = set.items.size - xs.size
        fun fmt(k: String, vararg a: Any) = f(k, a)
        val own = Labels.find(addr)
        if (own?.cat == "threat") out += Finding("high", "Address is flagged", own.name)
        if (info?.isScam == true) out += Finding("high", "Explorer marks this address as scam", info.tags.joinToString(", "))
        (firstFunding ?: if (set.truncated) null else firstFunder(addr, xs))?.let { out += Finding("info", "First funding seen", "${Fmt.amt(it.amount)} ${it.asset} ← ${Labels.show(it.from)} · ${Fmt.time(it.time)}", it.hash, it.from) }
        if (spam > 0) out += Finding("medium", "Spam or look-alike tokens were set aside", fmt("{0} transfers of fake or advertising tokens (for example symbols imitating ETH or USDT) are ignored in the checks below. Such tokens are bait: never copy an address from them.", spam))
        fun value(x: Xfer): BigDecimal = if (x.contract in Chains.stable) x.amount else if (x.kind != "token" && info != null && info.usd.signum() > 0) x.amount * info.usd else BigDecimal.ZERO
        val big = xs.filter { it.from == addr && it.to != addr && value(it) >= BigDecimal(100000) }.sortedByDescending { value(it) }
        if (big.isNotEmpty()) out += Finding("medium", "Large outgoing transfers", fmt("{0} transfers above $100,000 at today's price; the largest: {1} {2} → {3} · {4}", big.size, Fmt.amt(big[0].amount), big[0].asset, Labels.show(big[0].to), Fmt.time(big[0].time)), big[0].hash, big[0].to)
        val cps = counterparties(addr, xs)
        for (cat in listOf("mixer", "bridge", "exchange", "threat")) {
            val hit = cps.filter { it.cat == cat }; if (hit.isEmpty()) continue
            if (cat == "mixer") {
                val got = hit.filter { it.inn.signum() > 0 }; val gave = hit.filter { it.out.signum() > 0 }
                if (got.isNotEmpty()) out += Finding("high", "Received funds from a mixer", got.groupBy { it.asset }.map { Fmt.amt(it.value.map { c -> c.inn }.total()) + " " + it.key }.joinToString("; ") + " ← " + got.map { it.label }.distinct().take(4).joinToString(", ") + " · " + Fmt.day(got.minOf { it.first }) + ".." + Fmt.day(got.maxOf { it.last }), address = got[0].address)
                if (gave.isNotEmpty()) out += Finding("high", "Sent funds into a mixer", gave.groupBy { it.asset }.map { Fmt.amt(it.value.map { c -> c.out }.total()) + " " + it.key }.joinToString("; ") + " → " + gave.map { it.label }.distinct().take(4).joinToString(", ") + " · " + Fmt.day(gave.minOf { it.first }) + ".." + Fmt.day(gave.maxOf { it.last }), address = gave[0].address)
                continue
            }
            val title = when (cat) { "mixer" -> "Interacted with a mixer / privacy tool"; "bridge" -> "Used a cross-chain bridge"; "exchange" -> "Sent to / received from an exchange"; else -> "Transacted with a flagged address" }
            out += Finding(if (cat == "mixer" || cat == "threat") "high" else "medium", title, hit.map { it.label }.distinct().take(4).joinToString(", ") + " · ${hit.sumOf { it.count }} tx", address = hit[0].address)
        }
        for (t in testTxs(addr, xs).take(5))
            out += Finding("medium", "Possible test transaction", fmt("{0} {1} → {2}, then {3} {4} after {5}", Fmt.amt(t.first.amount), t.first.asset, Labels.show(t.first.to), Fmt.amt(t.second.amount), t.second.asset, Fmt.dur(t.second.time - t.first.time)), t.first.hash, t.first.to)
        val pt = passThrough(addr, xs)
        if (pt.size >= 2) out += Finding("medium", "Pass-through behaviour", fmt("{0} deposits were forwarded within 30 minutes (median delay {1})", pt.size, Fmt.dur(pt.map { it.second.time - it.first.time }.sorted()[pt.size / 2])), pt[0].second.hash)
        val outCp = xs.filter { it.from == addr }.map { it.to }.distinct().size; val inCp = xs.filter { it.to == addr }.map { it.from }.distinct().size
        if (outCp >= 20 && outCp >= inCp * 4) out += Finding("medium", "Fan-out pattern", fmt("sends to {0} addresses, receives from {1}", outCp, inCp))
        if (inCp >= 20 && inCp >= outCp * 4) out += Finding("medium", "Fan-in (collection) pattern", fmt("receives from {0} addresses, sends to {1}", inCp, outCp))
        if (set.truncated) out += Finding("info", "Partial history", fmt("Only the most recent transfers were loaded; results marked * may change with the full history."))
        if (out.isEmpty()) out += Finding("info", "Nothing notable in the loaded transfers", "")
        return out
    }

    private fun distinctive(a: BigDecimal): Boolean {
        if (a.signum() <= 0) return false
        return a.stripTrailingZeros().toPlainString().replace(".", "").trimStart('0').trimEnd('0').length >= 3
    }

    fun match(deposits: List<Xfer>, withdrawals: List<Xfer>, windowSec: Long, tolPct: Double, sameAsset: Boolean = true): List<MatchRow> {
        val res = ArrayList<MatchRow>(); val wds = withdrawals.sortedBy { it.time }
        for (d in deposits.sortedBy { it.time }) {
            val lo = d.amount * BigDecimal(1 - tolPct / 100); val hi = d.amount * BigDecimal("1.0005")
            val cands = wds.filter { w -> w.time >= d.time && w.time - d.time <= windowSec && w.amount >= lo && w.amount <= hi && (!sameAsset || w.asset.equals(d.asset, true)) && w.hash != d.hash }
            for (w in cands.sortedWith(compareBy<Xfer> { (d.amount - it.amount).abs() }.thenBy { it.time }).take(5)) {
                val conf = if (cands.size == 1 && distinctive(d.amount)) "high" else if (cands.size <= 3 && distinctive(d.amount)) "medium" else "low"
                res += MatchRow(d, w, cands.size, w.time - d.time, if (d.amount.signum() == 0) 0.0 else (d.amount - w.amount).toDouble() / d.amount.toDouble() * 100, conf)
            }
        }
        return res
    }

    fun link(data: Map<String, List<Xfer>>, skipServices: Boolean = true): LinkResult {
        val set = data.keys; val seen = HashSet<String>(); val direct = ArrayList<Xfer>()
        for ((_, xs) in data) for (x in xs) if (x.from in set && x.to in set && x.from != x.to && seen.add(x.hash + x.from + x.to + x.asset)) direct += x
        val cp = HashMap<String, HashMap<String, Int>>()
        for ((k, xs) in data) for (x in xs) {
            val other = if (x.from == k) x.to else x.from
            if (other.isEmpty() || other in set) continue
            val d = cp.getOrPut(other) { HashMap() }; d[k] = (d[k] ?: 0) + 1
        }
        val shared = cp.filter { it.value.size >= 2 }.filter { (a, _) -> val l = Labels.find(a); !(skipServices && l != null && (Labels.isService(l.cat) || l.cat == "contract" || l.cat == "other")) }
            .map { Triple(it.key, it.value.keys.toList(), it.value.values.sum()) }.sortedWith(compareByDescending<Triple<String, List<String>, Int>> { it.second.size }.thenByDescending { it.third })
        val funders = data.mapNotNull { (a, xs) -> firstFunder(a, xs)?.let { it.from to a } }.groupBy({ it.first }, { it.second }).filter { it.value.size >= 2 }.map { it.key to it.value }
        return LinkResult(direct, shared, funders)
    }
}

/** Invented, offline scenario for the demo and the tests. None of these addresses exist. */
object Demo : ChainApi, FreezeCheck {
    val frozenWallet = "0x" + "0".repeat(37) + "fd0"
    override suspend fun frozen(address: String): List<String> = if (address == frozenWallet) listOf("USDT") else emptyList()
    override suspend fun frozenAmount(address: String, tokens: List<String>): BigDecimal = if (address == frozenWallet) BigDecimal(800) else BigDecimal.ZERO
    private fun a(c: Char) = "0x" + c.toString().repeat(38) + "d0"
    val victim = a('1'); val hacker = a('2'); val hop1 = a('3'); val hop2 = a('4'); val hop3 = a('5'); val exchange = a('6'); val mixer = a('7'); val bridge = a('8')
    val funder = a('9'); val hacker2 = a('a'); val payout = a('b'); val dust = a('c'); val other = a('e')
    const val T0 = 1750000000L
    val data: List<Xfer> = build()
    private val names = mapOf(exchange to Triple("Demo Exchange: Hot Wallet", "Exchange", false), mixer to Triple("Demo Mixer", "Mixer", true), bridge to Triple("Demo Bridge", "Bridge", true), victim to Triple("Victim treasury (demo)", "", false))

    private fun build(): List<Xfer> {
        val l = ArrayList<Xfer>(); var n = 0
        fun x(f: String, t: String, amt: String, min: Long, asset: String = "ETH") {
            n++; l += Xfer("demo", "0x" + "%04x".format(n) + "f".repeat(58) + "d0", f, t, asset, BigDecimal(amt), T0 + min * 60, if (asset == "ETH") "" else a('f'), if (asset == "ETH") "native" else "token", 1000 + min)
        }
        x(funder, hacker, "0.5", -3000); x(funder, hacker2, "0.5", -2990)
        x(victim, hacker, "1000", 0)
        x(hacker, hop1, "0.01", 20); x(hacker, hop1, "400", 31)
        x(hacker, hop2, "350", 45)
        x(hacker, mixer, "187.35", 70)
        x(hacker, dust, "5", 90)
        x(hop1, hop3, "399.6", 43)
        x(hop3, exchange, "399.2", 58)
        x(hop2, bridge, "349.5", 66)
        x(mixer, payout, "187.11", 250); x(mixer, other, "12", 260); x(other, mixer, "12.2", 100)
        x(hacker2, hop1, "20", 500); x(hop1, hop3, "19.9", 512)
        x(hacker, hop2, "1500", 120, "USDT"); x(hop2, exchange, "1499", 140, "USDT")
        x(hacker, frozenWallet, "800", 125, "USDT")
        return l
    }

    override suspend fun info(address: String): AddrInfo {
        val a = address.lowercase()
        val i = AddrInfo("demo", a); i.usd = BigDecimal(2500)
        names[a]?.let { i.name = it.first; if (it.second.isNotEmpty()) i.tags += it.second; i.isContract = it.third }
        i.balance = (data.filter { it.asset == "ETH" && it.to == a }.map { it.amount }.total() - data.filter { it.asset == "ETH" && it.from == a }.map { it.amount }.total()).max(BigDecimal.ZERO)
        i.txCount = if (a == exchange) 250000 else data.count { it.from == a || it.to == a }.toLong()
        return i
    }

    override suspend fun transfers(address: String, max: Int, asc: Boolean, fromTime: Long, toTime: Long): TransferSet {
        val a = address.lowercase()
        val q = data.filter { (it.from == a || it.to == a) && (fromTime == 0L || it.time >= fromTime) && (toTime == 0L || it.time <= toTime) }
        return TransferSet(if (asc) q.sortedBy { it.time } else q.sortedByDescending { it.time })
    }

    override suspend fun tx(hash: String): List<Xfer> = data.filter { it.hash == hash }.ifEmpty { throw ApiException("Transaction not found in the demo data") }
}
