using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Facet.ConditionPlugin;

internal sealed class ConditionRecordEditor : UserControl
{
    private ConditionEntry? original;
    private string kind="";
    private DateTimeOffset at;
    private bool retryDelete;
    private readonly TextBlock error=new(){FontSize=12,TextWrapping=TextWrapping.Wrap,Visibility=Visibility.Collapsed,Margin=new(0,8,0,0)};
    private readonly TextBlock status=new(){FontSize=11,VerticalAlignment=VerticalAlignment.Center};
    private readonly List<Button> options=[];
    internal Button Timestamp {get;}=new(){Margin=new(0),HorizontalContentAlignment=HorizontalAlignment.Left,Focusable=false,IsTabStop=false,FocusVisualStyle=null};
    internal Button Delete {get;}=new(){Content=ConditionGlyphs.Stroke(ConditionGlyphs.Trash,18),ContentTemplate=null,
        Width=32,Height=32,Padding=new(7),BorderThickness=new(0),Background=Brushes.Transparent,ToolTip="컨디션 기록 삭제",Margin=new(0),Focusable=false,IsTabStop=false,FocusVisualStyle=null};
    internal Button Retry {get;}=new(){Content="다시 시도",Visibility=Visibility.Collapsed,Margin=new(0,8,0,0)};
    internal ConditionDateTimePicker TimePicker {get;}=new();
    internal Popup TimePopup {get;}
    internal Func<Action<List<ConditionEntry>>,bool>? Commit {get;set;}
    internal event Action? Saved;
    internal event Action? Deleted;
    internal string Error=>error.Text;
    internal IReadOnlyList<Button> Options=>options;
    internal DateTimeOffset DraftTimestamp=>at;
    internal bool HasPendingChanges=>original!=null && (original.At!=at || original.Kind!=kind);

    internal ConditionRecordEditor()
    {
        Width=320;
        AutomationProperties.SetName(Delete,"컨디션 기록 삭제");
        foreach(var button in new[]{Timestamp,Delete}) {
            button.SetResourceReference(StyleProperty,"ButtonWithoutFocusOutline");
            button.BorderBrush=Brushes.Transparent;
        }
        var root=new StackPanel();root.Children.Add(new TextBlock{Text="컨디션 기록",FontSize=16,FontWeight=FontWeights.SemiBold});
        root.Children.Add(new TextBlock{Text="기록 시각",FontSize=12,Margin=new(0,16,0,6)});root.Children.Add(Timestamp);
        AutomationProperties.SetName(Timestamp,"기록 날짜와 시각 변경");
        TimePopup=new Popup{Child=TimePicker,PlacementTarget=Timestamp,Placement=PlacementMode.Bottom,VerticalOffset=8,
            StaysOpen=false,AllowsTransparency=true,PopupAnimation=PopupAnimation.Fade};
        Timestamp.Click+=(_,_)=>{if(TimePopup.IsOpen)CloseTimePicker();else{TimePicker.SetTimestamp(at);TimePopup.IsOpen=true;}};
        TimePicker.Changed+=value=>{at=value;RefreshTimestamp();SaveChanges(DateTimeOffset.Now);};
        TimePicker.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){HandleEscape();e.Handled=true;}};
        root.Children.Add(new TextBlock{Text="컨디션",FontSize=12,Margin=new(0,16,0,6)});
        var tiles=new UniformGrid{Columns=3};
        foreach(var item in ConditionIcons.All) {
            var content=new StackPanel();content.Children.Add(new Image{Source=ConditionIcons.Load(item.Id),Width=44,Height=44});
            content.Children.Add(new TextBlock{Text=item.Name,FontSize=12,TextAlignment=TextAlignment.Center});
            var button=new Button{Content=content,ContentTemplate=null,Height=78,Padding=new(4),Margin=new(2),Tag=item.Id,Focusable=false,IsTabStop=false,FocusVisualStyle=null};
            button.SetResourceReference(StyleProperty,"ButtonWithoutFocusOutline");button.BorderBrush=Brushes.Transparent;
            AutomationProperties.SetName(button,item.Name+" 선택");button.Click+=(_,_)=>{Choose(item.Id);SaveChanges(DateTimeOffset.Now);};
            options.Add(button);tiles.Children.Add(button);
        }
        root.Children.Add(tiles);error.SetResourceReference(TextBlock.ForegroundProperty,"Danger");root.Children.Add(error);
        Retry.Click+=(_,_)=>{if(retryDelete)DeleteRecord();else SaveChanges(DateTimeOffset.Now);};root.Children.Add(Retry);
        var actions=new Grid{Margin=new(0,16,0,0)};actions.ColumnDefinitions.Add(new());actions.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");actions.Children.Add(status);
        Delete.SetResourceReference(ForegroundProperty,"Danger");Grid.SetColumn(Delete,1);actions.Children.Add(Delete);root.Children.Add(actions);
        Delete.Click+=(_,_)=>DeleteRecord();
        var border=new Border{Child=root,CornerRadius=new(16),Padding=new(20)};border.SetResourceReference(Border.BackgroundProperty,"Panel");Content=border;
        Unloaded+=(_,_)=>CloseTimePicker();
    }

    internal void Open(ConditionEntry entry)
    {
        CloseTimePicker();
        if(HasPendingChanges) {
            if(original!.At!=entry.At || original.Kind!=entry.Kind)ShowError("미저장 변경을 먼저 다시 시도해 주세요.");
            return;
        }
        original=entry with{};at=entry.At;status.Text="";ClearError();Choose(entry.Kind);RefreshTimestamp();
    }
    private void RefreshTimestamp()=>Timestamp.Content=at.ToLocalTime().ToString("yyyy.MM.dd (ddd) HH:mm:ss",System.Globalization.CultureInfo.GetCultureInfo("ko-KR"));
    private void Choose(string id)
    {
        kind=id;foreach(var option in options)option.SetResourceReference(Button.BackgroundProperty,Equals(option.Tag,id)?"Selected":"Input");
    }
    internal bool SaveChanges(DateTimeOffset now)
    {
        if(original==null)return false;
        if(!HasPendingChanges){ClearError();status.Text="저장됨";return true;}
        var next=new ConditionEntry{At=at,Kind=kind};
        try {
            if(Commit?.Invoke(data=>ConditionTracking.Update(data,original,kind,at,now))!=true){ShowError("저장하지 못했습니다. 변경을 유지했습니다.");return false;}
            original=next;ClearError();status.Text="저장됨";Saved?.Invoke();return true;
        }
        catch(ArgumentException ex){ShowError(ex.Message);return false;}
    }
    internal bool DeleteRecord()
    {
        if(original==null)return false;
        try {
            if(Commit?.Invoke(data=>ConditionTracking.Delete(data,original))!=true){ShowError("삭제하지 못했습니다. 다시 시도해 주세요.",true);return false;}
            original=null;ClearError();CloseTimePicker();Deleted?.Invoke();return true;
        }
        catch(ArgumentException ex){ShowError(ex.Message,true);return false;}
    }
    internal bool HandleEscape()
    {
        if(!TimePopup.IsOpen)return false;
        if(!TimePicker.CloseDropDown())CloseTimePicker();return true;
    }
    internal void CloseTimePicker(){TimePicker.CloseDropDown();TimePopup.IsOpen=false;}
    private void ClearError(){error.Visibility=Visibility.Collapsed;Retry.Visibility=Visibility.Collapsed;retryDelete=false;}
    private void ShowError(string message,bool deletion=false){error.Text=message;error.Visibility=Visibility.Visible;Retry.Visibility=Visibility.Visible;status.Text="미저장";retryDelete=deletion;}
}
