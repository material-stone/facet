using System.Globalization;
using System.Windows.Documents;
using System.Windows.Input;

namespace Facet;

internal sealed class LayoutInspector : Adorner
{
    private readonly FrameworkElement root;
    private FrameworkElement? target;
    private readonly AdornerLayer layer;
    internal Rect? HoverBounds { get; private set; }

    internal LayoutInspector(FrameworkElement root, AdornerLayer layer) : base(root)
    {
        this.root = root; this.layer = layer; IsHitTestVisible = false;
        root.PreviewMouseMove += Move; root.MouseLeave += Leave; layer.Add(this);
    }

    private void Move(object sender, MouseEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source != null && source is not FrameworkElement)
            source = source is FrameworkContentElement content ? content.Parent : source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        Inspect(source as FrameworkElement);
    }

    private void Leave(object sender, MouseEventArgs e) => Inspect(null);

    internal void Inspect(FrameworkElement? element)
    {
        target = element != null && (ReferenceEquals(element, root) || root.IsAncestorOf(element)) ? element : null;
        HoverBounds = target == null ? null : target.TransformToAncestor(root).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var accent = (Brush)root.FindResource("Mint"); var panel = (Brush)root.FindResource("Panel");
        double dpi = VisualTreeHelper.GetDpi(root).PixelsPerDip;
        drawing.PushClip(new RectangleGeometry(new Rect(root.RenderSize)));
        var badge = new FormattedText("F8 · 영역 디버그", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Design.Typography.Regular, 11, accent, dpi);
        drawing.DrawText(badge, new Point(Math.Max(8, root.ActualWidth-badge.Width-24), 24));
        if (target != null && HoverBounds is Rect bounds && bounds.Width > 0 && bounds.Height > 0) {
            drawing.DrawRectangle(null, new Pen(accent, 1.5), bounds);
            string name = string.IsNullOrWhiteSpace(target.Name) ? target.GetType().Name : target.Name;
            var label = new FormattedText($"{name}  {bounds.Width:0.#} × {bounds.Height:0.#}", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Design.Typography.Regular, 11, accent, dpi);
            double left = Math.Clamp(bounds.Left, 4, Math.Max(4, root.ActualWidth-label.Width-12));
            double top = bounds.Top > label.Height+12 ? bounds.Top-label.Height-8 : bounds.Bottom+4;
            top = Math.Clamp(top, 4, Math.Max(4, root.ActualHeight-label.Height-12));
            drawing.DrawRoundedRectangle(panel, new Pen(accent, 1), new Rect(left, top, label.Width+12, label.Height+6), 4, 4);
            drawing.DrawText(label, new Point(left+6, top+3));
        }
        drawing.Pop();
    }

    internal void Detach()
    {
        root.PreviewMouseMove -= Move; root.MouseLeave -= Leave; layer.Remove(this);
    }
}
