using System.Windows.Input;

namespace Facet;

// Scales a view to fit: fixed or content-height views shrink uniformly and fill views get the exact size.
// Boards no longer use it (they stretch with WidgetStretch, ADR 0109); previews of single widgets still do.
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

// Lays a widget's view out at its base size and stretches it to the tile, width and height apart (ADR 0109).
internal sealed class WidgetStretch : Decorator
{
    internal double BaseWidth { get; init; }
    internal double BaseHeight { get; init; }
    internal Size Scale { get; private set; } = new(1, 1);

    protected override Size MeasureOverride(Size available)
    {
        Child?.Measure(new Size(Math.Max(1, BaseWidth), Math.Max(1, BaseHeight)));
        return new Size(double.IsFinite(available.Width) ? available.Width : BaseWidth, double.IsFinite(available.Height) ? available.Height : BaseHeight);
    }

    protected override Size ArrangeOverride(Size size)
    {
        if (Child == null) return size;
        double width = Math.Max(1, BaseWidth), height = Math.Max(1, BaseHeight);
        Scale = new(size.Width / width, size.Height / height);
        Child.RenderTransform = Scale is { Width: 1, Height: 1 } ? Transform.Identity : new ScaleTransform(Scale.Width, Scale.Height);
        Child.Arrange(new Rect(0, 0, width, height));
        return size;
    }
}

// One tab's board, three screens wide and tall around its origin plus the view offset. Placements are board pixels (ADR 0109).
internal sealed class OverlayWorkspaceView : UserControl
{
    // Shared by every board; Controller sets it from Settings and the boards render again.
    internal static double Pitch { get; private set; } = WorkspaceGrid.DefaultCellSize;
    internal static string GridStyle { get; private set; } = WorkspaceGrid.Dots;
    internal static void UseGrid(Settings settings) { Pitch = WorkspaceGrid.CellSize(settings); GridStyle = WorkspaceGrid.Style(settings); }
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
    private const double GridSweepMs = 420, GridCellFadeMs = 200, GridMarkLevel = .6;
    // Marks closer than this on screen are thinned to every Nth grid line, so tiny cells stay cheap to draw.
    private const double MinMarkSpacing = 10;
    private readonly System.Diagnostics.Stopwatch gridClock = new();
    private bool gridFading;
    private double? gridInterruptedAt;

    internal string TabId { get; }
    internal bool IsDragging => drag != null;
    internal bool IsPanning => pan != null;
    // Edit drags snap to the grid; Alt inverts it for one drag.
    internal bool Snap { get; set; } = true;
    // Changed since the last save; a render keeps this live view instead of the stored one.
    internal bool ViewDirty { get; private set; }
    internal double ViewX => viewX;
    internal double ViewY => viewY;
    internal double Zoom => zoom;
    internal event Action? ViewChanged;
    // Fires while the tab bar's tile moves or after it settles, so the real tab strip can follow it.
    internal event Action? TabBarMoved;
    // Board pixels from the origin to each edge; unmeasured boards apply no limit yet.
    internal int HalfWidth => ActualWidth > 0 ? (int)(ActualWidth * 1.5) : WorkspaceLayouts.MaxCell;
    internal int HalfHeight => ActualHeight > 0 ? (int)(ActualHeight * 1.5) : WorkspaceLayouts.MaxCell;
    internal Func<WidgetPlacement, FrameworkElement>? CreateView;
    internal Func<WidgetPlacement, (int Width, int Height)>? BaseSize;
    internal Func<WidgetPlacement, bool>? HasPanel;
    internal Func<WidgetPlacement, bool>? DefaultPanel;
    // null clears the placement's choice so it follows the widget's default again.
    internal Action<string, bool?>? PanelChanged;
    internal Func<string, int, int, int, int, bool>? CommitMove;
    internal Action<string>? RemoveRequested;
    // The tab bar widget's placement shared by every tab; its tile only marks the place, the strip itself lies under the board.
    internal Func<WidgetPlacement?>? TabBar;
    internal Func<int, int, int, int, bool>? CommitTabBar;
    // Skin widget layer (ADR 0110): whether the world page draws a widget (given its view) or the tab bar instead of WPF,
    // the tab bar's base size, and pointer input over such a tile in base-size coordinates (id, kind, point, delta, button).
    internal Func<WidgetPlacement, FrameworkElement?, bool>? SkinDrawn;
    internal Func<bool>? TabBarSkinDrawn;
    internal Func<(int Width, int Height)>? TabBarBaseSize;
    internal Action<string, string, Point, int, int>? SkinPointer;
    // Fires whenever a tile moves on screen without the view changing, such as during a drag.
    internal event Action? TilesMoved;
    private readonly HashSet<string> skinTiles = [];
    internal bool IsSkinTile(string id) => skinTiles.Contains(id);

    // The skin-drawn tiles in this view's own pixels, following a drag, with the base size the page draws them at.
    internal IEnumerable<(string Id, Rect Bounds, int BaseWidth, int BaseHeight)> SkinTiles() =>
        Rects().Where(r => skinTiles.Contains(r.Id)).Select(r => {
            var (baseWidth, baseHeight) = SkinBase(r.Id);
            return (r.Id, Design.SkinWidgets.BoardToView(r.X, r.Y, r.Width, r.Height, ActualWidth, ActualHeight, viewX, viewY, Zoom), baseWidth, baseHeight);
        }).ToArray();

    private (int Width, int Height) SkinBase(string id) =>
        id == WorkspaceLayouts.TabBarWidget ? TabBarBaseSize?.Invoke() ?? (tiles[id].Item.Width, tiles[id].Item.Height)
        : BaseSize?.Invoke(tiles[id].Item) ?? (tiles[id].Item.Width, tiles[id].Item.Height);

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
        if (hasView) { viewX = Limit(viewX, HalfWidth, ActualWidth, size); viewY = Limit(viewY, HalfHeight, ActualHeight, size); }
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
        // Below 33% (after a fit) the wheel can only come back in, never go further out.
        double next = Math.Clamp(zoom * factor, Math.Min(zoom, WorkspaceLayouts.MinZoom), WorkspaceLayouts.MaxZoom);
        if (Math.Abs(next - 1) < .005 || (zoom - 1) * (next - 1) < 0) next = 1;
        double ratio = next / zoom;
        double fromOriginX = point.X - ActualWidth / 2 - viewX, fromOriginY = point.Y - ActualHeight / 2 - viewY;
        zoom = next;
        PanTo(viewX - fromOriginX * (ratio - 1), viewY - fromOriginY * (ratio - 1));
    }

    internal void ReturnToOrigin() => PanTo(0, 0);

    // Shows every widget and the tab bar at once, going under the wheel's 33% when they do not fit.
    internal void FitAll()
    {
        var rects = Rects().Select(r => (r.X, r.Y, r.Width, r.Height)).ToList();
        if (TabBar?.Invoke() is { } bar && !tiles.ContainsKey(bar.Id)) rects.Add((bar.X, bar.Y, bar.Width, bar.Height));
        if (WorkspaceLayouts.Fit(rects, ActualWidth, ActualHeight) is not { } fit) { ReturnToOrigin(); return; }
        TakeView();
        zoom = fit.Zoom;
        PanTo(fit.X, fit.Y);
    }
    internal void MarkViewSaved() => ViewDirty = false;

    // The board point at the middle of the screen.
    internal (int X, int Y) ViewedCenter() => ((int)Math.Round(-viewX / Zoom), (int)Math.Round(-viewY / Zoom));

    // Board pixels to this view's own coordinates.
    internal Point ToScreen(double x, double y) => new(ActualWidth / 2 + viewX + x * Zoom, ActualHeight / 2 + viewY + y * Zoom);

    // The tab bar's place, following its tile while it is dragged.
    internal (int X, int Y, int Width, int Height)? TabBarRect =>
        drag is { } state && state.Id == WorkspaceLayouts.TabBarWidget ? (state.NextX, state.NextY, state.NextWidth, state.NextHeight)
        : tiles.TryGetValue(WorkspaceLayouts.TabBarWidget, out var bar) ? (bar.Item.X, bar.Item.Y, bar.Item.Width, bar.Item.Height) : null;

    private IEnumerable<(string Id, int X, int Y, int Width, int Height)> Rects() =>
        tiles.Select(pair => drag is { } state && state.Id == pair.Key ? (pair.Key, state.NextX, state.NextY, state.NextWidth, state.NextHeight)
            : (pair.Key, pair.Value.Item.X, pair.Value.Item.Y, pair.Value.Item.Width, pair.Value.Item.Height));

    // A dragged tile covers its preview, not the place it left. Edges count as uncovered so marks run along them.
    internal bool IsCovered(double x, double y) => Rects().Any(r => x > r.X && x < r.X + r.Width && y > r.Y && y < r.Y + r.Height);

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if ((!editing && !gridFading) || FindResource("Edge") is not Brush edge) return;
        double size = Zoom, originX = ActualWidth / 2 + viewX, originY = ActualHeight / 2 + viewY;
        int stride = Math.Max(1, (int)Math.Ceiling(MinMarkSpacing / (Pitch * size)));
        double unit = Pitch * stride, pitch = unit * size;
        int halfColumns = (int)(HalfWidth / unit), halfRows = (int)(HalfHeight / unit);
        var tint = edge.Clone(); tint.Opacity = GridMarkLevel; tint.Freeze();
        var pen = new Pen(tint, 1); pen.Freeze();
        int firstRow = Math.Max(-halfRows, (int)Math.Floor(-originY / pitch)), firstColumn = Math.Max(-halfColumns, (int)Math.Floor(-originX / pitch));
        int lastRow = Math.Min(halfRows, (int)Math.Ceiling((ActualHeight - originY) / pitch)), lastColumn = Math.Min(halfColumns, (int)Math.Ceiling((ActualWidth - originX) / pitch));
        if (lastColumn < firstColumn || lastRow < firstRow) return;
        // Tiles are read once per frame in screen pixels, and marks are grouped by fade level so a frame issues a few dozen draws, not one per mark.
        var covered = Rects().Select(r => (Left: originX + r.X * size, Top: originY + r.Y * size, Right: originX + (r.X + r.Width) * size, Bottom: originY + (r.Y + r.Height) * size)).ToArray();
        bool Inside(double x, double y) { foreach (var r in covered) if (x > r.Left && x < r.Right && y > r.Top && y < r.Bottom) return true; return false; }
        const int Steps = 24;
        var groups = new StreamGeometryContext?[Steps + 1];
        var shapes = new StreamGeometry?[Steps + 1];
        StreamGeometryContext? Group(double x, double y)
        {
            int step = (int)Math.Round(GridLevel(x, y) * Steps);
            if (step <= 0) return null;
            if (groups[step] == null) { shapes[step] = new StreamGeometry(); groups[step] = shapes[step]!.Open(); }
            return groups[step];
        }
        void Line(StreamGeometryContext? g, double x1, double y1, double x2, double y2) { if (g == null) return; g.BeginFigure(new(x1, y1), false, false); g.LineTo(new(x2, y2), true, false); }
        bool lines = GridStyle == WorkspaceGrid.Lines, crosses = GridStyle == WorkspaceGrid.Crosses;
        double arm = Math.Min(6, pitch / 6), dot = Math.Max(1, Math.Min(2, pitch / 30));
        // Marks inside a widget are skipped; its edges keep theirs so lines run under rounded corners.
        for (int row = firstRow; row <= lastRow; row++)
            for (int column = firstColumn; column <= lastColumn; column++) {
                double x = originX + column * pitch, y = originY + row * pitch;
                if (lines) {
                    if (column < lastColumn && row > -halfRows && row < halfRows && !Inside(x + pitch / 2, y))
                        Line(Group(x + pitch / 2, y), x, y, x + pitch, y);
                    if (row < lastRow && column > -halfColumns && column < halfColumns && !Inside(x, y + pitch / 2))
                        Line(Group(x, y + pitch / 2), x, y, x, y + pitch);
                    continue;
                }
                if (Inside(x, y)) continue;
                if (Group(x, y) is not { } g) continue;
                if (crosses) { Line(g, x - arm, y, x + arm, y); Line(g, x, y - arm, x, y + arm); }
                else {
                    g.BeginFigure(new(x - dot, y), true, true);
                    g.ArcTo(new(x + dot, y), new(dot, dot), 0, false, SweepDirection.Clockwise, false, false);
                    g.ArcTo(new(x - dot, y), new(dot, dot), 0, false, SweepDirection.Clockwise, false, false);
                }
            }
        for (int step = 1; step <= Steps; step++) {
            if (groups[step] is not { } g) continue;
            g.Close(); shapes[step]!.Freeze();
            context.PushOpacity(step / (double)Steps);
            if (lines || crosses) context.DrawGeometry(null, pen, shapes[step]); else context.DrawGeometry(tint, null, shapes[step]);
            context.Pop();
        }
        if (FindResource("Mint") is not Brush mint) return;
        var mark = new Pen(mint, 2); mark.Freeze();
        context.PushOpacity(GridLevel(originX, originY));
        context.DrawLine(mark, new Point(originX - 12, originY), new Point(originX + 12, originY));
        context.DrawLine(mark, new Point(originX, originY - 12), new Point(originX, originY + 12));
        context.Pop();
    }

    // Both directions sweep from the top-left corner; a reversal continues from each cell's current level.
    private void FadeGrid()
    {
        if (!IsVisible) { StopGridFade(); return; }
        gridInterruptedAt = gridFading ? gridClock.Elapsed.TotalMilliseconds : null;
        gridClock.Restart();
        if (!gridFading) CompositionTarget.Rendering += AdvanceGridFade;
        gridFading = true;
    }

    private void AdvanceGridFade(object? sender, EventArgs e)
    {
        if (!IsVisible || gridClock.Elapsed.TotalMilliseconds >= GridSweepMs + GridCellFadeMs) StopGridFade();
        InvalidateVisual();
    }

    private void StopGridFade()
    {
        if (gridFading) CompositionTarget.Rendering -= AdvanceGridFade;
        gridFading = false; gridInterruptedAt = null; gridClock.Reset();
    }

    private double GridLevel(double x, double y)
    {
        double target = editing ? 1 : 0;
        if (!gridFading) return target;
        double delay = Math.Clamp((x + y) / Math.Max(1, ActualWidth + ActualHeight), 0, 1) * GridSweepMs;
        double Step(double elapsed) => Math.Clamp((elapsed - delay) / GridCellFadeMs, 0, 1);
        double from = gridInterruptedAt is { } at ? (editing ? 1 - Step(at) : Step(at)) : 1 - target;
        return from + (target - from) * Step(gridClock.Elapsed.TotalMilliseconds);
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
            if (tile.Children.OfType<WidgetStretch>().FirstOrDefault() is { } fit) fit.Child = null;
        tiles.Clear(); surface.Children.Clear(); skinTiles.Clear();
    }

    internal void Render(WorkspaceTab next, bool edit)
    {
        bool toggled = editing != edit;
        tab = next; editing = edit; drag = null; ReleaseViews();
        if (toggled) FadeGrid();
        // Empty space takes pointer input only while editing, so the tab strip and fixed pages below keep working otherwise.
        Background = editing ? Brushes.Transparent : null;
        if (!ViewDirty) {
            hasView = next.View != null;
            (viewX, viewY, zoom) = next.View is { } view ? (view.X, view.Y, view.Zoom) : (0, 0, 1);
        }
        foreach (var item in next.Widgets) {
            var tile = BuildTile(item, false);
            tiles[item.Id] = (tile, item); surface.Children.Add(tile); Place(tile, item.X, item.Y, item.Width, item.Height);
        }
        if (TabBar?.Invoke() is { } bar) {
            var tile = BuildTile(bar, true);
            tiles[bar.Id] = (tile, bar); surface.Children.Add(tile); Place(tile, bar.X, bar.Y, bar.Width, bar.Height);
        }
        UpdateView();
        InvalidateVisual();
        TabBarMoved?.Invoke();
        TilesMoved?.Invoke();
    }

    // The tab bar's default place follows the strip's natural size until the user moves it.
    internal void RefreshTabBar()
    {
        if (drag?.Id == WorkspaceLayouts.TabBarWidget || TabBar?.Invoke() is not { } bar || !tiles.TryGetValue(bar.Id, out var known)) return;
        tiles[bar.Id] = (known.Tile, bar);
        Place(known.Tile, bar.X, bar.Y, bar.Width, bar.Height);
        if (editing) InvalidateVisual();
        TabBarMoved?.Invoke();
    }

    private Grid BuildTile(WidgetPlacement item, bool tabBar)
    {
        var tile = new Grid { Tag = item.Id };
        if (tabBar) {
            if (TabBarSkinDrawn?.Invoke() == true) skinTiles.Add(item.Id);
            if (!editing) {
                if (skinTiles.Contains(item.Id)) tile.Children.Add(PointerCatcher(item.Id, tile));
                else tile.IsHitTestVisible = false;
                return tile;
            }
        }
        else {
            var inset = new Thickness(0);
            var view = CreateView?.Invoke(item);
            // The page draws this widget: no panel, and the view stays alive for its state but is not shown (ADR 0110).
            if (SkinDrawn?.Invoke(item, view) == true) {
                skinTiles.Add(item.Id);
                var (skinWidth, skinHeight) = BaseSize?.Invoke(item) ?? (item.Width, item.Height);
                tile.Children.Add(new WidgetStretch { BaseWidth = skinWidth, BaseHeight = skinHeight, Child = view, Visibility = Visibility.Hidden });
                if (!editing) { tile.Children.Add(PointerCatcher(item.Id, tile)); return tile; }
                return EditFrame(item, false, tile, false);
            }
            bool panel = HasPanel?.Invoke(item) == true;
            if (panel && Facet.Plugins.WidgetTheme.IsHud) {
                inset = new(20, 14, 20, 14);
                tile.Children.Add(new Facet.Widgets.HudPanel());
            }
            else if (panel) {
                // A skin without a frame image still gives the panel type its token surface.
                var frame = Design.Skins.Visuals?.Frame;
                inset = new(frame?.Padding ?? 12);
                tile.Children.Add(frame != null ? new Design.NineSliceFrame(frame) : new Border { Background = (Brush)FindResource("Panel"), BorderBrush = (Brush)FindResource("Edge"), BorderThickness = new(1), CornerRadius = new(12), IsHitTestVisible = false });
            }
            // The panel stays at tile size; only the view inside it stretches from its base size.
            var (baseWidth, baseHeight) = BaseSize?.Invoke(item) ?? (item.Width, item.Height);
            tile.Children.Add(new WidgetStretch { BaseWidth = baseWidth - inset.Left - inset.Right, BaseHeight = baseHeight - inset.Top - inset.Bottom, Margin = inset, Child = view });
            if (!editing) return tile;
        }
        return EditFrame(item, tabBar, tile, !tabBar);
    }

    // A transparent surface that keeps hit-testing on a skin-drawn tile and hands its pointer to the page.
    private Border PointerCatcher(string id, Grid tile)
    {
        var catcher = new Border { Background = Brushes.Transparent };
        System.Windows.Automation.AutomationProperties.SetName(catcher, "스킨 위젯");
        Point At(MouseEventArgs e) => Design.SkinWidgets.ToBase(e.GetPosition(tile), new Size(tile.ActualWidth, tile.ActualHeight), SkinBase(id).Width, SkinBase(id).Height);
        static int Button(MouseButton button) => button switch { MouseButton.Middle => 1, MouseButton.Right => 2, _ => 0 };
        catcher.MouseMove += (_, e) => SkinPointer?.Invoke(id, "move", At(e), 0, 0);
        catcher.MouseDown += (_, e) => { catcher.CaptureMouse(); SkinPointer?.Invoke(id, "down", At(e), 0, Button(e.ChangedButton)); };
        catcher.MouseUp += (_, e) => {
            if (catcher.IsMouseCaptured && Mouse.LeftButton == MouseButtonState.Released && Mouse.RightButton == MouseButtonState.Released && Mouse.MiddleButton == MouseButtonState.Released) catcher.ReleaseMouseCapture();
            SkinPointer?.Invoke(id, "up", At(e), 0, Button(e.ChangedButton));
        };
        catcher.MouseWheel += (_, e) => { e.Handled = true; SkinPointer?.Invoke(id, "wheel", At(e), e.Delta, 0); };
        catcher.MouseLeave += (_, e) => { if (!catcher.IsMouseCaptured) SkinPointer?.Invoke(id, "leave", At(e), 0, 0); };
        return catcher;
    }

    private Grid EditFrame(WidgetPlacement item, bool tabBar, Grid tile, bool panelToggle)
    {
        var cover = new Border { Background = (Brush)FindResource("Panel"), Opacity = .55, CornerRadius = new(8), BorderThickness = new(1), BorderBrush = (Brush)FindResource("Mint"), Cursor = Cursors.SizeAll };
        System.Windows.Automation.AutomationProperties.SetName(cover, tabBar ? "탭 막대 이동" : "위젯 이동");
        cover.MouseLeftButtonDown += (_, e) => BeginDrag(item, false, cover, e);
        Hook(cover); tile.Children.Add(cover);
        // The tab bar cannot be removed.
        if (!tabBar) {
            var remove = new Button { Content = "✕", Width = 28, Height = 28, Padding = new(0), Margin = new(4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            System.Windows.Automation.AutomationProperties.SetName(remove, "위젯 제거");
            remove.Click += (_, _) => RemoveRequested?.Invoke(item.Id);
            tile.Children.Add(remove);
            if (panelToggle && PanelChanged != null) {
                var toggle = new System.Windows.Controls.Primitives.ToggleButton { Content = "▣", IsChecked = HasPanel?.Invoke(item) == true, Width = 28, Height = 28, Padding = new(0), Margin = new(4, 4, 36, 4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, ToolTip = "배경 패널 켜기/끄기" };
                System.Windows.Automation.AutomationProperties.SetName(toggle, "배경 패널");
                toggle.Click += (_, _) => {
                    bool next = toggle.IsChecked == true;
                    PanelChanged(item.Id, next == (DefaultPanel?.Invoke(item) == true) ? null : next);
                };
                tile.Children.Add(toggle);
            }
        }
        var handle = new Border { Width = 20, Height = 20, Margin = new(2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new(4), Background = (Brush)FindResource("Mint"), Cursor = Cursors.SizeNWSE,
            ToolTip = "끌어서 크기 조절 · Shift: 비율 유지 · Alt: 스냅 반대로" };
        System.Windows.Automation.AutomationProperties.SetName(handle, tabBar ? "탭 막대 크기 조절" : "위젯 크기 조절");
        handle.MouseLeftButtonDown += (_, e) => BeginDrag(item, true, handle, e);
        Hook(handle); tile.Children.Add(handle);
        return tile;
    }

    private void Hook(UIElement target)
    {
        target.MouseMove += (_, e) => { if (drag != null) UpdateDrag(e.GetPosition(surface), Keyboard.Modifiers); };
        target.MouseLeftButtonUp += (_, e) => { if (drag != null) { e.Handled = true; FinishDrag(); target.ReleaseMouseCapture(); } };
        target.LostMouseCapture += (_, _) => CancelDrag();
    }

    private static void Place(FrameworkElement tile, int x, int y, int width, int height)
    {
        Canvas.SetLeft(tile, x); Canvas.SetTop(tile, y);
        tile.Width = width; tile.Height = height;
    }

    private void BeginDrag(WidgetPlacement item, bool resize, UIElement capture, MouseButtonEventArgs e)
    {
        e.Handled = true;
        drag = (item.Id, resize, e.GetPosition(surface), item.X, item.Y, item.Width, item.Height, item.X, item.Y, item.Width, item.Height);
        Panel.SetZIndex(tiles[item.Id].Tile, 1);
        capture.CaptureMouse();
    }

    private bool CanDrop(string id, int x, int y, int width, int height) =>
        id == WorkspaceLayouts.TabBarWidget ? WorkspaceLayouts.InBounds(x, y, width, height) : tab != null && WorkspaceLayouts.CanPlace(tab, id, x, y, width, height);

    // Moves by the pointer, snapped to the grid unless Snap is off (Alt inverts it); a corner drag stretches freely or,
    // with Shift, keeps the start's aspect ratio. The tile previews the result and dims while it cannot be dropped.
    internal void UpdateDrag(Point position, ModifierKeys modifiers = ModifierKeys.None)
    {
        if (drag is not { } state || tab == null) return;
        int step = Snap ^ modifiers.HasFlag(ModifierKeys.Alt) ? Math.Max(1, (int)Pitch) : 1;
        double dx = position.X - state.Start.X, dy = position.Y - state.Start.Y;
        int x = state.X, y = state.Y, width = state.Width, height = state.Height;
        if (state.Resize) (width, height) = WorkspaceLayouts.Resize(x, y, width, height, dx, dy, modifiers.HasFlag(ModifierKeys.Shift), step, HalfWidth, HalfHeight);
        else {
            x = Math.Clamp(WorkspaceLayouts.Snap(x + dx, step), -HalfWidth, Math.Max(-HalfWidth, HalfWidth - width));
            y = Math.Clamp(WorkspaceLayouts.Snap(y + dy, step), -HalfHeight, Math.Max(-HalfHeight, HalfHeight - height));
        }
        drag = state with { NextX = x, NextY = y, NextWidth = width, NextHeight = height };
        var tile = tiles[state.Id].Tile;
        Place(tile, x, y, width, height);
        tile.Opacity = CanDrop(state.Id, x, y, width, height) ? 1 : .4;
        InvalidateVisual();
        if (state.Id == WorkspaceLayouts.TabBarWidget) TabBarMoved?.Invoke();
        TilesMoved?.Invoke();
    }

    internal void FinishDrag()
    {
        if (drag is not { } state || tab == null) return;
        drag = null;
        var tile = tiles[state.Id].Tile; Panel.SetZIndex(tile, 0); tile.Opacity = 1;
        bool changed = (state.NextX, state.NextY, state.NextWidth, state.NextHeight) != (state.X, state.Y, state.Width, state.Height);
        bool bar = state.Id == WorkspaceLayouts.TabBarWidget;
        if (changed && CanDrop(state.Id, state.NextX, state.NextY, state.NextWidth, state.NextHeight)
            && (bar ? CommitTabBar?.Invoke(state.NextX, state.NextY, state.NextWidth, state.NextHeight) : CommitMove?.Invoke(state.Id, state.NextX, state.NextY, state.NextWidth, state.NextHeight)) == true) return;
        Place(tile, state.X, state.Y, state.Width, state.Height);
        InvalidateVisual();
        if (bar) TabBarMoved?.Invoke();
        TilesMoved?.Invoke();
    }

    internal bool CancelDrag()
    {
        if (drag is not { } state) return false;
        drag = null;
        var tile = tiles[state.Id].Tile; Panel.SetZIndex(tile, 0); tile.Opacity = 1;
        Place(tile, state.X, state.Y, state.Width, state.Height);
        InvalidateVisual();
        if (state.Id == WorkspaceLayouts.TabBarWidget) TabBarMoved?.Invoke();
        TilesMoved?.Invoke();
        return true;
    }

    internal void BeginDragForCheck(string id, bool resize = false)
    {
        var item = tiles[id].Item;
        drag = (id, resize, new Point(), item.X, item.Y, item.Width, item.Height, item.X, item.Y, item.Width, item.Height);
    }

    internal FrameworkElement? TileForCheck(string id) => tiles.TryGetValue(id, out var known) ? known.Tile : null;
}
