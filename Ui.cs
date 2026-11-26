using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Athar
{
    public class Col
    {
        public string Header, Path, Format; public double Width; public bool Ltr, Right, Mono, Bold, Star;
        public Col(string header, string path, double width = 0, bool ltr = false, bool right = false, bool mono = false, bool bold = false, bool star = false, string format = null)
        { Header = header; Path = path; Width = width; Ltr = ltr; Right = right; Mono = mono; Bold = bold; Star = star; Format = format; }
        public const string Num = "#,##0.######";
    }

    public static class Ui
    {
        static SolidColorBrush B(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex); b.Freeze(); return b; }
        public static readonly Brush Brand = B("#1565C0"), BrandDark = B("#0D47A1"), BrandSoft = B("#E3EEFB"), Bg = B("#F4F6F8"), Card0 = Brushes.White, Line = B("#DDE2E8"),
            Text = B("#1F2933"), Muted = B("#66788A"), Green = B("#2E7D32"), Red = B("#C62828"), Purple = B("#6A1B9A"), Orange = B("#E65100"), Gray = B("#78909C"), Teal = B("#00796B"),
            RedSoft = B("#FDECEA"), AmberSoft = B("#FFF4E0"), GreenSoft = B("#E8F5E9"), Amber = B("#B26A00");
        public static readonly FontFamily Sans = new("Segoe UI"), Mono = new("Consolas"), Icons = new("Segoe MDL2 Assets");

        public static Brush CatBrush(string cat) => cat switch { "exchange" => Green, "mixer" => Red, "bridge" => Purple, "dex" => Orange, "threat" => Red, "contract" => Gray, "user" => Brand, "other" => Teal, _ => Gray };
        public static string CatName(string cat) => L.T(cat switch { "exchange" => "Exchange", "mixer" => "Mixer", "bridge" => "Bridge", "dex" => "DEX / swap", "threat" => "Flagged", "contract" => "Contract", "user" => "My label", "other" => "Other", _ => "Unlabelled" });
        public static readonly Brush Ice = B("#00838F"), Pale = B("#B0BEC5");
        public static Brush StatusBrush(string status, string cat = "") => status switch { "start" => Brand, "frozen" => Ice, "holding" => Amber, "exchange" => Green, "mixer" => Red, "bridge" => Purple, "dex" => Orange, "burn" => Teal, "otherchain" => Purple, "unexplored" => cat == "contract" ? Gray : Pale, _ => Gray };
        public static string StatusName(string status) => L.T(status switch { "start" => "Start", "frozen" => "Frozen by the issuer", "holding" => "Still holding (dormant)", "exchange" => "Exchange", "mixer" => "Mixer", "bridge" => "Bridge", "dex" => "Swapped (DEX)", "burn" => "Burned", "otherchain" => "On a network Athar cannot read", "unexplored" => "Not followed further", _ => "Passed on" });
        public static string StopName(string s) => L.T(s switch { "service" => "Stopped: known service", "contract" => "Stopped: contract", "busy" => "Stopped: very active wallet (likely a service)", "hops" => "Hop limit reached", "end" => "No later outflow", "small" => "Only small transfers", "error" => "Could not be read", "burn" => "Burn / null address", _ => "" });

        public static TextBlock Tx(string text, double size = 13.5, bool bold = false, Brush color = null, bool wrap = true)
            => new() { Text = text ?? "", FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = color ?? Text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };

        /// <summary>Latin identifiers (addresses, hashes) stay left-to-right inside Arabic layout.</summary>
        public static TextBlock Code(string text, double size = 13, Brush color = null)
            => new() { Text = text ?? "", FontFamily = Mono, FontSize = size, Foreground = color ?? Text, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

        public static TextBlock Glyph(string code, double size = 16, Brush color = null)
            => new() { Text = code, FontFamily = Icons, FontSize = size, Foreground = color ?? Text, VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };

        public static Button Btn(string text, Action click, string kind = "plain", string glyph = null)
        {
            var b = new Button { Style = (Style)Application.Current.Resources["Btn"] };
            Brush fg = Text;
            if (kind == "primary") { b.Background = Brand; b.BorderBrush = Brand; fg = Brushes.White; }
            else if (kind == "danger") { b.Background = Brushes.White; b.BorderBrush = Red; fg = Red; }
            else if (kind == "link") { b.Background = Brushes.Transparent; b.BorderThickness = new Thickness(0); b.Padding = new Thickness(4, 2, 4, 2); fg = Brand; }
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (glyph != null) sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 14, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, text == "" ? 0 : 7, 0) });
            if (text != "") sp.Children.Add(new TextBlock { Text = text, Foreground = fg, FontWeight = kind == "primary" ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center });
            b.Content = sp;
            b.Click += (_, _) => { try { click(); } catch (Exception e) { MainWindow.Toast(e.Message, true); } };
            return b;
        }

        public static TextBox Input(string text = "", double width = 0, bool ltr = false, bool multi = false)
        {
            var t = new TextBox { Text = text ?? "" };
            if (width > 0) t.Width = width;
            if (ltr) { t.FlowDirection = FlowDirection.LeftToRight; t.FontFamily = Mono; }
            if (multi) { t.AcceptsReturn = true; t.TextWrapping = TextWrapping.Wrap; t.MinHeight = 96; t.VerticalContentAlignment = VerticalAlignment.Top; t.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
            return t;
        }

        public static ComboBox Combo(IEnumerable<(string key, string label)> items, string selected, double width = 0)
        {
            var c = new ComboBox();
            if (width > 0) c.Width = width;
            foreach (var (k, l) in items) c.Items.Add(new ComboBoxItem { Content = l, Tag = k, IsSelected = k == selected });
            if (c.SelectedIndex < 0 && c.Items.Count > 0) c.SelectedIndex = 0;
            return c;
        }
        public static string Val(this ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        public static ComboBox ChainCombo(string selected, double width = 170, bool demo = true)
            => Combo(Chains.All.Where(c => demo || c.Key != "demo").Select(c => (c.Key, c.Name + (c.NeedsKey && !c.Ready ? "  (" + L.T("needs key") + ")" : ""))), selected, width);

        public static FrameworkElement Field(string label, FrameworkElement control, string hint = null)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 16, 12) };
            sp.Children.Add(Tx(label, 12.5, true, Muted));
            control.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(control);
            if (hint != null) { var h = Tx(hint, 12, false, Muted); h.Margin = new Thickness(0, 3, 0, 0); h.MaxWidth = 420; h.HorizontalAlignment = HorizontalAlignment.Left; sp.Children.Add(h); }
            return sp;
        }

        public static WrapPanel Row(params UIElement[] items)
        {
            var w = new WrapPanel();
            foreach (var i in items) if (i != null) w.Children.Add(i);
            return w;
        }

        public static Border Card(UIElement child, string title = null, string sub = null)
        {
            var sp = new StackPanel();
            if (title != null) { var t = Tx(title, 16, true); t.Margin = new Thickness(0, 0, 0, sub == null ? 12 : 2); sp.Children.Add(t); }
            if (sub != null) { var s = Tx(sub, 12.5, false, Muted); s.Margin = new Thickness(0, 0, 0, 12); sp.Children.Add(s); }
            if (child != null) sp.Children.Add(child);
            return new Border { Background = Card0, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(20, 18, 20, 16), Margin = new Thickness(0, 0, 0, 16), Child = sp };
        }

        public static Border Chip(string text, Brush color)
        {
            var bg = new SolidColorBrush(((SolidColorBrush)color).Color) { Opacity = 0.12 };
            return new Border { Background = bg, CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Child = Tx(text, 12, true, color, false) };
        }
        public static FrameworkElement LabelChip(string addr)
        {
            var l = Labels.Find(addr);
            return l == null ? null : Chip(l.Name == "" ? CatName(l.Cat) : l.Name + " · " + CatName(l.Cat), CatBrush(l.Cat));
        }

        public static Border Stat(string title, string value, string sub = null, Brush color = null)
        {
            var sp = new StackPanel();
            sp.Children.Add(Tx(title, 12.5, true, Muted));
            var v = Tx(value, 22, true, color ?? Text, false); v.Margin = new Thickness(0, 4, 0, 0); v.FlowDirection = FlowDirection.LeftToRight; v.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(v);
            if (sub != null) sp.Children.Add(Tx(sub, 12, false, Muted));
            return new Border { Background = Card0, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 12, 16), MinWidth = 190, Child = sp };
        }

        public static Border Notice(string level, string title, string detail = null, UIElement extra = null)
        {
            Brush fg = level == "high" ? Red : level == "medium" ? Amber : level == "ok" ? Green : Brand;
            Brush bg = level == "high" ? RedSoft : level == "medium" ? AmberSoft : level == "ok" ? GreenSoft : BrandSoft;
            string g = level == "high" ? "" : level == "medium" ? "" : level == "ok" ? "" : "";
            var d = new DockPanel();
            var ic = Glyph(g, 18, fg); ic.Margin = new Thickness(0, 1, 12, 0); ic.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(ic, Dock.Left); d.Children.Add(ic);
            if (extra != null) { DockPanel.SetDock(extra, Dock.Right); d.Children.Add(extra); }
            var sp = new StackPanel();
            sp.Children.Add(Tx(title, 13.5, true));
            if (!string.IsNullOrEmpty(detail)) sp.Children.Add(Tx(detail, 13, false, Text));
            d.Children.Add(sp);
            return new Border { Background = bg, CornerRadius = new CornerRadius(5), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8), Child = d };
        }

        public static void Open(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception e) { MainWindow.Toast(e.Message, true); } }
        public static void Copy(string s) { try { Clipboard.SetText(s ?? ""); MainWindow.Toast(L.T("Copied")); } catch { } }

        /// <summary>Clickable address: label if known, otherwise the shortened address.</summary>
        public static FrameworkElement AddrLink(string chain, string addr, bool full = false)
        {
            var l = Labels.Find(addr);
            var t = Code(full ? addr : (l != null && l.Name != "" ? l.Name : Fmt.Short(addr)), 13, Brand);
            if (l != null && l.Name != "" && !full) t.FontFamily = Sans;
            t.Cursor = Cursors.Hand; t.ToolTip = addr;
            t.MouseLeftButtonUp += (_, _) => MainWindow.I.Go("address", (chain, addr));
            return t;
        }

        public static DataGrid Table(IEnumerable rows, Col[] cols, Action<object> open = null, Func<object, (string label, Action act)[]> menu = null, double maxHeight = 440)
        {
            var g = new DataGrid { ItemsSource = rows, MaxHeight = maxHeight };
            foreach (var c in cols)
            {
                var st = new Style(typeof(TextBlock));
                st.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
                st.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
                if (c.Ltr || c.Right) { st.Setters.Add(new Setter(FrameworkElement.FlowDirectionProperty, FlowDirection.LeftToRight)); st.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, c.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left)); }
                if (c.Mono) st.Setters.Add(new Setter(TextBlock.FontFamilyProperty, Mono));
                if (c.Bold) st.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
                var bind = new Binding(c.Path) { Mode = BindingMode.OneWay, ConverterCulture = System.Globalization.CultureInfo.InvariantCulture };
                if (c.Format != null) bind.StringFormat = c.Format;
                var col = new DataGridTextColumn { Header = L.T(c.Header), Binding = bind, ElementStyle = st };
                col.Width = c.Star ? new DataGridLength(1, DataGridLengthUnitType.Star) : c.Width > 0 ? new DataGridLength(c.Width) : DataGridLength.Auto;
                g.Columns.Add(col);
            }
            if (open != null) g.MouseDoubleClick += (_, e) => { if (g.SelectedItem != null && e.OriginalSource is DependencyObject d && FindRow(d)) open(g.SelectedItem); };
            if (menu != null)
                g.ContextMenuOpening += (_, e) =>
                {
                    if (g.SelectedItem == null) { e.Handled = true; return; }
                    var cm = new ContextMenu();
                    foreach (var (label, act) in menu(g.SelectedItem)) { var mi = new MenuItem { Header = label }; var a = act; mi.Click += (_, _) => a(); cm.Items.Add(mi); }
                    g.ContextMenu = cm;
                };
            if (menu != null) g.ContextMenu = new ContextMenu();
            return g;
        }
        static bool FindRow(DependencyObject d)
        {
            while (d != null) { if (d is DataGridRow) return true; if (d is System.Windows.Controls.Primitives.DataGridColumnHeader) return false; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d); }
            return false;
        }

        public static FrameworkElement Empty(string text) { var t = Tx(text, 13.5, false, Muted); t.Margin = new Thickness(0, 6, 0, 6); return t; }

        public static FrameworkElement Steps(params string[] steps)
        {
            var sp = new StackPanel();
            int i = 1;
            foreach (var s in steps)
            {
                var d = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var n = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = BrandSoft, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = (i++).ToString(), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = BrandDark, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                DockPanel.SetDock(n, Dock.Left); d.Children.Add(n); d.Children.Add(Tx(L.T(s), 13));
                sp.Children.Add(d);
            }
            return sp;
        }
    }

    /// <summary>Row shown in transfer tables.</summary>
    public class XRow
    {
        public Xfer X;
        public string Time { get; set; }
        public string Dir { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public decimal Amount { get; set; }
        public string AmountText { get; set; }
        public string Asset { get; set; }
        public string Kind { get; set; }
        public string Hash { get; set; }
        public static XRow Of(Xfer x, string focus = null) => new()
        {
            X = x, Time = Fmt.Time(x.Time), Dir = focus == null ? "" : x.From == focus ? L.T("OUT") : L.T("IN"),
            From = Labels.Show(x.From), To = Labels.Show(x.To), Amount = x.Amount, AmountText = Fmt.Amt(x.Amount), Asset = x.Asset,
            Kind = L.T(x.Kind == "token" ? "token" : x.Kind == "internal" ? "internal" : "native"), Hash = Fmt.Short(x.Hash)
        };
    }
}
