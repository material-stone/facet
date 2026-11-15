using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Facet.Plugins;

namespace Facet.ConditionPlugin;

// A single-date calendar in the look of the app's schedule calendar, which a plugin cannot reference.
internal sealed class ConditionDateCalendar : Border
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

    internal ConditionDateCalendar(string name)
    {
        YearInput.SetResourceReference(StyleProperty,"CalendarYearInput");
        MonthInput.SetResourceReference(StyleProperty,"CalendarMonthInput");
        Padding=new(10);CornerRadius=new(8);Background=Brush("Input");BorderBrush=Brush("Edge");BorderThickness=new(1);
        var body=new StackPanel();var header=new Grid();
        foreach(var width in new[]{26d,6,0,6,80,6,26})header.ColumnDefinitions.Add(new(){Width=width==0?new GridLength(1,GridUnitType.Star):new(width)});
        Button Arrow(string glyph,string label) {var button=new Button{Style=(Style)Application.Current.FindResource("ButtonWithoutFocusOutline"),BorderBrush=Brushes.Transparent,Width=26,Height=30,Padding=new(3),Margin=new(0),ContentTemplate=null,Content=ConditionGlyphs.Stroke(glyph,16)};AutomationProperties.SetName(button,name+label);return button;}
        previous=Arrow(ConditionGlyphs.Left," 이전 달");next=Arrow(ConditionGlyphs.Right," 다음 달");
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
    private void Choose(DateOnly date){if(!IsDateEnabled(date))return;SetDate(date);Chosen?.Invoke(date);}

    private void Render()
    {
        updating=true;YearInput.Text=month.Year.ToString(CultureInfo.InvariantCulture);MonthInput.SelectedIndex=month.Month-1;updating=false;
        previous.IsEnabled=month>DateOnly.MinValue;next.IsEnabled=month.Year<9999 || month.Month<12;days.Children.Clear();
        int offset=(int)month.DayOfWeek,count=DateTime.DaysInMonth(month.Year,month.Month);
        for(int cell=0;cell<42;cell++) {
            int number=cell-offset+1;if(number<1 || number>count){days.Children.Add(new Border{Height=24});continue;}
            var date=new DateOnly(month.Year,month.Month,number);bool active=date==selected;
            var button=new Button {Style=(Style)Application.Current.FindResource("ButtonWithoutFocusOutline"),Content=number.ToString(),FontSize=12,Height=24,Padding=new(0),Margin=new(1),
                Background=Brushes.Transparent,BorderBrush=active?Brush("Mint"):Brushes.Transparent,
                Foreground=Brush(cell%7==0?"CalendarSunday":cell%7==6?"CalendarSaturday":"Ink"),Focusable=false,IsEnabled=IsDateEnabled(date)};
            AutomationProperties.SetName(button,date.ToString("yyyy년 M월 d일"));button.Click+=(_,_)=>Choose(date);
            days.Children.Add(button);
        }
    }

    private static Brush Brush(string name)=>(Brush)Application.Current.FindResource(name);
}

internal static class ConditionGlyphs
{
    internal const string Left="M15,5 L8,12 L15,19",Right="M9,5 L16,12 L9,19",Trash="M4,7 H20 M9,7 V4 H15 V7 M6,7 L7,20 H17 L18,7 M10,11 V16 M14,11 V16";

    // Line art on a 24-unit box that follows the button's text color.
    internal static FrameworkElement Stroke(string data,double size)
    {
        var path=new System.Windows.Shapes.Path{Data=Geometry.Parse(data),StrokeThickness=2,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};
        path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new System.Windows.Data.Binding(nameof(Control.Foreground)){RelativeSource=new(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(Control),1)});
        return new Viewbox{Width=size,Height=size,Child=new Canvas{Width=24,Height=24,Children={path}}};
    }
}
