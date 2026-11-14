using System.Globalization;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Media.Animation;

namespace Facet;

// Presentation and pointer geometry only; CalendarNotes owns the persisted data.
public sealed class OverlayScheduleDial : FrameworkElement
{
    private IReadOnlyList<DaySchedule> items = [];
    private int hover = -1, startSlot, lastSlot, unwrapped, low, high;
    private Point origin;
    private DaySchedule? pressedSchedule, hoveredSchedule, liftedSchedule;
    private enum DragPart {None,Create,Move,Start,End,Label}
    private DragPart dragPart;
    private DaySchedule? dragPreview;
    private long originalStart,originalEnd,dragDelta;
    private double dragRadius,lastDragAngle,dragTravel;
    private DateOnly viewDate=DateOnly.FromDateTime(DateTime.Now);
    internal DateOnly ViewDate {get=>viewDate;set{viewDate=value;RefreshCurrentTime();}}
    internal DaySchedule? TimeChangePreview=>dragPreview?.Copy();
    private int hoverTransition;
    private static readonly DependencyProperty HoverAmountProperty=DependencyProperty.Register(
        "HoverAmount",typeof(double),typeof(OverlayScheduleDial),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    private double HoverAmount {get=>(double)GetValue(HoverAmountProperty);set=>SetValue(HoverAmountProperty,value);}
    private IReadOnlyList<(DaySchedule Item,double Radius)>? scheduleLayout;
    private Size scheduleLayoutSize;
    private IReadOnlyList<ScheduleCallout>? callouts;
    private Rect legendBounds=Rect.Empty;
    private double calloutDpi;
    internal sealed record ScheduleCallout(DaySchedule Item,Rect Bounds,Point Anchor,double AnchorSlot,Geometry Leader,FormattedText NameText,bool IsTrimmed);
    private readonly ToolTip labelToolTip=new() {Placement=PlacementMode.Bottom,Focusable=false,IsHitTestVisible=false};
    private ScheduleCallout? tooltipLabel;

    private readonly DrawingVisual currentTimeBar=new();
    private readonly RotateTransform currentTimeRotation=new();
    private readonly DispatcherTimer clock=new(DispatcherPriority.Background) {Interval=TimeSpan.FromSeconds(1)};
    private Size timeBarSize;
    private double timeBarScale;
    private Func<DateTime> timeProvider=()=>DateTime.Now;
    internal Func<DateTime> TimeProvider {get=>timeProvider;set{timeProvider=value;RefreshCurrentTime();}}
    internal double CurrentTimeAngle=>currentTimeRotation.Angle;
    internal bool IsClockRunning=>clock.IsEnabled;
    private bool pressed, selecting;
    internal Point Center => new(ActualWidth * .5, ActualHeight * .49);
    internal double Radius => Math.Min(ActualWidth * .30, ActualHeight * .40);
    internal bool IsSelecting => selecting;
    internal (int Start, int Duration) Selection => (low * 30, (high - low + 1) * 30);
    internal event Action<int, int>? RangeSelected;
    internal event Action<DaySchedule>? EditRequested;
    internal event Action<DaySchedule>? TimeChangeRequested;
    internal event Action? InteractionStarted;

    public OverlayScheduleDial()
    {
        // Drawn labels share this element: WPF's owner-level hover state does
        // not reliably reopen a tooltip when moving between its hit regions.
        ToolTipService.SetIsEnabled(this,false);
        AddVisualChild(currentTimeBar);currentTimeBar.Transform=currentTimeRotation;
        clock.Tick+=(_,_)=>RefreshCurrentTime();
        Loaded+=(_,_)=>UpdateClockActivity();IsVisibleChanged+=(_,_)=>UpdateClockActivity();
        Focusable = false; KeyboardNavigation.SetIsTabStop(this,false); Cursor = Cursors.Arrow;
        AutomationProperties.SetName(this, "24시간 파이 일정 · 위 0시, 아래 12시 · 30분 단위 드래그");
        MouseEnter += (_, e) => MovePointer(e.GetPosition(this));
        MouseMove += (_, e) => MovePointer(e.GetPosition(this));
        MouseLeave += (_, _) => {SetLabelToolTip(null);SetHoveredSchedule(null);Cursor=Cursors.Arrow;hover=-1;InvalidateVisual();};
        MouseLeftButtonDown += (_, e) => {
            var point=e.GetPosition(this);Keyboard.ClearFocus();InteractionStarted?.Invoke();
            if(!BeginSelection(point))return;
            e.Handled=true;CaptureMouse();
        };
        MouseLeftButtonUp += (_, e) => {
            if (!pressed) return;
            e.Handled = true; EndSelection(e.GetPosition(this));
        };
        LostMouseCapture += (_, _) => CancelSelection();
        Unloaded += (_, _) => {clock.Stop();CancelSelection();ResetHover();};
        RefreshCurrentTime();

    }

    protected override int VisualChildrenCount=>1;
    protected override Visual GetVisualChild(int index)=>index==0?currentTimeBar:throw new ArgumentOutOfRangeException(nameof(index));

    private void UpdateClockActivity()
    {
        if(IsLoaded && IsVisible && Window.GetWindow(this)?.IsVisible==true){RefreshCurrentTime();clock.Start();}
        else {clock.Stop();if(!IsVisible)ResetHover();}
    }
    internal void RefreshCurrentTime()
    {
        var now=TimeProvider();currentTimeRotation.Angle=now.TimeOfDay.TotalSeconds/86400*360;
        // The now bar only belongs to today's dial.
        currentTimeBar.Opacity=DateOnly.FromDateTime(now)==viewDate?1:0;
        EnsureCurrentTimeBar();
    }
    private void EnsureCurrentTimeBar()
    {
        if(ActualWidth<=0 || ActualHeight<=0)return;
        double scale=ScreenScale();
        if(timeBarSize==RenderSize && Math.Abs(timeBarScale-scale)<.0001)return;
        timeBarSize=RenderSize;timeBarScale=scale;
        currentTimeRotation.CenterX=Center.X;currentTimeRotation.CenterY=Center.Y;
        using var dc=currentTimeBar.RenderOpen();var tip=PointAt(0,Radius+8);
        dc.DrawLine(new Pen(Resource("Bg"),9/scale) {StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},Center,tip);
        dc.DrawLine(new Pen(Resource("ScheduleNow"),4.5/scale) {StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},Center,tip);
    }

    internal void SetSchedules(IReadOnlyList<DaySchedule> schedules) { CancelSelection();ResetHover();items = schedules.Select(item=>item.Copy()).ToArray();scheduleLayout=null;callouts=null;InvalidateVisual(); }

    internal void SetLegendBounds(Rect bounds)
    {
        if(legendBounds==bounds)return;
        legendBounds=bounds;callouts=null;SetLabelToolTip(null);InvalidateVisual();
    }

    internal int SlotAt(Point point)
    {
        var delta = point - Center;
        if (delta.Length > Radius || delta.Length < 4) return -1;
        double angle = (Math.Atan2(delta.X,-delta.Y) + Math.PI * 2) % (Math.PI * 2);
        return Math.Min(47,(int)(angle / (Math.PI * 2) * 48));
    }

    internal Point PointAt(double slot, double radius) => new(Center.X + Math.Sin(slot / 48 * Math.PI * 2) * radius,
        Center.Y - Math.Cos(slot / 48 * Math.PI * 2) * radius);

    internal bool BeginSelection(Point point)
    {
        SetLabelToolTip(null);
        int slot = SlotAt(point);
        var label=LayoutCallouts().FirstOrDefault(item=>item.Bounds.Contains(point));
        var hit=label?.Item ?? HitSchedule(point);
        if(slot<0 && hit==null)return false;
        origin=point;pressedSchedule=hit?.Copy();dragPreview=null;dragDelta=0;dragTravel=0;lastDragAngle=DirectionSlot(point);
        dragPart=label!=null?DragPart.Label:hit==null?DragPart.Create:PartAt(hit,point);
        if(hit!=null) {
            originalStart=(long)hit.StartDayOffset*1440+hit.StartMinute;originalEnd=originalStart+hit.DurationMinutes;
            dragRadius=LayoutSchedules().Single(item=>item.Item.Id==hit.Id).Radius;
        }
        startSlot=lastSlot=low=high=unwrapped=Math.Max(0,slot);pressed=true;selecting=false;
        return true;
    }

    private DragPart PartAt(DaySchedule item,Point point)=>PartAt(item,LayoutSchedules().Single(entry=>entry.Item.Id==item.Id).Radius,point);
    private DragPart PartAt(DaySchedule item,double radius,Point point)
    {
        if(liftedSchedule?.Id==item.Id)point+=new Vector(0,2*HoverAmount/ScreenScale());
        double distance=(point-Center).Length;
        if(distance<radius*.22 || distance>radius)return DragPart.Move;
        long start=(long)item.StartDayOffset*1440+item.StartMinute,end=start+item.DurationMinutes;
        double ToEdge(long minute) {
            var vector=PointAt(minute/30d,radius)-Center;var sample=point-Center;
            double along=Vector.Multiply(sample,vector)/vector.LengthSquared;
            return (sample-vector*Math.Clamp(along,0,1)).Length;
        }
        double tolerance=Math.Min(6/ScreenScale(),distance*Math.Sin(Math.Min(item.VisibleDurationMinutes,720)*Math.PI/1440)*.45);
        bool atStart=start>=0 && start<1440 && ToEdge(start)<=tolerance;
        bool atEnd=end>0 && end<=1440 && ToEdge(end)<=tolerance;
        if(atStart && atEnd)return distance<radius*.65?DragPart.Start:DragPart.End;
        return atStart?DragPart.Start:atEnd?DragPart.End:DragPart.Move;
    }

    internal void MovePointer(Point point)
    {
        int pointerSlot=SlotAt(point);hover=pointerSlot;
        int slot=pressed && pressedSchedule!=null && dragPart!=DragPart.Label && (point-Center).Length>=4?Math.Min(47,(int)DirectionSlot(point)):pointerSlot;
        if(pressed && slot>=0 && dragPart!=DragPart.Label) {
            int delta=slot-lastSlot;if(delta>24)delta-=48;if(delta< -24)delta+=48;lastSlot=slot;
            if(dragPart==DragPart.Create) {
                unwrapped=Math.Clamp(unwrapped+delta,startSlot-47,startSlot+47);
                low=Math.Min(startSlot,unwrapped);high=Math.Max(startSlot,unwrapped);
            } else {
                double angle=DirectionSlot(point),turn=angle-lastDragAngle;if(turn>24)turn-=48;if(turn< -24)turn+=48;
                dragTravel+=turn;lastDragAngle=angle;dragDelta=(long)Math.Round(dragTravel,MidpointRounding.AwayFromZero);
            }
            if((point-origin).Length>=8)selecting=true;
            if(selecting && pressedSchedule!=null)UpdateTimeChange();
        }
        SetHoveredSchedule(selecting?null:HitItem(point));
        SetLabelToolTip(pressed?null:LayoutCallouts().FirstOrDefault(label=>label.IsTrimmed && label.Bounds.Contains(point)));
        if(selecting && pressedSchedule!=null)Cursor=dragPart==DragPart.Move?Cursors.SizeAll:Cursors.SizeWE;
        else if(hoveredSchedule!=null && !LayoutCallouts().Any(label=>label.Bounds.Contains(point)) && PartAt(hoveredSchedule,point) is DragPart.Start or DragPart.End)Cursor=Cursors.SizeWE;
        else Cursor=hoveredSchedule!=null?Cursors.Hand:slot>=0?Cursors.Cross:Cursors.Arrow;
        InvalidateVisual();
    }

    private void UpdateTimeChange()
    {
        if(pressedSchedule==null)return;
        long start=originalStart,end=originalEnd,delta=dragDelta*30;
        long minimum=-(long)ViewDate.DayNumber*1440,maximum=((long)DateOnly.MaxValue.DayNumber+1-ViewDate.DayNumber)*1440;
        if(dragPart==DragPart.Move){start=Math.Clamp(start+delta,minimum,maximum-(end-start));end=start+pressedSchedule.DurationMinutes;}
        else if(dragPart==DragPart.Start)start=Math.Clamp(start+delta,minimum,end-30);
        else if(dragPart==DragPart.End)end=Math.Clamp(end+delta,start+30,maximum);
        var value=pressedSchedule.Copy();long day=start/1440;if(start<0 && start%1440!=0)day--;
        value.StartDayOffset=(int)day;value.StartMinute=(int)(start-day*1440);value.DurationMinutes=end-start;dragPreview=value;
    }

    internal void EndSelection(Point point)
    {
        if(!pressed)return;
        MovePointer(point);
        bool commit=selecting,editing=pressedSchedule!=null;var range=Selection;
        var changed=dragPreview?.Copy();var original=pressedSchedule;
        var released=HitItem(point);
        var hit=original!=null && released?.Id==original.Id && (point-origin).Length<8?original:null;
        CancelSelection();
        if(commit && editing) {
            if(changed!=null && original!=null && (changed.StartMinute!=original.StartMinute || changed.StartDayOffset!=original.StartDayOffset || changed.DurationMinutes!=original.DurationMinutes))TimeChangeRequested?.Invoke(changed);
        }
        else if(commit)RangeSelected?.Invoke(range.Start,range.Duration);
        else if(hit!=null)EditRequested?.Invoke(hit.Copy());
    }

    internal void CancelSelection()
    {
        pressed=selecting=false;pressedSchedule=dragPreview=null;dragPart=DragPart.None;
        if(IsMouseCaptured)ReleaseMouseCapture();InvalidateVisual();
    }

    private void ResetHover()
    {
        SetLabelToolTip(null);
        hoverTransition++;BeginAnimation(HoverAmountProperty,null);HoverAmount=0;hoveredSchedule=liftedSchedule=null;hover=-1;
    }
    private void SetLabelToolTip(ScheduleCallout? label)
    {
        if(ReferenceEquals(tooltipLabel,label))return;
        labelToolTip.IsOpen=false;ToolTip=null;tooltipLabel=label;
        if(label==null)return;
        labelToolTip.Content=label.NameText.Text;
        labelToolTip.PlacementTarget=this;labelToolTip.PlacementRectangle=label.Bounds;
        ToolTip=labelToolTip;
        if(IsLoaded && IsVisible && IsMouseOver)labelToolTip.IsOpen=true;
    }
    private void SetHoveredSchedule(DaySchedule? item)
    {
        if(hoveredSchedule?.Id==item?.Id)return;
        double from=liftedSchedule?.Id==item?.Id || item==null?HoverAmount:0;
        hoveredSchedule=item;if(item!=null)liftedSchedule=item;
        int version=++hoverTransition;double target=item==null?0:1;
        BeginAnimation(HoverAmountProperty,null);HoverAmount=from;
        var animation=new DoubleAnimation(from,target,TimeSpan.FromMilliseconds(140)) {
            EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop
        };
        animation.Completed+=(_,_)=>{if(version!=hoverTransition)return;HoverAmount=target;if(target==0)liftedSchedule=null;};
        BeginAnimation(HoverAmountProperty,animation);
    }

    internal IReadOnlyList<(DaySchedule Item, double Radius)> LayoutSchedules()
    {
        if(scheduleLayout!=null && scheduleLayoutSize==RenderSize)return scheduleLayout;
        scheduleLayoutSize=RenderSize;callouts=null;
        var result = new List<(DaySchedule Item, double Radius)>();
        foreach (var item in items.Where(s=>s.VisibleDurationMinutes>0).OrderByDescending(s => s.VisibleDurationMinutes).ThenBy(s => s.Id)) {
            bool Overlaps(DaySchedule other) => Enumerable.Range(0,48).Any(s => item.ContainsSlot(s) && other.ContainsSlot(s));
            double radius = Radius*.9;
            foreach (var other in result.Where(s => Overlaps(s.Item))) radius = Math.Min(radius,other.Radius - Radius * .12);
            result.Add((item,Math.Max(Radius * .22,radius)));
        }
        return scheduleLayout=result;
    }

    // Layout is rebuilt only for new data, size or DPI; pointer rendering reuses it.
    internal IReadOnlyList<ScheduleCallout> LayoutCallouts()
    {
        var sectors=LayoutSchedules();double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if(callouts!=null && calloutDpi==dpi)return callouts;
        calloutDpi=dpi;var result=new List<ScheduleCallout>();
        if(Radius<=0)return callouts=result;
        var overflow=new Rect(12,12,110,24);
        double widthLimit=Math.Clamp(Center.X-Radius-54,80,190), clearance=Radius+44;
        foreach(var entry in sectors.OrderBy(item=>item.Item.VisibleDurationMinutes).ThenBy(item=>item.Item.Id)) {
            var title=entry.Item.Name.Replace('\r',' ').Replace('\n',' ');
            if(string.IsNullOrWhiteSpace(entry.Item.Name)) {
                var content=entry.Item.Content.AsSpan().TrimStart();int lineEnd=content.IndexOfAny('\r','\n');
                title=(lineEnd<0?content:content[..lineEnd]).TrimEnd().ToString();
            }
            var name=new FormattedText(title,CultureInfo.GetCultureInfo("ko-KR"),
                FlowDirection.LeftToRight,Design.Typography.Regular,13,Resource("Ink"),dpi);
            double fullWidth=name.WidthIncludingTrailingWhitespace;
            name.MaxTextWidth=widthLimit-20;name.MaxLineCount=1;name.Trimming=TextTrimming.CharacterEllipsis;
            double width=Math.Min(widthLimit,Math.Max(100,Math.Ceiling(name.WidthIncludingTrailingWhitespace)+20));
            name.MaxTextWidth=width-20;double height=Math.Ceiling(name.Height)+14;
            Rect best=Rect.Empty;double bestScore=double.PositiveInfinity,bestSlot=0;
            for(double y=12;y+height<=ActualHeight-12;y+=8) {
                double dy=Math.Max(0,Math.Max(y-Center.Y,Center.Y-y-height));
                double reach=Math.Sqrt(Math.Max(0,clearance*clearance-dy*dy));
                foreach(int side in new[]{-1,1})for(int column=0;column<2;column++) {
                    double x=side<0?Center.X-reach-8-width:Center.X+reach+8;
                    x+=side*column*(width+8);var box=new Rect(x,y,width,height);
                    if(box.Left<12 || box.Right>ActualWidth-12 || box.IntersectsWith(legendBounds) || box.IntersectsWith(overflow))continue;
                    var padded=box;padded.Inflate(5,5);
                    if(result.Any(label=>padded.IntersectsWith(label.Bounds)))continue;
                    var center=new Point(box.X+width/2,box.Y+height/2);double target=DirectionSlot(center);
                    double slot=NearestSectorSlot(entry.Item,target);
                    var anchor=PointAt(slot,Math.Max(4,entry.Radius-5));
                    double score=(anchor-center).Length+SlotDistance(slot,target)*(Radius+18)*Math.PI/24*.2;
                    if(score<bestScore) {bestScore=score;best=box;bestSlot=slot;}
                }
            }
            if(best.IsEmpty)continue;
            var start=PointAt(bestSlot,Math.Max(4,entry.Radius-5));
            var boxCenter=new Point(best.X+best.Width/2,best.Y+best.Height/2);
            double routeSlot=DirectionSlot(boxCenter), turn=(routeSlot-bestSlot)%48;
            if(turn>24)turn-=48;if(turn< -24)turn+=48;
            var exit=PointAt(bestSlot,Radius+18);var route=PointAt(routeSlot,Radius+18);
            var end=new Point(Math.Clamp(route.X,best.Left,best.Right),Math.Clamp(route.Y,best.Top,best.Bottom));
            var leader=new StreamGeometry();using(var path=leader.Open()) {
                path.BeginFigure(start,false,false);path.LineTo(exit,true,false);
                if(Math.Abs(turn)>.001)path.ArcTo(route,new(Radius+18,Radius+18),0,false,turn>0?SweepDirection.Clockwise:SweepDirection.Counterclockwise,true,false);
                path.LineTo(end,true,false);
            }
            leader.Freeze();result.Add(new(entry.Item,best,start,bestSlot,leader,name,fullWidth>name.MaxTextWidth));
        }
        return callouts=result;
    }

    private double DirectionSlot(Point point) => (Math.Atan2(point.X-Center.X,Center.Y-point.Y)+Math.PI*2)%(Math.PI*2)*24/Math.PI;
    private static double SlotDistance(double first,double second) {double distance=Math.Abs(first-second)%48;return Math.Min(distance,48-distance);}
    private static double NearestSectorSlot(DaySchedule item,double target)
    {
        double start=item.VisibleStartMinute/30d,count=item.VisibleDurationMinutes/30d,delta=(target-start+48)%48;
        if(delta<=count)return start+Math.Clamp(delta,.08,count-.08);
        double first=start+.08,last=start+count-.08;
        return SlotDistance(target,first)<=SlotDistance(target,last)?first:last;
    }

    private DaySchedule? HitItem(Point point) => LayoutCallouts().FirstOrDefault(label=>label.Bounds.Contains(point))?.Item ?? HitSchedule(point);

    private DaySchedule? HitSchedule(Point point)
    {
        foreach(var entry in LayoutSchedules().Reverse()) {
            bool Contains(Point sample){int slot=SlotAt(sample);return slot>=0 && entry.Item.ContainsSlot(slot) && (sample-Center).Length<=entry.Radius;}
            if(Contains(point) || PartAt(entry.Item,entry.Radius,point) is DragPart.Start or DragPart.End)return entry.Item;
            if(!selecting && liftedSchedule?.Id==entry.Item.Id && HoverAmount>0 && Contains(point+new Vector(0,2*HoverAmount/ScreenScale())))return entry.Item;
        }
        return null;
    }

    internal Geometry Sector(double start,double count,double radius,bool rounded=false)
    {
        if(count>=48){var circle=new EllipseGeometry(Center,radius,radius);circle.Freeze();return circle;}
        double corner=rounded?Math.Min(6/ScreenScale(),radius*Math.Sin(count*Math.PI/48)/(1+Math.Sin(count*Math.PI/48))*.8):0;
        double inset=corner>0?Math.Sqrt(radius*radius-2*radius*corner):radius;
        double turn=corner>0?Math.Asin(corner/(radius-corner))*24/Math.PI:0;
        var geometry=new StreamGeometry();using(var path=geometry.Open()) {
            path.BeginFigure(Center,true,true);path.LineTo(PointAt(start,inset),true,false);
            if(corner>0)path.ArcTo(PointAt(start+turn,radius),new(corner,corner),0,false,SweepDirection.Clockwise,true,false);
            double arc=count-2*turn,half=arc/2;
            path.ArcTo(PointAt(start+turn+half,radius),new(radius,radius),0,half>24,SweepDirection.Clockwise,true,false);
            path.ArcTo(PointAt(start+count-turn,radius),new(radius,radius),0,half>24,SweepDirection.Clockwise,true,false);
            if(corner>0)path.ArcTo(PointAt(start+count,inset),new(corner,corner),0,false,SweepDirection.Clockwise,true,false);
        }
        geometry.Freeze();return geometry;
    }

    private void DrawTimeChange(DrawingContext dc)
    {
        if(dragPreview==null)return;
        long start=(long)dragPreview.StartDayOffset*1440+dragPreview.StartMinute,end=start+dragPreview.DurationMinutes;
        var color=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dragPreview.Color));
        void Piece(int minute,int duration,bool adjacent) {
            dc.PushOpacity(adjacent?.35:1);dc.DrawGeometry(color,new Pen(color,1.5) {LineJoin=PenLineJoin.Round},Sector(minute/30d,duration/30d,dragRadius,true));dc.Pop();
        }
        void Outside(long first,long last) {
            long length=last-first;if(length<=0)return;
            if(length>=1440){Piece(0,1440,true);return;}
            int local=(int)((first%1440+1440)%1440),count=(int)length,firstCount=Math.Min(count,1440-local);
            Piece(local,firstCount,true);if(count>firstCount)Piece(0,count-firstCount,true);
        }
        Outside(start,Math.Min(end,0));Outside(Math.Max(start,1440),end);
        long visibleStart=Math.Max(0,start),visibleEnd=Math.Min(1440,end);
        if(visibleEnd>visibleStart)Piece((int)visibleStart,(int)(visibleEnd-visibleStart),false);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var edge = Resource("Edge"); var muted = Resource("Muted");
        dc.DrawRectangle(Brushes.Transparent,null,new Rect(RenderSize));
        dc.DrawEllipse(Resource("Input"),new(edge,1),Center,Radius,Radius);
        for (int slot = 0; slot < 48; slot++) {
            dc.DrawLine(new Pen(edge,slot%2==0?1.3:.7),PointAt(slot,Radius+3),PointAt(slot,Radius+(slot%2==0?13:8)));
            if (slot%4==0) Text(dc,$"{slot/2:00}",PointAt(slot,Radius+29),muted,13,center:true);
        }
        double scale=ScreenScale();
        if(!selecting && hover>=0 && hoveredSchedule==null) {
            var guideBrush=Resource("Ink").Clone();guideBrush.Opacity=.28;
            dc.DrawGeometry(null,new Pen(guideBrush,1.5/scale) {LineJoin=PenLineJoin.Round},Sector(hover,1,Radius));
        }
        var layout = LayoutSchedules();
        foreach (var entry in layout) {
            if(selecting && dragPreview?.Id==entry.Item.Id)continue;
            var color = (Color)ColorConverter.ConvertFromString(entry.Item.Color);
            var shape=Sector(entry.Item.VisibleStartMinute/30d,entry.Item.VisibleDurationMinutes/30d,entry.Radius,true);
            bool active=!selecting && hoveredSchedule?.Id==entry.Item.Id;
            double amount=!selecting && liftedSchedule?.Id==entry.Item.Id?HoverAmount:0;
            if(amount>0) {
                dc.PushTransform(new TranslateTransform(0,2*amount/scale));
                var shadow=new SolidColorBrush(Colors.Black) {Opacity=.20*amount};
                dc.DrawGeometry(shadow,new Pen(new SolidColorBrush(Colors.Black) {Opacity=.035*amount},8/scale) {LineJoin=PenLineJoin.Round},shape);
                dc.DrawGeometry(null,new Pen(new SolidColorBrush(Colors.Black) {Opacity=.05*amount},4/scale) {LineJoin=PenLineJoin.Round},shape);dc.Pop();
            }
            dc.PushTransform(new TranslateTransform(0,-2*amount/scale));
            dc.DrawGeometry(new SolidColorBrush(color),new Pen(new SolidColorBrush(active?HoverColor(color):color),active?1.75/scale:1.5) {LineJoin=PenLineJoin.Round},shape);
            dc.Pop();
        }
        if(selecting && dragPreview!=null)DrawTimeChange(dc);
        if (selecting && dragPreview==null) foreach(var part in DaySchedule.SplitRange(Selection.Start,Selection.Duration)) {
            dc.PushOpacity(part.DayOffset==0?1:.35);
            dc.DrawGeometry(Resource("Mint"),new Pen(Resource("Mint"),2) {LineJoin=PenLineJoin.Round},Sector(part.StartMinute/30d,part.DurationMinutes/30d,Radius,true));dc.Pop();
        }

        var labels=LayoutCallouts();
        foreach(var label in labels) {
            var color=new SolidColorBrush((Color)ColorConverter.ConvertFromString(label.Item.Color));
            dc.DrawGeometry(null,new Pen(color,2.4) {StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round},label.Leader);
            dc.DrawEllipse(color,null,label.Anchor,3,3);
        }
        // Opaque badges are drawn last, so no leader can obscure another name.
        foreach(var label in labels) {
            var color=new SolidColorBrush((Color)ColorConverter.ConvertFromString(label.Item.Color));
            var background=Resource("Panel");
            Brush outline=!selecting && hoveredSchedule?.Id==label.Item.Id?new SolidColorBrush(HoverColor(color.Color)):color;
            dc.DrawRoundedRectangle(background,new Pen(outline,2),label.Bounds,8,8);
            dc.DrawText(label.NameText,new Point(label.Bounds.Left+10,label.Bounds.Top+(label.Bounds.Height-label.NameText.Height)/2));
        }
        if (items.Count == 0 && !selecting) {
            Text(dc,"원 안을 드래그하여 일정 추가",Center,muted,13,center:true);
        }
        if (items.Count>labels.Count)Text(dc,$"외 {items.Count-labels.Count}개",new Point(12,14),muted,12,maxWidth:110);
        if(selecting && dragPreview==null) Text(dc,$"{RangeTime(Selection.Start)} – {RangeTime(Selection.Start+Selection.Duration)}  ·  {Selection.Duration/60d:0.#}시간",
            new Point(Center.X,ActualHeight-30),muted,13,center:true);
        // Child visual is composited above all parent drawing, including labels and drag previews.
        EnsureCurrentTimeBar();
    }

    private static string RangeTime(int minute)
    {
        int day=(int)Math.Floor(minute/1440d),local=(minute%1440+1440)%1440;
        return (day<0?"전날 ":day>0?"다음 날 ":"")+Time(local);
    }

    private static Color HoverColor(Color color)=>Color.FromArgb(color.A,
        (byte)(color.R+(255-color.R)*.28),(byte)(color.G+(255-color.G)*.28),(byte)(color.B+(255-color.B)*.28));

    private double ScreenScale()
    {
        Visual root=this;while(VisualTreeHelper.GetParent(root) is Visual parent)root=parent;
        if(ReferenceEquals(root,this))return 1;
        var transform=TransformToAncestor(root);
        return Math.Max(.1,(transform.Transform(new Point(1,0))-transform.Transform(new Point())).Length);
    }

    internal static string Time(int minute) => minute == 1440 ? "24:00" : $"{minute%1440/60:00}:{minute%60:00}" + (minute>1440?" (+1일)":"");
    private Brush Resource(string key) => (Brush)FindResource(key);
    private void Text(DrawingContext dc,string text,Point at,Brush brush,double size,bool center=false,double maxWidth=double.PositiveInfinity)
    {
        var formatted = new FormattedText(text,CultureInfo.GetCultureInfo("ko-KR"),FlowDirection.LeftToRight,Design.Typography.Regular,size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (!double.IsInfinity(maxWidth)) { formatted.MaxTextWidth=maxWidth; formatted.MaxLineCount=1; formatted.Trimming=TextTrimming.CharacterEllipsis; }
        dc.DrawText(formatted,new Point(at.X-(center?formatted.Width/2:0),at.Y-(center?formatted.Height/2:0)));
    }
}
