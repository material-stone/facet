using System.Windows.Automation;

namespace Facet.ConditionPlugin;

internal sealed class ConditionDateTimePicker : Border
{
    internal ConditionDateCalendar Calendar { get; } = new("기록 날짜");
    internal ComboBox Hour { get; } = new() { Margin = new(0) };
    internal ComboBox Minute { get; } = new() { Margin = new(0) };
    internal ComboBox Second { get; } = new() { Margin = new(0) };
    internal event Action<DateTimeOffset>? Changed;
    private long fraction;
    private bool updating;

    internal ConditionDateTimePicker()
    {
        foreach(var input in new[]{Hour,Minute,Second})input.SetResourceReference(StyleProperty,"CalendarTimeInput");
        Width=320;MaxHeight=420;Padding=new(12);CornerRadius=new(12);
        SetResourceReference(BackgroundProperty,"Panel");SetResourceReference(BorderBrushProperty,"Edge");BorderThickness=new(1);
        Calendar.IsDateEnabled=date=>date<=DateOnly.FromDateTime(DateTime.Now);
        var body=new StackPanel();body.Children.Add(Calendar);
        var times=new Grid {Margin=new(0,12,0,0)};
        for(int i=0;i<3;i++)times.ColumnDefinitions.Add(new());
        var fields=new[]{(Hour,"시",24),(Minute,"분",60),(Second,"초",60)};
        for(int i=0;i<fields.Length;i++) {
            var (input,name,count)=fields[i];
            for(int value=0;value<count;value++)input.Items.Add(value.ToString("00"));
            AutomationProperties.SetName(input,"기록 "+name);
            var field=new StackPanel {Margin=new(i==0?0:8,0,0,0)};
            field.Children.Add(new TextBlock {Text=name,FontSize=12,Margin=new(0,0,0,4)});field.Children.Add(input);
            Grid.SetColumn(field,i);times.Children.Add(field);input.SelectionChanged+=(_,_)=>Publish();
        }
        body.Children.Add(times);
        Child=new ScrollViewer {Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        Calendar.Chosen+=_=>Publish();
    }

    internal void SetTimestamp(DateTimeOffset value)
    {
        updating=true;var local=value.ToLocalTime();fraction=local.Ticks%TimeSpan.TicksPerSecond;
        Calendar.SetDate(DateOnly.FromDateTime(local.DateTime));Hour.SelectedIndex=local.Hour;Minute.SelectedIndex=local.Minute;Second.SelectedIndex=local.Second;
        updating=false;
    }
    private void Publish()
    {
        if(updating || Hour.SelectedIndex<0 || Minute.SelectedIndex<0 || Second.SelectedIndex<0)return;
        var local=DateTime.SpecifyKind(Calendar.SelectedDate.ToDateTime(new TimeOnly(Hour.SelectedIndex,Minute.SelectedIndex,Second.SelectedIndex)),DateTimeKind.Local).AddTicks(fraction);
        if(TimeZoneInfo.Local.IsInvalidTime(local))return;
        Changed?.Invoke(new DateTimeOffset(local));
    }
    internal bool CloseDropDown()
    {
        foreach(var input in new[]{Hour,Minute,Second,Calendar.MonthInput})if(input.IsDropDownOpen){input.IsDropDownOpen=false;return true;}
        return false;
    }
}
