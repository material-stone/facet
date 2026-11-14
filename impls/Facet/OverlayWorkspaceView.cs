using System.Windows.Input;

namespace Facet;

// Scales a tile's view to fit: fixed or content-height views shrink uniformly and fill views get the exact tile.
internal sealed class WidgetFit : Decorator
{
    internal bool FillHeight { get; init; }
    // Keeps the shrink-to-fit of a tall child but hands a short one the spare height (soft HUD form).
    internal bool StretchChild { get; init; }
    private double scale = 1;

    protected override Size MeasureOverride(Size available)
    {
        if (Child == null) return default;
        if (FillHeight) { scale = 1; Child.Measure(available); return available; }
        double fixedWidth = Child is FrameworkElement { Width: > 0 and var width } ? width : double.NaN;
        double natural = double.IsNaN(fixedWidth) ? available.Width : fixedWidth;
        scale = Math.Min(1, available.Width / natural);
        Child.Measure(new Size(natural, double.PositiveInfinity));
        if (Child.DesiredSize.Height * scale > available.Height) {
            scale = available.Height / Child.DesiredSize.Height;
            // A narrower scale leaves more logical width, which can reflow to less height.
            if (double.IsNaN(fixedWidth)) Child.Measure(new Size(available.Width / scale, double.PositiveInfinity));
        }
        return available;
    }

    protected override Size ArrangeOverride(Size size)
    {
        if (Child == null) return size;
        Child.RenderTransform = scale < 1 ? new ScaleTransform(scale, scale) : Transform.Identity;
        Child.Arrange(new Rect(0, 0, FillHeight ? size.Width : Math.Max(size.Width / scale, 0), FillHeight ? size.Height : StretchChild ? Math.Max(Child.DesiredSize.Height, size.Height / scale) : Child.DesiredSize.Height));
        return size;
    }
}

// One tab's board, three screens wide and tall around its origin, which sits between four cells at the center plus the view offset.
internal sealed class OverlayWorkspaceView : UserControl
{
    internal const double Cell = 80, Gap = 16, Pitch = Cell + Gap;
    private readonly Canvas surface = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly ScaleTransform scale = new();
    private readonly TranslateTransform move = new();
    private readonly Dictionary<string, (Grid Tile, WidgetPlacement Item)> tiles = [];
    private WorkspaceTab? tab;
    private bool editing;
    private (string Id, bool Resize, Point Start, int X, int Y, int Width, int Height, int NextX, int NextY, int NextWidth, int NextHeight)? drag;
    private double viewX, viewY, zoom = 1;
    private bool hasView;
    private (Point Start, double X, double Y)? pan;

    internal string TabId { get; }
    internal bool IsDragging => drag != null;
    internal bool IsPanning => pan != null;
    // Changed since the last save; a render keeps this live view instead of the stored one.
    internal bool ViewDirty { get; private set; }
    internal double ViewX => viewX;
    internal double ViewY => viewY;
    internal double Zoom => zoom;
    internal event Action? ViewChanged;
    // Whole cells from the origin to each edge; unmeasured boards apply no limit yet.
    internal int HalfColumns => ActualWidth > 0 ? (int)(ActualWidth * 1.5 / Pitch) : WorkspaceLayouts.MaxCell;
    internal int HalfRows => ActualHeight > 0 ? (int)(ActualHeight * 1.5 / Pitch) : WorkspaceLayouts.MaxCell;
    internal Func<WidgetPlacement, FrameworkElement>? CreateView;
    internal Func<WidgetPlacement, (int Columns, int Rows)>? MinimumSize;
    internal Func<WidgetPlacement, bool>? FillsHeight;
    internal Func<WidgetPlacement, bool>? HasPanel;
    internal Func<string, int, int, int, int, bool>? CommitMove;
    internal Action<string>? RemoveRequested;

    internal OverlayWorkspaceView(string tabId)
    {
        TabId = tabId; Focusable = false; ClipToBounds = true;
        var transform = new TransformGroup(); transform.Children.Add(scale); transform.Children.Add(move);
        surface.RenderTransform = transform;
        Content = surface;
        SizeChanged += (_, _) => UpdateView();
    }

    // The screen stays on the board; when the board is smaller than the screen it stays centered.
    private static double Limit(double offset, double boardHalf, double screen, double size) =>
        screen <= 0 ? offset : Math.Clamp(offset, -Math.Max(0, boardHalf * size - screen / 2), Math.Max(0, boardHalf * size - screen / 2));

    private void UpdateView()
    {
        double size = Zoom;
        if (hasView) { viewX = Limit(viewX, HalfColumns * Pitch, ActualWidth, size); viewY = Limit(viewY, HalfRows * Pitch, ActualHeight, size); }
        scale.ScaleX = scale.ScaleY = size;
        move.X = ActualWidth / 2 + viewX; move.Y = ActualHeight / 2 + viewY;
        if (editing) InvalidateVisual();
        ViewChanged?.Invoke();
    }

    private void TakeView()
    {
        hasView = true;
        ViewDirty = true;
    }

    internal void PanTo(double x, double y)
    {
        TakeView();
        viewX = Math.Clamp(x, -WorkspaceLayouts.MaxOffset, WorkspaceLayouts.MaxOffset); viewY = Math.Clamp(y, -WorkspaceLayouts.MaxOffset, WorkspaceLayouts.MaxOffset);
        UpdateView();
    }

    // Keeps the board point under the pointer in place. A step that would cross 100% stops there.
    internal void ZoomAt(Point point, double factor)
    {
        TakeView();
        double next = Math.Clamp(zoom * factor, WorkspaceLayouts.MinZoom, WorkspaceLayouts.MaxZoom);
        if (Math.Abs(next - 1) < .005 || (zoom - 1) * (next - 1) < 0) next = 1;
        double ratio = next / zoom;
        double fromOriginX = point.X - ActualWidth / 2 - viewX, fromOriginY = point.Y - ActualHeight / 2 - viewY;
        zoom = next;
        PanTo(viewX - fromOriginX * (ratio - 1), viewY - fromOriginY * (ratio - 1));
    }

    internal void ReturnToOrigin() => PanTo(0, 0);
    internal void MarkViewSaved() => ViewDirty = false;

    // The cell at the top-left of a default-sized area around the view's center.
    internal (int Left, int Top) ViewedArea() => (
        (int)Math.Round(-viewX / Zoom / Pitch) - WorkspaceLayouts.Columns / 2,
        (int)Math.Round(-viewY / Zoom / Pitch) - WorkspaceLayouts.Rows / 2);

    // A dragged tile covers the cells of its preview, not the ones it left.
    internal bool IsCellCovered(int column, int row)
    {
        foreach (var (id, (_, item)) in tiles) {
            var (x, y, width, height) = drag is { } state && state.Id == id ? (state.NextX, state.NextY, state.NextWidth, state.NextHeight) : (item.X, item.Y, item.Width, item.Height);
            if (column >= x && column < x + width && row >= y && row < y + height) return true;
        }
        return false;
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if (!editing || FindResource("Edge") is not Brush edge) return;
        double size = Zoom, pitch = Pitch * size, originX = ActualWidth / 2 + viewX, originY = ActualHeight / 2 + viewY, inset = Gap / 2 * size;
        var pen = new Pen(edge, 1); pen.Freeze();
        for (int row = Math.Max(-HalfRows, (int)Math.Floor(-originY / pitch)); row < HalfRows && originY + row * pitch < ActualHeight; row++)
            for (int column = Math.Max(-HalfColumns, (int)Math.Floor(-originX / pitch)); column < HalfColumns && originX + column * pitch < ActualWidth; column++)
                if (!IsCellCovered(column, row)) context.DrawRoundedRectangle(null, pen, new Rect(originX + column * pitch + inset, originY + row * pitch + inset, Cell * size, Cell * size), 8 * size, 8 * size);
        if (FindResource("Mint") is not Brush mint) return;
        var mark = new Pen(mint, 2); mark.Freeze();
        context.DrawLine(mark, new Point(originX - 12, originY), new Point(originX + 12, originY));
        context.DrawLine(mark, new Point(originX, originY - 12), new Point(originX, originY + 12));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!editing || e.Handled) return;
        pan = (e.GetPosition(this), viewX, viewY);
        e.Handled = CaptureMouse();
        if (!e.Handled) pan = null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (pan is not { } state) return;
        var point = e.GetPosition(this);
        PanTo(state.X + point.X - state.Start.X, state.Y + point.Y - state.Start.Y);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (pan != null) { e.Handled = true; ReleaseMouseCapture(); }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); pan = null; }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (!editing) return;
        e.Handled = true;
        ZoomAt(e.GetPosition(this), Math.Pow(1.1, e.Delta / 120d));
    }

    // Views can move between tabs, so every workspace releases its views before any renders again.
    internal void ReleaseViews()
    {
        foreach (var (tile, _) in tiles.Values)
            if (tile.Children.OfType<WidgetFit>().FirstOrDefault() is { } fit) fit.Child = null;
        tiles.Clear(); surface.Children.Clear();
    }

    internal void Render(WorkspaceTab next, bool edit)
    {
        tab = next; editing = edit; drag = null; ReleaseViews();
        // Empty cells take pointer input only while editing, so a fixed page below keeps working otherwise.
        Background = editing ? Brushes.Transparent : null;
        if (!ViewDirty) {
            hasView = next.View != null;
            (viewX, viewY, zoom) = next.View is { } view ? (view.X, view.Y, view.Zoom) : (0, 0, 1);
        }
        foreach (var item in next.Widgets) {
            var tile = BuildTile(item);
            tiles[item.Id] = (tile, item); surface.Children.Add(tile); Place(tile, item.X, item.Y, item.Width, item.Height);
        }
        UpdateView();
        InvalidateVisual();
    }

    private Grid BuildTile(WidgetPlacement item)
    {
        var tile = new Grid { Tag = item.Id };
        double padding = 0;
        var inset = new Thickness(0);
        bool panel = HasPanel?.Invoke(item) == true;
        if (panel && Facet.Plugins.WidgetTheme.IsHud) {
            inset = new(20, 14, 20, 14);
            tile.Children.Add(new Facet.Widgets.HudPanel());
        }
        else if (panel) {
            // A skin without a frame image still gives the panel type its token surface.
            var frame = Design.Skins.Visuals?.Frame;
            padding = frame?.Padding ?? 12;
            tile.Children.Add(frame != null ? new Design.NineSliceFrame(frame) : new Border { Background = (Brush)FindResource("Panel"), BorderBrush = (Brush)FindResource("Edge"), BorderThickness = new(1), CornerRadius = new(12), IsHitTestVisible = false });
        }
        tile.Children.Add(new WidgetFit { FillHeight = FillsHeight?.Invoke(item) == true, StretchChild = panel && Facet.Plugins.WidgetTheme.IsHud, Margin = panel && Facet.Plugins.WidgetTheme.IsHud ? inset : new(padding), Child = CreateView?.Invoke(item) });
        if (!editing) return tile;
        var cover = new Border { Background = (Brush)FindResource("Panel"), Opacity = .55, CornerRadius = new(8), BorderThickness = new(1), BorderBrush = (Brush)FindResource("Mint"), Cursor = Cursors.SizeAll };
        System.Windows.Automation.AutomationProperties.SetName(cover, "위젯 이동");
        cover.MouseLeftButtonDown += (_, e) => BeginDrag(item, false, cover, e);
        Hook(cover); tile.Children.Add(cover);
        var remove = new Button { Content = "✕", Width = 28, Height = 28, Padding = new(0), Margin = new(4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        System.Windows.Automation.AutomationProperties.SetName(remove, "위젯 제거");
        remove.Click += (_, _) => RemoveRequested?.Invoke(item.Id);
        tile.Children.Add(remove);
        var handle = new Border { Width = 20, Height = 20, Margin = new(2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new(4), Background = (Brush)FindResource("Mint"), Cursor = Cursors.SizeNWSE };
        System.Windows.Automation.AutomationProperties.SetName(handle, "위젯 크기 조절");
        handle.MouseLeftButtonDown += (_, e) => BeginDrag(item, true, handle, e);
        Hook(handle); tile.Children.Add(handle);
        return tile;
    }

    private void Hook(UIElement target)
    {
        target.MouseMove += (_, e) => { if (drag != null) UpdateDrag(e.GetPosition(surface)); };
        target.MouseLeftButtonUp += (_, e) => { if (drag != null) { e.Handled = true; FinishDrag(); target.ReleaseMouseCapture(); } };
        target.LostMouseCapture += (_, _) => CancelDrag();
    }

    private static void Place(FrameworkElement tile, int x, int y, int width, int height)
    {
        Canvas.SetLeft(tile, x * Pitch + Gap / 2); Canvas.SetTop(tile, y * Pitch + Gap / 2);
        tile.Width = width * Pitch - Gap; tile.Height = height * Pitch - Gap;
    }

    private void BeginDrag(WidgetPlacement item, bool resize, UIElement capture, MouseButtonEventArgs e)
    {
        e.Handled = true;
        drag = (item.Id, resize, e.GetPosition(surface), item.X, item.Y, item.Width, item.Height, item.X, item.Y, item.Width, item.Height);
        Panel.SetZIndex(tiles[item.Id].Tile, 1);
        capture.CaptureMouse();
    }

    // Snaps the pointer to cells; the tile previews the snapped rectangle and dims while it cannot be dropped.
    internal void UpdateDrag(Point position)
    {
        if (drag is not { } state || tab == null) return;
        int columns = (int)Math.Round((position.X - state.Start.X) / Pitch), rows = (int)Math.Round((position.Y - state.Start.Y) / Pitch);
        var item = tiles[state.Id].Item;
        int x = state.X, y = state.Y, width = state.Width, height = state.Height;
        if (state.Resize) {
            var minimum = MinimumSize?.Invoke(item) ?? (1, 1);
            width = Math.Clamp(width + columns, minimum.Item1, Math.Max(minimum.Item1, HalfColumns - x));
            height = Math.Clamp(height + rows, minimum.Item2, Math.Max(minimum.Item2, HalfRows - y));
        }
        else {
            x = Math.Clamp(x + columns, -HalfColumns, Math.Max(-HalfColumns, HalfColumns - width));
            y = Math.Clamp(y + rows, -HalfRows, Math.Max(-HalfRows, HalfRows - height));
        }
        drag = state with { NextX = x, NextY = y, NextWidth = width, NextHeight = height };
        var tile = tiles[state.Id].Tile;
        Place(tile, x, y, width, height);
        tile.Opacity = WorkspaceLayouts.CanPlace(tab, state.Id, x, y, width, height) ? 1 : .4;
        InvalidateVisual();
    }

    internal void FinishDrag()
    {
        if (drag is not { } state || tab == null) return;
        drag = null;
        var tile = tiles[state.Id].Tile; Panel.SetZIndex(tile, 0); tile.Opacity = 1;
        bool changed = (state.NextX, state.NextY, state.NextWidth, state.NextHeight) != (state.X, state.Y, state.Width, state.Height);
        if (changed && WorkspaceLayouts.CanPlace(tab, state.Id, state.NextX, state.NextY, state.NextWidth, state.NextHeight)
            && CommitMove?.Invoke(state.Id, state.NextX, state.NextY, state.NextWidth, state.NextHeight) == true) return;
        Place(tile, state.X, state.Y, state.Width, state.Height);
        InvalidateVisual();
    }

    internal bool CancelDrag()
    {
        if (drag is not { } state) return false;
        drag = null;
        var tile = tiles[state.Id].Tile; Panel.SetZIndex(tile, 0); tile.Opacity = 1;
        Place(tile, state.X, state.Y, state.Width, state.Height);
        InvalidateVisual();
        return true;
    }

    internal void BeginDragForCheck(string id)
    {
        var item = tiles[id].Item;
        drag = (id, false, new Point(), item.X, item.Y, item.Width, item.Height, item.X, item.Y, item.Width, item.Height);
    }
}
