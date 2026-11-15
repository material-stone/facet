using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Facet.SchedulePlugin;

internal readonly record struct ScheduleTimeRange(DateOnly StartDate,int StartMinute,DateOnly EndDate,int EndMinute)
{
    public long Duration {
        get {
            long value=(long)(EndDate.DayNumber-StartDate.DayNumber)*1440+EndMinute-StartMinute;
            if(value<30)throw new ArgumentException("끝은 시작보다30분 이상 뒤로 설정해 주세요.");
            return value;
        }
    }
}

internal sealed class ScheduleDateCalendar : Border
{
    internal readonly TextBox YearInput=new(){MaxLength=4,Margin=new(0),Padding=new(4),VerticalContentAlignment=VerticalAlignment.Center};
    internal readonly ComboBox MonthInput=new(){Margin=new(0),Padding=new(6)};
    private readonly UniformGrid days=new(){Columns=7,Rows=6};
    private readonly Button previous,next;
    private DateOnly selected=DateOnly.FromDateTime(DateTime.Now),month;
    private bool updating;
    internal DateOnly SelectedDate => selected;
    internal event Action<DateOnly>? Chosen;
    internal Func<DateOnly,bool> IsDateEnabled {get;set;}=_=>true;
    internal DateOnly? RangeStart {get;set;}
    internal DateOnly? RangeEnd {get;set;}
    internal ScheduleDateCalendar(string name)
    {
        YearInput.SetResourceReference(StyleProperty,"CalendarYearInput");
        MonthInput.SetResourceReference(StyleProperty,"CalendarMonthInput");
        Padding=new(10);CornerRadius=new(8);Background=Brush("Input");BorderBrush=Brush("Edge");BorderThickness=new(1);
        var body=new StackPanel();var header=new Grid();
        foreach(var width in new[]{26d,6,0,6,80,6,26})header.ColumnDefinitions.Add(new(){Width=width==0?new GridLength(1,GridUnitType.Star):new(width)});
        Button Arrow(IconKind kind,string label) {var button=new Button{Style=(Style)Application.Current.FindResource("ButtonWithoutFocusOutline"),BorderBrush=Brushes.Transparent,Width=26,Height=30,Padding=new(3),Margin=new(0),ContentTemplate=null,Content=new Icon{Kind=kind,Width=16,Height=16}};AutomationProperties.SetName(button,name+label);return button;}
        previous=Arrow(IconKind.Left," 이전 달");next=Arrow(IconKind.Right," 다음 달");
        UIElement[] controls=[previous,YearInput,MonthInput,next];for(int i=0;i<4;i++){Grid.SetColumn(controls[i],i*2);header.Children.Add(controls[i]);}
        body.Children.Add(header);
        AutomationProperties.SetName(YearInput,name+" 연도");AutomationProperties.SetName(MonthInput,name+" 월");
        for(int i=1;i<=12;i++)MonthInput.Items.Add(i+"월");
        var weekdays=new UniformGrid{Columns=7,Margin=new(0,8,0,2)};
        foreach(var day in new[]{"일","월","화","수","목","금","토"})weekdays.Children.Add(new TextBlock{Text=day,FontSize=11,HorizontalAlignment=HorizontalAlignment.Center});
        body.Children.Add(weekdays);body.Children.Add(days);Child=body;
        previous.Click+=(_,_)=>MoveMonth(-1);next.Click+=(_,_)=>MoveMonth(1);
        YearInput.LostKeyboardFocus+=(_,_)=>CommitYear();YearInput.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){CommitYear();e.Handled=true;}};
        MonthInput.SelectionChanged+=(_,_)=>{if(!updating && MonthInput.SelectedIndex>=0)ChooseMonth(month.Year,MonthInput.SelectedIndex+1);};
        SetDate(selected);
    }
    internal void SetDate(DateOnly value){selected=value;month=new(value.Year,value.Month,1);Render();}
    private void CommitYear(){if(updating)return;if(int.TryParse(YearInput.Text,out int year) && year>=1 && year<=9999)ChooseMonth(year,month.Month);else YearInput.Text=month.Year.ToString(CultureInfo.InvariantCulture);}
    private void ChooseMonth(int year,int value){month=new(year,value,1);Render();}
    private void MoveMonth(int delta){long index=(month.Year-1)*12L+month.Month-1+delta;if(index>=0 && index<9999*12){var value=month.AddMonths(delta);ChooseMonth(value.Year,value.Month);}}
    internal void Refresh()=>Render();
    private void Choose(DateOnly date){if(!IsDateEnabled(date))return;SetDate(date);Chosen?.Invoke(date);FocusSelected();}
    private void Render()
    {
        updating=true;YearInput.Text=month.Year.ToString(CultureInfo.InvariantCulture);MonthInput.SelectedIndex=month.Month-1;updating=false;
        previous.IsEnabled=month>DateOnly.MinValue;next.IsEnabled=month.Year<9999 || month.Month<12;days.Children.Clear();
        int offset=(int)month.DayOfWeek,count=DateTime.DaysInMonth(month.Year,month.Month);
        for(int cell=0;cell<42;cell++) {
            int number=cell-offset+1;if(number<1 || number>count){days.Children.Add(new Border{Height=24});continue;}
            var date=new DateOnly(month.Year,month.Month,number);bool active=date==selected;
            var button=new Button {Style=(Style)Application.Current.FindResource("ButtonWithoutFocusOutline"),Content=number.ToString(),FontSize=12,Height=24,Padding=new(0),Margin=new(1),
                Background=date>=RangeStart && date<=RangeEnd?Brush("Selected"):Brushes.Transparent,BorderBrush=active || date==RangeStart || date==RangeEnd?Brush("Mint"):Brushes.Transparent,
                Foreground=Brush(cell%7==0?"CalendarSunday":cell%7==6?"CalendarSaturday":"Ink"),IsTabStop=active && IsDateEnabled(date),IsEnabled=IsDateEnabled(date)};
            AutomationProperties.SetName(button,date.ToString("yyyy년 M월 d일"));button.Click+=(_,_)=>Choose(date);
            button.PreviewKeyDown+=(_,e)=>{int shift=e.Key switch{Key.Left=>-1,Key.Right=>1,Key.Up=>-7,Key.Down=>7,_=>0};if(shift!=0 && date.DayNumber+shift>=0 && date.DayNumber+shift<=DateOnly.MaxValue.DayNumber){var target=date.AddDays(shift);if(IsDateEnabled(target)){SetDate(target);FocusSelected();}e.Handled=true;}};
            days.Children.Add(button);
        }
        if(!days.Children.OfType<Button>().Any(button=>button.IsTabStop)) {var first=days.Children.OfType<Button>().FirstOrDefault(button=>button.IsEnabled);if(first!=null)first.IsTabStop=true;}
    }
    internal void FocusSelected()=>(days.Children.OfType<Button>().FirstOrDefault(button=>button.IsTabStop) ?? days.Children.OfType<Button>().FirstOrDefault(button=>button.IsEnabled))?.Focus();
    private Brush Brush(string name)=>(Brush)Application.Current.FindResource(name);
}

internal sealed class OverlayScheduleTimePicker : Border
{
    internal readonly ScheduleDateCalendar Calendar=new("일정 날짜");
    internal readonly ComboBox StartInput=new(){Margin=new(0)},EndInput=new(){Margin=new(0)};
    internal readonly Button StartDateButton=new(){Margin=new(0,0,8,0)},EndDateButton=new(){Margin=new(0)};
    private readonly TextBlock instruction=new(){FontSize=12,Margin=new(0,6,0,6)};
    private DateOnly startDate,endDate;
    private bool selectingEnd,updating;
    internal bool IsSelectingEnd=>selectingEnd;
    internal DateOnly StartDate=>startDate;
    internal DateOnly EndDate=>endDate;
    internal event Action? RangeChanged;
    internal OverlayScheduleTimePicker()
    {
        StartDateButton.SetResourceReference(StyleProperty,"ButtonWithoutFocusOutline");
        EndDateButton.SetResourceReference(StyleProperty,"ButtonWithoutFocusOutline");
        StartInput.SetResourceReference(StyleProperty,"CalendarTimeInput");
        EndInput.SetResourceReference(StyleProperty,"CalendarTimeInput");
        Width=436;MaxHeight=400;Padding=new(12);CornerRadius=new(12);Background=Brush("Panel");BorderBrush=Brush("Edge");BorderThickness=new(1);
        KeyboardNavigation.SetTabNavigation(this,KeyboardNavigationMode.Cycle);
        var body=new StackPanel();var dates=new Grid();dates.ColumnDefinitions.Add(new());dates.ColumnDefinitions.Add(new());
        dates.Children.Add(StartDateButton);Grid.SetColumn(EndDateButton,1);dates.Children.Add(EndDateButton);body.Children.Add(dates);
        StartDateButton.Click+=(_,_)=>SelectEndpoint(false);EndDateButton.Click+=(_,_)=>SelectEndpoint(true);
        body.Children.Add(instruction);body.Children.Add(Calendar);
        var times=new Grid{Margin=new(0,10,0,0)};times.ColumnDefinitions.Add(new());times.ColumnDefinitions.Add(new(){Width=new(12)});times.ColumnDefinitions.Add(new());
        StackPanel TimeField(string title,ComboBox time){var panel=new StackPanel();panel.Children.Add(new TextBlock{Text=title,FontSize=12,Margin=new(0,0,0,4)});panel.Children.Add(time);return panel;}
        times.Children.Add(TimeField("시작 시간",StartInput));var end=TimeField("끝 시간",EndInput);Grid.SetColumn(end,2);times.Children.Add(end);body.Children.Add(times);
        Child=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        for(int i=0;i<48;i++)StartInput.Items.Add(new ComboBoxItem{Content=OverlayScheduleDial.Time(i*30)});
        for(int i=0;i<=48;i++)EndInput.Items.Add(new ComboBoxItem{Content=OverlayScheduleDial.Time(i*30)});
        AutomationProperties.SetName(StartInput,"시작 시간");AutomationProperties.SetName(EndInput,"끝 시간");
        Calendar.Chosen+=ChooseDate;
        StartInput.SelectionChanged+=(_,_)=>{if(!updating)RefreshRange();};EndInput.SelectionChanged+=(_,_)=>{if(!updating)RefreshRange();};
    }
    internal void SetRange(ScheduleTimeRange value)
    {
        _=value.Duration;updating=true;startDate=value.StartDate;endDate=value.EndDate;StartInput.SelectedIndex=value.StartMinute/30;EndInput.SelectedIndex=value.EndMinute/30;updating=false;
        SelectEndpoint(false);RefreshRange();
    }
    internal void SelectEndpoint(bool end){selectingEnd=end;Calendar.SetDate(end?endDate:startDate);RefreshCalendar();}
    internal void ChooseDate(DateOnly date)
    {
        if(selectingEnd){if(!CanEndOn(date))return;endDate=date;RefreshRange();}
        else {startDate=date;RefreshRange();SelectEndpoint(true);}
    }
    private long StartAbsolute=>(long)startDate.DayNumber*1440+Math.Max(0,StartInput.SelectedIndex)*30;
    private bool CanEndOn(DateOnly date)
    {
        long day=(long)date.DayNumber*1440;
        return day+1440>=StartAbsolute+30 && date>=startDate;
    }
    private void RefreshRange()
    {
        if(updating)return;updating=true;
        if(StartInput.SelectedIndex<0)StartInput.SelectedIndex=0;
        long first=StartAbsolute+30,last=((long)DateOnly.MaxValue.DayNumber+1)*1440;
        long current=(long)endDate.DayNumber*1440+Math.Max(0,EndInput.SelectedIndex)*30;
        current=Math.Clamp(current,first,last);
        // Keep an explicitly selected 24:00 on its date instead of shifting it to the next day.
        if(!CanEndOn(endDate) || current<(long)endDate.DayNumber*1440 || current>(long)(endDate.DayNumber+1)*1440){
            int day=(int)(current/1440);if(day>DateOnly.MaxValue.DayNumber)day=DateOnly.MaxValue.DayNumber;
            endDate=DateOnly.FromDayNumber(day);
        }
        EndInput.SelectedIndex=(int)((current-(long)endDate.DayNumber*1440)/30);
        for(int i=0;i<EndInput.Items.Count;i++)((ComboBoxItem)EndInput.Items[i]).IsEnabled=(long)endDate.DayNumber*1440+i*30>=first && (long)endDate.DayNumber*1440+i*30<=last;
        updating=false;RefreshCalendar();RangeChanged?.Invoke();
    }
    private void RefreshCalendar()
    {
        Calendar.IsDateEnabled=date=>!selectingEnd || CanEndOn(date);Calendar.RangeStart=startDate;Calendar.RangeEnd=endDate;Calendar.Refresh();
        StartDateButton.Content=$"시작 {startDate:M월 d일}";EndDateButton.Content=$"끝 {endDate:M월 d일}";
        StartDateButton.BorderBrush=selectingEnd?Brushes.Transparent:Brush("Mint");EndDateButton.BorderBrush=selectingEnd?Brush("Mint"):Brushes.Transparent;
        instruction.Text=selectingEnd?"끝 날짜를 선택하세요":"시작 날짜를 선택하세요";
    }
    internal ScheduleTimeRange GetRange()
    {
        if(StartInput.SelectedIndex<0 || EndInput.SelectedIndex<0)throw new ArgumentException("시작과 끝 시간을 선택해 주세요.");
        var value=new ScheduleTimeRange(startDate,StartInput.SelectedIndex*30,endDate,EndInput.SelectedIndex*30);_=value.Duration;return value;
    }
    internal bool CloseDropDown()
    {
        foreach(var input in new[]{StartInput,EndInput,Calendar.MonthInput})if(input.IsDropDownOpen){input.IsDropDownOpen=false;return true;}return false;
    }
    private Brush Brush(string name)=>(Brush)Application.Current.FindResource(name);
}
