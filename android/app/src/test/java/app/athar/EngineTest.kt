package app.athar

import kotlinx.coroutines.runBlocking
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.math.BigDecimal

class EngineTest {
    private fun bd(s: String) = BigDecimal(s)
    private fun eq(a: BigDecimal, b: String) = assertTrue("$a != $b", a.compareTo(bd(b)) == 0)

    @Test fun units() {
        eq(Units.from("1000000000000000000", 18), "1"); eq(Units.from("168000000", 6), "168")
        assertTrue(Units.from("401346768858404671846374", 18) > bd("401346"))
        eq(Units.fromHex("0xde0b6b3a7640000", 18), "1"); assertEquals(16, Units.hexLong("0x10")); eq(Units.sats(5772038901), "57.72038901")
    }

    @Test fun addresses() {
        assertEquals("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", Addr.tronFromHex("0xa614f803b6fd780986a42c78ec9c7f77e6ded13c"))
        assertEquals("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", Addr.tronFromHex("41a614f803b6fd780986a42c78ec9c7f77e6ded13c"))
        assertEquals("address" to "evm", Addr.detect("0x47666Fab8bd0Ac7003bce3f5C3585383F09486E2"))
        assertEquals("tx" to "evm", Addr.detect("0x25800d105db4f21908d646a7a3db849343737c5fba0bc5701f782bf0e75217c9"))
        assertEquals("address" to "btc", Addr.detect("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa"))
        assertEquals("address" to "btc", Addr.detect("bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh"))
        assertEquals("address" to "tron", Addr.detect("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t"))
        assertEquals("unknown", Addr.detect("hello").first); assertEquals("unknown", Addr.detect("0x123").first)
    }

    @Test fun formatting() {
        assertEquals("1,234.5", Fmt.amt(bd("1234.5"))); assertEquals("0.00012", Fmt.amt(bd("0.00012"))); assertEquals("400.01", Fmt.amt(bd("400.01")))
        assertEquals(1740096000L, Fmt.parseDate("2025-02-21")); assertEquals(1740147311L, Fmt.parseDate("2025-02-21T14:15:11.000000Z"))
        assertEquals("2025-02-21 14:15:11", Fmt.time(1740147311)); assertEquals("0x47666f…9486e2", Fmt.short("0x47666fab8bd0ac7003bce3f5c3585383f09486e2"))
        assertEquals("3h 0m", Fmt.dur(10800))
    }

    @Test fun labels() {
        assertEquals("exchange", Labels.find("0x28C6c06298d514Db089934071355E5743bf21d60")?.cat)
        assertEquals("threat", Labels.catFromText("Bybit Exploiter", false, false)); assertEquals("exchange", Labels.catFromText("Bybit: Hot Wallet 1 Exchange", false, false))
        assertEquals("mixer", Labels.catFromText("Tornado.Cash 10 ETH", false, true)); assertEquals("contract", Labels.catFromText("", false, true)); assertEquals("", Labels.catFromText("", false, false))
    }

    @Test fun traceForward() = runBlocking {
        val g = Tracer.run(TraceOptions("demo", Demo.hacker, maxHops = 4, branch = 5))
        assertEquals("service", g.node(Demo.exchange)?.stop); assertEquals("exchange", g.node(Demo.exchange)?.cat); assertEquals(2, g.node(Demo.hop3)?.depth)
        assertEquals("service", g.node(Demo.mixer)?.stop); assertEquals("service", g.node(Demo.bridge)?.stop)
        assertNull(g.node(Demo.payout)); assertNull(g.node(Demo.dust)); assertNull(g.node(Demo.funder)); assertNull(g.node(Demo.victim))
        val e = g.edges.first { it.from == Demo.hacker && it.to == Demo.hop1 }
        eq(e.amount, "400.01"); assertEquals(2, e.count)
        eq(g.edges.first { it.from == Demo.hop1 && it.to == Demo.hop3 }.amount, "419.5")
        eq(g.usd(g.edges.first { it.to == Demo.exchange && it.asset == "ETH" })!!, "998000")
        assertTrue(g.nodes.groupBy { it.x to it.y }.all { it.value.size == 1 })
        val b = Graph.of(JSONObject(g.json().toString()))
        assertEquals(g.nodes.size, b.nodes.size); assertEquals(g.edges.size, b.edges.size); assertEquals("service", b.node(Demo.exchange)?.stop)
        eq(b.edges.map { it.amount }.total(), g.edges.map { it.amount }.total().toPlainString())
    }

    @Test fun traceOptions() = runBlocking {
        val u = Tracer.run(TraceOptions("demo", Demo.hacker, asset = "USDT"))
        assertTrue(u.edges.all { it.asset == "USDT" }); assertNotNull(u.node(Demo.exchange)); assertNull(u.node(Demo.hop1))
        val h = Tracer.run(TraceOptions("demo", Demo.hacker, maxHops = 1))
        assertEquals(1, h.nodes.maxOf { it.depth }); assertEquals("hops", h.node(Demo.hop1)?.stop)
        val o = TraceOptions("demo", Demo.hacker, maxHops = 1)
        val m = Tracer.run(o); Tracer.more(m, m.node(Demo.hop1)!!, o, 2); assertNotNull(m.node(Demo.exchange))
        val n = Tracer.run(TraceOptions("demo", Demo.hacker, maxNodes = 3)); assertEquals(3, n.nodes.size); assertTrue("limit" in n.notes)
        val back = Tracer.run(TraceOptions("demo", Demo.hacker, forward = false, minSharePct = 0.01))
        assertNotNull(back.node(Demo.victim)); assertNotNull(back.node(Demo.funder))
        assertTrue(back.edges.all { back.node(it.from)!!.x < back.node(it.to)!!.x })
    }

    @Test fun heuristics() = runBlocking {
        val set = Demo.transfers(Demo.hacker, 500, false); val xs = set.items
        assertEquals(Demo.funder, Analysis.firstFunder(Demo.hacker, xs)?.from)
        val t = Analysis.testTxs(Demo.hacker, xs); assertEquals(1, t.size); eq(t[0].first.amount, "0.01"); eq(t[0].second.amount, "400")
        fun x(f: String, to: String, amt: String, time: Long, h: String = "h$time", asset: String = "ETH") = Xfer("t", h, f, to, asset, bd(amt), time)
        assertEquals(0, Analysis.testTxs("a", listOf(x("a", "b", "10", 1), x("a", "b", "12", 50))).size)
        assertEquals(0, Analysis.testTxs("a", listOf(x("a", "b", "0.01", 1), x("a", "b", "100", 400000))).size)
        val p = Analysis.passThrough(Demo.hop1, Demo.transfers(Demo.hop1, 500, false).items); assertEquals(2, p.size)
        val c = Analysis.counterparties(Demo.hacker, xs); eq(c.first { it.address == Demo.hop1 }.out, "400.01")
        Labels.learn(Demo.info(Demo.mixer))
        val f = Analysis.findings(Demo.hacker, Demo.info(Demo.hacker), set)
        assertTrue(f.any { it.title.contains("mixer") }); assertTrue(f.any { it.title.contains("test") && it.detail.contains("then 400 ETH after 11m 0s") }); assertTrue(f.any { it.title.contains("First funding") })
        assertEquals(xs.count { it.from == Demo.hacker }, Analysis.hours(Demo.hacker, xs).sum())

        val deps = xs.filter { it.from == Demo.hacker && it.to == Demo.mixer }
        val wds = Demo.transfers(Demo.mixer, 500, true).items.filter { it.from == Demo.mixer }
        val m = Analysis.match(deps, wds, 86400, 1.0); assertEquals(1, m.size); assertEquals(Demo.payout, m[0].wd.to); assertEquals("high", m[0].confidence); assertEquals(10800L, m[0].delay)
        val weak = Analysis.match(listOf(x("s", "m", "10", 100, "d")), (0 until 5).map { x("m", "r$it", "10", 200L + it, "w$it") }, 3600, 1.0)
        assertEquals(5, weak.size); assertTrue(weak.all { it.confidence == "low" })
        val one = Analysis.match(listOf(x("s", "m", "12.345", 100, "d")), listOf(x("m", "late", "12.34", 100000, "1"), x("m", "low", "11", 200, "2"), x("m", "usdt", "12.34", 200, "3", "USDT"), x("m", "before", "12.34", 50, "4"), x("m", "good", "12.30", 300, "5")), 3600, 1.0)
        assertEquals(1, one.size); assertEquals("good", one[0].wd.to)

        val r = Analysis.link(listOf(Demo.hacker, Demo.hacker2).associateWith { Demo.transfers(it, 500, false).items })
        assertEquals(1, r.funders.size); assertEquals(Demo.funder, r.funders[0].first); assertTrue(r.shared.any { it.first == Demo.hop1 }); assertEquals(0, r.direct.size)
        assertEquals(2, Analysis.link(listOf(Demo.hacker, Demo.hop1).associateWith { Demo.transfers(it, 500, false).items }).direct.size)
        Labels.learn(Demo.info(Demo.exchange))
        val d2 = listOf(Demo.hop3, Demo.hop2).associateWith { Demo.transfers(it, 500, false).items }
        assertTrue(Analysis.link(d2, true).shared.none { it.first == Demo.exchange }); assertTrue(Analysis.link(d2, false).shared.any { it.first == Demo.exchange })
    }

    @Test fun statusAndNetworks() = runBlocking {
        val g = Tracer.run(TraceOptions("demo", Demo.hacker, maxHops = 4, branch = 5))
        val fz = g.node(Demo.frozenWallet)!!
        assertEquals("USDT", fz.frozen); assertEquals("frozen", g.status(fz)); eq(fz.frozenUsd, "800")
        assertEquals("exchange", g.status(g.node(Demo.exchange)!!)); assertEquals("mixer", g.status(g.node(Demo.mixer)!!)); assertEquals("start", g.status(g.node(Demo.hacker)!!)); assertEquals("passed", g.status(g.node(Demo.hop1)!!))
        val v = Tracer.run(TraceOptions("demo", Demo.mixer, maxHops = 2, minSharePct = 0.01))
        val p = v.node(Demo.payout)!!; assertEquals("end", p.stop); assertEquals("holding", v.status(p)); assertTrue(!p.expanded)
        val two = Tracer.run(TraceOptions("demo", Demo.hacker + ", " + Demo.hacker2 + "\nnope", maxHops = 3))
        assertEquals(2, two.nodes.count { it.depth == 0 }); assertTrue(two.edges.any { it.from == Demo.hacker2 && it.to == Demo.hop1 })
        for (a in g.nodes.indices) for (b in a + 1 until g.nodes.size) { val x = g.nodes[a]; val y = g.nodes[b]; assertTrue(Math.hypot(x.bx - y.bx, x.by - y.by) >= x.r + y.r - 0.5) }
        val gx = Graph("eth", "0xabc", true); gx.usdRate = bd("2000"); gx.rates["btc"] = bd("60000")
        assertEquals("btc", gx.chainOf("btc:bc1qxyz")); assertEquals("eth", gx.chainOf("0xabc")); assertEquals("tron:Tabc", gx.idFor("tron", "Tabc")); assertEquals("0xabc", gx.idFor("eth", "0xabc"))
        assertEquals("bc1qa5n7qryprtttd65nm4tjyq4gkvnglf79t92dxr", Addr.bare("btc:bc1qa5n7qryprtttd65nm4tjyq4gkvnglf79t92dxr")); assertEquals("btc:bc1qa5n7…t92dxr", Fmt.short("btc:bc1qa5n7qryprtttd65nm4tjyq4gkvnglf79t92dxr"))
        eq(gx.usd(GEdge("a", "btc:b", "BTC", "", bd("2"), 1, 1, 1, emptyList(), "btc", true))!!, "120000"); eq(gx.usd(GEdge("a", "b", "ETH", "", bd("1"), 1, 1, 1, emptyList()))!!, "2000")
        assertEquals("a614f803b6fd780986a42c78ec9c7f77e6ded13c", Addr.tronToHex("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t"))
        val f = Analysis.findings(Demo.payout, Demo.info(Demo.payout), Demo.transfers(Demo.payout, 500, false))
        assertTrue(f.any { it.title == "Received funds from a mixer" && it.detail.contains("187.11 ETH") })
    }

    @Test fun report() = runBlocking {
        val g = Tracer.run(TraceOptions("demo", Demo.hacker))
        val h = Report.traceHtml(g, "", true) { it }
        assertTrue(h.contains("<svg")); assertTrue(h.contains(Demo.exchange)); assertTrue(h.contains("dir='rtl'")); assertTrue(h.endsWith("</html>"))
    }

    /** Live explorers; run with -Dathar.live=1 */
    @Test fun live() = runBlocking {
        if (System.getProperty("athar.live") != "1") return@runBlocking
        val bybit = "0x47666fab8bd0ac7003bce3f5c3585383f09486e2"; val eth = Chains.get("eth").api
        assertTrue(eth.info(bybit).txCount > 100)
        val s = eth.transfers(bybit, 100, true, 1740148187, 1740148187 + 3600); assertTrue(s.items.isNotEmpty() && s.items.all { it.time in 1740148187..1740148187 + 3600 })
        assertTrue(eth.tx("0x25800d105db4f21908d646a7a3db849343737c5fba0bc5701f782bf0e75217c9").any { it.asset == "USDT" && it.amount.compareTo(bd("90")) == 0 && it.to == bybit })
        val btc = Chains.get("btc").api; val gen = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa"
        assertTrue(btc.info(gen).txCount > 1000); val bs = btc.transfers(gen, 25, false); assertTrue(bs.items.isNotEmpty() && bs.items.all { it.to == gen }); assertTrue(btc.tx(bs.items[0].hash).isNotEmpty())
        val tron = Chains.get("tron").api; val ts = tron.transfers("TRTHH8aLyckjbffhQSWykaHceBtJAAmBUA", 200, false)
        val tok = ts.items.first { it.kind == "token" }; assertTrue(tron.tx(tok.hash).any { it.amount.compareTo(tok.amount) == 0 })
        assertTrue(Chains.get("avax").api.transfers("0x28c6c06298d514db089934071355e5743bf21d60", 20, false).items.isNotEmpty())
        val t = Tracer.run(TraceOptions("eth", bybit, maxHops = 2, branch = 3, maxNodes = 12, fromTime = 1740096000, allTokens = true)); assertTrue(t.nodes.size >= 4)
        val fzc = eth as FreezeCheck; assertTrue(fzc.frozen("0x5227f603c1ee34ce3c06efe6477003bdfccb2f8c").containsAll(listOf("USDT", "USDC")))
        val hop = Bridges.resolve("eth", "0x0188a69f107f98afee301751ba2140826faf406db4184f37422bc7914fe5e283"); assertTrue(hop.any { it.service == "THORChain" && it.chain == "btc" && it.address.startsWith("bc1") })
        val rl = Bridges.resolve("eth", "0x202ac2f8bb88400726b41a022604080925e7eabc79a779422163d3985bad1615"); assertTrue(rl.isEmpty() || rl.any { it.service == "Relay" && it.chain == "tron" && !it.outbound })   // empty when Relay's keyless endpoint refuses
        val th = Tracer.run(TraceOptions("eth", "0x2a828d0b500d93abf98f135fa5d750634c062029", maxHops = 2, branch = 6, maxNodes = 30, fromTime = 1793318400))
        assertTrue(th.nodes.any { it.id.startsWith("btc:bc1qa5n7") }); assertTrue(th.edges.any { it.bridge && it.asset == "BTC" && it.amount > bd("50") })
    }
}
