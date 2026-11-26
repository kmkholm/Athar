using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Athar
{
    public partial class MainWindow
    {
        public class EdgeRow
        {
            public GEdge E;
            public string From { get; set; } public string To { get; set; } public decimal Amount { get; set; } public string Asset { get; set; }
            public string Usd { get; set; } public int Count { get; set; } public string First { get; set; } public string Last { get; set; }
        }
        public class EndRow
        {
            public GNode N;
            public string Name { get; set; } public string Address { get; set; } public string Type { get; set; } public string Received { get; set; } public int Hops { get; set; } public string Why { get; set; }
        }

        FrameworkElement PageTrace(object a)
        {
            var o = a as TraceOptions ?? new TraceOptions { Chain = chainBox.Val() };
            var saved = a as Graph;
            if (a is TraceOptions == false) { o.MaxHops = Store.S.Hops; o.Branch = Store.S.Branch; o.MaxNodes = Store.S.MaxNodes; o.MinSharePct = Store.S.MinShare; }
            else { o.MaxHops = Store.S.Hops; o.Branch = Store.S.Branch; o.MaxNodes = Store.S.MaxNodes; o.MinSharePct = Store.S.MinShare; }
            if (saved != null) { o.Chain = saved.Chain; o.Start = saved.Start; o.Forward = saved.Forward; }

            var root = new StackPanel();
            root.Children.Add(Head(L.T("Trace funds"), L.T("Start from an address. At every hop Athar keeps the largest transfers made after the money arrived, and stops when it reaches a known service (exchange, bridge, mixer) or a contract.")));

            var addr = Ui.Input(o.Start.Replace("\n", ", "), 560, true);
            var chain = Ui.ChainCombo(o.Chain, 180);
            var dir = Ui.Combo(new[] { ("f", L.T("Forward — where did the money go?")), ("b", L.T("Backward — where did it come from?")) }, o.Forward ? "f" : "b", 300);
            var hops = Ui.Combo(Enumerable.Range(1, 6).Select(i => (i.ToString(), i.ToString())), o.MaxHops.ToString(), 80);
            var branch = Ui.Combo(new[] { 3, 5, 8, 10, 15, 25, 40 }.Select(i => (i.ToString(), i.ToString())), o.Branch.ToString(), 80);
            var share = Ui.Combo(new[] { "0.5", "1", "2", "3", "5", "10", "20" }.Select(i => (i, i + " %")), o.MinSharePct.ToString(System.Globalization.CultureInfo.InvariantCulture), 90);
            var nodes = Ui.Combo(new[] { 50, 100, 150, 250, 400 }.Select(i => (i.ToString(), i.ToString())), o.MaxNodes.ToString(), 90);
            var from = Ui.Input(o.FromTime > 0 ? Fmt.Time(o.FromTime) : "", 170, true);
            var asset = Ui.Input(o.Asset, 110, true);
            var window = Ui.Combo(new[] { ("0", L.T("No limit")), ("1", L.T("1 day")), ("7", L.T("7 days")), ("30", L.T("30 days")), ("90", L.T("90 days")) }, o.WindowDays.ToString(), 120);
            var stop = new CheckBox { Content = L.T("Stop at exchanges, bridges and mixers"), IsChecked = o.StopAtServices };
            var allTok = new CheckBox { Content = L.T("Follow every token, not only the coin and major stablecoins"), IsChecked = o.AllTokens };
            var bridges = new CheckBox { Content = L.T("Follow bridges and cross-chain swaps to the other network"), IsChecked = o.FollowBridges };

            var result = new StackPanel();
            TraceOptions Read()
            {
                var r = new TraceOptions { Chain = chain.Val(), Start = addr.Text.Trim(), Forward = dir.Val() == "f", MaxHops = int.Parse(hops.Val()), Branch = int.Parse(branch.Val()), MaxNodes = int.Parse(nodes.Val()), MinSharePct = double.Parse(share.Val(), System.Globalization.CultureInfo.InvariantCulture), FromTime = Fmt.ParseDate(from.Text), Asset = asset.Text.Trim(), WindowDays = int.Parse(window.Val()), StopAtServices = stop.IsChecked == true, AllTokens = allTok.IsChecked == true, FollowBridges = bridges.IsChecked == true };
                Store.S.Hops = r.MaxHops; Store.S.Branch = r.Branch; Store.S.MaxNodes = r.MaxNodes; Store.S.MinShare = r.MinSharePct; Store.Save();
                return r;
            }
            void Run()
            {
                var opt = Read();
                var c = Chains.Get(opt.Chain);
                if (!Tracer.Starts(c, opt.Start).Any()) { Toast(L.T("Enter a valid address for the selected network."), true); return; }
                Load(result, (ct, say) => Tracer.Run(opt, s => say(L.T("Tracing…") + "  " + s), ct), g => ShowGraph(result, g, opt));
            }
            var form = new StackPanel();
            form.Children.Add(Ui.Row(Ui.Field(L.T("Start address"), addr, L.T("Several addresses of the same incident can be given, separated by commas.")), Ui.Field(L.T("Network"), chain), Ui.Field(L.T("Direction"), dir)));
            form.Children.Add(Ui.Row(
                Ui.Field(L.T("Hops"), hops, L.T("How many steps to follow.")),
                Ui.Field(L.T("Branches per address"), branch, L.T("Largest recipients kept at each step.")),
                Ui.Field(L.T("Ignore below"), share, L.T("Share of the address's outflow.")),
                Ui.Field(L.T("Max addresses"), nodes),
                Ui.Field(L.T("Only after (UTC date)"), from, L.T("e.g. 2025-02-21 or 2025-02-21 14:00")),
                Ui.Field(L.T("Only this asset"), asset, L.T("e.g. ETH, USDT — empty = all")),
                Ui.Field(L.T("Time window per hop"), window)));
            form.Children.Add(Ui.Row(stop, allTok, bridges));
            form.Children.Add(Ui.Row(Ui.Btn(L.T("Start tracing"), Run, "primary", "")));
            root.Children.Add(Ui.Card(form));
            root.Children.Add(result);
            if (saved != null) ShowGraph(result, saved, o);
            else if (a is TraceOptions && o.Start != "") Run();
            else result.Children.Add(Ui.Card(Ui.Steps("Paste the address that received the stolen or suspicious funds.", "Choose Forward to see where the money went, or Backward to find who funded it.", "Press Start. Click any box in the diagram to open it or to continue from it.", "Save the diagram to a case and export a report.")));
            return root;
        }

        public static bool SnapBubbles;
        void ShowGraph(StackPanel host, Graph g, TraceOptions opt)
        {
            host.Children.Clear();
            var chain = Chains.Get(g.Chain);
            var ends = g.Nodes.Where(n => n.Depth > 0 && n.Stop != "").ToList();
            int services = ends.Count(n => n.Stop == "service");
            var stats = new WrapPanel();
            stats.Children.Add(Ui.Stat(L.T("Addresses"), g.Nodes.Count.ToString(), null));
            stats.Children.Add(Ui.Stat(L.T("Flows"), g.Edges.Count.ToString(), null));
            stats.Children.Add(Ui.Stat(L.T("Reached known services"), services.ToString(), null, services > 0 ? Ui.Green : null));
            stats.Children.Add(Ui.Stat(L.T("Deepest hop"), g.Nodes.Max(n => n.Depth).ToString(), null));
            host.Children.Add(stats);

            int nolabel = g.Notes.Count(x => x == "nolabel");
            if (nolabel > 0) host.Children.Add(Ui.Notice("info", L.F("The explorer was slow: labels of {0} address(es) could not be read. Run the trace again later to complete them.", nolabel)));
            if (g.Notes.Contains("relaykey")) host.Children.Add(Ui.Notice("medium", L.T("Funds entered the Relay bridge. Its public lookup now requires an API key: add a Relay key in Settings to see where they came out."), null, Ui.Btn(L.T("Settings"), () => Go("settings"), "link")));
            foreach (var note in g.Notes.Where(x => x != "nolabel" && x != "relaykey").Distinct().Take(6))
                host.Children.Add(Ui.Notice(note == "recent" ? "info" : "medium", note == "limit" ? L.T("The address limit was reached; some branches were not followed. Raise “Max addresses” or continue from a box.") : note == "recent" ? L.T("The start address is very active and no start date was given, so only its most recent transfers were used. Set “Only after” to the date of the incident for a precise trace.") : note));

            // where the money stands now
            var endsNow = g.Nodes.Where(n => n.Depth > 0 || n.Frozen != "").ToList();
            decimal Left(GNode n) => g.Status(n) == "holding" || Labels.IsService(n.Cat) ? g.UsdIn(n) : Math.Max(0, g.UsdIn(n) - g.UsdOut(n));
            var tiles = new WrapPanel();
            foreach (var st in new[] { "frozen", "holding", "mixer", "exchange", "bridge", "otherchain", "dex", "unexplored" })
            {
                var ns = endsNow.Where(n => g.Status(n) == st).ToList();
                if (ns.Count == 0) continue;
                decimal usd = st == "frozen" ? ns.Sum(n => n.FrozenUsd) : ns.Sum(Left);
                tiles.Children.Add(Ui.Stat(Ui.StatusName(st), usd > 0 ? GraphGeo.Money(usd) : ns.Count.ToString(), L.F("{0} address(es)", ns.Count), Ui.StatusBrush(st)));
            }
            if (tiles.Children.Count > 0) host.Children.Add(Ui.Card(tiles, L.T("Where the money is now"), L.T("Totals of the traced value by where it stands, at today's price. “Still holding” wallets have not moved what they received.")));

            var view = new GraphView(540);
            var bubbles = new BubbleView(540) { Visibility = Visibility.Collapsed };
            var sel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            void ShowSel(GNode n)
            {
                sel.Children.Clear();
                if (n == null) { sel.Children.Add(Ui.Empty(L.T("Click a box in the diagram to see its details and continue from it."))); return; }
                var line = new WrapPanel();
                var code = Ui.Code(n.Id, 14); code.FontWeight = FontWeights.SemiBold; code.Margin = new Thickness(0, 0, 10, 6); line.Children.Add(code);
                var lc = Ui.LabelChip(n.Id); if (lc != null) line.Children.Add(lc);
                line.Children.Add(Ui.Chip(Ui.StatusName(g.Status(n)) + (n.Frozen != "" ? " · " + n.Frozen : ""), Ui.StatusBrush(g.Status(n), n.Cat)));
                if (n.Stop != "" && n.Stop != "service" && n.Stop != "end") line.Children.Add(Ui.Chip(Ui.StopName(n.Stop), Ui.Gray));
                sel.Children.Add(line);
                var inSum = g.Edges.Where(e => e.To == n.Id).GroupBy(e => e.Asset).Select(x => Fmt.Amt(x.Sum(e => e.Amount)) + " " + x.Key);
                var outSum = g.Edges.Where(e => e.From == n.Id).GroupBy(e => e.Asset).Select(x => Fmt.Amt(x.Sum(e => e.Amount)) + " " + x.Key);
                var info = Ui.Tx(L.T("Hop") + " " + n.Depth + "   ·   " + L.T("In (traced)") + ": " + (inSum.Any() ? string.Join(", ", inSum) : "—") + "   ·   " + L.T("Out (traced)") + ": " + (outSum.Any() ? string.Join(", ", outSum) : "—") + "   ·   " + L.T("Balance") + ": " + Fmt.Amt(n.Balance) + " " + (Chains.All.FirstOrDefault(c => c.Key == g.ChainOf(n.Id))?.Symbol ?? "") + (n.TxCount >= 0 ? "   ·   " + L.T("Transactions") + ": " + n.TxCount.ToString("#,##0") : ""), 13, false, Ui.Muted);
                sel.Children.Add(info);
                var row = Ui.Row(
                    Ui.Btn(L.T("Open address profile"), () => Go("address", (g.ChainOf(n.Id), Addr.Bare(n.Id))), "plain", ""),
                    n.Expanded ? null : Ui.Btn(L.T("Continue tracing from here (+2 hops)"), () =>
                    {
                        Load(host, async (ct, say) => { await Tracer.More(g, n, opt, 2, s2 => say(L.T("Tracing…") + "  " + s2), ct); return g; }, gg => ShowGraph(host, gg, opt));
                    }, "primary", ""),
                    n.Cat == "bridge" || n.Cat == "mixer" ? Ui.Btn(L.T("Match what came out of this service"), () =>
                    {
                        var dep = g.Edges.FirstOrDefault(e => e.To == n.Id);
                        Go("match", (g.ChainOf(n.Id), Addr.Bare(dep?.From ?? g.Nodes[0].Id), Addr.Bare(n.Id)));
                    }, "primary", "") : null,
                    Ui.Btn(L.T("Watch"), () => AddWatch(g.ChainOf(n.Id), Addr.Bare(n.Id)), "plain", ""),
                    Ui.Btn(L.T("Label"), () => { EditLabel(Addr.Bare(n.Id)); var l = Labels.Find(n.Id); if (l != null) { n.Label = l.Name; n.Cat = l.Cat; } ShowGraph(host, g, opt); }, "plain", ""),
                    Ui.Btn(L.T("Add to case"), () => AddToCase(g.ChainOf(n.Id), "address", Addr.Bare(n.Id)), "plain", ""),
                    Ui.Btn(L.T("Copy"), () => Ui.Copy(Addr.Bare(n.Id)), "plain", ""));
                row.Margin = new Thickness(0, 10, 0, -8);
                sel.Children.Add(row);
            }
            view.Selected += n => { bubbles.Select(n, false); ShowSel(n); };
            bubbles.Selected += n => { view.Select(n, false); ShowSel(n); };
            view.Load(g); bubbles.Load(g);
            ShowSel(null);
            PanZoomView Cur() => bubbles.Visibility == Visibility.Visible ? bubbles : view;
            Button bFlow = null, bBub = null;
            void Mode(bool bub)
            {
                bubbles.Visibility = bub ? Visibility.Visible : Visibility.Collapsed; view.Visibility = bub ? Visibility.Collapsed : Visibility.Visible;
                bFlow.Opacity = bub ? 0.55 : 1; bBub.Opacity = bub ? 1 : 0.55;
                Dispatcher.BeginInvoke(new Action(() => Cur().Fit()), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            bFlow = Ui.Btn(L.T("Flow diagram"), () => Mode(false), "primary", "");
            bBub = Ui.Btn(L.T("Bubble map"), () => Mode(true), "primary", "");
            bBub.Opacity = 0.55;
            string first = g.Nodes[0].Id;
            var tools = Ui.Row(bFlow, bBub,
                Ui.Btn(L.T("Fit"), () => Cur().Fit(), "plain", ""), Ui.Btn("", () => Cur().Zoom(1.25), "plain", ""), Ui.Btn("", () => Cur().Zoom(0.8), "plain", ""),
                Ui.Btn(L.T("Save to case"), () => { var c = ActiveCase(); c.Traces.Add(g); foreach (var st in g.Nodes.Where(n => n.Depth == 0)) if (!c.Items.Any(it => it.Value == st.Id)) c.Items.Add(new CaseItem { Chain = g.Chain, Kind = "address", Value = st.Id, Role = "trace start", Added = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }); Store.SaveCase(c); Toast(L.F("Added to case “{0}”", c.Name)); }, "plain", ""),
                Ui.Btn(L.T("Export report (HTML)"), () => { string p = SaveFile("trace_" + Fmt.Short(first).Replace("…", "_") + ".html", "HTML|*.html"); if (p != null) { File.WriteAllText(p, Report.TraceHtml(g, Store.S.Lang), new UTF8Encoding(true)); Ui.Open(p); } }, "plain", ""),
                Ui.Btn(L.T("Export flows (CSV)"), () => { string p = SaveFile("flows.csv", "CSV|*.csv"); if (p != null) { File.WriteAllText(p, Report.EdgesCsv(g), new UTF8Encoding(true)); Toast(L.T("Saved")); } }, "plain", ""));
            var legend = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var st in new[] { "start", "frozen", "holding", "exchange", "bridge", "otherchain", "mixer", "dex", "passed", "unexplored" }) legend.Children.Add(Ui.Chip(Ui.StatusName(st), Ui.StatusBrush(st)));
            var gsp = new StackPanel();
            gsp.Children.Add(tools); gsp.Children.Add(legend); gsp.Children.Add(view); gsp.Children.Add(bubbles); gsp.Children.Add(sel);
            host.Children.Add(Ui.Card(gsp, g.Forward ? L.T("Where the money went") : L.T("Where the money came from"), L.T("Drag to move, scroll to zoom. Colour shows where the value stands now; in the bubble map the size follows the amount.")));
            if (SnapBubbles) Mode(true);

            host.Children.Add(AiCard(L.T("AI explanation of this trace"), () => Ai.TracePrompt(g)));

            if (ends.Count > 0 || g.Nodes.Any(n => n.Frozen != ""))
            {
                var rows = g.Nodes.Where(n => (n.Depth > 0 && n.Stop != "") || n.Frozen != "").OrderByDescending(n => g.UsdIn(n)).Select(n => new EndRow { N = n, Name = n.Label != "" ? n.Label : L.T("Unlabelled wallet"), Address = n.Id, Type = Ui.StatusName(g.Status(n)) + (n.Frozen != "" ? " · " + n.Frozen : ""), Received = string.Join(", ", g.Edges.Where(e => (g.Forward ? e.To : e.From) == n.Id).GroupBy(e => e.Asset).Select(x => Fmt.Amt(x.Sum(e => e.Amount)) + " " + x.Key)), Hops = n.Depth, Why = Fmt.Amt(Math.Round(n.Balance, 4)) + " " + (Chains.All.FirstOrDefault(c => c.Key == g.ChainOf(n.Id))?.Symbol ?? "") }).ToList();
                host.Children.Add(Ui.Card(Ui.Table(rows, new[] { new Col("Name", "Name", 190, bold: true), new Col("Address", "Address", 330, ltr: true, mono: true), new Col("Status", "Type", 190, bold: true), new Col(g.Forward ? "Received (traced)" : "Sent (traced)", "Received", 190, ltr: true), new Col("Hop", "Hops", 60, right: true), new Col("Balance now", "Why", 0, ltr: true, star: true) },
                    r => Go("address", (g.ChainOf(((EndRow)r).N.Id), Addr.Bare(((EndRow)r).N.Id))), r => new[] { (L.T("Open address"), (Action)(() => Go("address", (g.ChainOf(((EndRow)r).N.Id), Addr.Bare(((EndRow)r).N.Id))))), (L.T("Copy address"), () => Ui.Copy(Addr.Bare(((EndRow)r).N.Id))), (L.T("Watch"), () => AddWatch(g.ChainOf(((EndRow)r).N.Id), Addr.Bare(((EndRow)r).N.Id))) }, 420),
                    L.T("Where the trail ends"), L.T("Funds that reach an exchange can be frozen or identified by that exchange after a formal request; dormant wallets are worth watching: right-click to add them to the watchlist.")));
            }

            var edges = g.Edges.OrderBy(e => g.Node(e.From)?.Depth ?? 0).ThenByDescending(e => e.Amount).Select(e => new EdgeRow { E = e, From = Labels.Show(e.From), To = Labels.Show(e.To), Amount = e.Amount, Asset = e.Asset, Usd = g.Usd(e) is decimal u ? "$" + Fmt.Amt(Math.Round(u, 0)) : "", Count = e.Count, First = Fmt.Time(e.First), Last = Fmt.Time(e.Last) }).ToList();
            host.Children.Add(Ui.Card(Ui.Table(edges, new[] { new Col("From", "From", 170, ltr: true), new Col("To", "To", 170, ltr: true), new Col("Amount", "Amount", 110, right: true, bold: true, format: Col.Num), new Col("Asset", "Asset", 70, ltr: true), new Col("≈ USD today", "Usd", 110, right: true), new Col("Transfers", "Count", 80, right: true), new Col("First (UTC)", "First", 150, ltr: true), new Col("Last (UTC)", "Last", 0, ltr: true, star: true) },
                r => { var e = ((EdgeRow)r).E; if (e.Hashes.Count > 0) Go("tx", (e.ChainKey == "" ? g.Chain : e.ChainKey, e.Hashes[0])); },
                r => { var e = ((EdgeRow)r).E; return new[] { (L.T("Open largest transaction"), (Action)(() => { if (e.Hashes.Count > 0) Go("tx", (e.ChainKey == "" ? g.Chain : e.ChainKey, e.Hashes[0])); })), (L.T("Open sender"), () => Go("address", (g.ChainOf(e.From), Addr.Bare(e.From)))), (L.T("Open receiver"), () => Go("address", (g.ChainOf(e.To), Addr.Bare(e.To)))) }; }, 380),
                L.T("All flows"), L.T("Double-click a row to open its largest transaction.")));

            host.Children.Add(Ui.Notice("info", L.T("How to read a trace"), L.T("A trace shows where value moved, not who owns the addresses. After the first hops an address may mix the traced funds with unrelated money. Confirm every key step on the explorer before relying on it.")));
        }
    }
}
