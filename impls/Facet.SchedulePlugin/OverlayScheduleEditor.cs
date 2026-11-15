using System.Windows.Automation;
using System.Windows.Input;

namespace Facet.SchedulePlugin;

internal sealed class OverlayScheduleEditor : Border
{
    internal readonly TextBox NameInput=new(){MaxLength=200};
    internal readonly TextBox ColorInput=new(){MaxLength=7},LabelInput=new(){MaxLength=40};
    internal readonly TextBox ContentInput=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxLength=20000,Height=140,
        VerticalContentAlignment=VerticalAlignment.Top,HorizontalContentAlignment=HorizontalAlignment.Left,TextAlignment=TextAlignment.Left,
        VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    internal OverlayScheduleTimePicker TimePicker {get;}=new() {Visibility=Visibility.Collapsed};
    // ColorInput/LabelInput hold the draft color/label; they are chosen only through presets and shown on the category button.
    internal OverlaySchedulePresetPicker PresetPicker {get;}=new() {Visibility=Visibility.Collapsed};
    internal OverlaySchedulePresetDialog PresetDialog {get;}=new() {Visibility=Visibility.Collapsed};
    internal OverlayScheduleColorPicker ColorPicker=>PresetDialog.ColorPicker;
    internal bool IsColorPickerOpen=>IsPresetDialogOpen && PresetDialog.IsColorPickerOpen;
    internal bool IsPresetPickerOpen {get;private set;}
    internal bool IsPresetDialogOpen {get;private set;}
    internal bool IsPresetOpen=>IsPresetPickerOpen || IsPresetDialogOpen;
    internal FrameworkElement CategoryButton=>categoryButton;
    internal Func<IReadOnlyList<SchedulePreset>>? ReadPresets;
    internal Func<SchedulePreset,bool,bool>? WritePreset;
    internal ComboBox StartInput=>TimePicker.StartInput;
    internal ComboBox EndInput=>TimePicker.EndInput;
    internal bool IsTimePickerOpen {get;private set;}
    internal FrameworkElement TimeRangeButton=>rangeButton;
    private readonly TextBlock heading=new(){FontSize=22,FontWeight=FontWeights.SemiBold};
    private readonly TextBlock error=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,0),Visibility=Visibility.Collapsed};
    private readonly TextBlock rangeText=new(){TextWrapping=TextWrapping.Wrap,FontSize=13,VerticalAlignment=VerticalAlignment.Center};
    private readonly Border colorPreview=new(){Width=18,Height=18,CornerRadius=new(4),BorderThickness=new(1),Margin=new(0,0,10,0),VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBlock categoryText=new(){FontSize=13,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
    private readonly Button categoryButton=new(){Margin=new(0),ContentTemplate=null,HorizontalContentAlignment=HorizontalAlignment.Left,VerticalContentAlignment=VerticalAlignment.Center};
    private readonly Button rangeButton=new(){Margin=new(0),ContentTemplate=null,HorizontalContentAlignment=HorizontalAlignment.Left,VerticalContentAlignment=VerticalAlignment.Center};
    private readonly Button delete=new(){Content=new Icon{Kind=IconKind.Trash,Width=18,Height=18},ContentTemplate=null,
        Width=32,Height=32,Padding=new(7),BorderThickness=new(0),Background=Brushes.Transparent,ToolTip="일정 삭제",Margin=new(0),HorizontalAlignment=HorizontalAlignment.Left};
    private readonly Grid layout=new();
    private DaySchedule draft=new();
    private bool editingExisting;
    private DateOnly referenceDate;
    private Func<DaySchedule,bool,bool>? commit;
    internal event Action? Closed;
    internal event Action? TimePickerOpened,TimePickerClosed;
    internal event Action? PresetOpened,PresetClosed;

    public OverlayScheduleEditor()
    {
        Width=740d*2/3;MaxHeight=690;Padding=new(28);CornerRadius=new(20);BorderThickness=new(1);
        Background=Brush("Panel");BorderBrush=Brush("Edge");error.Foreground=Brush("Danger");KeyboardNavigation.SetTabNavigation(this,KeyboardNavigationMode.Cycle);
        rangeButton.SetResourceReference(StyleProperty,"ButtonWithoutFocusOutline");rangeButton.BorderBrush=Brushes.Transparent;
        categoryButton.SetResourceReference(StyleProperty,"ButtonWithoutFocusOutline");categoryButton.BorderBrush=Brushes.Transparent;
        foreach(var input in new[]{NameInput,ContentInput}) {
            input.SetResourceReference(StyleProperty,"TextBoxWithoutFocusOutline");input.Margin=new(0);
        }
        var body=new StackPanel();
        StackPanel Field(string label,UIElement input) {
            var group=new StackPanel {Margin=new(0,0,0,24)};
            group.Children.Add(new TextBlock{Text=label,Margin=new(0,0,0,10),Foreground=Brush("Muted")});
            group.Children.Add(input);body.Children.Add(group);AutomationProperties.SetName(input,label);return group;
        }
        Field("이름",NameInput);
        var rangeLabel=new Grid();rangeLabel.ColumnDefinitions.Add(new(){Width=GridLength.Auto});rangeLabel.ColumnDefinitions.Add(new());rangeLabel.Children.Add(new Icon{Kind=IconKind.Calendar,Width=18,Height=18,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,10,0)});Grid.SetColumn(rangeText,1);rangeLabel.Children.Add(rangeText);rangeButton.Content=rangeLabel;
        Field("시작 · 끝 날짜와 시간",rangeButton);rangeButton.Click+=(_,_)=>OpenTimePicker();
        var category=new DockPanel();DockPanel.SetDock(colorPreview,Dock.Left);category.Children.Add(colorPreview);category.Children.Add(categoryText);
        categoryButton.Content=category;colorPreview.BorderBrush=Brush("Edge");
        Field("색상 · 레이블",categoryButton);categoryButton.Click+=(_,_)=>OpenPresetPicker();
        ColorInput.TextChanged+=(_,_)=>UpdateCategoryText();LabelInput.TextChanged+=(_,_)=>UpdateCategoryText();
        PresetPicker.PresetSelected+=preset=>{ColorInput.Text=preset.Color;LabelInput.Text=preset.Label;ClosePresetPicker();};
        PresetPicker.PresetRemoved+=preset=>{if(WritePreset?.Invoke(preset,true)==true)PresetPicker.Remove(preset);};
        PresetPicker.AddRequested+=OpenPresetDialog;
        PresetDialog.Canceled+=ClosePresetDialog;
        PresetDialog.Confirm=preset=> {
            if(WritePreset?.Invoke(preset,false)!=true)return false;
            ColorInput.Text=preset.Color;LabelInput.Text=preset.Label;ClosePresetPicker();return true;
        };
        Field("내용",ContentInput).Margin=new(0);body.Children.Add(error);
        delete.Margin=new(0,12,0,0);delete.Click+=(_,_)=>Submit(true);
        delete.SetResourceReference(StyleProperty,"ButtonWithoutFocusOutline");delete.BorderBrush=Brushes.Transparent;
        AutomationProperties.SetName(delete,"일정 삭제");
        layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        heading.Margin=new(0,0,0,16);layout.Children.Add(heading);
        var scroll=new ScrollViewer{MaxHeight=480,Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);layout.Children.Add(scroll);
        Grid.SetRow(delete,2);layout.Children.Add(delete);
        Child=layout;
        TimePicker.RangeChanged+=UpdateRangeText;
        TimePicker.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;HandleEscape();}else if(e.Key==Key.S && Keyboard.Modifiers==ModifierKeys.Control){e.Handled=true;CloseTimePicker();}};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;HandleEscape();}else if(e.Key==Key.S && Keyboard.Modifiers==ModifierKeys.Control){e.Handled=true;Submit();}};
    }
    internal void Open(DaySchedule item,bool existing,Func<DaySchedule,bool,bool> write,DateOnly? date=null)
    {
        ClosePresetPicker();
        if(IsTimePickerOpen)CloseTimePicker();
        referenceDate=date??DateOnly.FromDateTime(DateTime.Now);draft=item.Copy();editingExisting=existing;commit=write;heading.Text=existing?"일정 수정":"새 일정";
        delete.Visibility=existing?Visibility.Visible:Visibility.Collapsed;
        var start=referenceDate.AddDays(item.StartDayOffset);
        long end=(long)start.DayNumber*1440+item.StartMinute+item.DurationMinutes;
        int endDay=(int)Math.Min(end/1440,DateOnly.MaxValue.DayNumber);
        var endDate=DateOnly.FromDayNumber(endDay);int endMinute=(int)(end-(long)endDay*1440);
        TimePicker.SetRange(new(start,item.StartMinute,endDate,endMinute));UpdateRangeText();
        NameInput.Text=item.Name;ColorInput.Text=item.Color;LabelInput.Text=item.Label;ContentInput.Text=item.Content;error.Text="";error.Visibility=Visibility.Collapsed;
        FocusName();Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,FocusName);
    }
    private void FocusName(){NameInput.Focus();Keyboard.Focus(NameInput);FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this),NameInput);NameInput.CaretIndex=NameInput.Text.Length;}
    internal void OpenTimePicker()
    {
        if(IsTimePickerOpen){CloseTimePicker();return;}
        ClosePresetPicker();TimePicker.Visibility=Visibility.Visible;
        IsTimePickerOpen=true;TimePickerOpened?.Invoke();TimePicker.Calendar.FocusSelected();
    }
    internal void CloseTimePicker()
    {
        if(!IsTimePickerOpen)return;
        TimePicker.CloseDropDown();IsTimePickerOpen=false;TimePicker.Visibility=Visibility.Collapsed;TimePickerClosed?.Invoke();UpdateRangeText();rangeButton.Focus();
    }
    internal void OpenPresetPicker()
    {
        if(IsPresetPickerOpen){ClosePresetPicker();return;}
        CloseTimePicker();TimePicker.Visibility=Visibility.Collapsed;PresetPicker.SetPresets(ReadPresets?.Invoke()??[],ColorInput.Text,LabelInput.Text);
        IsPresetPickerOpen=true;PresetPicker.Visibility=Visibility.Visible;PresetOpened?.Invoke();
        (PresetPicker.PresetInput.IsVisible?(UIElement)PresetPicker.PresetInput:PresetPicker.AddButton).Focus();
    }
    internal void ClosePresetPicker()
    {
        if(IsPresetDialogOpen){PresetDialog.CloseColorPicker();IsPresetDialogOpen=false;PresetDialog.Visibility=Visibility.Collapsed;}
        if(!IsPresetPickerOpen)return;
        IsPresetPickerOpen=false;PresetPicker.Visibility=Visibility.Collapsed;PresetClosed?.Invoke();categoryButton.Focus();
    }
    internal void OpenPresetDialog()
    {
        if(!IsPresetPickerOpen || IsPresetDialogOpen)return;
        PresetPicker.Visibility=Visibility.Collapsed;
        IsPresetDialogOpen=true;PresetDialog.Visibility=Visibility.Visible;PresetDialog.Open(ColorInput.Text);PresetOpened?.Invoke();
    }
    // Cancel returns to the preset list.
    internal void ClosePresetDialog()
    {
        if(!IsPresetDialogOpen)return;
        PresetDialog.CloseColorPicker();IsPresetDialogOpen=false;PresetDialog.Visibility=Visibility.Collapsed;
        PresetPicker.SetPresets(ReadPresets?.Invoke()??[],ColorInput.Text,LabelInput.Text);PresetPicker.Visibility=Visibility.Visible;PresetOpened?.Invoke();PresetPicker.AddButton.Focus();
    }
    internal void OpenColorPicker()
    {
        if(!IsPresetPickerOpen)OpenPresetPicker();
        OpenPresetDialog();PresetDialog.OpenColorPicker();PresetOpened?.Invoke();
    }
    internal void CloseColorPicker()=>PresetDialog.CloseColorPicker();
    internal void HandleEscape()
    {
        if(IsColorPickerOpen){CloseColorPicker();return;}
        if(IsPresetDialogOpen){ClosePresetDialog();return;}
        if(IsPresetPickerOpen){ClosePresetPicker();return;}
        if(IsTimePickerOpen){if(!TimePicker.CloseDropDown())CloseTimePicker();return;}Submit();
    }
    private void UpdateCategoryText()
    {
        colorPreview.Background=OverlayScheduleColorPicker.TryColor(ColorInput.Text,out var color)?new SolidColorBrush(color):Brushes.Transparent;
        categoryText.Text=string.IsNullOrWhiteSpace(LabelInput.Text)?"레이블 없음":LabelInput.Text.Trim();
        AutomationProperties.SetName(categoryButton,"일정 색상 · 레이블 프리셋 선택 · "+categoryText.Text+" "+ColorInput.Text);
    }
    private void UpdateRangeText()
    {
        var value=TimePicker.GetRange();rangeText.Text=$"{value.StartDate:yyyy년 M월 d일} {OverlayScheduleDial.Time(value.StartMinute)} → {value.EndDate:yyyy년 M월 d일} {OverlayScheduleDial.Time(value.EndMinute)}";
        AutomationProperties.SetName(rangeButton,"일정 날짜와 시간 선택 · "+rangeText.Text);
    }
    internal bool Submit(bool deleting=false)
    {
        if(IsColorPickerOpen){CloseColorPicker();return true;}
        if(IsPresetDialogOpen){ClosePresetDialog();return true;}
        if(IsPresetPickerOpen){ClosePresetPicker();return true;}
        if(IsTimePickerOpen){CloseTimePicker();return true;}
        try {
            if(!deleting && !editingExisting && string.IsNullOrWhiteSpace(NameInput.Text) && string.IsNullOrWhiteSpace(ContentInput.Text)) {
                Close();return true;
            }
            var item=draft.Copy();
            if(!deleting) {
                var range=TimePicker.GetRange();item.Name=NameInput.Text.Trim();item.StartMinute=range.StartMinute;
                item.StartDayOffset=range.StartDate.DayNumber-referenceDate.DayNumber;item.DurationMinutes=range.Duration;
                item.Color=ColorInput.Text.Trim().ToUpperInvariant();item.Label=LabelInput.Text.Trim();item.Content=ContentInput.Text;
                DaySchedule.Validate(item);
            }
            if(commit?.Invoke(item,deleting)!=true){ShowError("저장하지 못했습니다. 입력을 유지했습니다. 다시 저장해 주세요.");return false;}
            Close();return true;
        }catch(Exception ex){ShowError(ex.Message);return false;}
    }
    internal void ShowSaveError(string message)=>ShowError(message);
    private void ShowError(string message){error.Text=message;error.Visibility=Visibility.Visible;}
    internal void Close(){ClosePresetPicker();if(IsTimePickerOpen)CloseTimePicker();commit=null;Closed?.Invoke();}
    private Brush Brush(string name)=>(Brush)FindResource(name);
}
