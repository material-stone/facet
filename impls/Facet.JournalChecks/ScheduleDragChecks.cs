using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Facet;
using Facet.Core;

internal static partial class Program
{
    private static void CheckScheduleDragging(Controller controller,string qa)
    {
        var date=new DateOnly(2030,3,1);var dial=new OverlayScheduleDial {Width=800,Height=560,ViewDate=date,TimeProvider=()=>new(2026,10,24,18,0,0)};
        dial.Measure(new Size(800,560));dial.Arrange(new Rect(0,0,800,560));dial.UpdateLayout();
        DaySchedule? changed=null;int writes=0,modals=0;
        dial.TimeChangeRequested+=item=>{changed=item;writes++;};dial.EditRequested+=_=>modals++;
        void Set(int minute,long duration,int offset=0){changed=null;writes=modals=0;dial.SetSchedules([new(){Name="드래그 일정",StartMinute=minute,DurationMinutes=duration,StartDayOffset=offset,OwnerDate=date.AddDays(offset)}]);}
        void Begin(double slot,double radius=.6){Assert(dial.BeginSelection(dial.PointAt(slot,dial.Radius*radius)),"schedule drag accepts its handle or body at "+slot);}
        void Move(params double[] slots){foreach(var slot in slots)dial.MovePointer(dial.PointAt(slot,dial.Radius*.6));}
        void End(double slot)=>dial.EndSelection(dial.PointAt(slot,dial.Radius*.6));
        void Preview(double opaque,double adjacent,string name){
            dial.UpdateLayout();var bitmap=new RenderTargetBitmap(800,560,96,96,PixelFormats.Pbgra32);bitmap.Render(dial);
            Color Pixel(double slot){var at=dial.PointAt(slot,dial.Radius*.55);var bytes=new byte[4];bitmap.CopyPixels(new Int32Rect((int)at.X,(int)at.Y,1,1),bytes,4,0);return Color.FromArgb(bytes[3],bytes[2],bytes[1],bytes[0]);}
            Assert(Pixel(opaque).G>Pixel(adjacent).G,"schedule manipulation keeps the viewed date opaque and adjacent date translucent: "+name);
            SavePlannerImage(dial,new Size(800,560),Path.Combine(qa,"schedule-drag-"+name+".png"));
        }
        Set(1320,120);Begin(46.2);Move(47.2,.2);var value=dial.TimeChangePreview!;
        Assert(value.StartDayOffset==0 && value.StartMinute==1380 && value.DurationMinutes==120,"clockwise body movement preserves duration across midnight");Preview(47.5,.5,"move-next");End(.2);
        Assert(writes==1 && changed?.StartMinute==1380 && modals==0 && dial.TimeChangePreview==null,"body drop emits one time change without opening the edit modal");
        Set(30,120);Begin(3.2);Move(2.2,1.2);value=dial.TimeChangePreview!;
        Assert(value.StartDayOffset== -1 && value.StartMinute==1410 && value.DurationMinutes==120,"counterclockwise body movement reaches the previous date without changing duration");Preview(.5,47.5,"move-previous");End(1.2);
        Set(30,90);Begin(1);Move(0,47);value=dial.TimeChangePreview!;
        Assert(value.StartDayOffset== -1 && value.StartMinute==1410 && value.DurationMinutes==150,"start edge can extend counterclockwise through midnight while its end stays fixed");End(47);
        Set(1260,150);Begin(47);Move(0,1);value=dial.TimeChangePreview!;
        Assert(value.StartMinute==1260 && value.DurationMinutes==210,"end edge can extend clockwise through midnight while its start stays fixed");Preview(45.5,.5,"resize-next");End(1);
        Set(1380,120,-1);Begin(2);Move(1,0,47);value=dial.TimeChangePreview!;
        Assert(value.StartDayOffset== -1 && value.StartMinute==1380 && value.DurationMinutes==30,"incoming end edge can move back to the previous day");End(47);
        Set(600,60);Begin(20);Move(21,22,23);Assert(dial.TimeChangePreview!.DurationMinutes==30,"resize clamps to a positive half hour instead of inverting start and end");dial.CancelSelection();
        Set(0,1440);Begin(0,.4);Move(47);Assert(dial.TimeChangePreview!.StartDayOffset== -1 && dial.TimeChangePreview.DurationMinutes==1470,"inner coincident midnight handle edits a full day's start");dial.CancelSelection();
        Set(0,1440);Begin(0,.85);Move(1);Assert(dial.TimeChangePreview!.StartMinute==0 && dial.TimeChangePreview.DurationMinutes==1470,"outer coincident midnight handle edits a full day's end");dial.CancelSelection();
        Set(1380,240,-1);Begin(.1);Move(1.1);Assert(dial.TimeChangePreview!.DurationMinutes==240,"clipped midnight boundary moves the whole event rather than resizing an artificial edge");dial.CancelSelection();
        Set(600,120);Begin(22.2);Move(23.2);dial.CancelSelection();End(23.2);Assert(writes==0,"capture or Escape cancellation discards the schedule time draft");
        dial.ViewDate=DateOnly.MinValue;Set(0,60);Begin(1);Move(0,47);Assert(dial.TimeChangePreview!.StartDayOffset==0,"moving at the first supported date clamps without underflow");dial.CancelSelection();dial.ViewDate=date;
        dial.ViewDate=DateOnly.MaxValue;Set(1380,60);Begin(47);Move(0,1);
        Assert(dial.TimeChangePreview!.StartMinute==1380 && dial.TimeChangePreview.DurationMinutes==60,"body movement clamps at the last supported date without shortening the event");dial.CancelSelection();
        Set(1380,60);Begin(0);Move(1);Assert(dial.TimeChangePreview!.DurationMinutes==60,"end resize cannot overflow the final supported midnight");dial.CancelSelection();dial.ViewDate=date;
        Set(600,120);Begin(22.2);dial.MovePointer(dial.PointAt(23.2,dial.Radius*1.2));
        Assert(dial.TimeChangePreview!.StartMinute==630,"captured schedule drag follows the pointer angle outside the circle");dial.CancelSelection();
        Set(600,120);Begin(22.2);for(int i=1;i<=96;i++)dial.MovePointer(dial.PointAt((22.2+i)%48,dial.Radius*.6));
        Assert(dial.TimeChangePreview!.StartDayOffset==2 && dial.TimeChangePreview.StartMinute==600 && dial.TimeChangePreview.DurationMinutes==120,"multiple clockwise turns retain the full signed day movement and duration");dial.CancelSelection();
        Geometry plain=dial.Sector(20,4,dial.Radius),rounded=dial.Sector(20,4,dial.Radius,true);
        var corner=dial.PointAt(20.01,dial.Radius-.5);var inner=dial.PointAt(20.1,dial.Radius-12);
        Assert(plain.FillContains(corner) && !rounded.FillContains(corner) && rounded.FillContains(inner),"outer arc corners are rounded while the radial interior stays filled");
        Assert(dial.Sector(20,1,20,true).GetArea()>0 && dial.Sector(0,48,100,true) is EllipseGeometry,"narrow sectors stay valid and full days remain seamless circles");
        Set(600,30);var compact=new Viewbox {Width=400,Height=280,Child=dial};compact.Measure(new Size(400,280));compact.Arrange(new Rect(0,0,400,280));compact.UpdateLayout();
        Begin(20.5);Move(21.5);Assert(dial.TimeChangePreview!.StartMinute==630 && dial.TimeChangePreview.DurationMinutes==30,"compact half-hour sectors keep a body area distinct from resize handles");dial.CancelSelection();compact.Child=null;
        CheckScheduleDragPersistence(controller,qa,date);
    }

    private static void CheckNativeScheduleDragging(Controller controller)
    {
        using var host=new KeyboardHost();host.Window.Show();Pump(100);var original=System.Windows.Forms.Cursor.Position;
        var date=DateOnly.FromDateTime(DateTime.Now);var item=new DaySchedule {Name="실제 드래그 일정",StartMinute=1320,DurationMinutes=120};
        controller.Commit(data=>{data.Settings.OverlayShowMilliseconds=0;data.Settings.OverlayHideMilliseconds=0;CalendarNotes.Save(data,new(){Date=date,Schedules=[item]},DateTime.Now);});
        var overlay=controller.Overlay;var hud=(OverlayTabs)((Grid)overlay.Content).Children[2];hud.Connect(controller);hud.AnimateDayTransitions=false;
        ((TabControl)hud.FindName("Tabs")).SelectedItem=hud.FindName("ManagementTab");
        var dial=(OverlayScheduleDial)hud.FindName("ScheduleDial");var editor=(OverlayDayEditor)hud.FindName("DayEditor");var modal=(Grid)hud.FindName("ScheduleModal");
        var viewport=(Viewbox)hud.FindName("DayViewport");double originalWidth=viewport.Width;
        void Pointer(double slot){
            bool settled=false;
            for(int attempt=0;attempt<5;attempt++) {
                overlay.UpdateLayout();var point=dial.PointToScreen(dial.PointAt(slot,dial.Radius*.6));
                System.Windows.Forms.Cursor.Position=new((int)System.Math.Round(point.X),(int)System.Math.Round(point.Y));Pump(80);System.Windows.Input.Mouse.Synchronize();
                var cursor=System.Windows.Forms.Cursor.Position;var local=dial.PointFromScreen(new Point(cursor.X,cursor.Y));
                double angle=(System.Math.Atan2(local.X-dial.Center.X,dial.Center.Y-local.Y)+System.Math.PI*2)%(System.Math.PI*2)*24/System.Math.PI;
                double error=System.Math.Abs(angle-slot)%48;error=System.Math.Min(error,48-error);
                if(error<.15 && (System.Windows.Input.Mouse.GetPosition(dial)-local).Length<2){settled=true;break;}
            }
            Assert(settled,"native pointer settles on the requested dial angle at "+slot);
        }
        void Drag(double first,params double[] rest){Pointer(first);mouse_event(0x0002,0,0,0,0);Pump(35);Assert(dial.IsMouseCaptured,"native schedule drag captures the pointer");foreach(var slot in rest)Pointer(slot);mouse_event(0x0004,0,0,0,0);Pump(120);}
        DateOnly savedOwner=default;
        DaySchedule Read(){var data=new Store(controller.Store.Root).Load();var owner=data.CalendarDays.Single(day=>day.Schedules.Any(schedule=>schedule.Id==item.Id));savedOwner=owner.Date;return owner.Schedules.Single(schedule=>schedule.Id==item.Id);}
        try {
            controller.ToggleOverlay();AwaitBackdrop(()=>overlay.IsVisible);Pump(200);hud.OpenDay(date);overlay.UpdateLayout();Pump(100);
            editor.BeginNoteEdit();var note=(TextBox)typeof(OverlayDayEditor).GetField("note",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!;note.Text="드래그 전 기록";
            Drag(46.2,47.2,.2);var saved=Read();
            Assert(savedOwner==date && saved.StartMinute==1380 && saved.DurationMinutes==120 && modal.Visibility==Visibility.Collapsed && CalendarNotes.Get(controller.Data,date).Note==note.Text,"native body drag crosses midnight and saves duration while record blur stays safe");
            hud.OpenDay(date.AddDays(1));overlay.UpdateLayout();Drag(2,3,4);saved=Read();
            Assert(savedOwner==date && saved.StartMinute==1380 && saved.DurationMinutes==180,"native continuation end-edge drag updates the original event");
            hud.OpenDay(date);overlay.UpdateLayout();Drag(46,45,44);saved=Read();
            Assert(savedOwner==date && saved.StartMinute==1320 && saved.DurationMinutes==240,"native start-edge drag preserves the end across the date boundary");
            string before=JsonSerializer.Serialize(controller.Data);Pointer(46.2);mouse_event(0x0002,0,0,0,0);Pointer(47.2);Assert(dial.IsSelecting,"native schedule movement reaches its drag preview");
            Chord(0x1B);mouse_event(0x0004,0,0,0,0);Pump(100);
            Assert(hud.IsEditingDay && !dial.IsSelecting && JsonSerializer.Serialize(controller.Data)==before,"native Escape cancels a captured time change without leaving the day or saving (day="+hud.IsEditingDay+", selecting="+dial.IsSelecting+", same="+(JsonSerializer.Serialize(controller.Data)==before)+")");
            var mini=new DaySchedule {Name="작은30분 일정",StartMinute=600,DurationMinutes=30};
            controller.Commit(data=>{var entry=CalendarNotes.Get(data,date);entry.Schedules.Add(mini);CalendarNotes.Save(data,entry,DateTime.Now);});
            hud.Connect(controller);hud.OpenDay(date);viewport.Width=780;overlay.UpdateLayout();Pump(60);Drag(20.5,21.5);
            var compact=CalendarNotes.Get(new Store(controller.Store.Root).Load(),date).Schedules.Single(schedule=>schedule.Id==mini.Id);
            Assert(compact.StartMinute==630 && compact.DurationMinutes==30,"native compact half-hour body drag moves instead of resizing the narrow sector");
        } finally {viewport.Width=originalWidth;mouse_event(0x0004,0,0,0,0);controller.HideOverlay();Pump(100);System.Windows.Forms.Cursor.Position=original;}
    }

    private static void CheckScheduleDragPersistence(Controller controller,string qa,DateOnly date)
    {
        var next=date.AddDays(1);var item=new DaySchedule {Name="시간 직접 편집",StartMinute=1320,DurationMinutes=120};
        controller.Commit(data=>{CalendarNotes.Save(data,new(){Date=date,Note="시작 날짜 기록",Schedules=[item]},DateTime.Now);CalendarNotes.Save(data,new(){Date=next,Note="다음 날짜 기록"},DateTime.Now);});
        var hud=(OverlayTabs)((Grid)controller.Overlay.Content).Children[2];hud.Connect(controller);hud.AnimateDayTransitions=false;hud.OpenDay(date);
        hud.Measure(new Size(1440,900));hud.Arrange(new Rect(0,0,1440,900));hud.UpdateLayout();
        var dial=(OverlayScheduleDial)hud.FindName("ScheduleDial");var editor=(OverlayDayEditor)hud.FindName("DayEditor");var modal=(Grid)hud.FindName("ScheduleModal");var form=(OverlayScheduleEditor)hud.FindName("ScheduleEditor");
        void Drag(double first,params double[] rest){dial.BeginSelection(dial.PointAt(first,dial.Radius*.6));foreach(double slot in rest)dial.MovePointer(dial.PointAt(slot,dial.Radius*.6));dial.EndSelection(dial.PointAt(rest[^1],dial.Radius*.6));hud.UpdateLayout();}
        Drag(46.2,47.2,.2);var loaded=new Store(controller.Store.Root).Load();var stored=CalendarNotes.Get(loaded,date).Schedules.Single();
        Assert(stored.Id==item.Id && stored.StartMinute==1380 && stored.DurationMinutes==120 && modal.Visibility==Visibility.Collapsed,"body drop immediately persists the same event through the encrypted writer");
        Assert(editor.Schedules.Single().VisibleDurationMinutes==60 && CalendarNotes.SchedulesForDate(loaded,next).Single().VisibleDurationMinutes==60,"saved manipulation clips each date instead of leaving a wrapped ghost");
        Drag(46,45,44);stored=CalendarNotes.Get(new Store(controller.Store.Root).Load(),date).Schedules.Single();
        Assert(stored.StartMinute==1320 && stored.DurationMinutes==180,"start edge drop preserves the original end timestamp");
        hud.OpenDay(next);Drag(2,3,4);stored=CalendarNotes.Get(new Store(controller.Store.Root).Load(),date).Schedules.Single();
        Assert(stored.StartMinute==1320 && stored.DurationMinutes==240 && editor.Schedules.Single().VisibleDurationMinutes==120,"continuation end edge edits the original owner's event");
        string before=JsonSerializer.Serialize(controller.Data);dial.BeginSelection(dial.PointAt(2.2,dial.Radius*.6));dial.MovePointer(dial.PointAt(3.2,dial.Radius*.6));
        Assert(hud.HandleEscape() && hud.IsEditingDay && !dial.IsSelecting && JsonSerializer.Serialize(controller.Data)==before,"Escape pops an in-progress schedule manipulation without saving or leaving the day");
        editor.Configure(day=>CalendarNotes.Get(controller.Data,day),_=>false,day=>CalendarNotes.SchedulesForDate(controller.Data,day),(_,_,_)=>false);
        Drag(2.2,3.2);var failed=form.TimePicker.GetRange();
        Assert(modal.Visibility==Visibility.Visible && failed.StartDate==date && failed.StartMinute==1350 && failed.EndDate==next && failed.EndMinute==150 && JsonSerializer.Serialize(controller.Data)==before,"failed direct time save preserves the draft in the existing edit modal and leaves storage unchanged");
        form.Close();hud.Connect(controller);hud.ReturnToCalendar();
    }
}
