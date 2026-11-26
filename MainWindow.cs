using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Athar
{
    public partial class MainWindow : Window
    {
        public static MainWindow I;
        public readonly Grid Root = new();
        readonly StackPanel nav = new();
        readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        readonly StackPanel toasts = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 24) };
        TextBox search; ComboBox chainBox;
        string page = "home"; object arg;
        CancellationTokenSource cts = new();
        public int Pending;
        DispatcherTimer watchTimer;

        public MainWindow()
        {
            I = this;
            Title = "Athar · أثر";
            Width = 1400; Height = 880; MinWidth = 1060; MinHeight = 640;
            Background = Ui.Bg; FontFamily = Ui.Sans; FontSize = 13.5;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Athar.ico")); } catch { }
            Content = Root;
            ApplyLang();
            Go("home");
            watchTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(Math.Max(2, Store.S.WatchMin)) };
            watchTimer.Tick += (_, _) => CheckWatch(false);
            watchTimer.Start();
        }

        public static FrameworkElement Logo(double size)
        {
            var c = new Canvas { Width = size, Height = size, FlowDirection = FlowDirection.LeftToRight };
            double s = size / 40.0;
            c.Children.Add(new Rectangle { Width = size, Height = size, RadiusX = 8 * s, RadiusY = 8 * s, Fill = Ui.Brand });
            void Ln(double x1, double y1, double x2, double y2) => c.Children.Add(new Line { X1 = x1 * s, Y1 = y1 * s, X2 = x2 * s, Y2 = y2 * s, Stroke = Brushes.White, StrokeThickness = 2.4 * s });
            void Dot(double x, double y, double r) { var e = new Ellipse { Width = 2 * r * s, Height = 2 * r * s, Fill = Brushes.White }; Canvas.SetLeft(e, (x - r) * s); Canvas.SetTop(e, (y - r) * s); c.Children.Add(e); }
            Ln(10, 20, 22, 11); Ln(10, 20, 22, 29); Ln(22, 11, 31, 20);
            Dot(10, 20, 4.2); Dot(22, 11, 3.4); Dot(22, 29, 3.4); Dot(31, 20, 3.4);
            return c;
        }

        static readonly (string key, string label, string glyph)[] Pages =
        {
            ("home", "Search", ""), ("trace", "Trace funds", ""), ("link", "Link addresses", ""), ("match", "Match in / out", ""),
            ("watch", "Watchlist", ""), ("cases", "Cases & reports", ""), ("labels", "Labels", ""), ("settings", "Settings", ""),
        };

        void BuildShell()
        {
            Root.Children.Clear(); Root.ColumnDefinitions.Clear();
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(236) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var side = new DockPanel { Background = Brushes.White };
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 18, 18, 18) };
            brand.Children.Add(Logo(40));
            var words = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(Ui.Tx(L.Ar ? "أثر" : "Athar", 20, true, Ui.Text, false));
            words.Children.Add(Ui.Tx(L.T("Transaction tracing"), 12, false, Ui.Muted, false));
            brand.Children.Add(words);
            DockPanel.SetDock(brand, Dock.Top); side.Children.Add(brand);

            var foot = new StackPanel { Margin = new Thickness(18, 10, 18, 16) };
            var langs = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            foreach (var (code, lab) in new[] { ("ar", "عربي"), ("en", "English") })
            {
                var b = Ui.Btn(lab, () => { Store.S.Lang = code; Store.Save(); ApplyLang(); Go(page, arg); }, Store.S.Lang == code ? "primary" : "plain");
                b.Margin = new Thickness(0, 0, code == "ar" ? 6 : 0, 0);
                langs.Children.Add(b);
            }
            foot.Children.Add(langs);
            var ver = Ui.Tx("Athar " + App.Version + " · " + L.T("public explorer data"), 11.5, false, Ui.Muted); ver.Margin = new Thickness(0, 10, 0, 0);
            foot.Children.Add(ver);
            foot.Children.Add(Ui.Tx(L.T("Developed by Dr. Mohammed Tawfik"), 11.5, true, Ui.Muted));
            DockPanel.SetDock(foot, Dock.Bottom); side.Children.Add(foot);
            side.Children.Add(new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var sideWrap = new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = side };
            Grid.SetColumn(sideWrap, 0); Root.Children.Add(sideWrap);

            var main = new DockPanel();
            var bar = new Border { Background = Brushes.White, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(28, 12, 28, 12) };
            var bd = new DockPanel();
            chainBox = Ui.ChainCombo(Store.S.DefaultChain, 190);
            chainBox.Margin = new Thickness(0, 0, 10, 0);
            chainBox.SelectionChanged += (_, _) => { Store.S.DefaultChain = chainBox.Val(); Store.Save(); };
            DockPanel.SetDock(chainBox, Dock.Left); bd.Children.Add(chainBox);
            var go = Ui.Btn(L.T("Search"), () => Search(search.Text), "primary", ""); go.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(go, Dock.Right); bd.Children.Add(go);
            search = Ui.Input("", 0, true); search.FontSize = 14; search.Padding = new Thickness(10, 8, 10, 8);
            search.KeyDown += (_, e) => { if (e.Key == Key.Enter) Search(search.Text); };
            var hint = Ui.Tx(L.T("Paste an address or a transaction hash (Ethereum, Bitcoin, Tron …)"), 13.5, false, Ui.Muted, false);
            hint.IsHitTestVisible = false; hint.Margin = new Thickness(12, 0, 12, 0);
            search.TextChanged += (_, _) => hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var sg = new Grid(); sg.Children.Add(search); sg.Children.Add(hint);
            bd.Children.Add(sg);
            bar.Child = bd;
            DockPanel.SetDock(bar, Dock.Top); main.Children.Add(bar);
            main.Children.Add(scroll);
            Grid.SetColumn(main, 1); Root.Children.Add(main);
            Grid.SetColumnSpan(toasts, 2); Root.Children.Add(toasts);
        }

        void DrawNav()
        {
            nav.Children.Clear();
            string active = page switch { "address" or "tx" => "home", "case" => "cases", _ => page };
            foreach (var p in Pages)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var ic = Ui.Glyph(p.glyph, 16, active == p.key ? Ui.BrandDark : Ui.Muted); ic.Margin = new Thickness(0, 0, 12, 0);
                sp.Children.Add(ic);
                sp.Children.Add(new TextBlock { Text = L.T(p.label), VerticalAlignment = VerticalAlignment.Center });
                if (p.key == "watch" && Store.S.Alerts.Count > 0) sp.Children.Add(new Border { Background = Ui.Red, CornerRadius = new CornerRadius(9), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(8, 0, 0, 0), Child = Ui.Tx(Store.S.Alerts.Count.ToString(), 11, true, Brushes.White, false) });
                var rb = new RadioButton { Style = (Style)Application.Current.Resources["Nav"], Content = sp, IsChecked = active == p.key, GroupName = "nav" };
                var key = p.key;
                rb.Click += (_, _) => Go(key);
                nav.Children.Add(rb);
            }
        }

        public void ApplyLang()
        {
            L.Ar = Store.S.Lang != "en";
            FlowDirection = L.Ar ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            Root.FlowDirection = FlowDirection;
            BuildShell();
        }

        /// <summary>Page heading: title, one-line explanation and optional action buttons.</summary>
        public static FrameworkElement Head(string title, string sub = null, params UIElement[] actions)
        {
            var d = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
            if (actions.Length > 0) { var w = Ui.Row(actions); w.VerticalAlignment = VerticalAlignment.Bottom; DockPanel.SetDock(w, Dock.Right); d.Children.Add(w); }
            var sp = new StackPanel();
            sp.Children.Add(Ui.Tx(title, 24, true));
            if (sub != null) { var s = Ui.Tx(sub, 13.5, false, Ui.Muted); s.Margin = new Thickness(0, 4, 0, 0); s.MaxWidth = 860; s.HorizontalAlignment = HorizontalAlignment.Left; sp.Children.Add(s); }
            d.Children.Add(sp);
            return d;
        }

        public void Go(string p, object a = null)
        {
            cts.Cancel(); cts = new CancellationTokenSource();
            page = p; arg = a;
            DrawNav();
            FrameworkElement content;
            try
            {
                content = p switch
                {
                    "address" => PageAddress(((string, string))a), "tx" => PageTx(((string, string))a), "trace" => PageTrace(a),
                    "link" => PageLink(a as string), "match" => PageMatch(a), "watch" => PageWatch(), "cases" => PageCases(), "case" => PageCase(a as CaseFile),
                    "labels" => PageLabels(), "settings" => PageSettings(), _ => PageHome()
                };
            }
            catch (Exception e) { content = Ui.Notice("high", L.T("Something went wrong"), e.Message); }
            scroll.Content = new Border { Padding = new Thickness(28, 24, 28, 60), Child = content };
            scroll.ScrollToTop();
        }

        public void Search(string text)
        {
            text = (text ?? "").Trim();
            if (text == "") return;
            var (kind, fam) = Addr.Detect(text);
            if (kind == "unknown") { Toast(L.T("This is not a valid address or transaction hash."), true); return; }
            string sel = chainBox.Val(); var sc = Chains.Get(sel);
            string chain = fam == "evm" ? (sc.Family == "evm" ? sel : "eth") : fam == "btc" ? "btc" : fam == "tron" ? "tron" : sc.Family == "tron" ? "tron" : sc.Family == "btc" ? "btc" : "btc?";
            if (kind == "address") Go("address", (chain, Addr.Norm(fam, text)));
            else Go("tx", (chain, fam == "evm" ? text.ToLowerInvariant() : text));
        }

        public static void Toast(string text, bool err = false)
        {
            if (I == null) return;
            var b = new Border { Background = err ? Ui.Red : Ui.Text, CornerRadius = new CornerRadius(5), Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(0, 6, 0, 0), Child = Ui.Tx(text, 13.5, true, Brushes.White) };
            I.toasts.Children.Add(b);
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(err ? 6 : 2.4) };
            t.Tick += (_, _) => { t.Stop(); I.toasts.Children.Remove(b); };
            t.Start();
        }

        /// <summary>Runs async work with a progress line and a cancel button, then shows the result or the error in place.</summary>
        public async void Load<T>(Panel host, Func<CancellationToken, Action<string>, Task<T>> work, Action<T> done)
        {
            var pageCt = cts.Token;
            var own = CancellationTokenSource.CreateLinkedTokenSource(pageCt);
            Pending++;
            var status = Ui.Tx(L.T("Loading from the blockchain explorer…"), 13, false, Ui.Muted);
            var pb = new ProgressBar { IsIndeterminate = true, Height = 4, Margin = new Thickness(0, 10, 0, 10), Foreground = Ui.Brand, BorderThickness = new Thickness(0), Background = Ui.BrandSoft };
            var box = new StackPanel();
            box.Children.Add(status); box.Children.Add(pb);
            box.Children.Add(Ui.Btn(L.T("Cancel"), () => own.Cancel(), "plain", ""));
            host.Children.Clear(); host.Children.Add(Ui.Card(box));
            try
            {
                var r = await work(own.Token, s => status.Text = s);
                if (pageCt.IsCancellationRequested) return;
                host.Children.Clear();
                done(r);
            }
            catch (OperationCanceledException) { if (!pageCt.IsCancellationRequested) { host.Children.Clear(); host.Children.Add(Ui.Notice("medium", L.T("Cancelled."))); } }
            catch (Exception e)
            {
                if (!pageCt.IsCancellationRequested) { host.Children.Clear(); host.Children.Add(Ui.Notice("high", L.T("Could not load the data"), e.Message)); }
            }
            finally { Pending--; }
        }

        /// <summary>Small modal form. Returns null when cancelled.</summary>
        public static string[] Ask(string title, params (string label, string value)[] fields)
        {
            var w = new Window { Title = title, Owner = I, WindowStartupLocation = WindowStartupLocation.CenterOwner, SizeToContent = SizeToContent.Height, Width = 480, ResizeMode = ResizeMode.NoResize, FlowDirection = I.FlowDirection, FontFamily = Ui.Sans, Background = Brushes.White, ShowInTaskbar = false };
            var sp = new StackPanel { Margin = new Thickness(22) };
            sp.Children.Add(Ui.Tx(title, 17, true));
            var boxes = new List<TextBox>();
            foreach (var (label, value) in fields)
            {
                var t = Ui.Input(value); boxes.Add(t);
                var f = Ui.Field(label, t); f.Margin = new Thickness(0, 12, 0, 0); sp.Children.Add(f);
            }
            string[] res = null;
            var ok = Ui.Btn(L.T("Save"), () => { res = boxes.Select(b => b.Text.Trim()).ToArray(); w.Close(); }, "primary");
            ok.IsDefault = true;
            var row = Ui.Row(ok, Ui.Btn(L.T("Cancel"), () => w.Close())); row.Margin = new Thickness(0, 18, 0, 0);
            sp.Children.Add(row);
            w.Content = sp;
            w.Loaded += (_, _) => boxes.FirstOrDefault()?.Focus();
            w.ShowDialog();
            return res;
        }

        /// <summary>Card with an "analyse with AI" button; the answer appears in place and can be copied or reused.</summary>
        public FrameworkElement AiCard(string title, Func<string> prompt, string useLabel = null, Action<string> use = null)
        {
            var sp = new StackPanel();
            var host = new StackPanel();
            if (!Ai.Ready)
            {
                sp.Children.Add(Ui.Notice("info", L.T("AI analysis is optional"), L.T("Add your Claude API key in Settings to use AI analysis."), Ui.Btn(L.T("Settings"), () => Go("settings"), "link")));
                return Ui.Card(sp, title);
            }
            void Run() => Load(host, (ct, say) => { say(L.T("Asking Claude…")); return Ai.Ask(prompt(), ct); }, text =>
            {
                var box = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Ui.Bg, Padding = new Thickness(14), FontSize = 14, AcceptsReturn = true };
                host.Children.Add(box);
                var row = Ui.Row(Ui.Btn(L.T("Copy"), () => Ui.Copy(text), "plain", ""), use == null ? null : Ui.Btn(useLabel, () => use(text), "plain", ""));
                row.Margin = new Thickness(0, 10, 0, -8);
                host.Children.Add(row);
                host.Children.Add(Ui.Tx(L.T("Written by an AI model from the data above. Check every statement against the explorer before relying on it."), 12, false, Ui.Muted));
            });
            var b = Ui.Btn(L.T("Analyse with AI"), Run, "primary", ""); b.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(b); sp.Children.Add(host);
            return Ui.Card(sp, title, L.T("Sends the public blockchain data shown on this page to the Claude API with your own key."));
        }

        public static string SaveFile(string name, string filter)
        {
            var d = new Microsoft.Win32.SaveFileDialog { FileName = name, Filter = filter };
            return d.ShowDialog() == true ? d.FileName : null;
        }

        public static CaseFile ActiveCase(bool create = true)
        {
            var all = Store.AllCases();
            var c = all.FirstOrDefault(x => x.Id == Store.S.ActiveCase) ?? all.FirstOrDefault();
            if (c == null && create) c = Store.NewCase(L.T("Case") + " 1");
            if (c != null && Store.S.ActiveCase != c.Id) { Store.S.ActiveCase = c.Id; Store.Save(); }
            return c;
        }

        public static void AddToCase(string chain, string kind, string value, string role = "")
        {
            var c = ActiveCase();
            if (c.Items.Any(i => i.Value == value)) { Toast(L.F("Already in case “{0}”", c.Name)); return; }
            c.Items.Add(new CaseItem { Chain = chain, Kind = kind, Value = value, Role = role, Added = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            Store.SaveCase(c);
            Toast(L.F("Added to case “{0}”", c.Name));
        }

        public static void EditLabel(string addr)
        {
            var cur = Store.UserLabels.FirstOrDefault(x => string.Equals(x.Address, addr, StringComparison.OrdinalIgnoreCase));
            var r = Ask(L.T("Label this address"), (L.T("Name (e.g. Suspect wallet 1)"), cur?.Name ?? ""), (L.T("Category: exchange, mixer, bridge, dex, threat, user"), cur?.Cat ?? "user"), (L.T("Note"), cur?.Note ?? ""));
            if (r == null || r[0] == "") return;
            if (cur != null) Store.UserLabels.Remove(cur);
            string cat = Labels.Cats.Contains(r[1].ToLowerInvariant()) ? r[1].ToLowerInvariant() : "user";
            Store.UserLabels.Add(new UserLabel { Address = addr, Name = r[0], Cat = cat, Note = r[2] });
            Store.SaveLabels();
            Toast(L.T("Label saved"));
        }

        public void SnapPng(string path, int w)
        {
            Content = null;
            Root.FlowDirection = L.Ar ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Root.Measure(new Size(w, double.PositiveInfinity));
            double h = Math.Max(880, Math.Min(5200, Root.DesiredSize.Height));
            Root.Measure(new Size(w, h)); Root.Arrange(new Rect(0, 0, w, h)); Root.UpdateLayout();
            Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Root.UpdateLayout();
            var rtb = new RenderTargetBitmap(w, (int)h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(Root);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path); enc.Save(fs);
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
    }
}
