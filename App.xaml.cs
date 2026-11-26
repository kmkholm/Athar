using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Athar
{
    public partial class App : Application
    {
        public const string Version = "1.0.0";

        protected override void OnStartup(StartupEventArgs e)
        {
            var args = e.Args;
            string Arg(string n) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            try { Store.Init(Arg("--data")); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Athar"); Shutdown(1); return; }
            Demo.Register();
            if (Arg("--selftest") is string log)
            {
                bool live = args.Contains("--live");
                int bad = System.Threading.Tasks.Task.Run(() => SelfTest.Run(log, live)).GetAwaiter().GetResult();
                Shutdown(bad == 0 ? 0 : 1);
                return;
            }
            if (Arg("--probe") is string probe)
            {
                string outDir = Arg("--out") ?? ".";
                try { Probe(probe, Arg("--chain") ?? "eth", outDir, Arg("--lang") ?? "ar"); Shutdown(0); }
                catch (Exception ex) { Directory.CreateDirectory(outDir); File.WriteAllText(Path.Combine(outDir, "probe_error.txt"), ex.ToString()); Shutdown(2); }
                return;
            }
            if (Arg("--icon") is string ico) { WriteIcon(ico); Shutdown(0); return; }
            if (Arg("--snap") is string dir)
            {
                try { Snap(dir, Arg("--lang") ?? "ar"); Shutdown(0); }
                catch (Exception ex) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "snap_error.txt"), ex.ToString()); Shutdown(2); }
                return;
            }
            DispatcherUnhandledException += (_, ex) => { Athar.MainWindow.Toast(ex.Exception.Message, true); ex.Handled = true; };
            base.OnStartup(e);
            new MainWindow().Show();
        }

        /// <summary>Runs the real address and trace pages against the live explorers, saves screenshots and a text summary (verification helper).
        /// Several start addresses may be given separated by commas. Environment: ATHAR_HOPS, ATHAR_BRANCH, ATHAR_NODES, ATHAR_SHARE, ATHAR_FROM (UTC date), ATHAR_BACK=1.</summary>
        static void Probe(string address, string chainKey, string dir, string lang)
        {
            Directory.CreateDirectory(dir);
            string Env(string k, string d) => Environment.GetEnvironmentVariable(k) ?? d;
            Store.S.Lang = lang; Store.S.DefaultChain = chainKey;
            Store.S.Hops = int.Parse(Env("ATHAR_HOPS", "3")); Store.S.Branch = int.Parse(Env("ATHAR_BRANCH", "25")); Store.S.MaxNodes = int.Parse(Env("ATHAR_NODES", "150")); Store.S.MinShare = double.Parse(Env("ATHAR_SHARE", "3"), System.Globalization.CultureInfo.InvariantCulture);
            long fromTime = Fmt.ParseDate(Env("ATHAR_FROM", ""));
            var chain = Chains.Get(chainKey);
            var starts = Tracer.Starts(chain, address).ToList();
            string addr = starts[0];
            var w = new MainWindow();
            void Shot(string name, string page, object arg)
            {
                w.Go(page, arg);
                var until = DateTime.UtcNow.AddSeconds(900);
                do { w.Dispatcher.Invoke(() => { }, DispatcherPriority.Background); Thread.Sleep(30); } while (w.Pending > 0 && DateTime.UtcNow < until);
                w.SnapPng(Path.Combine(dir, name + ".png"), 1400);
            }
            T Wait<T>(Func<System.Threading.Tasks.Task<T>> f) => System.Threading.Tasks.Task.Run(f).GetAwaiter().GetResult();
            Shot("address", "address", (chainKey, addr));
            Shot("trace_forward", "trace", new TraceOptions { Chain = chainKey, Start = address, Forward = true, FromTime = fromTime });
            Athar.MainWindow.SnapBubbles = true;
            Shot("trace_forward_bubbles", "trace", new TraceOptions { Chain = chainKey, Start = address, Forward = true, FromTime = fromTime });
            Athar.MainWindow.SnapBubbles = false;
            bool back = Env("ATHAR_BACK", "0") == "1";
            if (back) Shot("trace_backward", "trace", new TraceOptions { Chain = chainKey, Start = address, Forward = false, FromTime = fromTime });

            var sb = new StringBuilder();
            var info = Wait(() => chain.Api.Info(addr, CancellationToken.None));
            var set = Wait(() => chain.Api.Transfers(addr, 3000, false, 0, 0, CancellationToken.None));
            L.Ar = false;
            sb.AppendLine($"ADDRESS {addr} on {chain.Name}");
            sb.AppendLine($"label: {Labels.Find(addr)?.Name ?? "none"}; tags: {string.Join(", ", info.Tags)}; contract: {info.IsContract}; scam flag: {info.IsScam}");
            sb.AppendLine($"balance: {Fmt.Amt(info.Balance)} {chain.Symbol}; tx count: {info.TxCount}; usd rate: {info.Usd}");
            if (chain.Api is IFreezeCheck fz) sb.AppendLine("frozen by: " + string.Join(", ", Wait(() => fz.Frozen(addr, CancellationToken.None)).DefaultIfEmpty("-")));
            sb.AppendLine($"transfers loaded: {set.Items.Count}; truncated: {set.Truncated}; suspicious set aside: {set.Items.Count - Tokens.Clean(set.Items).Count}");
            Xfer first = null;
            if (set.Truncated) first = Analysis.FirstFunder(addr, Tokens.Clean(Wait(() => chain.Api.Transfers(addr, 10, true, 0, 0, CancellationToken.None)).Items));
            sb.AppendLine("\nFINDINGS");
            foreach (var f in Analysis.Findings(addr, info, set, first)) sb.AppendLine($"- [{f.Level}] {f.Title}: {f.Detail}");
            if (chain.Family == "evm")
            {
                sb.AppendLine("\nOTHER NETWORKS");
                foreach (var c in Chains.All.Where(c => c.Family == "evm" && c.Key != chainKey && c.Key != "demo" && c.Ready))
                {
                    try { var i2 = Wait(() => c.Api.Info(addr, CancellationToken.None)); sb.AppendLine($"- {c.Name}: balance {Fmt.Amt(i2.Balance)} {c.Symbol}; tx {i2.TxCount}"); }
                    catch (Exception e) { sb.AppendLine($"- {c.Name}: {e.GetBaseException().Message}"); }
                }
            }
            foreach (bool fwd in back ? new[] { true, false } : new[] { true })
            {
                var g = Wait(() => Tracer.Run(new TraceOptions { Chain = chainKey, Start = address, Forward = fwd, MaxHops = Store.S.Hops, Branch = Store.S.Branch, MaxNodes = Store.S.MaxNodes, MinSharePct = Store.S.MinShare, FromTime = fromTime }, null, CancellationToken.None));
                sb.AppendLine($"\nTRACE {(fwd ? "FORWARD" : "BACKWARD")}: {g.Nodes.Count} addresses, {g.Edges.Count} flows, deepest hop {g.Nodes.Max(n => n.Depth)}");
                sb.AppendLine("WHERE THE MONEY IS NOW");
                foreach (var grp in g.Nodes.Where(n => n.Depth > 0 || n.Frozen != "").GroupBy(n => g.Status(n)).OrderByDescending(x => x.Sum(n => g.UsdIn(n))))
                    sb.AppendLine($"  {grp.Key,-11} {grp.Count(),3} addresses  {GraphGeo.Money(grp.Sum(n => g.UsdIn(n)))} reached");
                sb.AppendLine("ADDRESSES");
                foreach (var n in g.Nodes) sb.AppendLine($"- hop {n.Depth} {n.Id} {n.Label} [{g.Status(n)}{(n.Frozen != "" ? ":" + n.Frozen + " " + GraphGeo.Money(n.FrozenUsd) : "")}] stop={n.Stop} balance={Fmt.Amt(n.Balance)} reached={GraphGeo.Money(g.UsdIn(n))}");
                sb.AppendLine("FLOWS");
                foreach (var e in g.Edges.OrderBy(e => g.Node(e.From)?.Depth ?? 0).ThenByDescending(e => g.Usd(e) ?? 0)) sb.AppendLine($"  {Fmt.Short(e.From)} -> {(e.Bridge ? e.To : Fmt.Short(e.To))} [{Labels.Find(e.To)?.Name}]{(e.Bridge ? " BRIDGE" : "")}: {Fmt.Amt(e.Amount)} {e.Asset} ({e.Count} tx, {Fmt.Time(e.First)})");
                foreach (var note in g.Notes.Distinct()) sb.AppendLine("  note: " + note);
            }
            sb.AppendLine($"\nhttp calls={Http.Calls} cache hits={Http.CacheHits}");
            File.WriteAllText(Path.Combine(dir, "probe.txt"), sb.ToString());
        }

        /// <summary>Draws the logo into a PNG-compressed .ico (build helper).</summary>
        static void WriteIcon(string path)
        {
            var logo = Athar.MainWindow.Logo(256);
            logo.Measure(new Size(256, 256)); logo.Arrange(new Rect(0, 0, 256, 256)); logo.UpdateLayout();
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(256, 256, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(logo);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var ms = new MemoryStream(); enc.Save(ms); byte[] png = ms.ToArray();
            using var bw = new BinaryWriter(File.Create(path));
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)1);
            bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0); bw.Write((short)1); bw.Write((short)32); bw.Write(png.Length); bw.Write(22);
            bw.Write(png);
        }

        /// <summary>Renders every page to PNG without showing a window (visual check) and lists untranslated strings.</summary>
        static void Snap(string dir, string lang)
        {
            Directory.CreateDirectory(dir);
            Store.S.Lang = lang; Store.S.DefaultChain = "demo";
            var c = Store.AllCases().FirstOrDefault() ?? Store.NewCase(lang == "ar" ? "سرقة محفظة الخزينة (تجريبي)" : "Treasury wallet theft (demo)");
            if (c.Traces.Count == 0)
            {
                var g = Tracer.Run(new TraceOptions { Chain = "demo", Start = Demo.Hacker, MaxHops = 4 }, null, CancellationToken.None).GetAwaiter().GetResult();
                c.Traces.Add(g);
                c.Notes = lang == "ar" ? "في 15 يونيو 2025 سُحب 1000 ETH من محفظة الخزينة إلى عنوان غير معروف." : "On 15 June 2025, 1000 ETH left the treasury wallet to an unknown address.";
                c.Findings.Add(lang == "ar" ? "وصل 399.2 ETH إلى محفظة منصة تداول بعد قفزتين خلال 58 دقيقة." : "399.2 ETH reached an exchange hot wallet after two hops within 58 minutes.");
                c.Items.Add(new CaseItem { Chain = "demo", Kind = "address", Value = Demo.Hacker, Role = lang == "ar" ? "مشتبه به" : "suspect" });
                c.Items.Add(new CaseItem { Chain = "demo", Kind = "address", Value = Demo.Victim, Role = lang == "ar" ? "الضحية" : "victim" });
                c.Items.Add(new CaseItem { Chain = "demo", Kind = "tx", Value = Demo.Data[2].Hash, Role = lang == "ar" ? "معاملة السرقة" : "theft transaction" });
                Store.SaveCase(c);
            }
            if (Store.UserLabels.Count == 0) { Store.UserLabels.Add(new UserLabel { Address = Demo.Hacker, Name = lang == "ar" ? "المشتبه به 1" : "Suspect 1", Cat = "threat" }); Store.SaveLabels(); }
            var w = new MainWindow();
            void Shot(string name, string page, object arg = null)
            {
                w.Go(page, arg);
                var until = DateTime.UtcNow.AddSeconds(20);
                do { w.Dispatcher.Invoke(() => { }, DispatcherPriority.Background); } while (w.Pending > 0 && DateTime.UtcNow < until);
                w.SnapPng(Path.Combine(dir, $"{lang}_{name}.png"), 1400);
            }
            Shot("01_home", "home");
            Shot("02_address", "address", ("demo", Demo.Hacker));
            Shot("03_tx", "tx", ("demo", Demo.Data[2].Hash));
            Shot("04_trace", "trace", new TraceOptions { Chain = "demo", Start = Demo.Hacker, Forward = true });
            Athar.MainWindow.SnapBubbles = true;
            Shot("04b_trace_bubbles", "trace", new TraceOptions { Chain = "demo", Start = Demo.Hacker, Forward = true });
            Athar.MainWindow.SnapBubbles = false;
            Shot("05_trace_back", "trace", new TraceOptions { Chain = "demo", Start = Demo.Exchange, Forward = false });
            Shot("06_link", "link", Demo.Hacker + "\n" + Demo.Hacker2);
            Shot("07_match", "match");
            Shot("08_watch", "watch");
            Shot("09_cases", "cases");
            Shot("10_case", "case", c);
            Shot("11_labels", "labels");
            Shot("12_settings", "settings");
            File.WriteAllText(Path.Combine(dir, $"{lang}_report.html"), Report.CaseHtml(c, lang), new UTF8Encoding(true));
            File.WriteAllLines(Path.Combine(dir, $"missing_{lang}.txt"), L.Missing.OrderBy(x => x));
        }
    }
}
