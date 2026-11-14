using System.IO;
using System.Text.Json;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Facet;
using Facet.Core;

internal static partial class Program
{
    private static void CheckCrossDatePlanner(Controller controller,string qa)
    {
        var hud=(OverlayTabs)((Grid)controller.Overlay.Content).Children[2];hud.Connect(controller);hud.AnimateDayTransitions=false;
        var date=new DateOnly(2028,3,1);var previous=date.AddDays(-1);var next=date.AddDays(1);
        controller.Commit(data=> {
            CalendarNotes.Save(data,new(){Date=previous,Note="전날 기록"},DateTime.Now);
            CalendarNotes.Save(data,new(){Date=date,Note="당일 기록",Todos=[new(){Title="당일 할 일"}]},DateTime.Now);
            CalendarNotes.Save(data,new(){Date=next,Note="다음 날 기록"},DateTime.Now);
        });
        hud.OpenDay(date);hud.Measure(new Size(1440,900));hud.Arrange(new Rect(0,0,1440,900));hud.UpdateLayout();
        var dial=(OverlayScheduleDial)hud.FindName("ScheduleDial");dial.TimeProvider=()=>new(2026,10,24,18,0,0);var editor=(OverlayDayEditor)hud.FindName("DayEditor");
        var modal=(Grid)hud.FindName("ScheduleModal");var form=(OverlayScheduleEditor)hud.FindName("ScheduleEditor");
        var note=(TextBox)typeof(OverlayDayEditor).GetField("note",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!;
        void Move(params double[] slots) {foreach(double slot in slots)dial.MovePointer(dial.PointAt(slot,dial.Radius*.95));}
        void CheckPreview(double currentSlot,double outsideSlot,string name) {
            dial.UpdateLayout();var image=new RenderTargetBitmap((int)dial.ActualWidth,(int)dial.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(dial);
            Color Pixel(double slot) {var at=dial.PointAt(slot,dial.Radius*.7);var bytes=new byte[4];image.CopyPixels(new Int32Rect((int)at.X,(int)at.Y,1,1),bytes,4,0);return Color.FromArgb(bytes[3],bytes[2],bytes[1],bytes[0]);}
            var current=Pixel(currentSlot);var outside=Pixel(outsideSlot);
            Assert(current.A==255 && outside.A>0 && current.G>outside.G,"preview keeps current date opaque and adjacent date translucent: "+name);
            SavePlannerImage(dial,new Size(dial.ActualWidth,dial.ActualHeight),Path.Combine(qa,"cross-date-preview-"+name+".png"));
        }
        Assert(dial.BeginSelection(dial.PointAt(1.2,dial.Radius*.95)),"backward creation begins on the selected date");Move(.2,47.2,46.2);
        Assert(dial.Selection.Start== -60 && dial.Selection.Duration==120,"counterclockwise midnight crossing selects yesterday23 to selected-date1");CheckPreview(.5,47.5,"previous");
        dial.EndSelection(dial.PointAt(46.2,dial.Radius*.95));
        Assert(modal.Visibility==Visibility.Visible && form.TimePicker.StartDate==previous && form.StartInput.SelectedIndex==46 && form.EndInput.SelectedIndex==2,"creation modal retains previous date and actual23-to1 times");
        form.NameInput.Text="전날23시부터당일1시";Assert(form.Submit(),"previous-day creation commits once");
        var loaded=new Store(controller.Store.Root).Load();
        Assert(CalendarNotes.Get(loaded,previous).Schedules.Single().StartMinute==1380 && CalendarNotes.Get(loaded,date).Schedules.Count==0,"encrypted storage owns the schedule on its real start date without duplicating it");
        var projected=editor.Schedules.Single();
        Assert(projected.VisibleStartMinute==0 && projected.VisibleDurationMinutes==60 && !projected.ContainsSlot(46) && !projected.ContainsSlot(47),"saved selected-date view shows only00-to01 and no yesterday23 wedge");
        Assert(dial.LayoutCallouts().Single().AnchorSlot<2,"saved label connects only to the visible selected-date fragment");
        hud.ShowSchedule(projected,true);Assert(form.StartInput.SelectedIndex==46 && form.TimePicker.StartDate==previous,"editing a clipped fragment restores its full original start date/time");
        form.ContentInput.Text="원래 구간 보존";Assert(form.Submit(),"editing incoming fragment updates the original previous-day event");
        editor.BeginNoteEdit();note.Text="당일 기록 수정";Assert(editor.TrySave(),"note autosave works alongside a carried schedule");editor.FinishNoteEdit(false);
        Assert(CalendarNotes.Get(controller.Data,previous).Schedules.Single().DurationMinutes==120 && CalendarNotes.Get(controller.Data,date).Schedules.Count==0,"note autosave cannot truncate or duplicate the carried event");
        string before=JsonSerializer.Serialize(controller.Data);
        editor.Configure(day=>CalendarNotes.Get(controller.Data,day),_=>false,day=>CalendarNotes.SchedulesForDate(controller.Data,day),(_,_,_)=>false);
        hud.ShowSchedule(projected,true);form.NameInput.Text="쓰기 실패 시 보존";
        Assert(!form.Submit() && modal.Visibility==Visibility.Visible && form.NameInput.Text=="쓰기 실패 시 보존" && JsonSerializer.Serialize(controller.Data)==before,"failed cross-date write preserves modal draft and all dates");form.Close();hud.Connect(controller);
        hud.ShowSchedule(editor.Schedules.Single(),true);Assert(form.Submit(true),"incoming fragment can delete its original previous-day schedule");
        Assert(CalendarNotes.Get(controller.Data,previous).Note=="전날 기록" && CalendarNotes.Get(controller.Data,previous).Schedules.Count==0 && CalendarNotes.Get(controller.Data,date).Todos.Count==1 && CalendarNotes.Get(controller.Data,date).Note=="당일 기록 수정","deleting a cross-date schedule preserves both dates notes and tasks");
        dial.BeginSelection(dial.PointAt(46.2,dial.Radius*.95));Move(47.2,.2,1.2);CheckPreview(47.5,.5,"next");dial.EndSelection(dial.PointAt(1.2,dial.Radius*.95));
        form.NameInput.Text="당일23시부터다음날1시";Assert(form.Submit(),"clockwise midnight creation saves on the selected date");
        Assert(editor.Schedules.Single().VisibleStartMinute==1380 && editor.Schedules.Single().VisibleDurationMinutes==60,"saved current-date outgoing fragment stops at24 instead of wrapping to00");
        hud.OpenDay(next);Assert(editor.Schedules.Single().VisibleStartMinute==0 && editor.Schedules.Single().VisibleDurationMinutes==60 && editor.Schedules.Single().OwnerDate==date,"next date displays only00-to01 of the same original schedule");
        Assert(CalendarNotes.Get(controller.Data,next).Note=="다음 날 기록","opening a continuation keeps the next date's independent note");
        hud.ShowSchedule(editor.Schedules.Single(),true);Assert(form.Submit(true),"deletion from next date removes the original event without leaving a duplicate");
        hud.ReturnToCalendar();hud.OpenDay(DateOnly.FromDateTime(DateTime.Now));hud.ReturnToCalendar();
    }
}
