using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Athar
{
    public partial class MainWindow
    {
        FrameworkElement PageHome()
        {
            var root = new StackPanel();
            root.Children.Add(Head(L.T("Follow the money on the blockchain"), L.T("Paste an address or a transaction hash in the search bar above. Athar reads public explorer data, explains what it sees and lets you follow the funds hop by hop.")));

            var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -12, 0) };
            void Tile(string glyph, string title, string text, string btn, Action go)
            {
                var sp = new StackPanel();
                var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                top.Children.Add(new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = Ui.BrandSoft, Margin = new Thickness(0, 0, 10, 0), Child = new TextBlock { Text = glyph, FontFamily = Ui.Icons, FontSize = 16, Foreground = Ui.BrandDark, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
                top.Children.Add(Ui.Tx(L.T(title), 15, true));
                sp.Children.Add(top);
                var t = Ui.Tx(L.T(text), 13, false, Ui.Muted); t.MinHeight = 56; sp.Children.Add(t);
                var b = Ui.Btn(L.T(btn), go, "plain"); b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 8, 0, 0); sp.Children.Add(b);
                var c = Ui.Card(sp); c.Margin = new Thickness(0, 0, 12, 12);
                tiles.Children.Add(c);
            }
            Tile("", "Profile an address", "Balance, labels, first funder, counterparties, test transactions and pass-through behaviour.", "Use the search bar", () => { search.Focus(); });
            Tile("", "Trace funds", "Follow the largest transfers forward or backward until they reach an exchange, a bridge or a mixer.", "Open tracing", () => Go("trace"));
            Tile("", "Link addresses", "Find direct transfers, shared counterparties and a common funder between several wallets.", "Open linking", () => Go("link"));
            Tile("", "Match deposits and withdrawals", "Pair what went into a bridge or privacy tool with what came out, by amount and timing.", "Open matching", () => Go("match"));
            Tile("", "Watch wallets", "Get an alert inside the app when a watched address moves funds.", "Open watchlist", () => Go("watch"));
            Tile("", "Cases and reports", "Collect addresses, traces and notes in a case and export a printable report.", "Open cases", () => Go("cases"));
            root.Children.Add(tiles);

            var demo = Ui.Notice("info", L.T("New here? Try the offline demo"), L.T("A small invented theft scenario (no internet needed) that shows every feature: test transaction, hops, mixer, bridge and exchange."),
                Ui.Btn(L.T("Open the demo"), () => { SelectChain("demo"); Go("address", ("demo", Demo.Hacker)); }, "primary"));
            demo.Margin = new Thickness(0, 4, 0, 16);
            root.Children.Add(demo);

            if (Store.S.Recent.Count > 0)
            {
                var sp = new StackPanel();
                foreach (var r in Store.S.Recent.Take(8))
                {
                    var parts = r.Split('|'); if (parts.Length != 2 || Chains.All.All(c => c.Key != parts[0])) continue;
                    var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                    var chip = Ui.Chip(Chains.Get(parts[0]).Name, Ui.Gray); DockPanel.SetDock(chip, Dock.Left); row.Children.Add(chip);
                    bool isTx = Addr.Detect(parts[1]).kind == "tx";
                    var link = Ui.Code(parts[1], 13, Ui.Brand); link.Cursor = System.Windows.Input.Cursors.Hand;
                    string ch = parts[0], v = parts[1];
                    link.MouseLeftButtonUp += (_, _) => Go(isTx ? "tx" : "address", (ch, v));
                    row.Children.Add(link);
                    var lc = Ui.LabelChip(v); if (lc != null) { lc.Margin = new Thickness(10, 0, 0, 0); row.Children.Add(lc); }
                    sp.Children.Add(row);
                }
                root.Children.Add(Ui.Card(sp, L.T("Recent searches")));
            }

            var rows = Chains.All.Select(c => new { Name = c.Name, Coin = c.Symbol, Status = c.Key == "demo" ? L.T("Offline demo data") : c.Ready ? L.T("Ready — no key needed") : L.T("Needs a free Etherscan API key (Settings)"), Source = c.Key == "demo" ? "—" : c.NeedsKey ? "api.etherscan.io" : c.Key == "btc" ? "mempool.space" : c.Key == "tron" ? "api.trongrid.io" : c.Key == "avax" || c.Key == "mantle" ? "api.routescan.io" : c.Key != "eth" ? "blockscout" : "api.routescan.io + eth.blockscout.com" }).ToList();
            root.Children.Add(Ui.Card(Ui.Table(rows, new[] { new Col("Network", "Name", 170, bold: true), new Col("Coin", "Coin", 80), new Col("Status", "Status", 330), new Col("Data source", "Source", 0, ltr: true, star: true) }, maxHeight: 400), L.T("Supported networks")));
            return root;
        }

        public void SelectChain(string key)
        {
            foreach (ComboBoxItem i in chainBox.Items) if ((string)i.Tag == key) { chainBox.SelectedItem = i; break; }
        }

        // ---------------------------------------------------------------- address

        FrameworkElement PageAddress((string chain, string addr) a)
        {
            var chain = Chains.Get(a.chain);
            var root = new StackPanel();
            root.Children.Add(Head(L.T("Address profile"), chain.Name));
            var host = new StackPanel();
            root.Children.Add(host);
            Store.AddRecent(a.chain, a.addr);
            Load(host, async (ct, say) =>
            {
                var ti = chain.Api.Info(a.addr, ct);
                var tt = chain.Api.Transfers(a.addr, 3000, false, 0, 0, ct);
                await Task.WhenAll(ti, tt);
                Labels.Learn(ti.Result);
                var cps = tt.Result.Items.Select(x => x.From == a.addr ? x.To : x.From).Where(x => !string.IsNullOrEmpty(x) && Labels.Find(x) == null).GroupBy(x => x).OrderByDescending(x => x.Count()).Take(6).Select(x => x.Key).ToList();
                int i = 0;
                foreach (var cp in cps)
                {
                    say(L.F("Reading labels of the main counterparties… {0}/{1}", ++i, cps.Count));
                    try { Labels.Learn(await chain.Api.Info(cp, ct)); } catch (ApiException) { }
                }
                Xfer first = null;
                if (tt.Result.Truncated)
                {
                    say(L.T("Looking up the first funding…"));
                    try { first = Analysis.FirstFunder(a.addr, Tokens.Clean((await chain.Api.Transfers(a.addr, 10, true, 0, 0, ct)).Items)); } catch (ApiException) { }
                }
                if (chain.Api is IFreezeCheck fz) { try { var f = await fz.Frozen(a.addr, ct); if (f.Count > 0) ti.Result.Note = "FROZEN:" + string.Join(", ", f); } catch (ApiException) { } }
                return (info: ti.Result, set: tt.Result, first);
            }, r => FillAddress(host, chain, a.addr, r.info, r.set, r.first));
            return root;
        }

        void FillAddress(StackPanel host, Chain chain, string addr, AddrInfo info, TransferSet set, Xfer first)
        {
            var xs = Tokens.Clean(set.Items);
            int spam = set.Items.Count - xs.Count;
            var head = new StackPanel();
            var line = new WrapPanel();
            var code = Ui.Code(addr, 16); code.FontWeight = FontWeights.SemiBold; code.Margin = new Thickness(0, 0, 12, 6);
            line.Children.Add(code);
            var chip = Ui.LabelChip(addr); if (chip != null) line.Children.Add(chip);
            if (info.IsContract) line.Children.Add(Ui.Chip(L.T("Contract"), Ui.Gray));
            foreach (var t in info.Tags.Take(5)) line.Children.Add(Ui.Chip(t, Ui.Teal));
            head.Children.Add(line);
            if (info.Note.StartsWith("FROZEN:")) head.Children.Add(Ui.Notice("high", L.F("Frozen by the issuer: {0}", info.Note.Substring(7)), L.T("The stablecoin issuer has blacklisted this address: its balance of that token cannot be moved.")));
            else if (info.Note != "") head.Children.Add(Ui.Tx(L.T(info.Note), 13, false, Ui.Muted));
            var acts = Ui.Row(
                Ui.Btn(L.T("Trace where the money went"), () => Go("trace", new TraceOptions { Chain = chain.Key, Start = addr, Forward = true }), "primary", ""),
                Ui.Btn(L.T("Trace where it came from"), () => Go("trace", new TraceOptions { Chain = chain.Key, Start = addr, Forward = false }), "plain", ""),
                Ui.Btn(L.T("Add to case"), () => AddToCase(chain.Key, "address", addr), "plain", ""),
                Ui.Btn(L.T("Watch"), () => { AddWatch(chain.Key, addr); }, "plain", ""),
                Ui.Btn(L.T("Label"), () => { EditLabel(addr); Go("address", (chain.Key, addr)); }, "plain", ""),
                Ui.Btn(L.T("Copy"), () => Ui.Copy(addr), "plain", ""),
                chain.Key == "demo" ? null : Ui.Btn(L.T("Open in explorer"), () => Ui.Open(chain.AddrUrl(addr)), "plain", ""));
            acts.Margin = new Thickness(0, 12, 0, -8);
            head.Children.Add(acts);
            host.Children.Add(Ui.Card(head));

            var stats = new WrapPanel();
            string usd = info.Usd > 0 ? "≈ $" + Fmt.Amt(Math.Round(info.Balance * info.Usd, 0)) + " " + L.T("(today's price)") : null;
            stats.Children.Add(Ui.Stat(L.T("Balance"), Fmt.Amt(info.Balance) + " " + chain.Symbol, usd));
            stats.Children.Add(Ui.Stat(L.T("Transactions"), info.TxCount >= 0 ? info.TxCount.ToString("#,##0") : "—", L.F("{0} transfers loaded", xs.Count) + (set.Truncated ? " *" : "")));
            stats.Children.Add(Ui.Stat(L.T("First seen"), xs.Count > 0 ? Fmt.Day(xs.Min(x => x.Time)) : "—", set.Truncated ? L.T("within loaded transfers") : null));
            stats.Children.Add(Ui.Stat(L.T("Last seen"), xs.Count > 0 ? Fmt.Day(xs.Max(x => x.Time)) : "—", "UTC"));
            stats.Children.Add(Ui.Stat(L.T("Counterparties"), xs.Select(x => x.From == addr ? x.To : x.From).Distinct().Count().ToString(), null));
            host.Children.Add(stats);

            var fsp = new StackPanel();
            foreach (var f in Analysis.Findings(addr, info, set, first))
            {
                var btns = new WrapPanel();
                if (f.Address != "") { string fa = f.Address; btns.Children.Add(Ui.Btn(L.T("Open address"), () => Go("address", (chain.Key, fa)), "link")); }
                if (f.Hash != "") { string fh = f.Hash; btns.Children.Add(Ui.Btn(L.T("Open transaction"), () => Go("tx", (chain.Key, fh)), "link")); }
                fsp.Children.Add(Ui.Notice(f.Level, L.T(f.Title), f.Detail, btns.Children.Count > 0 ? btns : null));
            }
            host.Children.Add(Ui.Card(fsp, L.T("What stands out"), L.T("Automatic checks on the loaded transfers. Treat them as leads to verify, not as proof.")));

            if (chain.Family == "evm" && chain.Key != "demo")
            {
                var onet = new StackPanel();
                host.Children.Add(Ui.Card(onet, L.T("The same address on other networks"), L.T("An EVM address is valid on every EVM network; funds often continue there after a bridge.")));
                Load(onet, async (ct, say) =>
                {
                    var res = new List<(Chain c, AddrInfo i, string err)>();
                    foreach (var c in Chains.All.Where(c => c.Family == "evm" && c.Key != chain.Key && c.Key != "demo" && c.Ready))
                    {
                        say(L.F("Checking {0}…", c.Name));
                        try { res.Add((c, await c.Api.Info(addr, ct), null)); } catch (ApiException e) { res.Add((c, null, e.Message)); }
                    }
                    return res;
                }, res =>
                {
                    var rows = res.Select(r => new NetRow { C = r.c, Network = r.c.Name, Activity = r.i == null ? L.T("Could not be read") : r.i.TxCount > 0 || r.i.Balance > 0 ? L.T("Active") : L.T("No activity"), Balance = r.i == null ? "" : Fmt.Amt(r.i.Balance) + " " + r.c.Symbol, Tx = r.i == null || r.i.TxCount < 0 ? "" : r.i.TxCount.ToString("#,##0") }).OrderBy(r => r.Activity == L.T("Active") ? 0 : 1).ToList();
                    onet.Children.Add(Ui.Table(rows, new[] { new Col("Network", "Network", 180, bold: true), new Col("Status", "Activity", 170), new Col("Balance", "Balance", 190, ltr: true), new Col("Transactions", "Tx", 0, right: true, star: true) }, o => Go("address", (((NetRow)o).C.Key, addr)), null, 330));
                    var missing = Chains.All.Where(c => c.Family == "evm" && c.NeedsKey && !c.Ready).Select(c => c.Name).ToList();
                    if (missing.Count > 0) onet.Children.Add(Ui.Tx(L.F("Not checked (need an Etherscan key in Settings): {0}", string.Join(", ", missing)), 12.5, false, Ui.Muted));
                    onet.Children.Add(Ui.Tx(L.T("Double-click a network to open the address there."), 12.5, false, Ui.Muted));
                });
            }

            host.Children.Add(AiCard(L.T("AI analysis of this address"), () => Ai.AddressPrompt(chain, addr, info, set, first)));

            // transfers
            var tsp = new StackPanel();
            var gridHost = new StackPanel();
            var dir = Ui.Combo(new[] { ("all", L.T("All directions")), ("in", L.T("Incoming only")), ("out", L.T("Outgoing only")) }, "all", 170);
            var assets = xs.GroupBy(x => x.Asset).OrderByDescending(g => g.Count()).Select(g => (g.Key, g.Key + " (" + g.Count() + ")")).ToList();
            assets.Insert(0, ("", L.T("All assets")));
            var asset = Ui.Combo(assets, "", 190);
            var showSpam = new CheckBox { Content = L.F("Show suspicious tokens ({0})", spam), IsChecked = false, VerticalAlignment = VerticalAlignment.Center, Visibility = spam > 0 ? Visibility.Visible : Visibility.Collapsed };
            void Draw()
            {
                gridHost.Children.Clear();
                var rows = (showSpam.IsChecked == true ? set.Items : xs).Where(x => (dir.Val() == "all" || (dir.Val() == "in") == (x.To == addr)) && (asset.Val() == "" || x.Asset == asset.Val())).Select(x => XRow.Of(x, addr)).ToList();
                if (rows.Count == 0) { gridHost.Children.Add(Ui.Empty(L.T("No transfers to show."))); return; }
                gridHost.Children.Add(TransferTable(rows, chain.Key, true));
            }
            dir.SelectionChanged += (_, _) => Draw(); asset.SelectionChanged += (_, _) => Draw();
            showSpam.Click += (_, _) => Draw();
            var bar = Ui.Row(dir, asset, showSpam, Ui.Btn(L.T("Export CSV"), () => ExportCsv(xs, "transfers_" + Fmt.Short(addr).Replace("…", "_")), "plain", ""));
            dir.Margin = new Thickness(0, 0, 8, 8); asset.Margin = new Thickness(0, 0, 8, 8);
            tsp.Children.Add(bar); tsp.Children.Add(gridHost);
            Draw();
            host.Children.Add(Ui.Card(tsp, L.T("Transfers"), L.T("Double-click a row to open the transaction. Right-click for more options.") + (set.Truncated ? " " + L.T("* Only the most recent transfers are loaded.") : "") + (set.Note != "" ? " " + L.T(set.Note) : "")));

            // counterparties
            var cps = Analysis.Counterparties(addr, xs).Select(c => new CpRow { C = c, Name = c.Label != "" ? c.Label : Fmt.Short(c.Address), Type = c.Cat == "" ? "" : Ui.CatName(c.Cat), Asset = c.Asset, In = c.In, Out = c.Out, Count = c.Count, First = Fmt.Day(c.First), Last = Fmt.Day(c.Last) }).ToList();
            if (cps.Count > 0)
                host.Children.Add(Ui.Card(Ui.Table(cps, new[] { new Col("Counterparty", "Name", 230, ltr: true, bold: true), new Col("Type", "Type", 120), new Col("Asset", "Asset", 90, ltr: true), new Col("Received from it", "In", 150, right: true, format: Col.Num), new Col("Sent to it", "Out", 150, right: true, format: Col.Num), new Col("Transfers", "Count", 90, right: true), new Col("First", "First", 105, ltr: true), new Col("Last", "Last", 105, ltr: true) },
                    o => Go("address", (chain.Key, ((CpRow)o).C.Address)),
                    o => new[] { (L.T("Open address"), (Action)(() => Go("address", (chain.Key, ((CpRow)o).C.Address)))), (L.T("Copy address"), () => Ui.Copy(((CpRow)o).C.Address)), (L.T("Label"), () => EditLabel(((CpRow)o).C.Address)) }, 380),
                    L.T("Counterparties"), L.T("Who this address deals with most. Double-click to open one.")));

            // hours
            var hours = Analysis.Hours(addr, xs);
            if (hours.Sum() >= 5)
            {
                int max = Math.Max(1, hours.Max());
                var ug = new UniformGrid { Columns = 24, Height = 120, FlowDirection = FlowDirection.LeftToRight };
                for (int h = 0; h < 24; h++)
                {
                    var col = new DockPanel { Margin = new Thickness(2, 0, 2, 0), ToolTip = $"{h:00}:00 UTC · {hours[h]}" };
                    var lab = new TextBlock { Text = h.ToString("00"), FontSize = 10.5, Foreground = Ui.Muted, HorizontalAlignment = HorizontalAlignment.Center };
                    DockPanel.SetDock(lab, Dock.Bottom); col.Children.Add(lab);
                    col.Children.Add(new Border { Background = Ui.Brand, VerticalAlignment = VerticalAlignment.Bottom, Height = Math.Max(hours[h] > 0 ? 2 : 0, 96.0 * hours[h] / max), CornerRadius = new CornerRadius(2, 2, 0, 0) });
                    ug.Children.Add(col);
                }
                host.Children.Add(Ui.Card(ug, L.T("Outgoing activity by hour (UTC)"), L.T("Quiet hours repeated every day hint at the operator's time zone or working hours.")));
            }
        }

        public class NetRow { public Chain C; public string Network { get; set; } public string Activity { get; set; } public string Balance { get; set; } public string Tx { get; set; } }

        public class CpRow
        {
            public Counterparty C;
            public string Name { get; set; } public string Type { get; set; } public string Asset { get; set; }
            public decimal In { get; set; } public decimal Out { get; set; } public int Count { get; set; }
            public string First { get; set; } public string Last { get; set; }
        }

        public DataGrid TransferTable(List<XRow> rows, string chain, bool withDir, double maxHeight = 440)
        {
            var cols = new List<Col> { new Col("Time (UTC)", "Time", 150, ltr: true) };
            if (withDir) cols.Add(new Col("Direction", "Dir", 70, bold: true));
            cols.AddRange(new[] { new Col("From", "From", 180, ltr: true), new Col("To", "To", 180, ltr: true), new Col("Amount", "Amount", 150, right: true, bold: true, format: Col.Num), new Col("Asset", "Asset", 90, ltr: true), new Col("Type", "Kind", 80), new Col("Transaction", "Hash", 0, ltr: true, mono: true, star: true) });
            return Ui.Table(rows, cols.ToArray(), o => Go("tx", (chain, ((XRow)o).X.Hash)), o =>
            {
                var x = ((XRow)o).X;
                return new[] { (L.T("Open transaction"), (Action)(() => Go("tx", (chain, x.Hash)))), (L.T("Open sender"), () => Go("address", (chain, x.From))), (L.T("Open receiver"), () => Go("address", (chain, x.To))), (L.T("Copy transaction hash"), () => Ui.Copy(x.Hash)), (L.T("Add transaction to case"), () => AddToCase(chain, "tx", x.Hash)) };
            }, maxHeight);
        }

        public static void ExportCsv(IEnumerable<Xfer> xs, string name)
        {
            string path = SaveFile(name + ".csv", "CSV|*.csv"); if (path == null) return;
            var sb = new StringBuilder("time_utc,chain,from,from_label,to,to_label,amount,asset,token_contract,type,tx_hash\n");
            foreach (var x in xs) sb.AppendLine(string.Join(",", Fmt.Time(x.Time), x.Chain, x.From, Fmt.Csv(Labels.Find(x.From)?.Name), x.To, Fmt.Csv(Labels.Find(x.To)?.Name), Fmt.Num(x.Amount), Fmt.Csv(x.Asset), x.Contract, x.Kind, x.Hash));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            Toast(L.T("Saved"));
        }

        // ---------------------------------------------------------------- transaction

        FrameworkElement PageTx((string chain, string hash) a)
        {
            var root = new StackPanel();
            root.Children.Add(Head(L.T("Transaction"), null));
            var host = new StackPanel(); root.Children.Add(host);
            Load(host, async (ct, say) =>
            {
                if (a.chain == "btc?")
                {
                    try { return (chain: "btc", list: await Chains.Get("btc").Api.Tx(a.hash, ct)); }
                    catch (ApiException) { return (chain: "tron", list: await Chains.Get("tron").Api.Tx(a.hash, ct)); }
                }
                return (chain: a.chain, list: await Chains.Get(a.chain).Api.Tx(a.hash, ct));
            }, r =>
            {
                var chain = Chains.Get(r.chain);
                Store.AddRecent(r.chain, a.hash);
                var head = new StackPanel();
                var code = Ui.Code(a.hash, 14.5); code.FontWeight = FontWeights.SemiBold; head.Children.Add(code);
                var meta = Ui.Tx(chain.Name + "  ·  " + Fmt.Time(r.list.Max(x => x.Time)) + " UTC" + (r.list[0].Block > 0 ? "  ·  " + L.T("block") + " " + r.list[0].Block : ""), 13, false, Ui.Muted); meta.Margin = new Thickness(0, 4, 0, 0);
                head.Children.Add(meta);
                var acts = Ui.Row(Ui.Btn(L.T("Add to case"), () => AddToCase(r.chain, "tx", a.hash), "plain", ""), Ui.Btn(L.T("Copy"), () => Ui.Copy(a.hash), "plain", ""),
                    chain.Key == "demo" ? null : Ui.Btn(L.T("Open in explorer"), () => Ui.Open(chain.TxUrl(a.hash)), "plain", ""),
                    chain.Key == "demo" ? null : Ui.Btn(L.T("Archive the explorer page"), () => Ui.Open("https://archive.ph/?run=1&url=" + Uri.EscapeDataString(chain.TxUrl(a.hash))), "plain", ""));
                acts.Margin = new Thickness(0, 12, 0, -8); head.Children.Add(acts);
                host.Children.Add(Ui.Card(head));

                var list = new StackPanel();
                foreach (var x in r.list.OrderByDescending(x => x.Amount == 0 ? 0 : 1))
                {
                    var g = new Grid { Margin = new Thickness(0, 0, 0, 2) };
                    foreach (double w in new double[] { 250, 40, 250, 0 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
                    void Cell(int c, FrameworkElement e) { Grid.SetColumn(e, c); g.Children.Add(e); }
                    FrameworkElement Party(string addr)
                    {
                        var sp = new StackPanel { Orientation = Orientation.Horizontal };
                        if (string.IsNullOrEmpty(addr) || addr == "coinbase" || addr == "unknown") { sp.Children.Add(Ui.Tx(addr == "coinbase" ? L.T("Newly mined coins") : "—", 13, false, Ui.Muted)); return sp; }
                        sp.Children.Add(Ui.AddrLink(r.chain, addr));
                        var l = Labels.Find(addr); if (l != null) { var ch = Ui.Chip(Ui.CatName(l.Cat), Ui.CatBrush(l.Cat)); ch.Margin = new Thickness(8, 0, 0, 0); sp.Children.Add(ch); }
                        return sp;
                    }
                    Cell(0, Party(x.From));
                    var arrow = Ui.Glyph(L.Ar ? "" : "", 14, Ui.Muted); Cell(1, arrow);
                    Cell(2, Party(x.To));
                    var amt = Ui.Tx(Fmt.Amt(x.Amount) + " " + x.Asset + (x.Kind == "internal" ? "  (" + L.T("internal") + ")" : ""), 14, true, Ui.Text, false); amt.FlowDirection = FlowDirection.LeftToRight; amt.HorizontalAlignment = HorizontalAlignment.Left;
                    Cell(3, amt);
                    list.Children.Add(new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 9, 0, 9), Child = g });
                }
                host.Children.Add(Ui.Card(list, L.T("Value moved in this transaction"), L.F("{0} movement(s). Click an address to profile it.", r.list.Count)));
                if (chain.Family == "btc") host.Children.Add(Ui.Notice("info", L.T("Bitcoin note"), L.T("A Bitcoin transaction can have many inputs and outputs. The sender shown is the largest input; outputs back to an input address (change) are hidden.")));
            });
            return root;
        }
    }
}
