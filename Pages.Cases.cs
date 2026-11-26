using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Athar
{
    public partial class MainWindow
    {
        // ---------------------------------------------------------------- watchlist

        public class WatchRow { public WatchItem W; public string Name { get; set; } public string Address { get; set; } public string Network { get; set; } public string Note { get; set; } public string Since { get; set; } }
        public class AlertRow { public Alert A; public string Time { get; set; } public string Watched { get; set; } public string Dir { get; set; } public string Other { get; set; } public decimal Amount { get; set; } public string Asset { get; set; } }

        public void AddWatch(string chain, string addr, string note = "")
        {
            if (chain == "demo") { Toast(L.T("The demo network cannot be watched."), true); return; }
            if (Store.S.Watch.Any(w => w.Address == addr && w.Chain == chain)) { Toast(L.T("Already on the watchlist")); return; }
            Store.S.Watch.Add(new WatchItem { Chain = chain, Address = addr, Note = note, LastTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            Store.Save();
            Toast(L.T("Added to the watchlist"));
        }

        bool watching;
        public async void CheckWatch(bool manual)
        {
            if (watching || Store.S.Watch.Count == 0) { if (manual && Store.S.Watch.Count == 0) Toast(L.T("The watchlist is empty.")); return; }
            watching = true; int found = 0; string err = null;
            try
            {
                Http.Fresh = true;
                foreach (var w in Store.S.Watch.ToList())
                {
                    try
                    {
                        var set = await Chains.Get(w.Chain).Api.Transfers(w.Address, 50, false, w.LastTime + 1, 0, CancellationToken.None);
                        foreach (var x in set.Items.Where(x => x.Time > w.LastTime).OrderBy(x => x.Time))
                        {
                            Store.S.Alerts.Insert(0, new Alert { Seen = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), X = x, Watched = w.Address });
                            found++;
                        }
                        if (set.Items.Count > 0) w.LastTime = Math.Max(w.LastTime, set.Items.Max(x => x.Time));
                    }
                    catch (Exception e) { err = e.Message; }
                }
            }
            finally { Http.Fresh = false; watching = false; }
            if (Store.S.Alerts.Count > 300) Store.S.Alerts.RemoveRange(300, Store.S.Alerts.Count - 300);
            Store.Save();
            if (found > 0) { Toast(L.F("{0} new transfer(s) on watched addresses", found)); System.Media.SystemSounds.Exclamation.Play(); DrawNav(); if (page == "watch") Go("watch"); }
            else if (manual) Toast(err != null ? err : L.T("No new transfers."), err != null);
        }

        FrameworkElement PageWatch()
        {
            var root = new StackPanel();
            root.Children.Add(Head(L.T("Watchlist"), L.T("Athar checks these addresses while it is open and alerts you when they move funds — useful for dormant wallets holding stolen money."),
                Ui.Btn(L.T("Check now"), () => CheckWatch(true), "primary", "")));
            var addr = Ui.Input("", 430, true); var chain = Ui.ChainCombo(chainBox.Val(), 180, false); var note = Ui.Input("", 260);
            var every = Ui.Combo(new[] { 2, 5, 10, 30, 60 }.Select(i => (i.ToString(), L.F("every {0} min", i))), Store.S.WatchMin.ToString(), 150);
            every.SelectionChanged += (_, _) => { Store.S.WatchMin = int.Parse(every.Val()); Store.Save(); watchTimer.Interval = TimeSpan.FromMinutes(Store.S.WatchMin); };
            root.Children.Add(Ui.Card(Ui.Row(Ui.Field(L.T("Address"), addr), Ui.Field(L.T("Network"), chain), Ui.Field(L.T("Note"), note), Ui.Field(L.T("Check"), every),
                Ui.Field(" ", Ui.Btn(L.T("Add"), () =>
                {
                    var c = Chains.Get(chain.Val()); var d = Addr.Detect(addr.Text);
                    if (d.kind != "address" || d.family != c.Family) { Toast(L.T("Enter a valid address for the selected network."), true); return; }
                    AddWatch(c.Key, Addr.Norm(c.Family, addr.Text), note.Text.Trim()); Go("watch");
                }, "primary", ""))), L.T("Add an address")));

            if (Store.S.Alerts.Count > 0)
            {
                var rows = Store.S.Alerts.Select(a => new AlertRow { A = a, Time = Fmt.Time(a.X.Time), Watched = Labels.Show(a.Watched), Dir = a.X.From == a.Watched ? L.T("OUT") : L.T("IN"), Other = Labels.Show(a.X.From == a.Watched ? a.X.To : a.X.From), Amount = a.X.Amount, Asset = a.X.Asset }).ToList();
                var sp = new StackPanel();
                sp.Children.Add(Ui.Table(rows, new[] { new Col("Time (UTC)", "Time", 150, ltr: true), new Col("Watched address", "Watched", 200, ltr: true, bold: true), new Col("Direction", "Dir", 80, bold: true), new Col("Counterparty", "Other", 200, ltr: true), new Col("Amount", "Amount", 150, right: true, format: Col.Num), new Col("Asset", "Asset", 0, ltr: true, star: true) },
                    o => Go("tx", (((AlertRow)o).A.X.Chain, ((AlertRow)o).A.X.Hash)), null, 300));
                var clr = Ui.Btn(L.T("Clear alerts"), () => { Store.S.Alerts.Clear(); Store.Save(); Go("watch"); }); clr.Margin = new Thickness(0, 10, 0, 0); clr.HorizontalAlignment = HorizontalAlignment.Left;
                sp.Children.Add(clr);
                root.Children.Add(Ui.Card(sp, L.T("Alerts"), L.T("Double-click to open the transaction.")));
            }

            if (Store.S.Watch.Count == 0) root.Children.Add(Ui.Card(Ui.Empty(L.T("Nothing is being watched yet. Add an address above, or press “Watch” on any address profile."))));
            else
            {
                var rows = Store.S.Watch.Select(w => new WatchRow { W = w, Name = Labels.Find(w.Address)?.Name ?? "", Address = w.Address, Network = Chains.All.FirstOrDefault(c => c.Key == w.Chain)?.Name ?? w.Chain, Note = w.Note, Since = Fmt.Time(w.LastTime) }).ToList();
                root.Children.Add(Ui.Card(Ui.Table(rows, new[] { new Col("Address", "Address", 340, ltr: true, mono: true), new Col("Name", "Name", 170, bold: true), new Col("Network", "Network", 120), new Col("Note", "Note", 200), new Col("Checked up to (UTC)", "Since", 0, ltr: true, star: true) },
                    o => Go("address", (((WatchRow)o).W.Chain, ((WatchRow)o).W.Address)),
                    o => new[] { (L.T("Open address"), (Action)(() => Go("address", (((WatchRow)o).W.Chain, ((WatchRow)o).W.Address)))), (L.T("Stop watching"), () => { Store.S.Watch.Remove(((WatchRow)o).W); Store.Save(); Go("watch"); }) }, 340),
                    L.T("Watched addresses"), L.T("Right-click a row to stop watching it.")));
            }
            return root;
        }

        // ---------------------------------------------------------------- cases

        FrameworkElement PageCases()
        {
            var root = new StackPanel();
            root.Children.Add(Head(L.T("Cases & reports"), L.T("A case keeps the addresses, transactions, traces and your notes together, and produces one printable report."),
                Ui.Btn(L.T("New case"), () => { var r = Ask(L.T("New case"), (L.T("Case name"), "")); if (r != null && r[0] != "") { var c = Store.NewCase(r[0]); Store.S.ActiveCase = c.Id; Store.Save(); Go("case", c); } }, "primary", "")));
            var all = Store.AllCases();
            if (all.Count == 0) { root.Children.Add(Ui.Card(Ui.Steps("Create a case and give it a name.", "While you search and trace, press “Add to case” or “Save to case”.", "Open the case, write your summary and findings, then export the report."))); return root; }
            var active = ActiveCase(false);
            foreach (var c in all)
            {
                var d = new DockPanel();
                var btns = Ui.Row(Ui.Btn(L.T("Open"), () => Go("case", c), "primary"),
                    c.Id == active?.Id ? null : Ui.Btn(L.T("Make active"), () => { Store.S.ActiveCase = c.Id; Store.Save(); Go("cases"); }),
                    Ui.Btn(L.T("Delete"), () => { if (MessageBox.Show(L.F("Delete case “{0}”? This cannot be undone.", c.Name), "Athar", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) { Store.DeleteCase(c); Go("cases"); } }, "danger"));
                btns.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(btns, Dock.Right); d.Children.Add(btns);
                var sp = new StackPanel();
                var line = new WrapPanel(); var nm = Ui.Tx(c.Name, 16, true); nm.Margin = new Thickness(0, 0, 10, 0); line.Children.Add(nm);
                if (c.Id == active?.Id) line.Children.Add(Ui.Chip(L.T("Active — new items go here"), Ui.Green));
                sp.Children.Add(line);
                sp.Children.Add(Ui.Tx(L.F("{0} item(s) · {1} trace(s) · opened {2}", c.Items.Count, c.Traces.Count, Fmt.Day(c.Created)), 13, false, Ui.Muted));
                d.Children.Add(sp);
                root.Children.Add(Ui.Card(d));
            }
            return root;
        }

        public class ItemRow { public CaseItem I; public string Kind { get; set; } public string Network { get; set; } public string Name { get; set; } public string Value { get; set; } public string Role { get; set; } public string Note { get; set; } }

        FrameworkElement PageCase(CaseFile c)
        {
            if (c == null) return PageCases();
            var root = new StackPanel();
            root.Children.Add(Head(c.Name, L.F("Opened {0}", Fmt.Day(c.Created)),
                Ui.Btn(L.T("Export report (HTML)"), () => { string p = SaveFile(c.Name + ".html", "HTML|*.html"); if (p != null) { File.WriteAllText(p, Report.CaseHtml(c, Store.S.Lang), new UTF8Encoding(true)); Ui.Open(p); } }, "primary", ""),
                Ui.Btn(L.T("Rename"), () => { var r = Ask(L.T("Rename"), (L.T("Case name"), c.Name)); if (r != null && r[0] != "") { c.Name = r[0]; Store.SaveCase(c); Go("case", c); } }),
                Ui.Btn(L.T("All cases"), () => Go("cases"))));

            var notes = Ui.Input(c.Notes, 0, false, true); notes.MinHeight = 110;
            var nsp = new StackPanel(); nsp.Children.Add(notes);
            var nb = Ui.Btn(L.T("Save summary"), () => { c.Notes = notes.Text; Store.SaveCase(c); Toast(L.T("Saved")); }, "primary"); nb.Margin = new Thickness(0, 10, 0, 0); nb.HorizontalAlignment = HorizontalAlignment.Left; nsp.Children.Add(nb);
            root.Children.Add(Ui.Card(nsp, L.T("Summary"), L.T("What happened, when, how much, and what you are trying to establish.")));

            root.Children.Add(AiCard(L.T("AI draft of the case summary"), () => Ai.CasePrompt(c), L.T("Use as the summary"), text => { c.Notes = text; Store.SaveCase(c); Go("case", c); }));

            var fsp = new StackPanel();
            int idx = 0;
            foreach (var f in c.Findings.ToList())
            {
                int k = idx++;
                var d = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var rm = Ui.Btn("", () => { c.Findings.RemoveAt(k); Store.SaveCase(c); Go("case", c); }, "link", ""); DockPanel.SetDock(rm, Dock.Right); d.Children.Add(rm);
                d.Children.Add(Ui.Tx((k + 1) + ". " + f, 13.5));
                fsp.Children.Add(d);
            }
            if (c.Findings.Count == 0) fsp.Children.Add(Ui.Empty(L.T("No findings written yet.")));
            var fa = Ui.Btn(L.T("Add finding"), () => { var r = Ask(L.T("Add finding"), (L.T("Finding (one sentence, with the evidence it rests on)"), "")); if (r != null && r[0] != "") { c.Findings.Add(r[0]); Store.SaveCase(c); Go("case", c); } }, "plain", ""); fa.Margin = new Thickness(0, 8, 0, 0); fa.HorizontalAlignment = HorizontalAlignment.Left;
            fsp.Children.Add(fa);
            root.Children.Add(Ui.Card(fsp, L.T("Findings")));

            if (c.Items.Count == 0) root.Children.Add(Ui.Card(Ui.Empty(L.T("No addresses or transactions yet. Use “Add to case” on any address or transaction page.")), L.T("Addresses and transactions")));
            else
            {
                var rows = c.Items.Select(i => new ItemRow { I = i, Kind = L.T(i.Kind == "tx" ? "Transaction" : "Address"), Network = Chains.All.FirstOrDefault(x => x.Key == i.Chain)?.Name ?? i.Chain, Name = i.Kind == "address" ? Labels.Find(i.Value)?.Name ?? "" : "", Value = i.Value, Role = i.Role, Note = i.Note }).ToList();
                root.Children.Add(Ui.Card(Ui.Table(rows, new[] { new Col("Kind", "Kind", 100), new Col("Network", "Network", 110), new Col("Name", "Name", 150, bold: true), new Col("Address / hash", "Value", 360, ltr: true, mono: true), new Col("Role", "Role", 150), new Col("Note", "Note", 0, star: true) },
                    o => { var i = ((ItemRow)o).I; Go(i.Kind == "tx" ? "tx" : "address", (i.Chain, i.Value)); },
                    o =>
                    {
                        var i = ((ItemRow)o).I;
                        return new[] { (L.T("Open"), (Action)(() => Go(i.Kind == "tx" ? "tx" : "address", (i.Chain, i.Value)))),
                            (L.T("Edit role and note"), () => { var r = Ask(L.T("Edit role and note"), (L.T("Role (victim, suspect, hop, exchange deposit …)"), i.Role), (L.T("Note"), i.Note)); if (r != null) { i.Role = r[0]; i.Note = r[1]; Store.SaveCase(c); Go("case", c); } }),
                            (L.T("Copy"), () => Ui.Copy(i.Value)), (L.T("Remove from case"), () => { c.Items.Remove(i); Store.SaveCase(c); Go("case", c); }) };
                    }, 340), L.T("Addresses and transactions"), L.T("Double-click to open. Right-click to edit the role, add a note or remove.")));
            }

            var tsp = new StackPanel();
            if (c.Traces.Count == 0) tsp.Children.Add(Ui.Empty(L.T("No saved traces. Run a trace and press “Save to case”.")));
            foreach (var g in c.Traces.ToList())
            {
                var d = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var btns = Ui.Row(Ui.Btn(L.T("Open"), () => Go("trace", g), "plain"), Ui.Btn("", () => { c.Traces.Remove(g); Store.SaveCase(c); Go("case", c); }, "link", ""));
                DockPanel.SetDock(btns, Dock.Right); d.Children.Add(btns);
                var sp = new StackPanel();
                sp.Children.Add(Ui.Code(g.Start.Replace("\n", ", "), 13));
                sp.Children.Add(Ui.Tx(L.T(g.Forward ? "Forward" : "Backward") + " · " + (Chains.All.FirstOrDefault(x => x.Key == g.Chain)?.Name ?? g.Chain) + " · " + L.F("{0} addresses, {1} flows", g.Nodes.Count, g.Edges.Count) + " · " + Fmt.Time(g.Created) + " UTC", 12.5, false, Ui.Muted));
                d.Children.Add(sp);
                tsp.Children.Add(d);
            }
            root.Children.Add(Ui.Card(tsp, L.T("Saved traces")));
            return root;
        }

        // ---------------------------------------------------------------- labels

        public class LabelRow { public UserLabel U; public string Name { get; set; } public string Type { get; set; } public string Address { get; set; } public string Note { get; set; } }

        FrameworkElement PageLabels()
        {
            var root = new StackPanel();
            root.Children.Add(Head(L.T("Labels"), L.T("Names make a trace readable. Your own labels win over the explorer's tags and the small built-in list."),
                Ui.Btn(L.T("Add label"), () => { var r = Ask(L.T("Add label"), (L.T("Address"), "")); if (r != null && Addr.Detect(r[0]).kind == "address") { EditLabel(Addr.Norm(Addr.Detect(r[0]).family, r[0])); Go("labels"); } else if (r != null) Toast(L.T("This is not a valid address or transaction hash."), true); }, "primary", ""),
                Ui.Btn(L.T("Import CSV"), () =>
                {
                    var d = new Microsoft.Win32.OpenFileDialog { Filter = "CSV|*.csv;*.txt" }; if (d.ShowDialog() != true) return;
                    int n = 0;
                    foreach (var line in File.ReadAllLines(d.FileName))
                    {
                        var p = line.Split(','); if (p.Length < 2 || Addr.Detect(p[0]).kind != "address") continue;
                        string a = Addr.Norm(Addr.Detect(p[0]).family, p[0]);
                        Store.UserLabels.RemoveAll(x => string.Equals(x.Address, a, StringComparison.OrdinalIgnoreCase));
                        Store.UserLabels.Add(new UserLabel { Address = a, Name = p[1].Trim().Trim('"'), Cat = p.Length > 2 && Labels.Cats.Contains(p[2].Trim().ToLowerInvariant()) ? p[2].Trim().ToLowerInvariant() : "user", Note = p.Length > 3 ? p[3].Trim() : "" }); n++;
                    }
                    Store.SaveLabels(); Toast(L.F("{0} label(s) imported", n)); Go("labels");
                }),
                Ui.Btn(L.T("Export CSV"), () => { string p = SaveFile("athar_labels.csv", "CSV|*.csv"); if (p != null) { File.WriteAllLines(p, Store.UserLabels.Select(u => string.Join(",", u.Address, Fmt.Csv(u.Name), u.Cat, Fmt.Csv(u.Note))), new UTF8Encoding(true)); Toast(L.T("Saved")); } })));

            if (Store.UserLabels.Count == 0) root.Children.Add(Ui.Card(Ui.Empty(L.T("You have no labels yet. Press “Label” on any address, or import a CSV file: address,name,category,note.")), L.T("My labels")));
            else
            {
                var rows = Store.UserLabels.Select(u => new LabelRow { U = u, Name = u.Name, Type = Ui.CatName(u.Cat), Address = u.Address, Note = u.Note }).ToList();
                root.Children.Add(Ui.Card(Ui.Table(rows, new[] { new Col("Name", "Name", 220, bold: true), new Col("Type", "Type", 120), new Col("Address", "Address", 360, ltr: true, mono: true), new Col("Note", "Note", 0, star: true) },
                    o => { EditLabel(((LabelRow)o).U.Address); Go("labels"); },
                    o => new[] { (L.T("Edit"), (Action)(() => { EditLabel(((LabelRow)o).U.Address); Go("labels"); })), (L.T("Copy address"), () => Ui.Copy(((LabelRow)o).U.Address)), (L.T("Delete"), () => { Store.UserLabels.Remove(((LabelRow)o).U); Store.SaveLabels(); Go("labels"); }) }, 340),
                    L.T("My labels"), L.T("Double-click to edit. Right-click to delete.")));
            }
            var built = Labels.BuiltinAll.OrderBy(k => k.Value.Cat).ThenBy(k => k.Value.Name).Select(k => new LabelRow { Name = k.Value.Name, Type = Ui.CatName(k.Value.Cat), Address = k.Key, Note = "" }).ToList();
            root.Children.Add(Ui.Card(Ui.Table(built, new[] { new Col("Name", "Name", 260, bold: true), new Col("Type", "Type", 120), new Col("Address", "Address", 0, ltr: true, mono: true, star: true) }, null, null, 320),
                L.T("Built-in list (Ethereum)"), L.T("A short list of well-known services. Most labels come live from the explorer; always confirm a label on the explorer before citing it.")));
            return root;
        }

        // ---------------------------------------------------------------- settings

        FrameworkElement PageSettings()
        {
            var root = new StackPanel();
            root.Children.Add(Head(L.T("Settings"), null));
            var examiner = Ui.Input(Store.S.Examiner, 320);
            var key = Ui.Input(Store.S.EtherscanKey, 420, true);
            var cache = Ui.Combo(new[] { ("0", L.T("Off")), ("300", "5 min"), ("900", "15 min"), ("3600", "1 h"), ("86400", "24 h") }, Store.S.CacheSec.ToString(), 120);
            var gap = Ui.Combo(new[] { ("0", L.T("Normal")), ("600", L.T("Slow")), ("1200", L.T("Very slow")) }, Store.S.GapMs.ToString(), 140);
            var sp = new StackPanel();
            sp.Children.Add(Ui.Field(L.T("Examiner name (printed on reports)"), examiner));
            sp.Children.Add(Ui.Field(L.T("Etherscan API key (optional)"), key, L.T("Only needed for Base, Arbitrum, Polygon and BNB Chain. Create a free key at etherscan.io → API Keys. It is stored on this computer only.")));
            var rkey = Ui.Input(Store.S.RelayKey, 420, true);
            sp.Children.Add(Ui.Field(L.T("Relay API key (optional)"), rkey, L.T("Relay bridge lookups work without a key through an older endpoint that is slow and being retired; a key from relay.link keeps them working.")));
            sp.Children.Add(Ui.Row(Ui.Field(L.T("Keep downloaded data for"), cache, L.T("Repeated lookups are instant and spare the explorers.")), Ui.Field(L.T("Request speed"), gap, L.T("Choose Slow if an explorer answers “rate limit”."))));
            sp.Children.Add(Ui.Row(
                Ui.Btn(L.T("Save"), () => { Store.S.Examiner = examiner.Text.Trim(); Store.S.EtherscanKey = key.Text.Trim(); Store.S.RelayKey = rkey.Text.Trim(); Http.HostHeaders["api.relay.link"] = ("x-api-key", Store.S.RelayKey); Store.S.CacheSec = int.Parse(cache.Val()); Store.S.GapMs = int.Parse(gap.Val()); Store.Save(); Chains.ResetAll(); ApplyLang(); Go("settings"); Toast(L.T("Saved")); }, "primary"),
                Ui.Btn(L.T("Clear downloaded data"), () => { Store.ClearCache(); Toast(L.T("Cleared")); }),
                Ui.Btn(L.T("Open data folder"), () => Ui.Open(Store.Data))));
            root.Children.Add(Ui.Card(sp, L.T("General")));

            var ckey = new PasswordBox { Width = 420, Padding = new Thickness(8, 6, 8, 6), FlowDirection = FlowDirection.LeftToRight };
            var cmodel = Ui.Combo(Ai.Models.Select(m => (m, m)), Store.S.ClaudeModel, 220);
            var asp = new StackPanel();
            asp.Children.Add(Ui.Notice(Ai.Ready ? "ok" : "info", Ai.Ready ? L.T("A Claude API key is saved on this computer (encrypted).") : L.T("No Claude API key saved yet.")));
            asp.Children.Add(Ui.Field(L.T("Claude API key"), ckey, L.T("Create a key at platform.claude.com → API keys. It is encrypted with your Windows account and never leaves this computer except to call the Claude API. Usage is billed to your Anthropic account.")));
            asp.Children.Add(Ui.Row(Ui.Field(L.T("Model"), cmodel, L.T("claude-opus-5-5 is the most capable; sonnet and haiku are faster and cheaper."))));
            asp.Children.Add(Ui.Row(
                Ui.Btn(L.T("Save"), () => { if (ckey.Password.Trim() != "") Ai.Key = ckey.Password; Store.S.ClaudeModel = cmodel.Val(); Store.Save(); Go("settings"); Toast(L.T("Saved")); }, "primary"),
                Ui.Btn(L.T("Test the key"), async () => { try { Toast(L.T("Asking Claude…")); await Ai.Ask("Reply with the single word: ready", System.Threading.CancellationToken.None); Toast(L.T("The key works.")); } catch (Exception e) { Toast(e.Message, true); } }),
                Ai.Ready ? Ui.Btn(L.T("Remove the key"), () => { Ai.Key = ""; Store.Save(); Go("settings"); }, "danger") : null));
            root.Children.Add(Ui.Card(asp, L.T("AI analysis (Claude API) — optional"), L.T("Explains an address, a trace or a whole case in plain language and suggests next steps. Everything else in Athar works without it.")));

            var about = new StackPanel();
            about.Children.Add(Ui.Tx(L.T("Athar reads public data from blockchain explorers. It never asks for a wallet, a seed phrase or a private key, and it cannot move funds."), 13.5));
            var m = Ui.Steps("Follow the outflows: from the theft address through swaps and hops until a service that can freeze or identify.", "Look for test transactions: a tiny transfer shortly before a large one to the same address.", "Look for commingling: funds from two incidents meeting in one wallet point to one operator.", "Match amount and timing across a bridge or privacy tool; unique amounts and short delays are the reliable cases.", "Tie on-chain behaviour to off-chain evidence, archive what you cite, and state your confidence.");
            m.Margin = new Thickness(0, 12, 0, 0); about.Children.Add(m);
            root.Children.Add(Ui.Card(about, L.T("How on-chain investigators work"), L.T("The same open-source steps this tool supports.")));
            root.Children.Add(Ui.Notice("medium", L.T("Use responsibly"), L.T("Results are investigative leads. An address is not a person; accusing someone publicly on the basis of a trace alone can be wrong and harmful. Report thefts to the exchange concerned and to the police.")));
            var dev = new StackPanel();
            dev.Children.Add(Ui.Tx(L.T("Developed by Dr. Mohammed Tawfik"), 15, true));
            dev.Children.Add(Ui.Tx("Athar " + App.Version + "  ·  MIT License", 12.5, false, Ui.Muted));
            root.Children.Add(Ui.Card(dev, L.T("About")));
            return root;
        }
    }
}
