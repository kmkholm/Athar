using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Athar
{
    public partial class MainWindow
    {
        public class SharedRow { public string A; public string Name { get; set; } public string Address { get; set; } public int By { get; set; } public string Who { get; set; } public int Transfers { get; set; } }
        public class MatchUi
        {
            public MatchRow M;
            public string DepTime { get; set; } public decimal DepAmount { get; set; } public string Asset { get; set; } public string WdTime { get; set; } public decimal WdAmount { get; set; }
            public string To { get; set; } public string Delay { get; set; } public string Diff { get; set; } public int Candidates { get; set; } public string Confidence { get; set; }
        }

        FrameworkElement PageLink(string preset)
        {
            var root = new StackPanel();
            root.Children.Add(Head(L.T("Link addresses"), L.T("Give two or more addresses. Athar looks for direct transfers between them, counterparties they share and a common first funder — the usual signs that wallets belong to the same operator.")));
            var box = Ui.Input(preset ?? "", 0, true, true); box.MinHeight = 120;
            var chain = Ui.ChainCombo(chainBox.Val(), 180);
            var skip = new CheckBox { Content = L.T("Ignore shared exchanges, bridges and contracts (everyone uses them)"), IsChecked = true };
            var result = new StackPanel();
            void Run()
            {
                var c = Chains.Get(chain.Val());
                var list = box.Text.Split(new[] { '\n', '\r', ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => Addr.Detect(s) is var d && d.kind == "address" && d.family == c.Family).Select(s => Addr.Norm(c.Family, s)).Distinct().ToList();
                if (list.Count < 2) { Toast(L.T("Enter at least two valid addresses for the selected network."), true); return; }
                if (list.Count > 12) { Toast(L.T("Use at most 12 addresses at a time."), true); return; }
                bool sk = skip.IsChecked == true;
                Load(result, async (ct, say) =>
                {
                    var data = new Dictionary<string, List<Xfer>>();
                    int i = 0;
                    foreach (var a in list)
                    {
                        say(L.F("Reading address {0} of {1}…", ++i, list.Count));
                        try { Labels.Learn(await c.Api.Info(a, ct)); } catch (ApiException) { }
                        data[a] = (await c.Api.Transfers(a, 500, false, 0, 0, ct)).Items;
                    }
                    var res = Analysis.Link(data, sk);
                    int j = 0;
                    foreach (var s in res.Shared.Take(8))
                    {
                        say(L.F("Reading labels of shared counterparties… {0}", ++j));
                        try { Labels.Learn(await c.Api.Info(s.address, ct)); } catch (ApiException) { }
                    }
                    return Analysis.Link(data, sk);
                }, r =>
                {
                    result.Children.Add(Ui.Row(Ui.Stat(L.T("Direct transfers"), r.Direct.Count.ToString(), null, r.Direct.Count > 0 ? Ui.Red : null), Ui.Stat(L.T("Shared counterparties"), r.Shared.Count.ToString(), null), Ui.Stat(L.T("Common first funders"), r.Funders.Count.ToString(), null, r.Funders.Count > 0 ? Ui.Red : null)));
                    foreach (var f in r.Funders)
                        result.Children.Add(Ui.Notice("high", L.T("Same first funder"), Labels.Show(f.funder) + "  →  " + string.Join(", ", f.funded.Select(Fmt.Short)), Ui.Btn(L.T("Open address"), () => Go("address", (c.Key, f.funder)), "link")));
                    if (r.Direct.Count > 0)
                        result.Children.Add(Ui.Card(TransferTable(r.Direct.OrderBy(x => x.Time).Select(x => XRow.Of(x)).ToList(), c.Key, false, 320), L.T("Direct transfers between the addresses"), L.T("Funds moving directly between two of your addresses is the strongest link.")));
                    if (r.Shared.Count > 0)
                    {
                        var rows = r.Shared.Take(200).Select(s => new SharedRow { A = s.address, Name = Labels.Find(s.address)?.Name ?? L.T("Unlabelled wallet"), Address = s.address, By = s.sharedBy.Count, Who = string.Join(", ", s.sharedBy.Select(Fmt.Short)), Transfers = s.transfers }).ToList();
                        result.Children.Add(Ui.Card(Ui.Table(rows, new[] { new Col("Name", "Name", 200, bold: true), new Col("Address", "Address", 330, ltr: true, mono: true), new Col("Shared by", "By", 90, right: true), new Col("Which of your addresses", "Who", 300, ltr: true), new Col("Transfers", "Transfers", 0, right: true, star: true) },
                            o => Go("address", (c.Key, ((SharedRow)o).A)), o => new[] { (L.T("Open address"), (Action)(() => Go("address", (c.Key, ((SharedRow)o).A)))), (L.T("Copy address"), () => Ui.Copy(((SharedRow)o).A)) }, 380),
                            L.T("Shared counterparties"), L.T("An unlabelled wallet used by several of your addresses (for example the same deposit address) deserves a closer look.")));
                    }
                    if (r.Direct.Count == 0 && r.Shared.Count == 0 && r.Funders.Count == 0)
                        result.Children.Add(Ui.Notice("ok", L.T("No link found in the loaded transfers"), L.T("That does not prove the addresses are unrelated: only the latest 500 transfers per type of each address were compared.")));
                });
            }
            var form = new StackPanel();
            form.Children.Add(Ui.Field(L.T("Addresses (one per line)"), box));
            form.Children.Add(Ui.Row(Ui.Field(L.T("Network"), chain)));
            form.Children.Add(skip);
            form.Children.Add(Ui.Row(Ui.Btn(L.T("Find links"), Run, "primary", ""),
                Ui.Btn(L.T("Fill with the demo addresses"), () => { box.Text = Demo.Hacker + "\n" + Demo.Hacker2; foreach (ComboBoxItem i in chain.Items) if ((string)i.Tag == "demo") chain.SelectedItem = i; }, "plain")));
            root.Children.Add(Ui.Card(form));
            root.Children.Add(result);
            return root;
        }

        FrameworkElement PageMatch(object preset = null)
        {
            var root = new StackPanel();
            root.Children.Add(Head(L.T("Match deposits and withdrawals"), L.T("When funds enter a bridge or a privacy tool the trail breaks. This tool pairs each deposit made by the suspect with later withdrawals of almost the same amount, and tells you how many candidates fit.")));
            var chain = Ui.ChainCombo(chainBox.Val(), 180);
            var suspect = Ui.Input("", 430, true);
            var service = Ui.Input("", 430, true);
            var chain2 = Ui.ChainCombo(chainBox.Val(), 180);
            var service2 = Ui.Input("", 430, true);
            var window = Ui.Combo(new[] { ("1", "1 h"), ("6", "6 h"), ("24", "24 h"), ("72", "3 d"), ("168", "7 d") }, "24", 90);
            var tol = Ui.Combo(new[] { "0.1", "0.5", "1", "2", "5" }.Select(i => (i, i + " %")), "1", 90);
            var same = new CheckBox { Content = L.T("Same asset on both sides"), IsChecked = true };
            var known = Ui.Combo(new[] { ("", L.T("Pick a known service…")) }.Concat(Labels.BuiltinAll.Where(k => k.Value.Cat == "mixer" || k.Value.Cat == "bridge").Select(k => (k.Key, k.Value.Name))), "", 260);
            known.SelectionChanged += (_, _) => { if (known.Val() != "") service.Text = known.Val(); };
            if (preset is ValueTuple<string, string, string> pre)
            {
                suspect.Text = pre.Item2; service.Text = pre.Item3;
                foreach (var cb in new[] { chain, chain2 }) foreach (ComboBoxItem i in cb.Items) if ((string)i.Tag == pre.Item1) cb.SelectedItem = i;
            }
            var result = new StackPanel();
            void Run()
            {
                var c1 = Chains.Get(chain.Val()); var c2 = Chains.Get(chain2.Val());
                string s = Addr.Norm(c1.Family, suspect.Text), v1 = Addr.Norm(c1.Family, service.Text), v2 = service2.Text.Trim() == "" ? (c1 == c2 ? v1 : "") : Addr.Norm(c2.Family, service2.Text);
                if (Addr.Detect(s).kind != "address" || Addr.Detect(v1).kind != "address") { Toast(L.T("Enter the suspect address and the service address."), true); return; }
                if (v2 == "") { Toast(L.T("Enter the service address on the payout network."), true); return; }
                long win = long.Parse(window.Val()) * 3600; double tl = double.Parse(tol.Val(), System.Globalization.CultureInfo.InvariantCulture); bool sa = same.IsChecked == true;
                Load(result, async (ct, say) =>
                {
                    say(L.T("Reading the suspect's deposits…"));
                    var deps = (await c1.Api.Transfers(s, 1000, false, 0, 0, ct)).Items.Where(x => x.From == s && x.To == v1).ToList();
                    if (deps.Count == 0) return (deps, rows: new List<MatchRow>(), wds: 0);
                    say(L.T("Reading the service's payouts in the time window…"));
                    var w = await c2.Api.Transfers(v2, 2000, true, deps.Min(x => x.Time), deps.Max(x => x.Time) + win, ct);
                    var wds = w.Items.Where(x => x.From == v2 && x.To != s).ToList();
                    return (deps, rows: Analysis.Match(deps, wds, win, tl, sa), wds: wds.Count);
                }, r =>
                {
                    if (r.deps.Count == 0) { result.Children.Add(Ui.Notice("medium", L.T("No deposit found"), L.T("The suspect address did not send anything directly to that service address in its latest 1000 transfers."))); return; }
                    result.Children.Add(Ui.Row(Ui.Stat(L.T("Deposits"), r.deps.Count.ToString(), null), Ui.Stat(L.T("Payouts examined"), r.wds.ToString(), null), Ui.Stat(L.T("Strong matches"), r.rows.Count(x => x.Confidence == "high").ToString(), null, Ui.Green)));
                    if (r.rows.Count == 0) { result.Children.Add(Ui.Notice("medium", L.T("No payout fits"), L.T("Try a longer time window or a larger tolerance (fees can be several percent)."))); return; }
                    var rows = r.rows.OrderBy(x => x.Confidence == "high" ? 0 : x.Confidence == "medium" ? 1 : 2).ThenBy(x => x.Dep.Time).Select(m => new MatchUi { M = m, DepTime = Fmt.Time(m.Dep.Time), DepAmount = m.Dep.Amount, Asset = m.Dep.Asset, WdTime = Fmt.Time(m.Wd.Time), WdAmount = m.Wd.Amount, To = Labels.Show(m.Wd.To), Delay = Fmt.Dur(m.Delay), Diff = m.DiffPct.ToString("0.00") + " %", Candidates = m.Candidates, Confidence = L.T(m.Confidence == "high" ? "Strong" : m.Confidence == "medium" ? "Medium" : "Weak") }).ToList();
                    result.Children.Add(Ui.Card(Ui.Table(rows, new[] { new Col("Deposit (UTC)", "DepTime", 150, ltr: true), new Col("Deposited", "DepAmount", 130, right: true, format: Col.Num), new Col("Asset", "Asset", 80, ltr: true), new Col("Payout (UTC)", "WdTime", 150, ltr: true), new Col("Paid out", "WdAmount", 130, right: true, format: Col.Num), new Col("Paid to", "To", 170, ltr: true, bold: true), new Col("Delay", "Delay", 90, ltr: true), new Col("Difference", "Diff", 90, right: true), new Col("Candidates", "Candidates", 90, right: true), new Col("Match", "Confidence", 0, bold: true, star: true) },
                        o => Go("address", (c2.Key, ((MatchUi)o).M.Wd.To)),
                        o => { var m = ((MatchUi)o).M; return new[] { (L.T("Open receiver"), (Action)(() => Go("address", (c2.Key, m.Wd.To)))), (L.T("Open deposit transaction"), () => Go("tx", (c1.Key, m.Dep.Hash))), (L.T("Open payout transaction"), () => Go("tx", (c2.Key, m.Wd.Hash))), (L.T("Add receiver to case"), () => AddToCase(c2.Key, "address", m.Wd.To, "possible payout")) }; }),
                        L.T("Candidate pairs"), L.T("“Strong” means a single payout fits and the amount is not a round number. Double-click to open the receiving address.")));
                    result.Children.Add(Ui.Notice("info", L.T("Limits of this method"), L.T("Fixed-amount pools (such as 1, 10 or 100 ETH) make amounts useless, so only timing remains and matches are weak. A match is a lead to corroborate with other evidence.")));
                });
            }
            var form = new StackPanel();
            form.Children.Add(Ui.Row(Ui.Field(L.T("Deposit network"), chain), Ui.Field(L.T("Suspect address (the depositor)"), suspect)));
            form.Children.Add(Ui.Row(Ui.Field(L.T("Service address the deposit went to"), service), Ui.Field(L.T("or choose"), known)));
            form.Children.Add(Ui.Row(Ui.Field(L.T("Payout network"), chain2), Ui.Field(L.T("Service address that pays out (empty = same)"), service2, L.T("For a bridge this is the bridge's address on the destination network."))));
            form.Children.Add(Ui.Row(Ui.Field(L.T("Time window"), window), Ui.Field(L.T("Amount tolerance"), tol)));
            form.Children.Add(same);
            form.Children.Add(Ui.Row(Ui.Btn(L.T("Find matches"), Run, "primary", ""),
                Ui.Btn(L.T("Fill with the demo"), () => { suspect.Text = Demo.Hacker; service.Text = Demo.Mixer; service2.Text = ""; foreach (var cb in new[] { chain, chain2 }) foreach (ComboBoxItem i in cb.Items) if ((string)i.Tag == "demo") cb.SelectedItem = i; }, "plain")));
            root.Children.Add(Ui.Card(form));
            root.Children.Add(result);
            return root;
        }
    }
}
