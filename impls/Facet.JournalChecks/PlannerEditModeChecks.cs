using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Facet;
using Facet.SchedulePlugin;
using Facet.Core;

internal static partial class Program
{
    private static void CheckDateRangeSelection(OverlayScheduleTimePicker picker)
    {
        var first=new DateOnly(2028,2,29);picker.SetRange(new(first,600,first,660));
        Assert(VisualElements(picker).OfType<ScheduleDateCalendar>().Count()==1,"date range uses exactly one calendar surface");
        var day=VisualElements(picker.Calendar).OfType<Button>().Single(button=>System.Windows.Automation.AutomationProperties.GetName(button)=="2028년 2월 29일");
        day.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(picker.IsSelectingEnd && picker.StartDate==first,"choosing the start date advances the same calendar to end-date selection");
        var before=VisualElements(picker.Calendar).OfType<Button>().Single(button=>System.Windows.Automation.AutomationProperties.GetName(button)=="2028년 2월 28일");
        Assert(!before.IsEnabled,"dates before the selected start are visibly disabled");
        picker.ChooseDate(first.AddDays(-1));Assert(picker.EndDate==first,"disabled earlier dates cannot change the end endpoint");
        Assert(!((ComboBoxItem)picker.EndInput.Items[20]).IsEnabled && !((ComboBoxItem)picker.EndInput.Items[19]).IsEnabled && ((ComboBoxItem)picker.EndInput.Items[21]).IsEnabled,"same-day end choices disable earlier and equal times but allow the next half hour");
        var disabled=(ComboBoxItem)picker.EndInput.Items[20];disabled.Style=picker.EndInput.ItemContainerStyle;disabled.ApplyTemplate();
        Assert(((Border)disabled.Template.FindName("Row",disabled)).Opacity<.5,"disabled end times also render dimmed in the shared dropdown style");
        picker.StartInput.SelectedIndex=24;
        Assert(picker.GetRange().Duration==30 && picker.EndInput.SelectedIndex==25,"moving start time past the end repairs the end to the next valid half hour");
        picker.ChooseDate(first.AddDays(1));
        Assert(((ComboBoxItem)picker.EndInput.Items[24]).IsEnabled && ((ComboBoxItem)picker.EndInput.Items[25]).IsEnabled,"every next-day end time is selectable regardless of the start hour");
        Assert(picker.Calendar.IsDateEnabled(first.AddDays(1)) && picker.Calendar.IsDateEnabled(first.AddYears(5)),"end calendar permits future dates without a 24-hour limit");
        picker.SetRange(new(first,0,first,30));picker.ChooseDate(first);picker.ChooseDate(first.AddDays(1));
        Assert(((ComboBoxItem)picker.EndInput.Items[0]).IsEnabled && picker.GetRange().Duration>1440,"next-day early times remain available without clamping an existing later end time");
        picker.SetRange(new(DateOnly.MaxValue,1410,DateOnly.MaxValue,1440));picker.StartInput.SelectedIndex=0;
        Assert(picker.GetRange().EndDate==DateOnly.MaxValue && picker.GetRange().EndMinute==1440,"last supported date keeps valid 24:00 without date overflow");
        picker.ChooseDate(DateOnly.MaxValue);
        Assert(picker.GetRange().EndDate==DateOnly.MaxValue,"the last supported date remains selectable as an end date");
        picker.SetRange(new(DateOnly.MinValue,0,DateOnly.MaxValue,1440));
        Assert(picker.GetRange().Duration>int.MaxValue,"date picker minute arithmetic supports the full date range");
        picker.SetRange(new(first,1380,first.AddDays(1),60));
    }
    private static void CheckPlannerEditModes(Controller controller,string qa)
    {
        var hud=(OverlayTabs)((Grid)controller.Overlay.Content).Children[2];var board=ScheduleBoardFor(hud,controller.Store.Root);board.AnimateDayTransitions=false;hud.Connect(controller);
        var date=DateOnly.FromDateTime(DateTime.Now);board.OpenDay(date);hud.Measure(new Size(1440,900));hud.Arrange(new Rect(0,0,1440,900));hud.UpdateLayout();
        var editor=(OverlayDayEditor)board.FindName("DayEditor");var dial=(OverlayScheduleDial)board.FindName("ScheduleDial");
        var form=(OverlayScheduleEditor)board.FindName("ScheduleEditor");var modal=(Grid)board.FindName("ScheduleModal");
        var note=(TextBox)typeof(OverlayDayEditor).GetField("note",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!;
        Assert(!dial.Focusable && !dial.Focus(),"schedule dial rejects keyboard focus");
        var back=(Button)board.FindName("BackToCalendar");
        Assert(!back.Focusable && !back.IsTabStop && !back.Focus(),"back button rejects keyboard focus and tab navigation");
        board.MoveOverlayFocus(false,(Grid)board.FindName("DayPage"));
        Assert(ReferenceEquals(FocusManager.GetFocusedElement(FocusManager.GetFocusScope(editor.TodoInput)),editor.TodoInput),"tab order skips the dial and reaches the todo input");
        board.MoveOverlayFocus(true,editor.TodoInput);
        Assert(ReferenceEquals(FocusManager.GetFocusedElement(FocusManager.GetFocusScope(back)),hud.FindName("DashboardTab")),"reverse tab from todo skips the back button and returns to the management tab");
        var tasks=(StackPanel)typeof(OverlayDayEditor).GetField("tasks",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!;
        Assert(tasks.Children.OfType<Border>().All(row=>System.Math.Abs(row.ActualWidth-editor.TodoInput.ActualWidth)<1),"short todo rows match the entry field without a scrollbar gutter");
        editor.BeginNoteEdit();note.Text="모드 스택 확인";
        Assert(hud.HandleEscape() && !editor.IsNoteEditing && board.IsEditingDay,"Escape pops record input to preview without leaving day mode");
        Assert(hud.HandleEscape() && !board.IsEditingDay && !hud.HandleEscape(),"next Escape pops day mode and the base calendar delegates overlay dismissal");
        board.OpenDay(date);var source=editor.Schedules.First();board.ShowSchedule(source,true);form.OpenTimePicker();
        form.TimePicker.ChooseDate(new(2028,2,29));var changed=form.TimePicker.GetRange();
        Assert(System.Windows.Automation.AutomationProperties.GetName(form.TimeRangeButton).Contains("2028년 2월 29일"),"date selection immediately updates the schedule draft button");
        Assert(hud.HandleEscape() && !form.IsTimePickerOpen && modal.Visibility==Visibility.Visible && form.TimePicker.GetRange()==changed,"Escape closes only date/time mode and retains the immediately applied range");
        Assert(hud.HandleEscape() && modal.Visibility==Visibility.Collapsed && board.IsEditingDay,"next Escape saves the range and closes the schedule editor only");
        var escaped=CalendarNotes.Get(ScheduleData(controller.Store.Root),changed.StartDate).Schedules.Single(item=>item.Id==source.Id);
        Assert(escaped.StartMinute==changed.StartMinute && escaped.DurationMinutes==changed.Duration,"nested picker Escape then editor Escape saves the selected cross-date range");
        board.OpenDay(changed.StartDate);source=editor.Schedules.Single(item=>item.Id==source.Id);
        board.ShowSchedule(source,true);form.OpenTimePicker();
        var cal=form.TimePicker.Calendar;cal.SetDate(new(2028,2,29));cal.YearInput.Text="2027";
        cal.YearInput.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice,0,cal.YearInput,cal.MonthInput){RoutedEvent=UIElement.LostKeyboardFocusEvent});
        Assert(cal.YearInput.Text=="2027" && cal.SelectedDate==new DateOnly(2028,2,29),"calendar year browsing does not prematurely commit a range endpoint");
        Assert(VisualElements(cal).OfType<Button>().Any(button=>button.IsTabStop && button.IsEnabled),"browsing another year keeps an enabled day reachable by Tab");
        form.TimePicker.SetRange(new(new(2028,2,29),1380,new(2028,3,1),60));
        CheckDateRangeSelection(form.TimePicker);
        string before=ScheduleRecords.Serialize(board.Records.Data);
        Assert(form.IsTimePickerOpen && ScheduleRecords.Serialize(board.Records.Data)==before,"date/time changes apply to the draft without prematurely saving the schedule");
        Assert(!VisualElements(form.TimePicker).OfType<Button>().Any(button=>Equals(button.Content,"확인") || Equals(button.Content,"취소")) && !VisualElements(form.TimePicker).OfType<TextBlock>().Any(text=>text.Text.Contains('→')),"compact picker has no confirmation buttons or duplicated range summary");
        form.CloseTimePicker();
        Assert(form.ContentInput.VerticalContentAlignment==VerticalAlignment.Top && form.ContentInput.TextAlignment==TextAlignment.Left,"schedule content starts at the top left");
        Assert(VisualElements(form).OfType<ComboBox>().All(combo=>form.TimePicker.IsAncestorOf(combo)) && !VisualElements(form).OfType<TextBox>().Any(box=>ReferenceEquals(box,form.ColorInput) || ReferenceEquals(box,form.LabelInput)),"schedule form shows color/label only through its preset button");
        form.LabelInput.Text="독립 레이블";form.ColorInput.Text="#91BFFF";
        Assert(System.Windows.Automation.AutomationProperties.GetName(form.CategoryButton).Contains("독립 레이블") && System.Windows.Automation.AutomationProperties.GetName(form.CategoryButton).Contains("#91BFFF"),"preset button shows the draft color and label in one row");
        double opacity=hud.Opacity;hud.Opacity=1;hud.UpdateLayout();
        SavePlannerImage(hud,new Size(1440,900),Path.Combine(qa,"schedule-editor-clean.png"));
        foreach(var size in new[]{new Size(1440,900),new Size(800,600),new Size(480,360)}) {
            hud.Measure(size);hud.Arrange(new Rect(size));hud.UpdateLayout();
            var original=form.TransformToAncestor(hud).TransformBounds(new Rect(form.RenderSize));
            form.OpenTimePicker();form.TimePicker.SelectEndpoint(true);hud.UpdateLayout();
            var current=form.TransformToAncestor(hud).TransformBounds(new Rect(form.RenderSize));
            var popup=form.TimePicker.TransformToAncestor(hud).TransformBounds(new Rect(form.TimePicker.RenderSize));
            var anchor=form.TimeRangeButton.TransformToAncestor(hud).TransformBounds(new Rect(form.TimeRangeButton.RenderSize));
            Assert(current==original && !form.IsAncestorOf(form.TimePicker),"date/time picker never changes the parent editor size or position at "+size);
            Assert(Math.Abs(popup.Top-anchor.Bottom-6)<1 && popup.Bottom<=hud.ActualHeight-11 && popup.Left>=11 && popup.Right<=hud.ActualWidth-11 && popup.Height<=400,"independent compact picker stays below its button and inside the screen at "+size);
            var layer=(Grid)board.FindName("ScheduleTimeModal");
            layer.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.MouseDownEvent});
            Assert(!form.IsTimePickerOpen && modal.Visibility==Visibility.Visible && form.TimePicker.GetRange()==new ScheduleTimeRange(new(2028,2,29),1380,new(2028,3,1),60),"outside click closes only the picker and retains the range at "+size);
            hud.UpdateLayout();Assert(form.TransformToAncestor(hud).TransformBounds(new Rect(form.RenderSize))==original,"closing the picker preserves parent bounds at "+size);
        }
        hud.Measure(new Size(1440,900));hud.Arrange(new Rect(0,0,1440,900));hud.UpdateLayout();form.OpenTimePicker();form.TimePicker.SelectEndpoint(true);hud.UpdateLayout();
        SavePlannerImage(hud,new Size(1440,900),Path.Combine(qa,"schedule-date-time-picker.png"));
        Assert(form.TimePicker.ActualHeight<400,"combined calendar and time picker uses compact content height");
        form.CloseTimePicker();hud.UpdateLayout();
        Assert(System.Math.Abs(form.ActualWidth-740d*2/3)<1 && form.ActualHeight<650,"schedule editor uses two thirds width and a compact content-driven height");
        var memo=form.ContentInput.TransformToAncestor(form).TransformBounds(new Rect(form.ContentInput.RenderSize));
        var delete=VisualElements(form).OfType<Button>().Single(button=>System.Windows.Automation.AutomationProperties.GetName(button)=="일정 삭제");
        var deleteBounds=delete.TransformToAncestor(form).TransformBounds(new Rect(delete.RenderSize));
        Assert(deleteBounds.Top-memo.Bottom<40,"schedule deletion follows the memo without an empty expanding row");
        var center=form.TransformToAncestor(modal).Transform(new Point(form.ActualWidth/2,form.ActualHeight/2));
        Assert(System.Math.Abs(center.X-modal.ActualWidth/2)<1 && System.Math.Abs(center.Y-modal.ActualHeight/2)<1,"compact editor remains centered in the day viewport");
        form.CloseTimePicker();hud.Opacity=opacity;
        Assert(form.Submit(),"schedule confirmation accepts an explicit cross-month date/time range");
        var saved=CalendarNotes.Get(ScheduleData(controller.Store.Root),new(2028,2,29)).Schedules.Single(item=>item.Id==source.Id);
        Assert(saved.StartMinute==1380 && saved.DurationMinutes==120 && !CalendarNotes.Get(ScheduleData(controller.Store.Root),date).Schedules.Any(item=>item.Id==source.Id),"date picker moves the canonical event while preserving its original identity");
        board.ShowSchedule(CalendarNotes.SchedulesForDate(ScheduleData(controller.Store.Root),new(2028,2,29)).Single(item=>item.Id==source.Id),true);
        form.TimePicker.SetRange(new(new(2028,2,29),1380,new(2028,3,3),60));Assert(form.Submit(),"multiple-day schedule persists through the encrypted writer");
        board.OpenDay(new(2028,3,1));var middle=editor.Schedules.Single(item=>item.Id==source.Id);
        Assert(middle.VisibleDurationMinutes==1440 && middle.OwnerDate==new DateOnly(2028,2,29),"middle date shows a full-day sector of the same original schedule");
        board.ShowSchedule(middle,true);
        Assert(form.TimePicker.GetRange().EndDate==new DateOnly(2028,3,3) && form.TimePicker.GetRange().EndMinute==60,"editing from a middle date restores the full multi-day range");
        form.Submit(true);board.ReturnToCalendar();
    }
}
