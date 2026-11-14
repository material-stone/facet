using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Facet;
using Facet.SchedulePlugin;
using Facet.Core;

internal static partial class Program
{
    private static void CheckCalendarDayEditor(Controller controller, string qa)
    {
        var hud=(OverlayTabs)((Grid)controller.Overlay.Content).Children[2];var board=ScheduleBoardFor(hud,controller.Store.Root);
        board.AnimateDayTransitions=false;
        var tabs=(TabControl)hud.FindName("Tabs");tabs.SelectedItem=hud.FindName("DashboardTab");
        var calendar=(OverlayCalendarView)board.FindName("Calendar");
        var editor=(OverlayDayEditor)board.FindName("DayEditor");
        var dial=(OverlayScheduleDial)board.FindName("ScheduleDial");
        var modal=(Grid)board.FindName("ScheduleModal");
        var form=(OverlayScheduleEditor)board.FindName("ScheduleEditor");
        var page=(Grid)board.FindName("DayPage");var calendarHost=(Viewbox)board.FindName("CalendarHost");
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string name)=>(T)typeof(OverlayDayEditor).GetField(name,flags)!.GetValue(editor)!;
        var input=Field<TextBox>("newTodo");var note=Field<TextBox>("note");var tasks=Field<StackPanel>("tasks");
        var today=DateOnly.FromDateTime(DateTime.Now);var other=today.AddDays(today==DateOnly.MaxValue?-1:1);
        Assert(!board.IsEditingDay && page.Visibility==Visibility.Collapsed && calendarHost.Visibility==Visibility.Visible,"management starts with calendar alone and no day panel");
        Assert(board.MoveCalendarMonth(1) && board.GoToCalendarToday(),"calendar retains month and today shortcuts");
        hud.Measure(new Size(1440,900));hud.Arrange(new Rect(0,0,1440,900));hud.UpdateLayout();
        var dateCell=calendar.SelectedDateFocusTarget();
        dateCell.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent});
        hud.UpdateLayout();
        Assert(board.IsEditingDay && page.Visibility==Visibility.Visible && calendarHost.Visibility==Visibility.Collapsed,"date activation replaces calendar with day planner");
        Assert(((TextBlock)board.FindName("DayHeading")).Text.Contains($"{today.Day}일"),"day heading keeps pie gestures isolated");
        Assert(!board.MoveCalendarMonth(1) && !board.GoToCalendarToday(),"hidden calendar does not consume day-screen navigation keys");

        input.Text="하루 화면의 할 일";
        Assert(editor.TrySave(),"day todo pending input saves");
        editor.BeginNoteEdit();note.Text="# 하루 기록\n\n**강조**와 `code`\n\n- [x] 완료\n- 다음 행동";
        Assert(editor.TrySave(),"Markdown source saves in day planner");editor.FinishNoteEdit(false);hud.UpdateLayout();
        Assert(!editor.IsNoteEditing && VisualElements(Field<Border>("notePreview")).OfType<TextBlock>().Any(t=>t.FontSize==24),"existing Markdown preview remains rendered in records panel");
        var row=(Border)tasks.Children[0];row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent});
        editor.TrySave();
        Assert(CalendarNotes.Get(ScheduleData(controller.Store.Root),today).Todos.Single().Done && VisualElements(row).OfType<TextBlock>().Single(t=>t.Text=="하루 화면의 할 일").TextDecorations==TextDecorations.Strikethrough,"todo completion survives encrypted reload");
        input.Text="삭제할 할 일";Assert(editor.TrySave(),"second todo saves before delete-button check");
        var deleteRow=(Border)tasks.Children[1];var deleteTodo=VisualElements(deleteRow).OfType<Button>().Single();
        deleteTodo.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(editor.TrySave() && CalendarNotes.Get(ScheduleData(controller.Store.Root),today).Todos is {Count:1} remaining && remaining[0].Title=="하루 화면의 할 일" && remaining[0].Done,"todo delete button removes only its own item and preserves another item's completion after encrypted reload");
        Assert(note.Tag==null,"record editor has no placeholder prompt");

        var item=new DaySchedule {Name="집중 제작",StartMinute=540,DurationMinutes=120,Color="#91BFFF",Label="작업",Content="첫 상세 내용"};
        string savedBackground=controller.Data.Settings.OverlayBackgroundColor;
        controller.Data.Settings.OverlayBackgroundColor="#3265A8";
        board.ShowSchedule(item,false);
        Assert(modal.Visibility==Visibility.Visible && form.NameInput.Text==item.Name,"range opens in-content schedule modal");
        var skinBg=((SolidColorBrush)Application.Current.FindResource("Bg")).Color;
        Assert(modal.Background is SolidColorBrush {Color:{A:184} scrimColor} && scrimColor.R==skinBg.R && scrimColor.G==skinBg.G && scrimColor.B==skinBg.B,"schedule scrim uses the skin background RGB with translucent alpha (a plugin does not read app settings)");
        controller.Data.Settings.OverlayBackgroundColor=savedBackground;
        form.ColorInput.Text="#wrong!";Assert(!form.Submit() && modal.Visibility==Visibility.Visible,"invalid category color retains modal draft");
        form.ColorInput.Text=item.Color;Assert(form.Submit() && modal.Visibility==Visibility.Collapsed,"valid modal save closes after commit");
        var stored=CalendarNotes.Get(ScheduleData(controller.Store.Root),today);
        Assert(stored.Schedules.Single().Name==item.Name && stored.Schedules[0].Content==item.Content && stored.Todos.Count==1 && stored.Note==note.Text,"schedules todos and records share one encrypted day without overwriting");
        note.Text+="\n\n일정 저장 후 기록";editor.TrySave();Assert(CalendarNotes.Get(ScheduleData(controller.Store.Root),today).Schedules.Count==1,"record autosave preserves schedule changes");
        board.OpenDay(other);Assert(editor.Schedules.Count==0 && note.Text=="","other date starts independently");board.OpenDay(today);
        Assert(editor.Schedules.Count==1 && note.Text.Contains("일정 저장 후 기록"),"returning to date reloads schedule and original note");
        var nameBadge=dial.LayoutCallouts().Single(label=>label.Item.Id==editor.Schedules[0].Id).Bounds;
        var namePoint=new Point(nameBadge.X+nameBadge.Width/2,nameBadge.Y+nameBadge.Height/2);
        Assert(dial.SlotAt(namePoint)<0 && dial.BeginSelection(namePoint),"schedule name accepts a click outside the circular time grid");
        dial.EndSelection(namePoint);
        Assert(modal.Visibility==Visibility.Visible && form.NameInput.Text==item.Name,"clicking a schedule name opens its own edit modal");hud.HandleEscape();
        var scheduledPoint=dial.PointAt(editor.Schedules[0].StartMinute/30d+.25,dial.Radius*.7);
        dial.BeginSelection(scheduledPoint);dial.EndSelection(scheduledPoint);
        Assert(modal.Visibility==Visibility.Visible && form.NameInput.Text==item.Name,"clicking a scheduled sector opens that schedule in the edit modal");
        form.TimePicker.SetRange(new(today,1410,today.AddDays(1),60));form.ContentInput.Text="자정을 넘는 내용";
        Assert(form.Submit(),"overnight schedule edit saves");
        stored=CalendarNotes.Get(ScheduleData(controller.Store.Root),today);
        Assert(stored.Schedules[0].StartMinute==1410 && stored.Schedules[0].DurationMinutes==90 && stored.Schedules[0].Content=="자정을 넘는 내용","modal start and end convert to overnight duration");
        board.ShowSchedule(stored.Schedules[0],true);form.NameInput.Text="Esc로 저장한 일정";form.ContentInput.Text="Esc 자동 저장 내용";
        Assert(hud.HandleEscape() && modal.Visibility==Visibility.Collapsed && board.IsEditingDay,"first Escape saves and closes only the schedule editor");
        var escapeSaved=CalendarNotes.Get(ScheduleData(controller.Store.Root),today).Schedules.Single();
        Assert(escapeSaved.Name=="Esc로 저장한 일정" && escapeSaved.Content=="Esc 자동 저장 내용","Escape autosave persists edited fields through encrypted reload");
        board.ShowSchedule(stored.Schedules[0],true);form.NameInput.Text="바깥 클릭으로 저장한 일정";form.ContentInput.Text="자동 저장한 변경 내용";
        form.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.MouseDownEvent});
        Assert(modal.Visibility==Visibility.Visible,"clicking inside the schedule form does not dismiss it");
        var outside=new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.MouseDownEvent};modal.RaiseEvent(outside);
        var autoSaved=CalendarNotes.Get(ScheduleData(controller.Store.Root),today).Schedules.Single();
        Assert(outside.Handled && modal.Visibility==Visibility.Collapsed && board.IsEditingDay && autoSaved.Name=="바깥 클릭으로 저장한 일정" && autoSaved.Content=="자동 저장한 변경 내용","outside click saves schedule changes to encrypted storage before closing and consumes the click");
        board.ShowSchedule(autoSaved,true);form.NameInput.Text=" ";form.ContentInput.Text="";
        Assert(!form.Submit() && modal.Visibility==Visibility.Visible && CalendarNotes.Get(ScheduleData(controller.Store.Root),today).Schedules.Single().Name==autoSaved.Name,"clearing an existing schedule name and content retains validation and never discards its stored record");form.Close();
        board.ShowSchedule(stored.Schedules[0],true);Assert(form.Submit(true) && editor.Schedules.Count==0,"schedule deletion leaves todo and record intact");

        // Real dial-to-modal wiring, including radial motion inside one 30-minute wedge.
        var start=dial.PointAt(18.25,dial.Radius*.55);var finish=dial.PointAt(18.3,dial.Radius*.75);
        Assert(dial.BeginSelection(start),"pointer begins inside the circle");dial.MovePointer(finish);dial.EndSelection(finish);
        Assert(modal.Visibility==Visibility.Visible && form.StartInput.SelectedIndex==18 && form.EndInput.SelectedIndex==19,"same-wedge drag above threshold selects a full half hour and opens modal");
        string emptyBefore=ScheduleRecords.Serialize(board.Records.Data);
        Assert(hud.HandleEscape() && modal.Visibility==Visibility.Collapsed && board.IsEditingDay && ScheduleRecords.Serialize(board.Records.Data)==emptyBefore,"Escape closes an untouched new schedule without creating or updating any data");
        board.ShowSchedule(new(){StartMinute=600,DurationMinutes=60},false);
        var emptyOutside=new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.MouseDownEvent};modal.RaiseEvent(emptyOutside);
        Assert(emptyOutside.Handled && modal.Visibility==Visibility.Collapsed && ScheduleRecords.Serialize(board.Records.Data)==emptyBefore,"outside click closes an untouched new schedule without saving and consumes the click");
        foreach(bool escape in new[]{true,false}) {
            board.ShowSchedule(new(){StartMinute=600,DurationMinutes=60},false);form.NameInput.Text=" \t";form.ContentInput.Text="\n ";
            form.LabelInput.Text="작성한 레이블";form.ColorInput.Text="#91BFFF";form.TimePicker.SetRange(new(today.AddDays(1),660,today.AddDays(2),720));
            if(escape)hud.HandleEscape();else modal.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.MouseDownEvent});
            Assert(modal.Visibility==Visibility.Collapsed && board.IsEditingDay && ScheduleRecords.Serialize(board.Records.Data)==emptyBefore,(escape?"Escape":"outside click")+" discards a new schedule with blank name and content despite changed color label and dates");
        }
        foreach(bool escape in new[]{true,false}) {
            string firstLine=escape?"Esc로 저장한 이름 없는 메모":"바깥 클릭으로 저장한 이름 없는 메모";
            string content=firstLine+(escape?"\r\n":"\n")+"레이블에 표시하지 않을 두 번째 줄";
            board.ShowSchedule(new(){StartMinute=600,DurationMinutes=60},false);form.NameInput.Text=" \t";form.ContentInput.Text=content;
            if(escape)hud.HandleEscape();else modal.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.MouseDownEvent});
            var contentOnly=CalendarNotes.Get(ScheduleData(controller.Store.Root),today).Schedules.Single();
            Assert(modal.Visibility==Visibility.Collapsed && board.IsEditingDay && contentOnly.Name=="" && contentOnly.Content==content,(escape?"Escape":"outside click")+" autosaves a content-only new schedule with an empty stored name");
            hud.UpdateLayout();var label=dial.LayoutCallouts().Single(callout=>callout.Item.Id==contentOnly.Id);
            Assert(label.NameText.Text==firstLine && label.Bounds.Width>0,"unnamed schedule labels show only the first content line for CRLF and LF notes");
            var point=new Point(label.Bounds.X+label.Bounds.Width/2,label.Bounds.Y+label.Bounds.Height/2);dial.BeginSelection(point);dial.EndSelection(point);
            Assert(modal.Visibility==Visibility.Visible && form.NameInput.Text=="" && form.ContentInput.Text==content,"fallback label reopens the unnamed schedule without manufacturing a stored name");
            Assert(form.Submit(true) && editor.Schedules.Count==0,"content-only schedule remains deletable after encrypted reload");
        }
        CheckDayDialGeometry(qa);CheckScheduleCurrentTime(qa);CheckDayEditorFill();CheckScheduleCallouts(qa);

        // Failed writes leave modal inputs and the existing day unchanged.
        editor.Configure(d=>CalendarNotes.Get(ScheduleData(controller.Store.Root),d),_=>false);
        board.ShowSchedule(new(){Name="실패 보존",StartMinute=600,DurationMinutes=60},false);
        Assert(!form.Submit() && modal.Visibility==Visibility.Visible && form.NameInput.Text=="실패 보존" && editor.Schedules.Count==0,"failed schedule save retains editable modal without mutating day schedule");
        var failedOutside=new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.MouseDownEvent};modal.RaiseEvent(failedOutside);
        Assert(failedOutside.Handled && modal.Visibility==Visibility.Visible && form.NameInput.Text=="실패 보존" && editor.Schedules.Count==0,"failed outside-click autosave retains the modal draft and consumes the click");
        Assert(hud.HandleEscape() && modal.Visibility==Visibility.Visible && form.NameInput.Text=="실패 보존" && editor.Schedules.Count==0,"failed Escape autosave keeps the schedule modal draft open");
        form.Close();note.Text="저장 실패 시 남겨야 하는 기록";
        Assert(hud.HandleEscape() && board.IsEditingDay && editor.HasPendingChanges,"failed record save consumes Escape and blocks calendar return");
        board.ConnectEditor();
        Assert(editor.TrySave(),"retry saves preserved record draft");

        editor.SaveSchedule(new(){Name="집중 작업",StartMinute=540,DurationMinutes=240,Color="#91BFFF",Label="작업"});
        editor.SaveSchedule(new(){Name="잠깐 휴식",StartMinute=600,DurationMinutes=30,Color="#D2ACF0",Label="휴식"});
        editor.SaveSchedule(new(){Name="저녁 산책",StartMinute=1080,DurationMinutes=60,Color="#82CFAD",Label="개인"});
        double previousOpacity=hud.Opacity;hud.Opacity=1;
        foreach(var size in new[]{new Size(1440,900),new Size(800,600),new Size(480,360)}) {
            // The screen fits whatever the tile gives it; a tile this small is the widget resized on a small display.
            var dayViewport=(Viewbox)board.FindName("DayViewport");dayViewport.Width=size.Width-112;dayViewport.Height=size.Height-232;
            hud.Measure(size);hud.Arrange(new Rect(size));hud.UpdateLayout();
            var dialRight=dial.TranslatePoint(new Point(dial.ActualWidth,0),hud);var editorLeft=editor.TranslatePoint(new Point(),hud);
            var bottom=editor.TranslatePoint(new Point(editor.ActualWidth,editor.ActualHeight),hud);
            Assert(dialRight.X<editorLeft.X && bottom.X<size.Width && bottom.Y<size.Height && ((ScrollViewer)Field<Border>("notePreview").Child).ViewportHeight>0,"pie on left and scrollable todo-record panel on right fit at "+size);
            var layout=dial.LayoutSchedules();Assert(layout.Single(s=>s.Item.Name=="잠깐 휴식").Radius<layout.Single(s=>s.Item.Name=="집중 작업").Radius && layout.Single(s=>s.Item.Name=="저녁 산책").Radius==dial.Radius*.9,"only overlapping shorter schedule shrinks inside the longer sector at "+size);
            Assert(editorLeft.X>size.Width*.6 && editor.ActualWidth>350,"day panel moves right while preserving editing width at "+size);
            var back=(Button)board.FindName("BackToCalendar");var heading=(TextBlock)board.FindName("DayHeading");
            var backCenter=back.TranslatePoint(new Point(0,back.ActualHeight/2),hud);var headingCenter=heading.TranslatePoint(new Point(0,heading.ActualHeight/2),hud);
            Assert(System.Math.Abs(backCenter.Y-headingCenter.Y)<1 && back.Content is Facet.SchedulePlugin.Icon {Kind:Facet.SchedulePlugin.IconKind.Left},"back chevron and date share one vertically centered row at "+size);
            Assert(VisualElements(hud).OfType<TextBlock>().Count(text=>text.Text==heading.Text)==1 && hud.FindName("AddSchedule")==null && hud.FindName("ScheduleList")==null,"day date appears once and redundant schedule controls are absent");
            dial.MovePointer(dial.PointAt(0,dial.Radius+40));hud.UpdateLayout();
            SavePlannerImage(hud,size,Path.Combine(qa,$"day-pie-planner-{size.Width}.png"));
            dial.MovePointer(dial.PointAt(14.5,dial.Radius*.95));hud.UpdateLayout();
            SavePlannerImage(hud,size,Path.Combine(qa,$"day-pie-hover-{size.Width}.png"));
            dial.MovePointer(dial.PointAt(0,dial.Radius+40));hud.UpdateLayout();
            board.ShowSchedule(editor.Schedules[0],true);hud.UpdateLayout();
            var scrim=modal.TransformToAncestor(hud).TransformBounds(new Rect(modal.RenderSize));
            Assert(scrim==new Rect(hud.RenderSize),"schedule scrim covers the whole overlay including margins and tabs at "+size+" ("+scrim+" / "+hud.RenderSize+")");
            Assert(((Grid)board.FindName("ScheduleTimeModal")).Visibility==Visibility.Collapsed,"nested picker layer is hidden before opening the schedule modal at "+size);
            // Offscreen visual tests must skip retained drawings of collapsed flyouts.
            HitTestResult? scrimHit=null;
            VisualTreeHelper.HitTest(modal,node=>node is UIElement element && (element.Visibility!=Visibility.Visible || !element.IsHitTestVisible)?HitTestFilterBehavior.ContinueSkipSelfAndChildren:HitTestFilterBehavior.Continue,
                hit=>{scrimHit=hit;return HitTestResultBehavior.Stop;},new PointHitTestParameters(new Point(2,2)));
            var layers=(Grid)hud.FindName("WidgetLayers");
            Assert(ReferenceEquals(scrimHit?.VisualHit,modal) && ReferenceEquals(VisualTreeHelper.GetParent(modal),layers) && ReferenceEquals(VisualTreeHelper.GetParent(layers),VisualTreeHelper.GetParent(tabs)) && Panel.GetZIndex(layers)>Panel.GetZIndex(tabs),"schedule scrim on the widget layer intercepts outer-margin clicks above tabs at "+size);
            var background=((SolidColorBrush)modal.Background).Color;
            var expected=((SolidColorBrush)Application.Current.FindResource("Bg")).Color;
            Assert(background.A==184 && background.R==expected.R && background.G==expected.G && background.B==expected.B,"reopening schedule modal refreshes its skin background color");
            var box=form.TransformToAncestor(hud).TransformBounds(new Rect(form.RenderSize));
            Assert(box.Left>=0 && box.Top>=0 && box.Right<=size.Width && box.Bottom<=size.Height,"schedule modal stays inside scaled planner at "+size);
            var deleteButton=VisualElements(form).OfType<Button>().Single(button=>System.Windows.Automation.AutomationProperties.GetName(button)=="일정 삭제");
            var deleteBounds=deleteButton.TransformToAncestor(form).TransformBounds(new Rect(deleteButton.RenderSize));
            Assert(deleteBounds.Bottom<form.ActualHeight && deleteBounds.Top>0 && !VisualElements(form).OfType<Button>().Any(button=>Equals(button.Content,"확인") || Equals(button.Content,"취소")) && VisualElements(form).OfType<Button>().Where(button=>button.Content is Border).All(button=>button.ContentTemplate==null),"modal retains deletion and color controls without confirmation or cancellation buttons at "+size);
            SavePlannerImage(hud,size,Path.Combine(qa,$"day-pie-modal-{size.Width}.png"));
            string originalColor=form.ColorInput.Text,originalLabel=form.LabelInput.Text;
            form.OpenPresetPicker();hud.UpdateLayout();
            var presetBox=form.PresetPicker.TransformToAncestor(hud).TransformBounds(new Rect(form.PresetPicker.RenderSize));
            var categoryAnchor=form.CategoryButton.TransformToAncestor(hud).TransformBounds(new Rect(form.CategoryButton.RenderSize));
            Assert(form.IsPresetPickerOpen && !form.IsTimePickerOpen && form.TransformToAncestor(hud).TransformBounds(new Rect(form.RenderSize))==box && presetBox.Top>=categoryAnchor.Bottom,"preset picker anchors below the color/label button without resizing its editor at "+size);
            Assert(form.TimePicker.Visibility!=Visibility.Visible && form.PresetDialog.Visibility!=Visibility.Visible,"opening the preset picker shows only that one layer at "+size);
            form.PresetPicker.AddButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));hud.UpdateLayout();
            Assert(form.IsPresetDialogOpen && form.PresetPicker.Visibility!=Visibility.Visible && form.PresetDialog.Visibility==Visibility.Visible,"new preset button opens the preset dialog");
            form.OpenColorPicker();hud.UpdateLayout();
            foreach(string hex in new[]{"#FF0000","#00FF00","#0000FF","#000000","#FFFFFF","#3A7BD5"}) {
                form.ColorPicker.HexInput.Text=hex;
                Assert(form.PresetDialog.ColorInput.Text==hex,"color picker round-trips RGB and grayscale without conversion loss: "+hex);
            }
            form.ColorPicker.HexInput.Text="#GGGGGG";
            Assert(form.PresetDialog.ColorInput.Text=="#3A7BD5" && form.ColorInput.Text==originalColor,"invalid picker code keeps the last valid preset color without touching the schedule draft");
            SavePlannerImage(hud,size,Path.Combine(qa,$"day-color-picker-{size.Width}.png"));
            hud.HandleEscape();Assert(!form.IsColorPickerOpen && form.IsPresetDialogOpen && modal.Visibility==Visibility.Visible,"Escape closes only the color picker while retaining the preset dialog");
            form.PresetDialog.LabelInput.Text="";Assert(!form.PresetDialog.Submit() && form.IsPresetDialogOpen,"empty preset label keeps the dialog open");
            form.PresetDialog.CancelButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert(!form.IsPresetDialogOpen && form.IsPresetPickerOpen && form.ColorInput.Text==originalColor,"cancel returns to the preset list without changing the schedule");
            string label="프리셋 "+size.Width;
            form.OpenPresetDialog();form.PresetDialog.ColorInput.Text="#3A7BD5";form.PresetDialog.LabelInput.Text=label;
            Assert(form.PresetDialog.Submit() && !form.IsPresetOpen && form.ColorInput.Text=="#3A7BD5" && form.LabelInput.Text==label,"confirming a new preset applies it to the schedule and closes the preset layers");
            Assert(CalendarNotes.SchedulePresets(ScheduleData(controller.Store.Root)).First().Key==new SchedulePreset{Color="#3A7BD5",Label=label}.Key,"new preset is saved at the front of the stored presets");
            form.OpenPresetPicker();hud.UpdateLayout();SavePlannerImage(hud,size,Path.Combine(qa,$"day-preset-list-{size.Width}.png"));
            var rows=form.PresetPicker.Presets.ToArray();
            Assert(form.PresetPicker.SelectedPreset is SchedulePreset current && current.Label==label,"preset list opens expanded and highlights the schedule's current preset");
            var previous=rows.FirstOrDefault(p=>p.Label!=label);
            if(previous!=null) {
                form.PresetPicker.Choose(previous);
                Assert(!form.IsPresetPickerOpen && form.ColorInput.Text==previous.Color && form.LabelInput.Text==previous.Label,"choosing a preset applies its color and label and closes the picker");
                form.OpenPresetPicker();hud.UpdateLayout();
            }
            Assert(form.PresetPicker.PresetInput.Visibility==Visibility.Visible && form.PresetPicker.PresetInput.MaxHeight<=264 && ScrollViewer.GetVerticalScrollBarVisibility(form.PresetPicker.PresetInput)==ScrollBarVisibility.Auto,"preset rows show immediately in a height-limited scroll list above the add button");
            var trash=form.PresetPicker.RemoveButton(form.PresetPicker.Presets.First(p=>p.Label==label));
            Assert(trash is {Content:Facet.SchedulePlugin.Icon {Kind:Facet.SchedulePlugin.IconKind.Trash}} && trash.ContentTemplate==null && trash.Visibility==Visibility.Visible && trash.ActualWidth>0 && ((FrameworkElement)trash.Content).ActualWidth>0,"each preset row ends with a rendered trash icon button");
            Assert(trash!=null,"each preset dropdown row ends with a visible remove button");
            trash!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert(form.PresetPicker.Presets.All(p=>p.Label!=label) && CalendarNotes.SchedulePresets(ScheduleData(controller.Store.Root)).All(p=>p.Label!=label) && form.IsPresetPickerOpen,"remove button deletes the preset from the list and storage immediately");
            hud.HandleEscape();Assert(!form.IsPresetOpen && modal.Visibility==Visibility.Visible,"Escape closes only the preset picker while retaining the schedule draft");
            form.ColorInput.Text=originalColor;form.LabelInput.Text=originalLabel;
            hud.HandleEscape();
        }
        hud.Opacity=previousOpacity;
        Assert(hud.HandleEscape() && !board.IsEditingDay && calendarHost.Visibility==Visibility.Visible,"Escape saves day and restores calendar");
        Assert(!hud.HandleEscape(),"calendar Escape falls through to existing overlay dismissal");
        bool ObserveTransition(Func<bool> predicate){for(int frame=0;frame<12;frame++){Pump(10);if(predicate())return true;}return false;}
        board.AnimateDayTransitions=true;board.OpenDay(today);
        Assert(ObserveTransition(()=>board.IsEditingDay && calendarHost.Opacity<1 && page.Opacity>0 && ((TranslateTransform)calendarHost.RenderTransform).X<0 && ((TranslateTransform)page.RenderTransform).X>0),"calendar exits left while planner fades in from right");
        hud.HandleEscape();
        Assert(ObserveTransition(()=>!board.IsEditingDay && ((TranslateTransform)calendarHost.RenderTransform).X<0 && ((TranslateTransform)page.RenderTransform).X>0),"reverse transition brings calendar back from left and sends day to right");
        Pump(300);Assert(page.Visibility==Visibility.Collapsed && calendarHost.Visibility==Visibility.Visible,"reversing in-progress animation completes with only calendar visible");
        board.AnimateDayTransitions=false;CheckCrossDatePlanner(controller,qa);CheckPlannerEditModes(controller,qa);CheckScheduleDragging(controller,qa);
    }

    private static void CheckDayDialGeometry(string qa)
    {
        var dial=new OverlayScheduleDial {TimeProvider=()=>new(2026,10,24,18,0,0)};dial.Measure(new Size(800,560));dial.Arrange(new Rect(0,0,800,560));
        Assert(dial.SlotAt(dial.PointAt(.2,dial.Radius*.8))==0 && dial.SlotAt(dial.PointAt(24.2,dial.Radius*.8))==24 && dial.SlotAt(dial.PointAt(48.2,dial.Radius*.8))==0,"24-hour geometry maps midnight up and noon down");
        Assert(!dial.BeginSelection(dial.PointAt(0,dial.Radius+20)) && !dial.BeginSelection(dial.Center),"outer circumference and ambiguous center cannot start a range");
        int count=0,start=0,duration=0;dial.RangeSelected+=(s,d)=>{count++;start=s;duration=d;};
        var point=dial.PointAt(10.2,dial.Radius*.7);dial.BeginSelection(point);dial.EndSelection(point+new Vector(1,0));Assert(count==0,"click and sub-threshold movement do not create accidental schedules");
        dial.BeginSelection(dial.PointAt(47.2,dial.Radius*.7));dial.MovePointer(dial.PointAt(.2,dial.Radius*.7));dial.EndSelection(dial.PointAt(1.2,dial.Radius*.7));
        Assert(start==1410 && duration==90,"clockwise drag follows the midnight arc");
        dial.BeginSelection(dial.PointAt(1.2,dial.Radius*.7));dial.MovePointer(dial.PointAt(.2,dial.Radius*.7));dial.EndSelection(dial.PointAt(47.2,dial.Radius*.7));
        Assert(start== -30 && duration==90,"counterclockwise drag retains its previous-day start instead of wrapping it into today");
        dial.BeginSelection(dial.PointAt(0.2,dial.Radius*.7));
        for(int i=1;i<48;i++)dial.MovePointer(dial.PointAt(i+.2,dial.Radius*.7));
        dial.EndSelection(dial.PointAt(47.2,dial.Radius*.7));Assert(start==0 && duration==1440,"full circuit selection spans exactly 24 hours");
        int before=count;dial.BeginSelection(point);dial.MovePointer(point+new Vector(20,0));dial.CancelSelection();dial.EndSelection(point);Assert(count==before,"capture cancellation discards pending range");
        var full=new DaySchedule {Name="하루",StartMinute=0,DurationMinutes=1440};var shortItem=new DaySchedule {Name="짧게",StartMinute=300,DurationMinutes=60};
        dial.SetSchedules([full,shortItem]);DaySchedule? clicked=null;dial.EditRequested+=s=>clicked=s;
        point=dial.PointAt(10.5,dial.Radius*.5);dial.BeginSelection(point);dial.EndSelection(point);
        Assert(clicked?.Id==shortItem.Id,"inner shorter sector is hit-tested above the long overlapping sector");
        clicked=null;point=dial.PointAt(10.5,dial.Radius*.85);dial.BeginSelection(point);dial.EndSelection(point);Assert(clicked?.Id==full.Id,"outer part of long sector remains independently editable");
        clicked=null;before=count;
        dial.BeginSelection(dial.PointAt(10.5,dial.Radius*.95));dial.MovePointer(dial.PointAt(12.5,dial.Radius*.95));dial.EndSelection(dial.PointAt(14.5,dial.Radius*.95));
        Assert(count==before+1 && clicked==null && dial.LayoutSchedules().All(entry=>entry.Radius<=dial.Radius*.9),"outer ten percent remains available for new ranges even with a full-day schedule");
        BitmapSource Paint() {dial.UpdateLayout();var image=new RenderTargetBitmap(800,560,96,96,PixelFormats.Pbgra32);image.Render(dial);return image;}
        Color Pixel(BitmapSource image,Point at) {var bytes=new byte[4];image.CopyPixels(new Int32Rect((int)at.X,(int)at.Y,1,1),bytes,4,0);return Color.FromArgb(bytes[3],bytes[2],bytes[1],bytes[0]);}
        dial.MovePointer(dial.PointAt(0,dial.Radius+40));var idle=Paint();
        dial.MovePointer(dial.PointAt(12.5,dial.Radius*.95));var hovering=Paint();
        var guide=dial.PointAt(12,dial.Radius*.95);var fill=dial.PointAt(4,dial.Radius*.65);
        Assert(Pixel(idle,guide)!=Pixel(hovering,guide) && Pixel(idle,dial.PointAt(4,dial.Radius*.95))==Pixel(hovering,dial.PointAt(4,dial.Radius*.95)),"creation hover outlines only its half-hour cell in the outer free ring");
        var expected=(Color)ColorConverter.ConvertFromString(full.Color);
        Assert(Pixel(idle,fill)==expected && Pixel(hovering,fill)==expected,"saved sector pixels stay fully opaque and cover underlying guides even on hover");
        var originalLabels=dial.LayoutCallouts();
        dial.MovePointer(dial.PointAt(14.5,dial.Radius*.5));Pump(180);var sectorHover=Paint();
        var outerEdge=dial.PointAt(4,dial.LayoutSchedules().Single(entry=>entry.Item.Id==full.Id).Radius);
        Assert(Pixel(sectorHover,fill)==expected && Pixel(sectorHover,outerEdge)!=Pixel(idle,outerEdge),"sector hover preserves opaque fill and gently highlights only its outline");
        int ContrastChange(BitmapSource beforeImage,BitmapSource afterImage,Point center,int radius=4) {
            int maximum=0;for(int y=(int)center.Y-radius;y<=(int)center.Y+radius;y++)for(int x=(int)center.X-radius;x<=(int)center.X+radius;x++) {
                var a=Pixel(beforeImage,new Point(x,y));var b=Pixel(afterImage,new Point(x,y));
                maximum=System.Math.Max(maximum,System.Math.Abs(a.R-b.R)+System.Math.Abs(a.G-b.G)+System.Math.Abs(a.B-b.B));
            }return maximum;
        }
                var top=dial.PointAt(0,dial.LayoutSchedules().Single(entry=>entry.Item.Id==full.Id).Radius)-new Vector(0,2);
        var shadow=dial.PointAt(24,dial.LayoutSchedules().Single(entry=>entry.Item.Id==full.Id).Radius)+new Vector(0,3);
        Assert(Pixel(idle,top)!=Pixel(sectorHover,top) && Pixel(idle,shadow)!=Pixel(sectorHover,shadow),"schedule hover gently lifts its face and adds a small shadow");
        Assert(ReferenceEquals(originalLabels,dial.LayoutCallouts()),"hover elevation leaves label positions and connector geometry unchanged");
        Assert(Pixel(sectorHover,dial.PointAt(0,dial.Radius*.5))==expected,"full-day hover remains a circle without a radial seam through its center");
        var fullBadge=dial.LayoutCallouts().Single(label=>label.Item.Id==full.Id).Bounds;
        var badgeFill=new Point(fullBadge.Left+4,fullBadge.Top+fullBadge.Height/2);
        dial.MovePointer(new Point(fullBadge.Left+fullBadge.Width/2,fullBadge.Top+fullBadge.Height/2));Pump(180);var nameHover=Paint();
        Assert(Pixel(nameHover,fill)==Pixel(sectorHover,fill) && Pixel(nameHover,badgeFill)==Pixel(sectorHover,badgeFill),"name hover and sector hover preserve the same sector fill and badge background");
        Assert(Pixel(nameHover,outerEdge)==Pixel(sectorHover,outerEdge) && Pixel(nameHover,badgeFill)==Pixel(idle,badgeFill),"name hover points to the same lifted sector without changing the badge background");

        dial.MovePointer(dial.PointAt(10.5,dial.Radius*.5));Pump(180);var overlapHover=Paint();
        var shortEdge=dial.PointAt(10.5,dial.LayoutSchedules().Single(entry=>entry.Item.Id==shortItem.Id).Radius);
        Assert(Pixel(overlapHover,fill)==expected && Pixel(overlapHover,outerEdge)==Pixel(idle,outerEdge) && Pixel(overlapHover,shortEdge)!=Pixel(idle,shortEdge),"overlap hover highlights only the topmost short sector outline");
        SavePlannerImage(dial,new Size(800,560),Path.Combine(qa,"schedule-outline-hover.png"));
        var liftedProbe=dial.PointAt(10.01,dial.Radius*.5)-new Vector(0,2);
        clicked=null;dial.MovePointer(liftedProbe);dial.BeginSelection(liftedProbe);dial.EndSelection(liftedProbe);
        Assert(clicked?.Id==shortItem.Id,"lifted sector retains stable pointer selection just outside its original edge");
        var badgeEdge=new Point(fullBadge.Left,fullBadge.Top+fullBadge.Height/2);
        Assert(Pixel(nameHover,badgeEdge)!=Pixel(idle,badgeEdge),"linked label highlights only its border");

        clicked=null;before=count;
        point=dial.PointAt(10.5,dial.Radius*.5);dial.BeginSelection(point);dial.MovePointer(dial.PointAt(11.5,dial.Radius*.5));dial.EndSelection(dial.PointAt(11.5,dial.Radius*.5));
        Assert(clicked==null && count==before,"dragging a saved sector neither creates a new range nor opens an accidental edit");

        dial.BeginSelection(dial.PointAt(12.5,dial.Radius*.95));dial.MovePointer(dial.PointAt(14.5,dial.Radius*.95));
        dial.MovePointer(dial.PointAt(14.5,dial.Radius+20));var outside=Paint();
        Assert(dial.IsSelecting && Pixel(outside,dial.PointAt(4,dial.Radius*.95))==Pixel(idle,dial.PointAt(4,dial.Radius*.95)),"radial grid stays absent outside the circle even during a captured drag");dial.CancelSelection();
        dial.SetSchedules([]);dial.MovePointer(dial.PointAt(5.5,dial.Radius*.7));var cellHover=Paint();
        SavePlannerImage(dial,new Size(800,560),Path.Combine(qa,"schedule-grid-hover.png"));
        dial.MovePointer(dial.PointAt(0,dial.Radius+40));var empty=Paint();
        Assert(Pixel(cellHover,dial.PointAt(5,dial.Radius*.7))!=Pixel(empty,dial.PointAt(5,dial.Radius*.7)),"empty half-hour hint disappears when the pointer leaves the diagram");
        dial.BeginSelection(dial.PointAt(39.2,dial.Radius*.8));dial.MovePointer(dial.PointAt(40.2,dial.Radius*.8));var creating=Paint();
        var background=((SolidColorBrush)Application.Current.FindResource("Input")).Color;
        bool AllBackground(BitmapSource bitmap) {
            for(int y=(int)dial.Center.Y-10;y<dial.Center.Y+10;y++)for(int x=(int)dial.Center.X+12;x<dial.Center.X+80;x++)
                if(Pixel(bitmap,new Point(x,y))!=background)return false;
            return true;
        }
        Assert(!AllBackground(empty) && AllBackground(creating),"empty-state creation hint disappears during a range drag");
        SavePlannerImage(dial,new Size(800,560),Path.Combine(qa,"schedule-creating-no-hint.png"));
        dial.CancelSelection();Assert(!AllBackground(Paint()),"cancelling an empty range restores the centered creation hint");
        dial.SetSchedules([full,shortItem]);
        foreach(double scale in new[]{.5,.85}) {
            dial.Width=800;dial.Height=560;
            var host=new System.Windows.Controls.Viewbox{Width=800*scale,Height=560*scale,Child=dial,Stretch=Stretch.Uniform};
            host.Measure(new Size(host.Width,host.Height));host.Arrange(new Rect(0,0,host.Width,host.Height));host.UpdateLayout();
            BitmapSource PaintScaled(){host.UpdateLayout();var image=new RenderTargetBitmap((int)host.Width,(int)host.Height,96,96,PixelFormats.Pbgra32);image.Render(host);return image;}
            dial.MovePointer(dial.PointAt(0,dial.Radius+40));Pump(180);var scaledIdle=PaintScaled();
            dial.MovePointer(dial.PointAt(10.5,dial.Radius*.5));Pump(180);var scaledHover=PaintScaled();
            var at=dial.TranslatePoint(shortEdge,host);
            Assert(ContrastChange(scaledIdle,scaledHover,at)>20,"schedule hover stays subtle and visible after viewport scaling at "+scale);
            SavePlannerImage(host,new Size(host.Width,host.Height),Path.Combine(qa,$"schedule-outline-hover-scaled-{scale}.png"));host.Child=null;
        }
        dial.Width=double.NaN;dial.Height=double.NaN;dial.Measure(new Size(800,560));dial.Arrange(new Rect(0,0,800,560));




    }

    private static void CheckDayEditorFill()
    {
        var entry=new CalendarDayEntry {Date=DateOnly.FromDateTime(DateTime.Now),Note=string.Join("\n\n",Enumerable.Repeat("기록 내용이 길어도 영역 안에서 스크롤할 수 있습니다.",100))};
        for(int i=0;i<60;i++)entry.Todos.Add(new(){Title=$"긴 목록의 할 일 {i}"});
        var editor=new OverlayDayEditor();editor.Configure(_=>CalendarNotes.Copy(entry),_=>true);editor.Open(entry.Date);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var preview=(Border)typeof(OverlayDayEditor).GetField("notePreview",flags)!.GetValue(editor)!;
        var note=(TextBox)typeof(OverlayDayEditor).GetField("note",flags)!.GetValue(editor)!;
        var tasks=(StackPanel)typeof(OverlayDayEditor).GetField("tasks",flags)!.GetValue(editor)!;
        foreach(var size in new[]{new Size(396,752),new Size(320,440)}) {
            editor.Measure(size);editor.Arrange(new Rect(size));editor.UpdateLayout();
            Rect Bounds(FrameworkElement element)=>element.TransformToAncestor(editor).TransformBounds(new Rect(element.RenderSize));
            var box=Bounds(preview);var title=VisualElements(editor).OfType<TextBlock>().First(text=>text.Text=="할 일");
            Assert(System.Math.Abs(box.Left-(size.Width-box.Right))<1 && System.Math.Abs((size.Height-box.Bottom)-Bounds(title).Top)<1 && box.Height>100,"record frame fills remaining height with symmetric outer padding at "+size);
            var scroll=(ScrollViewer)preview.Child;scroll.ScrollToBottom();editor.UpdateLayout();
            Assert(scroll.ScrollableHeight>0 && scroll.VerticalOffset>0,"long Markdown stays scrollable inside the record frame at "+size);
            double oldOffset=scroll.VerticalOffset;
            scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,0,120){RoutedEvent=Mouse.MouseWheelEvent});editor.UpdateLayout();
            Assert(scroll.VerticalOffset<oldOffset && scroll.Background==Brushes.Transparent,"record preview receives wheel input over its full area at "+size);

            var taskScroll=VisualElements(editor).OfType<ScrollViewer>().First(view=>ReferenceEquals(view.Content,tasks));taskScroll.ScrollToBottom();editor.UpdateLayout();
            Assert(taskScroll.ScrollableHeight>0 && taskScroll.VerticalOffset>0,"long todo list scrolls without pushing records below the panel at "+size);
            Assert(System.Math.Abs(tasks.Children.OfType<Border>().First().ActualWidth-editor.TodoInput.ActualWidth)<1,"todo rows and entry field have equal width with a visible scrollbar at "+size);

            editor.BeginNoteEdit();editor.UpdateLayout();note.ScrollToEnd();editor.UpdateLayout();
            Assert(Bounds(note)==box && note.VerticalOffset>0,"plain-text editing keeps the same full-height record frame and scrolling at "+size);
            editor.FinishNoteEdit(false);editor.UpdateLayout();
        }
    }

    private static void CheckScheduleCallouts(string qa)
    {
        var dial=new OverlayScheduleDial();
        var groups=new[] {
            new[] {new DaySchedule {Name="집중 작업",StartMinute=540,DurationMinutes=240,Color="#91BFFF"},
                new DaySchedule {Name="잠깐 휴식",StartMinute=600,DurationMinutes=30,Color="#D2ACF0"},
                new DaySchedule {Name="저녁 산책",StartMinute=1080,DurationMinutes=60},
                new DaySchedule {Name="자정을 넘는 긴 일정 이름도 상자 안에서 보입니다",StartMinute=1410,DurationMinutes=120}},
            Enumerable.Range(0,14).Select(i=>new DaySchedule {Name=$"같은 시간의 일정 {i+1}",StartMinute=540,DurationMinutes=30}).ToArray(),
            new[] {new DaySchedule {Name="자정부터 다음 날까지",StartMinute=1410,DurationMinutes=1440}}
        };
        for(int group=0;group<groups.Length;group++) {
            dial.SetSchedules(groups[group]);dial.Measure(new Size(1140,688));dial.Arrange(new Rect(0,0,1140,688));dial.UpdateLayout();
            var labels=dial.LayoutCallouts();
            Assert(labels.Count==groups[group].Length,"all labels fit for mixed dense and full-day schedules: "+group);
            Assert(ReferenceEquals(labels,dial.LayoutCallouts()),"pointer rendering reuses cached callout geometry: "+group);
            foreach(var label in labels) {
                var box=label.Bounds;var nearest=new Point(System.Math.Clamp(dial.Center.X,box.Left,box.Right),System.Math.Clamp(dial.Center.Y,box.Top,box.Bottom));
                Assert(box.Left>=12 && box.Top>=12 && box.Right<=dial.ActualWidth-12 && box.Bottom<=dial.ActualHeight-12 && (nearest-dial.Center).Length>dial.Radius+40,"name badge stays inside viewport and outside clock face and ticks");
                Assert(!labels.Any(other=>!ReferenceEquals(other,label) && other.Bounds.IntersectsWith(box)),"name badges never overlap each other");
                double offset=(label.AnchorSlot-label.Item.StartMinute/30d+96)%48;
                Assert(offset<=label.Item.DurationMinutes/30d && (label.Anchor-dial.Center).Length<dial.Radius*.9,"leader originates inside its own schedule sector");
            }
            if(group==0) {
                Assert(labels.Any(label=>label.Bounds.Left>dial.Center.X) && labels.Any(label=>label.Bounds.Right<dial.Center.X),"nearby labels use both sides of the diagram");
                var longLabel=labels.Single(label=>label.Item.StartMinute==1410);
                var shortLabel=labels.Single(label=>label.Item.Name=="집중 작업");
                Assert(longLabel.IsTrimmed && !shortLabel.IsTrimmed,"tooltip eligibility follows measured label overflow");
                Point Middle(Rect box)=>new(box.X+box.Width/2,box.Y+box.Height/2);
                dial.MovePointer(Middle(longLabel.Bounds));
                Assert(dial.ToolTip is ToolTip tip && (string)tip.Content==longLabel.NameText.Text && tip.PlacementRectangle==longLabel.Bounds,"ellipsized label exposes its full text beside the badge");
                dial.BeginSelection(Middle(longLabel.Bounds));
                Assert(dial.ToolTip==null,"pressing a label dismisses its tooltip before editing");dial.CancelSelection();
                dial.MovePointer(Middle(longLabel.Bounds));dial.MovePointer(Middle(shortLabel.Bounds));
                Assert(dial.ToolTip==null,"untrimmed label has no tooltip");
                dial.MovePointer(Middle(longLabel.Bounds));dial.MovePointer(dial.PointAt(20,dial.Radius*.6));
                Assert(dial.ToolTip==null,"sector hover does not retain a label tooltip");
            }
            if(group==2)Assert(System.Math.Abs(labels[0].AnchorSlot-(groups[group][0].StartMinute/30d+24))>1,"connector angle is chosen freely instead of forcing the sector midpoint");
            SavePlannerImage(dial,new Size(1140,688),Path.Combine(qa,$"schedule-callouts-{group}.png"));
            if(group==0) {
                var badge=labels[0].Bounds;dial.MovePointer(new Point(badge.X+badge.Width/2,badge.Y+badge.Height/2));dial.UpdateLayout();
                SavePlannerImage(dial,new Size(1140,688),Path.Combine(qa,"schedule-name-hover.png"));
            }

        }
        var before=dial.LayoutCallouts();dial.Measure(new Size(1000,650));dial.Arrange(new Rect(0,0,1000,650));dial.UpdateLayout();
        Assert(!ReferenceEquals(before,dial.LayoutCallouts()),"resizing recalculates label positions");
        string firstLine="이름 없이 작성한 내용의 첫 줄도 길면 전체 레이블을 툴팁으로 확인할 수 있습니다";
        dial.SetSchedules([new() {Name="",Content="\r\n  "+firstLine+"\r\n둘째 줄은 레이블에 포함하지 않습니다",StartMinute=540,DurationMinutes=60}]);
        var noteLabel=dial.LayoutCallouts().Single();dial.MovePointer(new(noteLabel.Bounds.X+8,noteLabel.Bounds.Y+8));
        Assert(noteLabel.IsTrimmed && dial.ToolTip is ToolTip noteTip && (string)noteTip.Content==firstLine,"content-only tooltip contains the complete first label line without later note lines");
        dial.SetSchedules([]);Assert(dial.ToolTip==null,"changing date data clears any label tooltip");
    }

    private static void CheckScheduleNativeClicks(Controller controller)
    {
        using var host=new KeyboardHost();host.Window.Show();Pump(150);
        var original=System.Windows.Forms.Cursor.Position;
        var overlay=controller.Overlay;var hud=(OverlayTabs)((Grid)overlay.Content).Children[2];var board=ScheduleBoardFor(hud,controller.Store.Root);
        var dial=(OverlayScheduleDial)board.FindName("ScheduleDial");var editor=(OverlayDayEditor)board.FindName("DayEditor");
        var modal=(Grid)board.FindName("ScheduleModal");var form=(OverlayScheduleEditor)board.FindName("ScheduleEditor");
        dial.TimeProvider=()=>new(2026,10,24,18,0,0);
        var schedule=new DaySchedule {Name="실제 클릭과 말줄임표 레이블의 전체 텍스트 툴팁을 검증하기 위한 긴 일정 이름",StartMinute=540,DurationMinutes=120};
        board.Records.Commit(data=> {data.Settings.OverlayShowMilliseconds=0;data.Settings.OverlayHideMilliseconds=0;
            CalendarNotes.Save(data,new(){Date=DateOnly.FromDateTime(DateTime.Now),Schedules=[schedule],Todos=[new(){Title="바깥 클릭 전달 보호"}]},DateTime.Now);});
        hud.Connect(controller);((TabControl)hud.FindName("Tabs")).SelectedItem=hud.FindName("DashboardTab");board.AnimateDayTransitions=false;
        try {
            controller.ToggleOverlay();AwaitBackdrop(()=>overlay.IsVisible);Pump(500);board.OpenDay(DateOnly.FromDateTime(DateTime.Now));overlay.UpdateLayout();Pump(100);
            Assert(overlay.IsActive,"native schedule click test owns the foreground overlay");
            var tooltipBadge=dial.LayoutCallouts().Single().Bounds;
            var tooltipPoint=dial.PointToScreen(new(tooltipBadge.X+tooltipBadge.Width/2,tooltipBadge.Y+tooltipBadge.Height/2));
            System.Windows.Forms.Cursor.Position=new((int)tooltipPoint.X,(int)tooltipPoint.Y);Pump(200);
            Assert(dial.ToolTip is ToolTip nativeTip && nativeTip.IsOpen && PresentationSource.FromVisual(nativeTip)!=null && (string)nativeTip.Content==schedule.Name &&
                ((SolidColorBrush)nativeTip.Background).Color==((SolidColorBrush)Application.Current.FindResource("Panel")).Color &&
                ((SolidColorBrush)nativeTip.Foreground).Color==((SolidColorBrush)Application.Current.FindResource("Ink")).Color,"native ellipsized badge hover immediately opens the full text tooltip in the dark theme");
            var openTip=(ToolTip)dial.ToolTip;
            var away=dial.PointToScreen(dial.Center);System.Windows.Forms.Cursor.Position=new((int)away.X,(int)away.Y);Pump(80);
            Assert(!openTip.IsOpen && dial.ToolTip==null,"leaving the badge closes the native tooltip");
            System.Windows.Forms.Cursor.Position=new((int)tooltipPoint.X,(int)tooltipPoint.Y);Pump(200);
            Assert(dial.ToolTip is ToolTip repeatTip && repeatTip.IsOpen,"returning from the dial face to the same label immediately reopens its tooltip");
            System.Windows.Forms.Cursor.Position=new((int)away.X,(int)away.Y);Pump(80);
            var secondSchedule=new DaySchedule {Name="다른 레이블로 바로 이동했을 때도 새 전체 텍스트가 표시되는지 확인하는 긴 이름",StartMinute=840,DurationMinutes=60};
            dial.SetSchedules([schedule,secondSchedule]);overlay.UpdateLayout();
            foreach(var target in new[]{schedule,secondSchedule,schedule}) {
                var badge=dial.LayoutCallouts().Single(label=>label.Item.Id==target.Id).Bounds;
                var screen=dial.PointToScreen(new(badge.X+badge.Width/2,badge.Y+badge.Height/2));
                System.Windows.Forms.Cursor.Position=new((int)screen.X,(int)screen.Y);Pump(200);
                Assert(dial.ToolTip is ToolTip switchedTip && switchedTip.IsOpen && (string)switchedTip.Content==target.Name,"native movement between drawn badges shows the matching full text without leaving the dial");
            }
            var switchedOpenTip=(ToolTip)dial.ToolTip;dial.SetSchedules(editor.Schedules);
            Assert(!switchedOpenTip.IsOpen,"replacing schedule data dismisses the visible native tooltip");
            void Click(Point local,string source) {
                var point=dial.PointToScreen(local);
                System.Windows.Forms.Cursor.Position=new((int)System.Math.Round(point.X),(int)System.Math.Round(point.Y));Pump(100);
                Assert(ReferenceEquals(overlay.InputHitTest(overlay.PointFromScreen(point)),dial) && dial.Cursor==Cursors.Hand,"native pointer reaches the clickable "+source);
                mouse_event(0x0002,0,0,0,0);Pump(30);mouse_event(0x0004,0,0,0,0);Pump(120);
                Assert(modal.Visibility==Visibility.Visible && form.NameInput.Text==schedule.Name,"native "+source+" click opens the matching edit modal");form.Close();Pump(80);
            }
            BitmapSource RenderDial(){dial.UpdateLayout();var image=new RenderTargetBitmap((int)dial.ActualWidth,(int)dial.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(dial);return image;}
            System.Windows.Forms.Cursor.Position=new((int)overlay.Left+8,(int)overlay.Top+8);Pump(100);var beforeHover=RenderDial();
            var hoverPoint=dial.PointToScreen(dial.PointAt(20,dial.Radius*.6));System.Windows.Forms.Cursor.Position=new((int)hoverPoint.X,(int)hoverPoint.Y);Pump(150);var afterHover=RenderDial();
            var actualHover=(DaySchedule?)typeof(OverlayScheduleDial).GetField("hoveredSchedule",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(dial);
            Assert(actualHover?.Id==schedule.Id,"actual native mouse move resolves the saved sector hover");
            var edge=dial.PointAt(20,dial.LayoutSchedules().Single().Radius);var pixelBefore=new byte[4];var pixelAfter=new byte[4];int maximum=0;
            for(int y=(int)edge.Y-4;y<=(int)edge.Y+4;y++)for(int x=(int)edge.X-4;x<=(int)edge.X+4;x++) {
                var rect=new Int32Rect(x,y,1,1);beforeHover.CopyPixels(rect,pixelBefore,4,0);afterHover.CopyPixels(rect,pixelAfter,4,0);
                maximum=System.Math.Max(maximum,System.Math.Abs(pixelBefore[0]-pixelAfter[0])+System.Math.Abs(pixelBefore[1]-pixelAfter[1])+System.Math.Abs(pixelBefore[2]-pixelAfter[2]));
            }
            Assert(maximum>20,"actual native hover presents a subtle outline and elevation on the active overlay");
            var cellPoint=dial.PointToScreen(dial.PointAt(20.5,dial.Radius*.97));System.Windows.Forms.Cursor.Position=new((int)cellPoint.X,(int)cellPoint.Y);Pump(180);var cellImage=RenderDial();
            var cellEdge=dial.PointAt(20,dial.Radius*.96);maximum=0;
            for(int y=(int)cellEdge.Y-3;y<=(int)cellEdge.Y+3;y++)for(int x=(int)cellEdge.X-3;x<=(int)cellEdge.X+3;x++) {
                var rect=new Int32Rect(x,y,1,1);beforeHover.CopyPixels(rect,pixelBefore,4,0);cellImage.CopyPixels(rect,pixelAfter,4,0);
                maximum=System.Math.Max(maximum,System.Math.Abs(pixelBefore[0]-pixelAfter[0])+System.Math.Abs(pixelBefore[1]-pixelAfter[1])+System.Math.Abs(pixelBefore[2]-pixelAfter[2]));
            }
            Assert(dial.Cursor==Cursors.Cross && maximum>20,"actual native pointer shows the half-hour creation-cell guide in the free ring");
            var box=dial.LayoutCallouts().Single(label=>label.Item.Id==schedule.Id).Bounds;
            Click(new Point(box.X+box.Width/2,box.Y+box.Height/2),"name label");
            Click(dial.PointAt(20,dial.Radius*.6),"saved sector");
            board.ShowSchedule(editor.Schedules.Single(),true);overlay.UpdateLayout();
            var originalForm=form.TransformToAncestor(hud).TransformBounds(new Rect(form.RenderSize));
            void ClickElement(FrameworkElement target) {
                var screen=target.PointToScreen(new Point(target.ActualWidth/2,target.ActualHeight/2));
                System.Windows.Forms.Cursor.Position=new((int)Math.Round(screen.X),(int)Math.Round(screen.Y));Pump(100);
                mouse_event(0x0002,0,0,0,0);Pump(30);mouse_event(0x0004,0,0,0,0);Pump(120);
            }
            ClickElement((FrameworkElement)form.CategoryButton);
            Assert(form.IsPresetPickerOpen && form.PresetPicker.IsKeyboardFocusWithin && overlay.IsActive && form.TransformToAncestor(hud).TransformBounds(new Rect(form.RenderSize))==originalForm,"native color/label button click opens the preset picker without dismissing or resizing the schedule modal");
            ClickElement(form.PresetPicker.AddButton);
            Assert(form.IsPresetDialogOpen && form.PresetDialog.IsKeyboardFocusWithin,"native new preset click opens the focused preset dialog");
            ClickElement(form.PresetDialog.ColorInput);
            Assert(form.IsColorPickerOpen,"native color box click opens the color picker inside the preset dialog");
            string beforeColor=form.PresetDialog.ColorInput.Text;
            ClickElement(form.ColorPicker.HueInput);
            Assert(form.PresetDialog.ColorInput.Text!=beforeColor,"native hue-strip click selects its visible color without a default track covering the rainbow");
            beforeColor=form.PresetDialog.ColorInput.Text;
            ClickElement(form.ColorPicker.ColorArea);
            Assert(form.PresetDialog.ColorInput.Text!=beforeColor,"native saturation and brightness click immediately updates the preset color");
            string chosenColor=form.PresetDialog.ColorInput.Text;Chord(0x1B);
            Assert(!form.IsColorPickerOpen && form.IsPresetDialogOpen && form.PresetDialog.ColorInput.Text==chosenColor,"native Escape closes the color picker and retains its selected color");
            form.PresetDialog.LabelInput.Focus();form.PresetDialog.LabelInput.Text="클릭 프리셋";
            ClickElement(form.PresetDialog.ConfirmButton);
            Assert(!form.IsPresetOpen && modal.Visibility==Visibility.Visible && form.ColorInput.Text==chosenColor && form.LabelInput.Text=="클릭 프리셋","native confirm applies the new preset to the schedule draft");
            ClickElement((FrameworkElement)form.CategoryButton);
            System.Windows.Forms.Cursor.Position=new((int)overlay.Left+8,(int)overlay.Top+8);Pump(100);
            mouse_event(0x0002,0,0,0,0);Pump(30);mouse_event(0x0004,0,0,0,0);Pump(120);
            Assert(!form.IsPresetOpen && modal.Visibility==Visibility.Visible && form.ColorInput.Text==chosenColor,"native outside click dismisses only the preset picker without discarding the schedule selection");
            ClickElement(form.TimeRangeButton);
            Assert(form.IsTimePickerOpen && form.TimePicker.IsKeyboardFocusWithin && overlay.IsActive && form.TransformToAncestor(hud).TransformBounds(new Rect(form.RenderSize))==originalForm,"native date/time button opens an independently focused picker without resizing its owner");
            var chosen=DateOnly.FromDateTime(DateTime.Now).AddDays(5);form.TimePicker.Calendar.SetDate(chosen);overlay.UpdateLayout();
            var dayButton=VisualElements(form.TimePicker.Calendar).OfType<Button>().Single(button=>System.Windows.Automation.AutomationProperties.GetName(button)==chosen.ToString("yyyy년 M월 d일"));
            ClickElement(dayButton);
            Assert(form.TimePicker.StartDate==chosen && System.Windows.Automation.AutomationProperties.GetName(form.TimeRangeButton).Contains(chosen.ToString("yyyy년 M월 d일")),"native calendar click immediately reflects its date in the schedule draft");
            Chord(0x1B);
            Assert(!form.IsTimePickerOpen && modal.Visibility==Visibility.Visible && form.TimePicker.StartDate==chosen && overlay.IsVisible,"native Escape closes the detached picker while retaining changes and its owner");
            ClickElement(form.TimeRangeButton);
            System.Windows.Forms.Cursor.Position=new((int)overlay.Left+8,(int)overlay.Top+8);Pump(100);
            mouse_event(0x0002,0,0,0,0);Pump(30);mouse_event(0x0004,0,0,0,0);Pump(120);
            Assert(!form.IsTimePickerOpen && modal.Visibility==Visibility.Visible && form.TimePicker.StartDate==chosen,"native outside click dismisses only the date/time picker without passing through");
            form.NameInput.Text="바깥 클릭 자동 저장";form.ContentInput.Text="실제 클릭으로 저장된 내용";
            var today=DateOnly.FromDateTime(DateTime.Now);form.TimePicker.SetRange(new(today,540,today,660));
            var todoRow=((StackPanel)typeof(OverlayDayEditor).GetField("tasks",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!).Children.OfType<Border>().Single();
            var outsideForm=todoRow.PointToScreen(new Point(todoRow.ActualWidth/2,todoRow.ActualHeight/2));
            System.Windows.Forms.Cursor.Position=new((int)Math.Round(outsideForm.X),(int)Math.Round(outsideForm.Y));Pump(100);
            Assert(ReferenceEquals(overlay.InputHitTest(overlay.PointFromScreen(outsideForm)),modal),"native outside click reaches the scrim inside the planner viewport");
            mouse_event(0x0002,0,0,0,0);Pump(30);mouse_event(0x0004,0,0,0,0);Pump(120);
            var saved=CalendarNotes.Get(ScheduleData(controller.Store.Root),today).Schedules.Single();
            Assert(modal.Visibility==Visibility.Collapsed && board.IsEditingDay && overlay.IsVisible && saved.Name=="바깥 클릭 자동 저장" && saved.Content=="실제 클릭으로 저장된 내용" && saved.Color=="#D2ACF0","native outside click persists the schedule draft and chosen color before dismissing the modal");
            Assert(!CalendarNotes.Get(ScheduleData(controller.Store.Root),DateOnly.FromDateTime(DateTime.Now)).Todos.Single().Done && !hud.IsMouseCaptured,"dismissal consumes mouse release instead of toggling a todo behind the modal and releases capture");
            board.ShowSchedule(editor.Schedules.Single(),true);form.NameInput.Text="실제 Esc 자동 저장";form.ContentInput.Text="키보드로 저장한 내용";Pump(80);Chord(0x1B);
            var escapeSaved=CalendarNotes.Get(ScheduleData(controller.Store.Root),today).Schedules.Single();
            Assert(modal.Visibility==Visibility.Collapsed && board.IsEditingDay && overlay.IsVisible && escapeSaved.Name=="실제 Esc 자동 저장" && escapeSaved.Content=="키보드로 저장한 내용","native Escape saves schedule changes and pops only the schedule modal");
            board.ShowSchedule(editor.Schedules.Single(),true);form.ColorInput.Text="#bad";Pump(80);Chord(0x1B);
            Assert(modal.Visibility==Visibility.Visible && board.IsEditingDay && form.ColorInput.Text=="#bad" && overlay.IsVisible,"native Escape with invalid input retains the schedule modal and draft");
            form.ColorInput.Text=escapeSaved.Color;Chord(0x1B);
            Assert(modal.Visibility==Visibility.Collapsed && board.IsEditingDay,"correcting invalid input allows the next Escape to save and close");
            string beforeEmpty=ScheduleRecords.Serialize(board.Records.Data);
            foreach(bool escape in new[]{true,false}) {
                board.ShowSchedule(new(){StartMinute=60,DurationMinutes=30},false);
                form.ColorInput.Text="#91BFFF";form.LabelInput.Text="바뀐 레이블";form.TimePicker.SetRange(new(today.AddDays(1),600,today.AddDays(2),660));Pump(80);
                if(escape)Chord(0x1B);
                else {
                    System.Windows.Forms.Cursor.Position=new((int)Math.Round(outsideForm.X),(int)Math.Round(outsideForm.Y));Pump(100);
                    mouse_event(0x0002,0,0,0,0);Pump(30);mouse_event(0x0004,0,0,0,0);Pump(120);
                }
                Assert(modal.Visibility==Visibility.Collapsed && board.IsEditingDay && overlay.IsVisible && !hud.IsMouseCaptured && ScheduleRecords.Serialize(board.Records.Data)==beforeEmpty,"native "+(escape?"Escape":"outside click")+" dismisses a text-empty new schedule after color label and date changes without saving or clicking through");
            }
            foreach(bool escape in new[]{true,false}) {
                string content="내용만 작성한 실제 "+(escape?"Esc":"바깥 클릭");
                board.ShowSchedule(new(){StartMinute=600,DurationMinutes=30},false);form.ContentInput.Text=content;Pump(80);
                if(escape)Chord(0x1B);
                else {
                    System.Windows.Forms.Cursor.Position=new((int)Math.Round(outsideForm.X),(int)Math.Round(outsideForm.Y));Pump(100);
                    mouse_event(0x0002,0,0,0,0);Pump(30);mouse_event(0x0004,0,0,0,0);Pump(120);
                }
                var contentOnly=CalendarNotes.Get(ScheduleData(controller.Store.Root),today).Schedules.Single(item=>item.Content==content);
                Assert(modal.Visibility==Visibility.Collapsed && board.IsEditingDay && overlay.IsVisible && contentOnly.Name=="","native "+(escape?"Escape":"outside click")+" persists a content-only new schedule without a name");
                board.ShowSchedule(contentOnly,true);Assert(form.NameInput.Text=="" && form.ContentInput.Text==content && form.Submit(true),"native content-only schedule reopens and deletes through the original identity");
            }
            var back=(Button)board.FindName("BackToCalendar");
            Assert(!back.Focusable && !back.IsTabStop && !back.Focus() && !back.IsKeyboardFocused,"native back button cannot receive focus after modal dismissal");
            ((TabItem)hud.FindName("DashboardTab")).Focus();Chord(0x09);
            Assert(editor.TodoInput.IsKeyboardFocused,"native Tab moves directly from management tab to todo input");
            ClickElement(back);
            Assert(!board.IsEditingDay && overlay.IsVisible && !back.IsKeyboardFocused,"unfocusable back button still returns to the calendar on native click");
            board.OpenDay(DateOnly.FromDateTime(DateTime.Now));Pump(80);
            Assert(!dial.Focusable && !dial.Focus(),"native dial cannot receive keyboard focus");
            var note=(TextBox)typeof(OverlayDayEditor).GetField("note",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!;
            editor.BeginNoteEdit();note.Text="실제 Esc 모드 확인";Pump(50);Chord(0x1B);
            Assert(overlay.IsVisible && board.IsEditingDay && !editor.IsNoteEditing,"first native Escape pops record input only and keeps the day screen");
            Chord(0x1B);Assert(overlay.IsVisible && !board.IsEditingDay,"second native Escape pops day mode back to the calendar");

        }
        finally {
            mouse_event(0x0004,0,0,0,0);form.Close();controller.HideOverlay();Pump(100);System.Windows.Forms.Cursor.Position=original;
        }
    }

    private static void SavePlannerImage(Visual visual,Size size,string path)
    {
        var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);
    }
}
