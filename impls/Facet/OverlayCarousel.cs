using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Facet;

public sealed class OverlayCarousel : Panel
{
    // Keep content layout stable while only its presentation size changes.
    public double ItemWidth => 1280;
    public double ItemHeight => 800;
    public int SelectedIndex { get; private set; }
    internal bool NavigationLocked { get; set; }
    public Func<bool>? BeforeNavigate { get; set; }
    public event Action? NavigationCompleted;
    internal bool AnimateTransitions { get; set; } = true;
    internal FrameworkElement SelectedContent => Children[SelectedIndex] is Viewbox view
        ? (FrameworkElement)view.Child : (FrameworkElement)Children[SelectedIndex];
    internal double ItemGap { get; private set; }
    internal int DragFrameCount { get; private set; }
    internal bool IsMotionActive { get; private set; }
    private const double NeighborScale = .86;
    private Point origin;
    private bool candidate, dragging, inactivePress, pendingFrame;
    private double pendingOffset, cardWidth, cardHeight;
    private Size arrangedSize;
    private int arrangedCount;
    private int motionTarget = -1;
    private int motionVersion;
    private int hiddenWrap = -1;
    private Pose[] dragStart = [];
    private readonly record struct Pose(double X, double Y, double Scale);
    private double Travel => Math.Max(100,cardWidth*.55);

    public OverlayCarousel()
    {
        Background = Brushes.Transparent; ClipToBounds = true;
        PreviewMouseLeftButtonDown += (_, e) => { BeginDrag(e.GetPosition(this),e.OriginalSource as DependencyObject); if (inactivePress) e.Handled = true; };
        PreviewMouseMove += (_, e) => {
            if (e.LeftButton == MouseButtonState.Pressed) { if (ContinueDrag(e.GetPosition(this))) e.Handled = true; }
            else if (dragging) CompleteDrag(e.GetPosition(this));
        };
        PreviewMouseLeftButtonUp += (_, e) => { if (CompleteDrag(e.GetPosition(this))) e.Handled = true; };
        LostMouseCapture += (_, _) => { if (dragging) CancelDrag(); };
        Unloaded += (_, _) => CancelDrag();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 1440 : Math.Max(1,availableSize.Width);
        double height = double.IsInfinity(availableSize.Height) ? 800 : Math.Max(1,availableSize.Height);
        ItemGap = Math.Clamp(width*.018,18,40);
        // Keep the selected item whole; only a small portion of each neighbor is visible.
        cardWidth = Math.Max(1,Math.Min(ItemWidth,Math.Min(width*.82,Math.Max(1,height-48)*ItemWidth/ItemHeight)));
        cardHeight = cardWidth*ItemHeight/ItemWidth;
        EnsureTransforms();
        foreach (UIElement child in Children) {
            child.Measure(new Size(cardWidth,cardHeight));
        }
        return new(width,cardHeight+48);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in Children) child.Arrange(new Rect(0,0,cardWidth,cardHeight));
        if (arrangedSize != finalSize || arrangedCount != Children.Count) {
            arrangedSize = finalSize; arrangedCount = Children.Count; CancelDrag(); UpdateNavigation();
        }
        return finalSize;
    }

    public bool Select(int index, bool animate = true)
    {
        if (NavigationLocked && index != SelectedIndex) return false;
        if (index < 0 || index >= Children.Count) return false;
        if (index != SelectedIndex && BeforeNavigate?.Invoke() == false) return false;
        EnsureTransforms();
        StopFrameUpdates(); candidate = dragging = inactivePress = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Settle(index,animate); return true;
    }

    internal bool BeginDrag(Point point, DependencyObject? source)
    {
        candidate = inactivePress = false;
        if (NavigationLocked || Children.Count<2) return false;
        int sourceItem = FindItem(source);
        inactivePress = sourceItem >= 0 && sourceItem != SelectedIndex;
        if (!inactivePress && IsInput(source)) return false;
        origin = point; candidate = true; return true;
    }

    internal bool ContinueDrag(Point point)
    {
        if (!candidate) return false;
        double dx = point.X-origin.X, dy = point.Y-origin.Y;
        if (!dragging) {
            if (Math.Abs(dx) < Math.Max(12,SystemParameters.MinimumHorizontalDragDistance) || Math.Abs(dx) <= Math.Abs(dy)) return false;
            dragStart = Enumerable.Range(0,Children.Count).Select(ReadPose).ToArray();
            RestoreWrap();
            for (int i=0;i<Children.Count;i++) ApplyPose(i,dragStart[i]);
            ++motionVersion;IsMotionActive=true;
            dragging = true; Mouse.Capture(this,CaptureMode.SubTree);
            CompositionTarget.Rendering += OnRendering;
        }
        pendingOffset = dx; pendingFrame = true;
        return true;
    }

    private void OnRendering(object? sender, EventArgs e) => RenderPendingDrag();

    internal void RenderPendingDrag()
    {
        if (!dragging || !pendingFrame) return;
        pendingFrame = false; DragFrameCount++;
        int target = Adjacent(pendingOffset<0 ? 1 : -1);
        if (motionTarget != target) {
            if (hiddenWrap>=0) ApplyPose(hiddenWrap,dragStart[hiddenWrap]);
            RestoreWrap();
            HideWrap(SelectedIndex,target);SetLayers(SelectedIndex,target);motionTarget=target;
        }
        double progress = Math.Min(1,Math.Abs(pendingOffset)/Travel);
        for (int i=0;i<Children.Count;i++) {
            if(i==hiddenWrap) {SetPose(i,LayoutPose(i,target));continue;}
            var from = dragStart[i];var to = LayoutPose(i,target);
            SetPose(i,new(from.X+(to.X-from.X)*progress,from.Y+(to.Y-from.Y)*progress,from.Scale+(to.Scale-from.Scale)*progress));
        }
    }

    internal bool CompleteDrag(Point point)
    {
        bool consumed = dragging || inactivePress, moved = dragging;
        int target = SelectedIndex;
        if (dragging && Math.Abs(point.X-origin.X) >= Math.Max(48,Math.Min(120,cardWidth*.18)))
            target = Adjacent(point.X<origin.X ? 1 : -1);
        StopFrameUpdates();candidate = dragging = inactivePress = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        // Preview motion never writes data. Commit only after the existing editor saves successfully.
        bool blocked = target != SelectedIndex && BeforeNavigate?.Invoke() == false;
        if (blocked) target = SelectedIndex;
        if (moved) Settle(target,true,notify:!blocked);
        return consumed;
    }

    private void StopFrameUpdates()
    {
        CompositionTarget.Rendering -= OnRendering;pendingFrame = false;
    }

    private void CancelDrag()
    {
        // Unloaded can arrive before the first measure, for example when tabs are rebuilt while plugins load.
        EnsureTransforms();
        StopFrameUpdates(); candidate = dragging = inactivePress = false;
        ++motionVersion;IsMotionActive=false;
        RestoreWrap();
        if (IsMouseCaptured) ReleaseMouseCapture();
        for (int i=0;i<Children.Count;i++) ApplyPose(i,LayoutPose(i,SelectedIndex));
        SetLayers(SelectedIndex,SelectedIndex);motionTarget=-1;
    }

    private int Adjacent(int direction) => Children.Count == 0 ? 0 : (SelectedIndex+direction+Children.Count)%Children.Count;

    private void EnsureTransforms()
    {
        foreach (UIElement child in Children)
            if (child.RenderTransform is not TransformGroup) child.RenderTransform = new TransformGroup {
                Children = { new ScaleTransform(), new TranslateTransform() }
            };
    }

    private Pose LayoutPose(int item, int selected)
    {
        int relative = (item-selected+Children.Count)%Children.Count;
        if (relative > Children.Count/2) relative -= Children.Count;
        double scale = item==selected ? 1 : NeighborScale;
        double center = arrangedSize.Width/2;
        double x = relative==0 ? center-cardWidth/2 : relative<0
            ? center-cardWidth/2-ItemGap-cardWidth*NeighborScale+(relative+1)*(cardWidth*NeighborScale+ItemGap)
            : center+cardWidth/2+ItemGap+(relative-1)*(cardWidth*NeighborScale+ItemGap);
        return new(x,(arrangedSize.Height-cardHeight*scale)/2,scale);
    }

    private void SetLayers(int outgoing, int incoming)
    {
        for (int i=0;i<Children.Count;i++) SetZIndex(Children[i],i==incoming ? 2 : i==outgoing ? 1 : 0);
    }

    private void RestoreWrap()
    {
        foreach(UIElement child in Children) child.Opacity=1;
        hiddenWrap=-1;
    }

    private void HideWrap(int outgoing,int incoming)
    {
        if(outgoing==incoming) return;
        for(int i=0;i<Children.Count;i++) if(i!=outgoing && i!=incoming) {
            hiddenWrap=i;Children[i].Opacity=0;
        }
    }

    private Pose ReadPose(int index)
    {
        var group=(TransformGroup)Children[index].RenderTransform;
        var scale=(ScaleTransform)group.Children[0];var move=(TranslateTransform)group.Children[1];
        return new(move.X,move.Y,scale.ScaleX);
    }

    private void Settle(int index, bool animate, bool notify = true)
    {
        if(index!=SelectedIndex) {RestoreWrap();HideWrap(SelectedIndex,index);}
        SetLayers(SelectedIndex,index);motionTarget=-1;
        SelectedIndex = index;UpdateNavigation();
        bool moving = animate && AnimateTransitions;
        int version=++motionVersion;IsMotionActive=moving;
        for (int i=0;i<Children.Count;i++) {
            var from=ReadPose(i);var to=LayoutPose(i,index);ApplyPose(i,to);
            if (!moving || i==hiddenWrap) continue;
            var group=(TransformGroup)Children[i].RenderTransform;
            var scale=(ScaleTransform)group.Children[0];var move=(TranslateTransform)group.Children[1];
            void Animate(Animatable owner, DependencyProperty property, double start, double end, bool last=false) {
                var animation=new DoubleAnimation(start,end,TimeSpan.FromMilliseconds(220)) {
                    EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut },FillBehavior=FillBehavior.Stop
                };
                if(last) animation.Completed+=(_,_)=> { if(version==motionVersion) {IsMotionActive=false;RestoreWrap();} };
                owner.BeginAnimation(property,animation);
            }
            Animate(move,TranslateTransform.XProperty,from.X,to.X);
            Animate(move,TranslateTransform.YProperty,from.Y,to.Y);
            Animate(scale,ScaleTransform.ScaleXProperty,from.Scale,to.Scale);
            Animate(scale,ScaleTransform.ScaleYProperty,from.Scale,to.Scale,i==index);
        }
        if(!moving) RestoreWrap();
        if (notify) NavigationCompleted?.Invoke();
    }

    private void ApplyPose(int index, Pose pose)
    {
        var group=(TransformGroup)Children[index].RenderTransform;
        var scale=(ScaleTransform)group.Children[0];var move=(TranslateTransform)group.Children[1];
        move.BeginAnimation(TranslateTransform.XProperty,null);move.BeginAnimation(TranslateTransform.YProperty,null);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        SetPose(index,pose);
    }

    private void SetPose(int index, Pose pose)
    {
        var group=(TransformGroup)Children[index].RenderTransform;
        var scale=(ScaleTransform)group.Children[0];var move=(TranslateTransform)group.Children[1];
        move.X=pose.X;move.Y=pose.Y;scale.ScaleX=scale.ScaleY=pose.Scale;
    }

    private void UpdateNavigation()
    {
        for (int i=0;i<Children.Count;i++) KeyboardNavigation.SetTabNavigation(Children[i],i==SelectedIndex ? KeyboardNavigationMode.Continue : KeyboardNavigationMode.None);
    }

    private int FindItem(DependencyObject? source)
    {
        for (int i=0;i<Children.Count;i++)
            if (ReferenceEquals(source,Children[i]) || source is Visual visual && Children[i].IsAncestorOf(visual)) return i;
        return -1;
    }

    private bool IsInput(DependencyObject? source)
    {
        for (DependencyObject? node=source;node!=null && !ReferenceEquals(node,this);node=node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is TextBoxBase or PasswordBox or ButtonBase or ScrollBar or Thumb) return true;
        return false;
    }
}
