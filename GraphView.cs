using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Athar
{
    /// <summary>Geometry shared by the on-screen graph and the SVG in reports.</summary>
    public static class GraphGeo
    {
        public const double W = 200, H = 50;
        public static (Point a, Point c1, Point c2, Point b, bool arrowRight) Curve(GNode s, GNode t)
        {
            double sy = s.Y + H / 2, ty = t.Y + H / 2;
            if (t.X > s.X + 1) { double m = (t.X - (s.X + W)) / 2; return (new Point(s.X + W, sy), new Point(s.X + W + m, sy), new Point(t.X - m, ty), new Point(t.X, ty), true); }
            if (t.X < s.X - 1) { double m = (s.X - (t.X + W)) / 2; return (new Point(s.X, sy), new Point(s.X - m, sy), new Point(t.X + W + m, ty), new Point(t.X + W, ty), false); }
            return (new Point(s.X + W, sy), new Point(s.X + W + 60, sy), new Point(t.X + W + 60, ty), new Point(t.X + W, ty), false);
        }
        public static double Thick(GEdge e, Graph g)
        {
            decimal max = g.Edges.Where(x => x.Asset == e.Asset && x.Contract == e.Contract).Max(x => x.Amount);
            if (max <= 0) return 1.4;
            return 1.4 + 4.2 * Math.Sqrt((double)(e.Amount / max));
        }
        public static Point Mid(Point a, Point c1, Point c2, Point b) => new((a.X + 3 * c1.X + 3 * c2.X + b.X) / 8, (a.Y + 3 * c1.Y + 3 * c2.Y + b.Y) / 8);
        public static string Title(GNode n) => n.Label != "" ? (n.Label.Length > 26 ? n.Label.Substring(0, 25) + "…" : n.Label) : Fmt.Short(n.Id);
        public static string Money(decimal usd) => usd >= 1_000_000_000m ? "$" + (usd / 1_000_000_000m).ToString("0.##", CultureInfo.InvariantCulture) + "B" : usd >= 1_000_000m ? "$" + (usd / 1_000_000m).ToString("0.#", CultureInfo.InvariantCulture) + "M" : usd >= 1000m ? "$" + (usd / 1000m).ToString("0.#", CultureInfo.InvariantCulture) + "K" : "$" + usd.ToString("0", CultureInfo.InvariantCulture);
        static string Hex(Brush b) { var c = ((SolidColorBrush)b).Color; return $"#{c.R:X2}{c.G:X2}{c.B:X2}"; }

        public static string Svg(Graph g)
        {
            if (g.Nodes.Count == 0) return "";
            var ci = CultureInfo.InvariantCulture;
            double w = g.Nodes.Max(n => n.X) + W + 80, h = g.Nodes.Max(n => n.Y) + H + 40;
            var sb = new StringBuilder();
            sb.Append(string.Format(ci, "<svg xmlns='http://www.w3.org/2000/svg' viewBox='-20 -20 {0:0} {1:0}' width='100%' style='max-width:{0:0}px;direction:ltr' font-family='Segoe UI,Arial,sans-serif'>", w, h));
            sb.Append("<defs><marker id='ar' viewBox='0 0 10 10' refX='9' refY='5' markerWidth='7' markerHeight='7' orient='auto'><path d='M0,0 L10,5 L0,10 z' fill='#78909C'/></marker></defs>");
            foreach (var e in g.Edges)
            {
                var s = g.Node(e.From); var t = g.Node(e.To); if (s == null || t == null) continue;
                var (a, c1, c2, b, _) = Curve(s, t); var m = Mid(a, c1, c2, b);
                sb.Append(string.Format(ci, "<path d='M{0:0.#},{1:0.#} C{2:0.#},{3:0.#} {4:0.#},{5:0.#} {6:0.#},{7:0.#}' fill='none' stroke='#90A4AE' stroke-width='{8:0.#}' marker-end='url(#ar)'/>", a.X, a.Y, c1.X, c1.Y, c2.X, c2.Y, b.X, b.Y, Math.Min(4, Thick(e, g))));
                string lab = Fmt.Html(Fmt.Amt(e.Amount) + " " + e.Asset);
                sb.Append(string.Format(ci, "<text x='{0:0.#}' y='{1:0.#}' font-size='10.5' text-anchor='middle' fill='#37474F' stroke='#fff' stroke-width='3' paint-order='stroke'>{2}</text>", m.X, m.Y - 3, lab));
            }
            foreach (var n in g.Nodes)
            {
                bool start = n.Depth == 0; string col = Hex(Ui.StatusBrush(g.Status(n), n.Cat));
                sb.Append(string.Format(ci, "<rect x='{0:0.#}' y='{1:0.#}' width='{2}' height='{3}' rx='5' fill='{4}' stroke='{5}'/>", n.X, n.Y, W, H, start ? "#E3EEFB" : "#fff", col));
                sb.Append(string.Format(ci, "<rect x='{0:0.#}' y='{1:0.#}' width='5' height='{2}' rx='2' fill='{3}'/>", n.X, n.Y, H, col));
                sb.Append(string.Format(ci, "<text x='{0:0.#}' y='{1:0.#}' font-size='12' font-weight='600' fill='#1F2933'>{2}</text>", n.X + 14, n.Y + 21, Fmt.Html(Title(n))));
                sb.Append(string.Format(ci, "<text x='{0:0.#}' y='{1:0.#}' font-size='10.5' fill='#66788A' font-family='Consolas,monospace'>{2}</text>", n.X + 14, n.Y + 38, Fmt.Html(Fmt.Short(n.Id))));
            }
            sb.Append("</svg>");
            return sb.ToString();
        }

        /// <summary>Bubble map as SVG for reports.</summary>
        public static string BubbleSvg(Graph g)
        {
            if (g.Nodes.Count == 0) return "";
            var ci = CultureInfo.InvariantCulture;
            double x0 = g.Nodes.Min(n => n.Bx - n.R) - 20, y0 = g.Nodes.Min(n => n.By - n.R) - 20, x1 = g.Nodes.Max(n => n.Bx + n.R) + 20, y1 = g.Nodes.Max(n => n.By + n.R) + 20;
            var sb = new StringBuilder();
            sb.Append(string.Format(ci, "<svg xmlns='http://www.w3.org/2000/svg' viewBox='{0:0} {1:0} {2:0} {3:0}' width='100%' style='max-width:{2:0}px;direction:ltr' font-family='Segoe UI,Arial,sans-serif'>", x0, y0, x1 - x0, y1 - y0));
            foreach (var e in g.Edges)
            {
                var s = g.Node(e.From); var t = g.Node(e.To); if (s == null || t == null) continue;
                sb.Append(string.Format(ci, "<line x1='{0:0.#}' y1='{1:0.#}' x2='{2:0.#}' y2='{3:0.#}' stroke='#B0BEC5' stroke-width='{4:0.#}'/>", s.Bx, s.By, t.Bx, t.By, Math.Min(5, Thick(e, g))));
            }
            foreach (var n in g.Nodes)
            {
                string col = Hex(Ui.StatusBrush(g.Status(n), n.Cat));
                sb.Append(string.Format(ci, "<circle cx='{0:0.#}' cy='{1:0.#}' r='{2:0.#}' fill='{3}' fill-opacity='0.22' stroke='{3}' stroke-width='2'/>", n.Bx, n.By, n.R, col));
                if (n.R >= 26)
                {
                    sb.Append(string.Format(ci, "<text x='{0:0.#}' y='{1:0.#}' font-size='10.5' font-weight='600' text-anchor='middle' fill='#1F2933'>{2}</text>", n.Bx, n.By - 2, Fmt.Html(n.Label != "" ? (n.Label.Length > 16 ? n.Label.Substring(0, 15) + "…" : n.Label) : n.Id.Substring(0, Math.Min(8, n.Id.Length)) + "…")));
                    decimal v = Math.Max(g.UsdIn(n), g.UsdOut(n));
                    if (v > 0) sb.Append(string.Format(ci, "<text x='{0:0.#}' y='{1:0.#}' font-size='10' text-anchor='middle' fill='#52606D'>{2}</text>", n.Bx, n.By + 12, Money(v)));
                }
            }
            sb.Append("</svg>");
            return sb.ToString();
        }
    }

    /// <summary>Pannable, zoomable drawing surface. Always left-to-right.</summary>
    public abstract class PanZoomView : Border
    {
        protected readonly Canvas canvas = new() { FlowDirection = FlowDirection.LeftToRight };
        readonly MatrixTransform view = new();
        protected Graph g;
        protected readonly Dictionary<string, Shape> marks = new();
        Point? drag; Matrix dragStart; bool fitted;
        public event Action<GNode> Selected;
        public GNode Current;

        protected PanZoomView(double height)
        {
            Height = height; Background = Ui.Bg; BorderBrush = Ui.Line; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(4); ClipToBounds = true;
            FlowDirection = FlowDirection.LeftToRight; Cursor = Cursors.SizeAll;
            canvas.RenderTransform = view;
            Child = canvas;
            MouseWheel += (_, e) => { var p = e.GetPosition(this); Zoom(e.Delta > 0 ? 1.15 : 1 / 1.15, p); e.Handled = true; };
            MouseLeftButtonDown += (_, e) => { drag = e.GetPosition(this); dragStart = view.Matrix; CaptureMouse(); };
            MouseLeftButtonUp += (_, _) => { drag = null; ReleaseMouseCapture(); };
            MouseMove += (_, e) => { if (drag is Point d && e.LeftButton == MouseButtonState.Pressed) { var p = e.GetPosition(this); var m = dragStart; m.Translate(p.X - d.X, p.Y - d.Y); view.Matrix = m; } };
            SizeChanged += (_, _) => { if (!fitted && g != null && ActualWidth > 0) { Fit(); fitted = true; } };
        }

        public void Zoom(double f, Point? at = null)
        {
            var p = at ?? new Point(ActualWidth / 2, ActualHeight / 2);
            var m = view.Matrix;
            double cur = m.M11 * f;
            if (cur < 0.08 || cur > 3) return;
            m.ScaleAt(f, f, p.X, p.Y); view.Matrix = m;
        }

        protected abstract Rect Bounds();
        protected abstract void Draw();

        public void Fit()
        {
            if (g == null || g.Nodes.Count == 0) return;
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            var b = Bounds();
            double s = Math.Max(0.08, Math.Min(1.15, Math.Min((w - 40) / b.Width, (h - 40) / b.Height)));
            var m = Matrix.Identity; m.Translate(-b.X, -b.Y); m.Scale(s, s); m.Translate((w - b.Width * s) / 2, (h - b.Height * s) / 2);
            view.Matrix = m;
        }

        public void Load(Graph graph)
        {
            g = graph; fitted = false;
            canvas.Children.Clear(); marks.Clear();
            Draw();
            if (ActualWidth > 0) { Fit(); fitted = true; }
        }

        public void Select(GNode n, bool raise = true)
        {
            if (Current != null && marks.TryGetValue(Current.Id, out var old)) old.StrokeThickness = MarkThickness(false);
            Current = n;
            if (n != null && marks.TryGetValue(n.Id, out var b)) b.StrokeThickness = MarkThickness(true);
            if (raise) Selected?.Invoke(n);
        }
        protected virtual double MarkThickness(bool selected) => selected ? 3.5 : 1.2;

        protected string Tip(GNode n)
        {
            string s = n.Id + (n.Label != "" ? "\n" + n.Label : "") + "\n" + Ui.StatusName(g.Status(n));
            if (n.Frozen != "") s += " (" + n.Frozen + ")";
            decimal v = Math.Max(g.UsdIn(n), g.UsdOut(n));
            if (v > 0) s += "\n≈ " + GraphGeo.Money(v);
            return s;
        }
    }

    /// <summary>Fund-flow diagram: one column per hop, senders on the left.</summary>
    public class GraphView : PanZoomView
    {
        public GraphView(double height = 520) : base(height) { }
        protected override Rect Bounds() => new(0, 0, g.Nodes.Max(n => n.X) + GraphGeo.W + 70, g.Nodes.Max(n => n.Y) + GraphGeo.H);

        protected override void Draw()
        {
            foreach (var e in g.Edges)
            {
                var s = g.Node(e.From); var t = g.Node(e.To); if (s == null || t == null) continue;
                var (a, c1, c2, b, right) = GraphGeo.Curve(s, t);
                var fig = new PathFigure { StartPoint = a, IsFilled = false };
                fig.Segments.Add(new BezierSegment(c1, c2, b, true));
                string tip = $"{Fmt.Short(e.From)} → {Fmt.Short(e.To)}\n{Fmt.Amt(e.Amount)} {e.Asset} · {e.Count} tx\n{Fmt.Time(e.First)} → {Fmt.Time(e.Last)}";
                var path = new Path { Data = new PathGeometry(new[] { fig }), Stroke = e.Bridge ? Ui.Purple : Ui.Gray, StrokeThickness = GraphGeo.Thick(e, g), Opacity = 0.75, ToolTip = tip };
                if (e.Bridge) path.StrokeDashArray = new DoubleCollection { 4, 3 };
                canvas.Children.Add(path);
                double d = right ? -1 : 1;
                canvas.Children.Add(new Polygon { Fill = Ui.Gray, Points = new PointCollection { b, new Point(b.X + d * 9, b.Y - 5), new Point(b.X + d * 9, b.Y + 5) } });
                var m = GraphGeo.Mid(a, c1, c2, b);
                var lab = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Child = new TextBlock { Text = Fmt.Amt(e.Amount) + " " + e.Asset, FontSize = 11, Foreground = Ui.Text }, ToolTip = tip };
                lab.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(lab, m.X - lab.DesiredSize.Width / 2); Canvas.SetTop(lab, m.Y - lab.DesiredSize.Height / 2);
                canvas.Children.Add(lab);
            }
            foreach (var n in g.Nodes)
            {
                bool start = n.Depth == 0;
                string status = g.Status(n);
                var col = Ui.StatusBrush(status, n.Cat);
                var frame = new Rectangle { Width = GraphGeo.W, Height = GraphGeo.H, RadiusX = 4, RadiusY = 4, Fill = start ? Ui.BrandSoft : Brushes.White, Stroke = col, StrokeThickness = 1.2 };
                Canvas.SetLeft(frame, n.X); Canvas.SetTop(frame, n.Y);
                canvas.Children.Add(frame); marks[n.Id] = frame;
                var stripe = new Rectangle { Width = 5, Height = GraphGeo.H, Fill = col, RadiusX = 2, RadiusY = 2, IsHitTestVisible = false };
                Canvas.SetLeft(stripe, n.X); Canvas.SetTop(stripe, n.Y); canvas.Children.Add(stripe);
                var sp = new StackPanel { Width = GraphGeo.W - 20, IsHitTestVisible = false };
                sp.Children.Add(new TextBlock { Text = GraphGeo.Title(n), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Text, TextTrimming = TextTrimming.CharacterEllipsis });
                string sub = n.Frozen != "" ? Ui.StatusName("frozen") + " · " + n.Frozen : status == "holding" && n.Balance > 0 ? L.F("holds {0} {1}", Fmt.Amt(Math.Round(n.Balance, 2)), Chains.Get(g.Chain).Symbol) : start ? L.T("Start") : n.Label != "" ? Fmt.Short(n.Id) : Ui.StatusName(status);
                sp.Children.Add(new TextBlock { Text = sub, FontSize = 10.5, Foreground = n.Frozen != "" || status == "holding" ? col : Ui.Muted, FontWeight = n.Frozen != "" ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis, FlowDirection = L.Ar && !(n.Label != "" && n.Frozen == "" && status != "holding") ? FlowDirection.RightToLeft : FlowDirection.LeftToRight });
                Canvas.SetLeft(sp, n.X + 13); Canvas.SetTop(sp, n.Y + 6); canvas.Children.Add(sp);
                frame.Cursor = Cursors.Hand; frame.ToolTip = Tip(n);
                var node = n;
                frame.MouseLeftButtonDown += (_, e) => { Select(node); e.Handled = true; };
            }
        }
    }

    /// <summary>Bubble map: every address is a circle sized by the value that reached it and coloured by where that value stands now.</summary>
    public class BubbleView : PanZoomView
    {
        public BubbleView(double height = 520) : base(height) { }
        protected override Rect Bounds()
        {
            double x0 = g.Nodes.Min(n => n.Bx - n.R), y0 = g.Nodes.Min(n => n.By - n.R), x1 = g.Nodes.Max(n => n.Bx + n.R), y1 = g.Nodes.Max(n => n.By + n.R);
            return new Rect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
        }
        protected override double MarkThickness(bool selected) => selected ? 5 : 2;

        protected override void Draw()
        {
            foreach (var e in g.Edges)
            {
                var s = g.Node(e.From); var t = g.Node(e.To); if (s == null || t == null) continue;
                canvas.Children.Add(new Line { X1 = s.Bx, Y1 = s.By, X2 = t.Bx, Y2 = t.By, Stroke = e.Bridge ? Ui.Purple : Ui.Gray, StrokeDashArray = e.Bridge ? new DoubleCollection { 4, 3 } : null, Opacity = e.Bridge ? 0.8 : 0.45, StrokeThickness = Math.Min(6, GraphGeo.Thick(e, g)), ToolTip = $"{Fmt.Short(e.From)} → {Fmt.Short(e.To)}\n{Fmt.Amt(e.Amount)} {e.Asset} · {e.Count} tx" });
            }
            foreach (var n in g.Nodes.OrderByDescending(n => n.R))
            {
                var col = (SolidColorBrush)Ui.StatusBrush(g.Status(n), n.Cat);
                var c = new Ellipse { Width = n.R * 2, Height = n.R * 2, Fill = new SolidColorBrush(col.Color) { Opacity = 0.22 }, Stroke = col, StrokeThickness = 2, Cursor = Cursors.Hand, ToolTip = Tip(n) };
                Canvas.SetLeft(c, n.Bx - n.R); Canvas.SetTop(c, n.By - n.R);
                canvas.Children.Add(c); marks[n.Id] = c;
                var node = n;
                c.MouseLeftButtonDown += (_, e) => { Select(node); e.Handled = true; };
                if (n.R < 26) continue;
                var sp = new StackPanel { Width = n.R * 2 - 10, IsHitTestVisible = false };
                string name = n.Label != "" ? n.Label : Fmt.Short(n.Id).Split('…')[0] + "…";
                sp.Children.Add(new TextBlock { Text = name, FontSize = n.R > 50 ? 12.5 : 10.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Text, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                decimal v = Math.Max(g.UsdIn(n), g.UsdOut(n));
                if (v > 0) sp.Children.Add(new TextBlock { Text = GraphGeo.Money(v), FontSize = n.R > 50 ? 12 : 10, Foreground = Ui.Muted, TextAlignment = TextAlignment.Center });
                if (n.R > 44) sp.Children.Add(new TextBlock { Text = Ui.StatusName(g.Status(n)), FontSize = 10, Foreground = col, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FlowDirection = L.Ar ? FlowDirection.RightToLeft : FlowDirection.LeftToRight });
                sp.Measure(new Size(n.R * 2 - 10, double.PositiveInfinity));
                Canvas.SetLeft(sp, n.Bx - n.R + 5); Canvas.SetTop(sp, n.By - sp.DesiredSize.Height / 2);
                canvas.Children.Add(sp);
            }
        }
    }
}
