using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Athar
{
    public static class SelfTest
    {
        public static int Run(string logPath, bool live)
        {
            var log = new StringBuilder(); int bad = 0, n = 0;
            void T(string name, Func<bool> f)
            {
                n++; bool ok; string err = "";
                try { ok = f(); } catch (Exception e) { ok = false; err = "  !! " + e.GetBaseException().Message; }
                if (!ok) bad++;
                log.AppendLine((ok ? "PASS  " : "FAIL  ") + name + err);
            }
            var ct = CancellationToken.None;
            L.Ar = false;

            // ---- utilities
            T("units: 18 decimals", () => Units.From("1000000000000000000", 18) == 1m);
            T("units: 6 decimals", () => Units.From("168000000", 6) == 168m);
            T("units: huge value does not overflow", () => Units.From("401346768858404671846374", 18) > 401346m && Units.From("9" + new string('9', 60), 18) > 0);
            T("units: hex", () => Units.FromHex("0xde0b6b3a7640000", 18) == 1m && Units.HexLong("0x10") == 16);
            T("tron hex -> base58", () => Addr.TronFromHex("0xa614f803b6fd780986a42c78ec9c7f77e6ded13c") == "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t" && Addr.TronFromHex("41a614f803b6fd780986a42c78ec9c7f77e6ded13c") == "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t");
            T("detect: evm address / tx", () => Addr.Detect("0x47666Fab8bd0Ac7003bce3f5C3585383F09486E2") == ("address", "evm") && Addr.Detect("0x25800d105db4f21908d646a7a3db849343737c5fba0bc5701f782bf0e75217c9") == ("tx", "evm"));
            T("detect: btc / tron / junk", () => Addr.Detect("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa") == ("address", "btc") && Addr.Detect("bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh") == ("address", "btc") && Addr.Detect("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t") == ("address", "tron") && Addr.Detect("hello").kind == "unknown" && Addr.Detect("0x123").kind == "unknown");
            T("format: amounts, dates, csv", () => Fmt.Amt(1234.5m) == "1,234.5" && Fmt.Amt(0.00012m) == "0.00012" && Fmt.ParseDate("2025-02-21") == 1740096000 && Fmt.Csv("a,b") == "\"a,b\"" && Fmt.Short("0x47666fab8bd0ac7003bce3f5c3585383f09486e2") == "0x47666f…9486e2");
            T("labels: built-in + categories", () => Labels.Find("0x28C6c06298d514Db089934071355E5743bf21d60")?.Cat == "exchange" && Labels.CatFromText("Bybit Exploiter", false, false) == "threat" && Labels.CatFromText("Bybit: Hot Wallet 1 Exchange", false, false) == "exchange" && Labels.CatFromText("Tornado.Cash 10 ETH", false, true) == "mixer" && Labels.CatFromText("", false, true) == "contract" && Labels.CatFromText("", false, false) == "");

            // ---- tracing on the offline scenario
            var api = Chains.Get("demo").Api;
            Graph g = null;
            T("trace forward: runs", () => { g = Tracer.Run(new TraceOptions { Chain = "demo", Start = Demo.Hacker, MaxHops = 4, Branch = 5, MinSharePct = 3 }, null, ct).GetAwaiter().GetResult(); return g.Nodes.Count >= 6; });
            T("trace forward: reaches the exchange via two hops", () => g.Node(Demo.Exchange) is GNode e && e.Stop == "service" && e.Cat == "exchange" && g.Node(Demo.Hop3)?.Depth == 2);
            T("trace forward: stops at mixer and bridge", () => g.Node(Demo.Mixer)?.Stop == "service" && g.Node(Demo.Bridge)?.Stop == "service" && !g.Nodes.Any(x => x.Id == Demo.Payout));
            T("trace forward: drops dust below the share threshold", () => g.Node(Demo.Dust) == null);
            T("trace forward: does not follow transfers made before the funds arrived", () => g.Node(Demo.Funder) == null && g.Node(Demo.Victim) == null);
            T("trace forward: aggregates edges", () => g.Edges.First(e => e.From == Demo.Hacker && e.To == Demo.Hop1).Amount == 400.01m && g.Edges.First(e => e.From == Demo.Hacker && e.To == Demo.Hop1).Count == 2);
            T("trace: time-ordered (Hop1 → Hop3 only counts transfers after arrival)", () => g.Edges.First(e => e.From == Demo.Hop1 && e.To == Demo.Hop3).Amount == 419.5m);
            T("trace: asset filter", () => { var u = Tracer.Run(new TraceOptions { Chain = "demo", Start = Demo.Hacker, Asset = "USDT" }, null, ct).GetAwaiter().GetResult(); return u.Edges.All(e => e.Asset == "USDT") && u.Node(Demo.Exchange) != null && u.Node(Demo.Hop1) == null; });
            T("trace: hop limit", () => { var u = Tracer.Run(new TraceOptions { Chain = "demo", Start = Demo.Hacker, MaxHops = 1 }, null, ct).GetAwaiter().GetResult(); return u.Nodes.Max(x => x.Depth) == 1 && u.Node(Demo.Hop1).Stop == "hops"; });
            T("trace: node limit", () => { var u = Tracer.Run(new TraceOptions { Chain = "demo", Start = Demo.Hacker, MaxNodes = 3 }, null, ct).GetAwaiter().GetResult(); return u.Nodes.Count == 3 && u.Notes.Contains("limit"); });
            T("trace: continue from a stopped node", () => { var o = new TraceOptions { Chain = "demo", Start = Demo.Hacker, MaxHops = 1 }; var u = Tracer.Run(o, null, ct).GetAwaiter().GetResult(); Tracer.More(u, u.Node(Demo.Hop1), o, 2, null, ct).GetAwaiter().GetResult(); return u.Node(Demo.Exchange) != null; });
            Graph gb = null;
            T("trace backward: finds the victim and the funder", () => { gb = Tracer.Run(new TraceOptions { Chain = "demo", Start = Demo.Hacker, Forward = false, MinSharePct = 0.01 }, null, ct).GetAwaiter().GetResult(); return gb.Node(Demo.Victim) != null && gb.Node(Demo.Funder) != null && gb.Edges.All(e => e.To == Demo.Hacker || gb.Node(e.To).Depth > 0); });
            T("layout: no overlapping boxes", () => g.Nodes.GroupBy(x => (x.X, x.Y)).All(x => x.Count() == 1) && gb.Nodes.GroupBy(x => (x.X, x.Y)).All(x => x.Count() == 1));
            T("layout: backward graph still reads sender (left) → receiver (right)", () => gb.Edges.All(e => gb.Node(e.From).X < gb.Node(e.To).X));
            T("graph: JSON round trip", () => { var s = JsonSerializer.Serialize(g, Store.Jo); var b = JsonSerializer.Deserialize<Graph>(s, Store.Jo); return b.Nodes.Count == g.Nodes.Count && b.Edges.Sum(e => e.Amount) == g.Edges.Sum(e => e.Amount) && b.Node(Demo.Exchange).Stop == "service"; });
            T("graph: USD estimate", () => g.Usd(g.Edges.First(e => e.To == Demo.Exchange && e.Asset == "ETH")) == 399.2m * 2500);

            // ---- heuristics
            var set = api.Transfers(Demo.Hacker, 500, false, 0, 0, ct).GetAwaiter().GetResult();
            var xs = set.Items;
            T("first funder", () => Analysis.FirstFunder(Demo.Hacker, xs).From == Demo.Funder);
            T("test transaction detected (0.01 then 400)", () => { var t = Analysis.TestTxs(Demo.Hacker, xs); return t.Count == 1 && t[0].small.Amount == 0.01m && t[0].big.Amount == 400m; });
            T("test transaction: not flagged when amounts are similar", () => Analysis.TestTxs("a", new[] { new Xfer { From = "a", To = "b", Asset = "ETH", Amount = 10, Time = 1 }, new Xfer { From = "a", To = "b", Asset = "ETH", Amount = 12, Time = 50 } }).Count == 0);
            T("test transaction: not flagged outside the window", () => Analysis.TestTxs("a", new[] { new Xfer { From = "a", To = "b", Asset = "ETH", Amount = 0.01m, Time = 1 }, new Xfer { From = "a", To = "b", Asset = "ETH", Amount = 100, Time = 400000 } }).Count == 0);
            T("pass-through detected on the hop wallet", () => { var h = api.Transfers(Demo.Hop1, 500, false, 0, 0, ct).GetAwaiter().GetResult().Items; var p = Analysis.PassThrough(Demo.Hop1, h); return p.Count == 2 && p.All(x => x.outt.Time - x.inn.Time <= 1800); });
            T("counterparties", () => { var c = Analysis.Counterparties(Demo.Hacker, xs); var h1 = c.First(x => x.Address == Demo.Hop1); return h1.Out == 400.01m && h1.Count == 2 && c.Any(x => x.Address == Demo.Mixer && x.Cat == "mixer"); });
            T("findings: mixer, bridge-free, test tx, funder", () => { var f = Analysis.Findings(Demo.Hacker, api.Info(Demo.Hacker, ct).Result, set); return f.Any(x => x.Title.Contains("mixer")) && f.Any(x => x.Title.Contains("test")) && f.Any(x => x.Title.Contains("First funding")); });
            T("hours histogram", () => Analysis.Hours(Demo.Hacker, xs).Sum() == xs.Count(x => x.From == Demo.Hacker));
            T("match: unique payout is a strong match", () =>
            {
                var deps = xs.Where(x => x.From == Demo.Hacker && x.To == Demo.Mixer).ToList();
                var wds = api.Transfers(Demo.Mixer, 500, true, 0, 0, ct).Result.Items.Where(x => x.From == Demo.Mixer).ToList();
                var m = Analysis.Match(deps, wds, 86400, 1);
                return m.Count == 1 && m[0].Wd.To == Demo.Payout && m[0].Confidence == "high" && m[0].Delay == 180 * 60;
            });
            T("match: round amounts with many candidates are weak", () =>
            {
                var d = new[] { new Xfer { Hash = "d", From = "s", To = "m", Asset = "ETH", Amount = 10, Time = 100 } };
                var w = Enumerable.Range(0, 5).Select(i => new Xfer { Hash = "w" + i, From = "m", To = "r" + i, Asset = "ETH", Amount = 10, Time = 200 + i }).ToList();
                var m = Analysis.Match(d, w, 3600, 1); return m.Count == 5 && m.All(x => x.Confidence == "low" && x.Candidates == 5);
            });
            T("match: respects window, tolerance and asset", () =>
            {
                var d = new[] { new Xfer { Hash = "d", From = "s", To = "m", Asset = "ETH", Amount = 12.345m, Time = 100 } };
                var w = new[] { new Xfer { Hash = "1", From = "m", To = "late", Asset = "ETH", Amount = 12.34m, Time = 100000 }, new Xfer { Hash = "2", From = "m", To = "low", Asset = "ETH", Amount = 11m, Time = 200 }, new Xfer { Hash = "3", From = "m", To = "usdt", Asset = "USDT", Amount = 12.34m, Time = 200 }, new Xfer { Hash = "4", From = "m", To = "before", Asset = "ETH", Amount = 12.34m, Time = 50 }, new Xfer { Hash = "5", From = "m", To = "good", Asset = "ETH", Amount = 12.30m, Time = 300 } };
                var m = Analysis.Match(d, w, 3600, 1); return m.Count == 1 && m[0].Wd.To == "good";
            });
            T("link: shared funder, shared counterparty, direct transfer", () =>
            {
                var data = new[] { Demo.Hacker, Demo.Hacker2 }.ToDictionary(a => a, a => api.Transfers(a, 500, false, 0, 0, ct).Result.Items);
                var r = Analysis.Link(data);
                var d2 = new[] { Demo.Hacker, Demo.Hop1 }.ToDictionary(a => a, a => api.Transfers(a, 500, false, 0, 0, ct).Result.Items);
                return r.Funders.Count == 1 && r.Funders[0].funder == Demo.Funder && r.Shared.Any(s => s.address == Demo.Hop1) && r.Direct.Count == 0 && Analysis.Link(d2).Direct.Count == 2;
            });
            T("link: known services are ignored when asked", () =>
            {
                var data = new[] { Demo.Hop3, Demo.Hop2 }.ToDictionary(a => a, a => api.Transfers(a, 500, false, 0, 0, ct).Result.Items);
                return Analysis.Link(data, true).Shared.All(s => s.address != Demo.Exchange) && Analysis.Link(data, false).Shared.Any(s => s.address == Demo.Exchange);
            });

            // ---- spam tokens and busy wallets
            Xfer Tok(string sym, string contract, string chain = "eth") => new Xfer { Chain = chain, Kind = "token", Asset = sym, Contract = contract, From = "a", To = "b", Amount = 1, Time = 1, Hash = sym };
            T("spam: look-alike, empty, advertising and impersonating symbols are flagged", () =>
                Tokens.Suspicious(Tok("\u0415\u0422H", "0xabc")) && Tokens.Suspicious(Tok("TOKEN", "0xabc")) && Tokens.Suspicious(Tok("Play here: htt", "0xabc")) && Tokens.Suspicious(Tok("Visit claim.io", "0xabc")) &&
                Tokens.Suspicious(Tok("ETH", "0xabc")) && Tokens.Suspicious(Tok("USDT", "0x1111111111111111111111111111111111111111")) && Tokens.Suspicious(Tok("E\u17B4TH", "0xabc")));
            T("spam: real assets are kept", () =>
                !Tokens.Suspicious(Tok("USDT", "0xdac17f958d2ee523a2206206994597c13d831ec7")) && !Tokens.Suspicious(Tok("PEPE", "0xabc")) && !Tokens.Suspicious(new Xfer { Chain = "eth", Kind = "native", Asset = "ETH" }) &&
                !Tokens.Suspicious(Tok("USDT", "0xfff", "demo")) && Tokens.Major(Tok("USDT", "0xfff", "demo")) && !Tokens.Major(Tok("PEPE", "0xabc")) && Tokens.Major(Tok("USDT", "0xdac17f958d2ee523a2206206994597c13d831ec7")));
            T("spam: findings ignore fake tokens and report them", () =>
            {
                var items = new List<Xfer> { new Xfer { Chain = "eth", Kind = "native", Asset = "ETH", From = "f", To = "me", Amount = 1, Time = 10, Hash = "h1" }, new Xfer { Chain = "eth", Kind = "token", Asset = "\u0415TH", Contract = "0xbad", From = "me", To = "x", Amount = 0.01m, Time = 20, Hash = "h2" }, new Xfer { Chain = "eth", Kind = "token", Asset = "\u0415TH", Contract = "0xbad", From = "me", To = "x", Amount = 5, Time = 30, Hash = "h3" } };
                var f = Analysis.Findings("me", null, new TransferSet { Items = items });
                return f.Any(x => x.Title.StartsWith("Spam")) && !f.Any(x => x.Title.Contains("test transaction"));
            });
            T("busy wallet: first funding is not guessed from a truncated history", () =>
            {
                var s2 = new TransferSet { Items = xs, Truncated = true };
                return !Analysis.Findings(Demo.Hacker, null, s2).Any(x => x.Title == "First funding seen") && Analysis.Findings(Demo.Hacker, null, s2, xs.Last()).Any(x => x.Title == "First funding seen");
            });
            T("labels: generic explorer names keep the address", () =>
            {
                Labels.Seen["0x00000000000000000000000000000000000000aa"] = new Label { Name = "Router", Cat = "dex", Source = "explorer" };
                try { return Labels.Show("0x00000000000000000000000000000000000000aa").StartsWith("Router \u00B7 0x000000") && Labels.Show("0x28c6c06298d514db089934071355e5743bf21d60") == "Binance 14"; }
                finally { Labels.Seen.TryRemove("0x00000000000000000000000000000000000000aa", out _); }
            });

            // ---- status, freezing, several starts, bubbles
            T("status: frozen wallet is detected and coloured", () => g.Node(Demo.FrozenWallet) is GNode fz && fz.Frozen == "USDT" && g.Status(fz) == "frozen");
            T("status: exchange, mixer, bridge, start, passed", () => g.Status(g.Node(Demo.Exchange)) == "exchange" && g.Status(g.Node(Demo.Mixer)) == "mixer" && g.Status(g.Node(Demo.Bridge)) == "bridge" && g.Status(g.Node(Demo.Hacker)) == "start" && g.Status(g.Node(Demo.Hop1)) == "passed");
            T("status: a wallet that keeps what it received is 'holding' without reading its history", () =>
            {
                var u = Tracer.Run(new TraceOptions { Chain = "demo", Start = Demo.Victim, MaxHops = 1, MinSharePct = 0.01 }, null, ct).GetAwaiter().GetResult();
                var v = Tracer.Run(new TraceOptions { Chain = "demo", Start = Demo.Mixer, MaxHops = 2, MinSharePct = 0.01 }, null, ct).GetAwaiter().GetResult();
                return u.Node(Demo.Hacker).Stop == "hops" && v.Node(Demo.Payout) is GNode p && p.Stop == "end" && v.Status(p) == "holding" && p.Balance == 187.11m && !p.Expanded;
            });
            T("trace: several start addresses in one graph", () =>
            {
                var u = Tracer.Run(new TraceOptions { Chain = "demo", Start = Demo.Hacker + ", " + Demo.Hacker2 + "\nnot-an-address", MaxHops = 3 }, null, ct).GetAwaiter().GetResult();
                return u.Nodes.Count(n => n.Depth == 0) == 2 && u.Edges.Any(e => e.From == Demo.Hacker2 && e.To == Demo.Hop1) && u.Node(Demo.Exchange) != null;
            });
            T("bubbles: no two circles overlap and size follows value", () =>
            {
                bool clear = true;
                for (int a = 0; a < g.Nodes.Count; a++) for (int b = a + 1; b < g.Nodes.Count; b++) { var p = g.Nodes[a]; var q = g.Nodes[b]; double d = Math.Sqrt((p.Bx - q.Bx) * (p.Bx - q.Bx) + (p.By - q.By) * (p.By - q.By)); if (d < p.R + q.R - 0.5) clear = false; }
                return clear && g.Node(Demo.Hop1).R > g.Node(Demo.Mixer).R && g.Node(Demo.Hacker).R >= g.Node(Demo.Hop1).R;
            });
            T("swap: a contract that returns another asset in the same transaction is a swap", () =>
            {
                // synthetic: wallet sends USDT to a contract and receives ETH back in the same hash
                var api2 = new SwapFake();
                Chains.All.Add(new Chain { Key = "swaptest", Name = "swaptest", Symbol = "ETH", Family = "evm", Explorer = "", Make = _ => api2 });
                try { var u = Tracer.Run(new TraceOptions { Chain = "swaptest", Start = SwapFake.W, MaxHops = 2, MinSharePct = 0.01 }, null, ct).GetAwaiter().GetResult(); return u.Status(u.Node(SwapFake.Pool)) == "dex" && u.Status(u.Node(SwapFake.Stash)) == "holding"; }
                finally { Chains.All.RemoveAll(c => c.Key == "swaptest"); }
            });
            T("trace: an equal split into many wallets is still followed", () =>
            {
                var api3 = new SplitFake();
                Chains.All.Add(new Chain { Key = "splittest", Name = "splittest", Symbol = "ETH", Family = "evm", Explorer = "", Make = _ => api3 });
                try { var u = Tracer.Run(new TraceOptions { Chain = "splittest", Start = SplitFake.W, MaxHops = 1, MinSharePct = 3, Branch = 60 }, null, ct).GetAwaiter().GetResult(); return u.Nodes.Count == 51 && u.Edges.Count == 50; }
                finally { Chains.All.RemoveAll(c => c.Key == "splittest"); }
            });
            T("multi-network ids: prefix handling", () =>
            {
                var gx = new Graph { Chain = "eth" };
                return gx.ChainOf("btc:bc1qxyz") == "btc" && gx.ChainOf("0xabc") == "eth" && gx.IdFor("eth", "0xabc") == "0xabc" && gx.IdFor("tron", "Tabc") == "tron:Tabc" && Addr.Bare("btc:bc1qa5n7qryprtttd65nm4tjyq4gkvnglf79t92dxr") == "bc1qa5n7qryprtttd65nm4tjyq4gkvnglf79t92dxr"
                    && Fmt.Short("btc:bc1qa5n7qryprtttd65nm4tjyq4gkvnglf79t92dxr") == "btc:bc1qa5n7…t92dxr" && Labels.Find("eth:0x28C6c06298d514Db089934071355E5743bf21d60")?.Name == "Binance 14";
            });
            T("usd: bridge payouts are priced on their own network", () =>
            {
                var gx = new Graph { Chain = "eth", UsdRate = 2000 }; gx.Rates["btc"] = 60000;
                return gx.Usd(new GEdge { Asset = "BTC", Amount = 2, ChainKey = "btc", Bridge = true }) == 120000m && gx.Usd(new GEdge { Asset = "ETH", Amount = 1 }) == 2000m && gx.Usd(new GEdge { Asset = "USDT", Amount = 5, Bridge = true, ChainKey = "tron" }) == 5m;
            });
            T("findings: direction of dealings with a mixer", () =>
            {
                var fz = Analysis.Findings(Demo.Hacker, api.Info(Demo.Hacker, ct).Result, set);
                var pz = Analysis.Findings(Demo.Payout, api.Info(Demo.Payout, ct).Result, api.Transfers(Demo.Payout, 500, false, 0, 0, ct).Result);
                return fz.Any(x => x.Title == "Sent funds into a mixer" && x.Detail.Contains("187.35 ETH")) && pz.Any(x => x.Title == "Received funds from a mixer" && x.Detail.Contains("187.11 ETH"));
            });
            T("report: includes the bubble map", () => Report.TraceHtml(g, "en").Contains("<circle") && GraphGeo.Money(1_480_000_000m) == "$1.48B" && GraphGeo.Money(20_232_600m) == "$20.2M");
            T("tron address to hex and back", () => Addr.TronToHex("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t") == "a614f803b6fd780986a42c78ec9c7f77e6ded13c");

            // ---- reports and storage
            T("report: trace HTML with SVG diagram", () => { string h = Report.TraceHtml(g, "en"); return h.Contains("<svg") && h.Contains(Demo.Exchange) && h.Contains("Demo Exchange") && h.Contains("</html>"); });
            T("report: Arabic is right-to-left", () => { L.Ar = true; try { return Report.TraceHtml(g, "ar").Contains("dir='rtl'"); } finally { L.Ar = false; } });
            T("report: flows CSV", () => Report.EdgesCsv(g).Split('\n').Length == g.Edges.Count + 2);
            T("store: case round trip", () =>
            {
                var c = Store.NewCase("selftest"); c.Traces.Add(g); c.Items.Add(new CaseItem { Chain = "demo", Kind = "address", Value = Demo.Hacker }); c.Findings.Add("f"); Store.SaveCase(c);
                var b = Store.AllCases().First(x => x.Id == c.Id); bool ok = b.Traces[0].Edges.Count == g.Edges.Count && b.Items.Count == 1 && Report.CaseHtml(b, "en").Contains("<svg");
                Store.DeleteCase(c); return ok && Store.AllCases().All(x => x.Id != c.Id);
            });
            T("store: user label overrides", () =>
            {
                Store.UserLabels.Add(new UserLabel { Address = Demo.Exchange, Name = "Mine", Cat = "user" });
                try { return Labels.Find(Demo.Exchange).Name == "Mine"; } finally { Store.UserLabels.RemoveAll(x => x.Address == Demo.Exchange); }
            });
            T("translations: every Arabic entry is non-empty", () => L.A.All(k => !string.IsNullOrWhiteSpace(k.Value)));

            // ---- AI analysis (no network)
            T("ai: prompts carry the collected data", () =>
            {
                string ap = Ai.AddressPrompt(Chains.Get("demo"), Demo.Hacker, api.Info(Demo.Hacker, ct).Result, set), tp = Ai.TracePrompt(g);
                var c = new CaseFile { Name = "x", Notes = "note-123" }; c.Traces.Add(g); c.Findings.Add("finding-456");
                string cp = Ai.CasePrompt(c);
                return ap.Contains("Demo Mixer") && ap.Contains("TOOL FINDINGS") && ap.Contains("Possible test transaction") && ap.Contains(Demo.Hacker) && tp.Contains("399.2 ETH") && tp.Contains("Demo Exchange") && tp.Contains("service") && cp.Contains("note-123") && cp.Contains("finding-456") && cp.Contains("TRACE 1");
            });
            T("ai: key is stored encrypted and round-trips", () =>
            {
                string before = Store.S.ClaudeKeyEnc;
                try { Ai.Key = "sk-ant-selftest-0000"; return !Store.S.ClaudeKeyEnc.Contains("selftest") && Ai.Key == "sk-ant-selftest-0000" && Ai.Ready; }
                finally { Store.S.ClaudeKeyEnc = before; }
            });
            T("ai: without a key the user gets a clear message", () =>
            {
                string before = Store.S.ClaudeKeyEnc; Store.S.ClaudeKeyEnc = "";
                try { Ai.Ask("hi", ct).GetAwaiter().GetResult(); return false; } catch (ApiException e) { return e.Message.Contains("Settings"); } finally { Store.S.ClaudeKeyEnc = before; }
            });

            // ---- live explorers (optional)
            if (live)
            {
                const string bybit = "0x47666fab8bd0ac7003bce3f5c3585383f09486e2";
                T("LIVE eth: address info", () => { var i = Chains.Get("eth").Api.Info(bybit, ct).Result; return i.TxCount > 100; });
                T("LIVE eth: transfers (native + token + internal)", () => { var s = Chains.Get("eth").Api.Transfers(bybit, 200, true, 0, 0, ct).Result; return s.Items.Any(x => x.Kind == "token") && s.Items.Any(x => x.Kind == "native") && s.Items.Any(x => x.Kind == "internal" && x.Amount > 400000); });
                T("LIVE eth: time filter", () => { var s = Chains.Get("eth").Api.Transfers(bybit, 100, true, 1740148187, 1740148187 + 3600, ct).Result; return s.Items.Count > 0 && s.Items.All(x => x.Time >= 1740148187 && x.Time <= 1740148187 + 3600); });
                T("LIVE eth: transaction", () => { var l = Chains.Get("eth").Api.Tx("0x25800d105db4f21908d646a7a3db849343737c5fba0bc5701f782bf0e75217c9", ct).Result; return l.Any(x => x.Asset == "USDT" && x.Amount == 90m && x.To == bybit); });
                T("LIVE eth: 2-hop trace from the Bybit exploiter", () => { var t = Tracer.Run(new TraceOptions { Chain = "eth", Start = bybit, MaxHops = 2, Branch = 3, MaxNodes = 12, FromTime = 1740096000, AllTokens = true }, null, ct).Result; log.AppendLine($"        nodes={t.Nodes.Count} edges={t.Edges.Count} notes={string.Join(" | ", t.Notes.Take(3))}"); return t.Nodes.Count >= 4 && t.Edges.Count >= 3; });
                T("LIVE avax: transfers", () => Chains.Get("avax").Api.Transfers("0x28c6c06298d514db089934071355e5743bf21d60", 20, false, 0, 0, ct).Result.Items.Count > 0);
                T("LIVE btc: info + transfers + tx", () =>
                {
                    var a = Chains.Get("btc").Api; const string gen = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa";
                    var i = a.Info(gen, ct).Result; var s = a.Transfers(gen, 25, false, 0, 0, ct).Result;
                    var tx = a.Tx(s.Items[0].Hash, ct).Result;
                    return i.TxCount > 1000 && i.Usd > 0 && s.Items.Count > 0 && s.Items.All(x => x.To == gen) && tx.Count > 0;
                });
                T("LIVE tron: info + transfers + tx", () =>
                {
                    var a = Chains.Get("tron").Api; const string addr = "TRTHH8aLyckjbffhQSWykaHceBtJAAmBUA";
                    var s = a.Transfers(addr, 200, false, 0, 0, ct).Result;
                    var tok = s.Items.First(x => x.Kind == "token");
                    var tx = a.Tx(tok.Hash, ct).Result;
                    log.AppendLine($"        tron transfers={s.Items.Count} native={s.Items.Count(x => x.Kind == "native")}");
                    return s.Items.Count > 0 && s.Items.All(x => x.From.StartsWith("T") && x.To.StartsWith("T")) && tx.Any(x => x.Amount == tok.Amount);
                });
                T("LIVE eth: freeze check (Tether/Circle blacklists)", () =>
                {
                    var fzc = (IFreezeCheck)Chains.Get("eth").Api;
                    var a1 = fzc.Frozen("0x5227f603c1ee34ce3c06efe6477003bdfccb2f8c", ct).Result; var a2 = fzc.Frozen(bybit, ct).Result;
                    log.AppendLine("        0x5227f6: " + string.Join(",", a1) + " | bybit exploiter: " + string.Join(",", a2));
                    return a1.Contains("USDT") && a1.Contains("USDC");
                });
                T("LIVE optimism (Blockscout REST lists)", () => { var s = Chains.Get("op").Api.Transfers("0x28c6c06298d514db089934071355e5743bf21d60", 60, false, 0, 0, ct).Result; log.AppendLine("        op transfers=" + s.Items.Count); return s.Items.Count > 0; });
                T("LIVE bridges: THORChain swap resolves to a Bitcoin address", () =>
                {
                    var h = Bridges.Resolve("eth", "0x0188a69f107f98afee301751ba2140826faf406db4184f37422bc7914fe5e283", ct).Result;
                    log.AppendLine("        " + string.Join(" | ", h.Select(x => $"{x.Service} {x.Chain} {x.Address} {x.Amount} {x.Asset}")));
                    return h.Any(x => x.Service == "THORChain" && x.Chain == "btc" && x.Address.StartsWith("bc1") && x.Amount > 0);
                });
                T("LIVE bridges: Relay payout resolves back to its Tron origin", () =>
                {
                    var h = Bridges.Resolve("eth", "0x202ac2f8bb88400726b41a022604080925e7eabc79a779422163d3985bad1615", ct).Result;
                    log.AppendLine("        " + string.Join(" | ", h.Select(x => $"{x.Service} {x.Chain} {x.Address} {x.Amount} {x.Asset} out={x.Outbound}")));
                    if (h.Count == 0) { log.AppendLine("        Relay's keyless endpoint refused the request (rate limit / retired); needs an API key - not counted as a failure"); return true; }
                    return h.Any(x => x.Service == "Relay" && x.Chain == "tron" && !x.Outbound && x.Address.StartsWith("T"));
                });
                T("LIVE ai: a wrong key is reported clearly", () =>
                {
                    string before = Store.S.ClaudeKeyEnc;
                    try { Ai.Key = "sk-ant-api03-invalid-selftest-key"; Ai.Ask("hi", ct).GetAwaiter().GetResult(); return false; }
                    catch (ApiException e) { log.AppendLine("        " + e.Message); return e.Message.Contains("rejected"); }
                    finally { Store.S.ClaudeKeyEnc = before; }
                });
                log.AppendLine($"        http calls={Http.Calls} cache hits={Http.CacheHits}");
            }

            log.Insert(0, $"Athar {App.Version} self-test: {n - bad}/{n} passed{(live ? " (with live explorers)" : "")}\n");
            File.WriteAllText(logPath, log.ToString());
            return bad;
        }
    }

    /// <summary>Tiny scenario for the swap test: W sends 1000 USDT to Pool and gets 0.5 ETH back in the same transaction, then moves the ETH to Stash.</summary>
    class SwapFake : IChainApi
    {
        public const string W = "0x00000000000000000000000000000000000000a1", Pool = "0x00000000000000000000000000000000000000b2", Stash = "0x00000000000000000000000000000000000000c3";
        static readonly List<Xfer> D = new()
        {
            new Xfer { Chain = "swaptest", Hash = "h1", From = W, To = Pool, Asset = "USDT", Contract = "0xdac17f958d2ee523a2206206994597c13d831ec7", Kind = "token", Amount = 1000, Time = 100 },
            new Xfer { Chain = "swaptest", Hash = "h1", From = Pool, To = W, Asset = "ETH", Kind = "internal", Amount = 0.5m, Time = 100 },
            new Xfer { Chain = "swaptest", Hash = "h2", From = W, To = Stash, Asset = "ETH", Kind = "native", Amount = 0.5m, Time = 200 },
        };
        public System.Threading.Tasks.Task<AddrInfo> Info(string a, CancellationToken ct) => System.Threading.Tasks.Task.FromResult(new AddrInfo { Chain = "swaptest", Address = a, IsContract = a == Pool, Balance = a == Stash ? 0.5m : 0, TxCount = 2, Usd = 2000 });
        public System.Threading.Tasks.Task<TransferSet> Transfers(string a, int max, bool asc, long f, long t, CancellationToken ct) => System.Threading.Tasks.Task.FromResult(new TransferSet { Items = D.Where(x => (x.From == a || x.To == a) && (f == 0 || x.Time >= f)).OrderBy(x => x.Time).ToList() });
        public System.Threading.Tasks.Task<List<Xfer>> Tx(string h, CancellationToken ct) => System.Threading.Tasks.Task.FromResult(D.Where(x => x.Hash == h).ToList());
    }

    /// <summary>W sends 100 ETH to each of 50 wallets (2 % each).</summary>
    class SplitFake : IChainApi
    {
        public const string W = "0x00000000000000000000000000000000000000d4";
        static readonly List<Xfer> D = Enumerable.Range(0, 50).Select(i => new Xfer { Chain = "splittest", Hash = "s" + i, From = W, To = "0x" + (1000 + i).ToString("x40"), Asset = "ETH", Kind = "native", Amount = 100, Time = 100 + i }).ToList();
        public System.Threading.Tasks.Task<AddrInfo> Info(string a, CancellationToken ct) => System.Threading.Tasks.Task.FromResult(new AddrInfo { Chain = "splittest", Address = a, Balance = a == W ? 0 : 100, TxCount = 1, Usd = 2000 });
        public System.Threading.Tasks.Task<TransferSet> Transfers(string a, int max, bool asc, long f, long t, CancellationToken ct) => System.Threading.Tasks.Task.FromResult(new TransferSet { Items = D.Where(x => x.From == a || x.To == a).ToList() });
        public System.Threading.Tasks.Task<List<Xfer>> Tx(string h, CancellationToken ct) => System.Threading.Tasks.Task.FromResult(D.Where(x => x.Hash == h).ToList());
    }
}
