using System.Threading.Tasks;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Facet.Design;

namespace Facet;

public partial class OverlaySettingsView : UserControl
{
    private sealed class SettingRow
    {
        internal required string Name, Description;
        internal required Grid Host;
        internal required TextBlock Label, Detail, Summary;
        internal required Func<string> Read;
        internal required Action<Settings> Load, Write;
        internal string Baseline = "";
        internal bool Changed => Read() != Baseline;
        internal Func<bool>? Available;
    }

    private readonly List<SettingRow> rows = [];
    private readonly List<(StackPanel Host, TextBlock Heading, string Name, List<SettingRow> Rows)> groups = [];
    private Func<Settings>? loadSettings;
    private Func<Settings, bool>? saveSettings;
    private Func<bool?> readStartup = () => null;
    private Action<bool>? writeStartup;
    private CheckBox startup = null!;
    private Controller? app;
    private readonly DispatcherTimer autoSave = new() { Interval=TimeSpan.FromMilliseconds(300) };
    private bool loading, saving;
    internal bool HasChanges => rows.Any(row => row.Changed);

    public OverlaySettingsView()
    {
        InitializeComponent();
        BuildRows();
        Reload(new Settings());
        AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, e) => { if (!ReferenceEquals(e.OriginalSource, SearchInput)) Changed(defer:true); }));
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => Changed()));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => Changed()));
        autoSave.Tick += (_,_) => TrySave();
        AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_,e) => {
            if (!loading && !saving && loadSettings!=null && e.OriginalSource is TextBox && !ReferenceEquals(e.OriginalSource,SearchInput)) TrySave();
        }));
        Unloaded += (_,_) => { autoSave.Stop(); if(loadSettings!=null)TrySave(); };
        Loaded += (_,_) => FocusSearch();
        IsVisibleChanged += (_, _) => { if (IsVisible && !HasChanges && loadSettings != null) Reload(loadSettings()); };
    }

    internal double ScrollOffset {
        get => SettingsScroll.VerticalOffset;
        set => SettingsScroll.ScrollToVerticalOffset(value);
    }

    internal void FocusSearch() => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => {
        if (!IsLoaded || !IsVisible) return;
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(SearchInput), SearchInput);
        SearchInput.Focus();
    }));

    internal void Connect(Controller controller)
    {
        app = controller;
        Configure(() => controller.Data.Settings, next => {
            bool saved = controller.ApplySettings(next);
            if (!saved && controller.LastError is { } error) throw new ArgumentException(error);
            return saved;
        }, SettingsEditing.StartupEnabled, SettingsEditing.SetStartup);
    }

    internal void Configure(Func<Settings> load, Func<Settings, bool> save, Func<bool?>? startupReader = null, Action<bool>? startupWriter = null)
    {
        loadSettings = load; saveSettings = save;
        readStartup = startupReader ?? (() => null); writeStartup = startupWriter;
        Reload(load());
    }

    private Brush Brush(string key) => (Brush)FindResource(key);

    internal WorkspaceSettingsPanel WorkspacePanel { get; } = new();
    internal PluginSettingsPanel PluginPanel { get; } = new();
    internal PackagesPanel PackagesPanel { get; } = new();
    // After a package install, so the skin list shows it without reopening the tab.
    internal void ReloadSkins() { if (loadSettings != null && !HasChanges) Reload(loadSettings()); }

    private void BuildRows()
    {
        Group("앱 시작");
        Toggle("직접 실행 시 오버레이 표시", "앱을 직접 실행하거나 다시 실행할 때 오버레이를 엽니다. 단축키로 여는 동작은 그대로 사용할 수 있습니다.", s => s.ShowOverlayOnLaunch, (s,v) => s.ShowOverlayOnLaunch=v);
        startup = Toggle("Windows 로그인 시 자동 실행", "Windows에 로그인하면 오버레이를 열지 않고 트레이에서 앱을 시작합니다.", _ => false, (_,_) => { });

        Group("탭");
        Add("탭 구성", "설정 외의 탭을 추가·삭제하고 순서·표시·이름을 바꿉니다. 위젯 배치는 각 탭의 위젯 편집 버튼에서 바꿉니다. 변경은 바로 저장됩니다.", WorkspacePanel, ()=>"", _=>{}, _=>{});

        Group("플러그인");
        Add("플러그인 구성", "플러그인을 켜거나 끄고 플러그인별 설정을 바꿉니다. 날씨 지역은 날씨 플러그인에서 설정합니다. 변경은 바로 저장됩니다.", PluginPanel, ()=>"", _=>{}, _=>{});
        Add("스킨·플러그인 받기", "온라인 목록이나 zip 파일로 스킨과 플러그인을 설치하고, 직접 만든 것을 올립니다. skins·plugins 폴더에 직접 넣어도 인식합니다.", PackagesPanel, ()=>"", _=>{}, _=>{});

        Group("오버레이 모양");
        var skins = new ComboBox { ItemsSource=Design.Skins.Options, DisplayMemberPath="Name", SelectedValuePath="Id" };
        Add("스킨", "앱의 색과 배경·위젯 틀을 함께 바꾸는 테마입니다. 선택하면 바로 적용됩니다. 오버레이 배경색은 별도 설정을 따릅니다.", skins,
            ()=>(skins.SelectedItem as Design.SkinOption)?.Name ?? "",
            s=>{ skins.ItemsSource=Design.Skins.Rescan();skins.SelectedValue=Design.Skins.Resolve(s.Skin); },
            s=>s.Skin=skins.SelectedValue as string ?? Design.Skins.DefaultId);
        // Only the chosen skin's own options appear, right under the list.
        var skinArea=new StackPanel();
        var skinBoxes=new List<(Design.ThemeOption Option,CheckBox Box)>();
        string ChosenSkin()=>skins.SelectedValue as string ?? Design.Skins.DefaultId;
        void ShowSkinOptions(Settings s)
        {
            skinArea.Children.Clear();skinBoxes.Clear();
            string skin=ChosenSkin();
            var visuals=Design.Skins.LoadVisuals(skin);
            foreach(var option in visuals?.Options ?? []) {
                var box=new CheckBox { Content=option.Name,ToolTip=option.Description,IsChecked=visuals!.IsOptionOn(s,skin,option.Id),Margin=new(0,4,0,4),HorizontalAlignment=HorizontalAlignment.Left };
                skinBoxes.Add((option,box));skinArea.Children.Add(box);
            }
        }
        Add("스킨 설정", "선택한 스킨에 설정할 항목이 있을 때 나타납니다. 변경하면 바로 적용됩니다.", skinArea,
            ()=>string.Join(" · ",skinBoxes.Select(item=>item.Option.Name+(item.Box.IsChecked==true ? " 켬" : " 끔"))),
            ShowSkinOptions,
            s=>{
                var next=new Dictionary<string,bool>(s.SkinOptions ?? []);
                foreach(var (option,box) in skinBoxes) next[Design.ThemeVisuals.OptionKey(ChosenSkin(),option.Id)]=box.IsChecked==true;
                s.SkinOptions=next;
            });
        var skinOptionsRow=rows[^1];
        skinOptionsRow.Available=()=>skinBoxes.Count>0;
        skins.SelectionChanged += (_,_)=>{
            if(!loading) { ShowSkinOptions(loadSettings?.Invoke() ?? new Settings());skinOptionsRow.Baseline=skinOptionsRow.Read(); }
            Changed();
        };
        Range("투명도", "오버레이 배경의 투명도입니다. 0%는 완전 불투명, 100%는 완전 투명입니다. 글자와 입력창은 유지합니다.", 0, 100, 1, OverlayAppearance.Transparency, (s,v)=>s.OverlayTransparencyPercent=v, v=>$"{v:0}%");
        var colors = new OverlayColorPalette();
        Add("배경색", "오버레이 배경색을 색상 팔레트에서 선택합니다. 투명도와 별도로 저장됩니다.", colors,
            ()=>colors.SelectedColor, s=>colors.SelectedColor=s.OverlayBackgroundColor, s=>s.OverlayBackgroundColor=colors.SelectedColor);
        colors.SelectionChanged += (_,_)=>Changed();
        Range("블러 강도", "오버레이 뒤 화면을 흐리게 만드는 강도입니다. 0으로 설정하면 블러를 끕니다.", 0, 40, 1, s=>s.OverlayBlurRadius, (s,v)=>s.OverlayBlurRadius=v, v=>$"{v:0}");
        Range("나타나는 시간", "오버레이가 나타나는 데 걸리는 시간입니다. 0ms는 즉시 표시합니다.", 0, 2000, 20, s=>s.OverlayShowMilliseconds, (s,v)=>s.OverlayShowMilliseconds=(int)v, v=>$"{v:0} ms");
        Range("사라지는 시간", "오버레이가 사라지는 데 걸리는 시간입니다. 0ms는 즉시 숨깁니다.", 0, 2000, 20, s=>s.OverlayHideMilliseconds, (s,v)=>s.OverlayHideMilliseconds=(int)v, v=>$"{v:0} ms");

        Group("단축키·빠른 실행");
        var engines = new ComboBox { ItemsSource=QuickLaunchWebSearch.Engines, DisplayMemberPath="Name", SelectedValuePath="Id" };
        Add("검색 엔진", "런처의 ? 검색어에 사용할 검색 엔진입니다. 선택하면 바로 적용됩니다. ? 웹 주소는 검색 대신 해당 페이지를 엽니다.", engines,
            ()=>(engines.SelectedItem as QuickLaunchWebSearch.EngineOption)?.Name ?? "",
            s=>engines.SelectedValue=s.LauncherSearchEngine,
            s=>s.LauncherSearchEngine=engines.SelectedValue is WebSearchEngine engine ? engine : throw new ArgumentException("검색 엔진을 선택해 주세요."));
        engines.SelectionChanged += (_,_)=>Changed();
        Text("오버레이 단축키", "전체 화면 오버레이를 열고 닫는 전역 단축키입니다. Win+Alt+Space는 빠른 실행, Win+Alt+End는 종료 전용입니다.", s=>s.OverlayHotkey, (s,v)=>s.OverlayHotkey=v.Trim());
        Text("탐색기 alias", "한 줄에 이름=절대경로를 입력합니다. 빠른 실행에서 @@이름으로 폴더를 엽니다.", s=>string.Join(Environment.NewLine,(s.LaunchAliases ?? []).Select(x=>x.Key+"="+x.Value)), (s,v)=>s.LaunchAliases=QuickLaunchCatalog.ParseAliases(v), multiline:true);

        Group("알림");
        Toggle("사용자 알림 사용", "로컬 알림 CLI로 등록한 사용자 알림을 표시합니다.", s=>s.Notifications, (s,v)=>s.Notifications=v);
        Toggle("전체 화면에서는 알림 숨김", "다른 앱이 전체 화면인 동안 예정 알림을 숨깁니다.", s=>s.HideFullscreen,(s,v)=>s.HideFullscreen=v);
        Toggle("화면 공유 모드", "알림에 구체적인 기록 대신 일반 안내만 표시합니다.", s=>s.Privacy,(s,v)=>s.Privacy=v);

    }

    private void Group(string name)
    {
        var host=new StackPanel { Margin=new(0,0,0,20) };
        var heading=new TextBlock { FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,8) };
        host.Children.Add(heading);
        host.Children.Add(new Border { Height=1,Background=Brush("Edge"),Margin=new(0,0,0,4) });
        Groups.Children.Add(host); groups.Add((host,heading,name,[]));
    }

    private void Add(string name,string description,FrameworkElement editor,Func<string> read,Action<Settings> load,Action<Settings> write)
    {
        var host=new Grid { MinHeight=44 };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(2,GridUnitType.Star) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(3,GridUnitType.Star) });
        var label=new TextBlock { Padding=new(0,5,0,0),ToolTip=description,Cursor=System.Windows.Input.Cursors.Arrow };
        ToolTipService.SetInitialShowDelay(label,350); ToolTipService.SetShowDuration(label,20000);
        var detail=new TextBlock { FontSize=12,Foreground=Brush("Muted"),Margin=new(0,8,0,0),Visibility=Visibility.Collapsed };
        // Top-aligned for tall editors; the padding keeps a one-line label level with a single control in a 44 DIP row.
        var left=new StackPanel { Margin=new(0,5,24,0),VerticalAlignment=VerticalAlignment.Top };
        left.Children.Add(label);left.Children.Add(detail);
        var summary=new TextBlock { FontSize=12,Foreground=Brush("Muted"),Margin=new(0,6,0,0),Visibility=Visibility.Collapsed };
        var right=new StackPanel { Margin=new(0,5,0,5),VerticalAlignment=VerticalAlignment.Center };
        editor.Margin=new(0);editor.HorizontalAlignment=editor is CheckBox ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        AutomationProperties.SetName(editor,name); AutomationProperties.SetHelpText(editor,description);
        right.Children.Add(editor);right.Children.Add(summary);Grid.SetColumn(right,1);
        host.Children.Add(left);host.Children.Add(right);
        var row=new SettingRow { Name=name,Description=description,Host=host,Label=label,Detail=detail,Summary=summary,Read=read,Load=load,Write=write };
        rows.Add(row);groups[^1].Rows.Add(row);groups[^1].Host.Children.Add(host);
    }

    private CheckBox Toggle(string name,string description,Func<Settings,bool> read,Action<Settings,bool> write)
    {
        var editor=new CheckBox { HorizontalContentAlignment=HorizontalAlignment.Right };
        void Caption() => editor.Content=editor.IsChecked==true ? "켬" : "끔";
        editor.Checked+=(_,_)=>Caption();editor.Unchecked+=(_,_)=>Caption();Caption();
        Add(name,description,editor,()=>editor.IsChecked==true ? "켬" : "끔",s=>editor.IsChecked=read(s),s=>write(s,editor.IsChecked==true));
        return editor;
    }

    private void Text(string name,string description,Func<Settings,string> read,Action<Settings,string> write,bool multiline=false)
    {
        var editor=new TextBox { AcceptsReturn=multiline,TextWrapping=TextWrapping.Wrap,MinHeight=multiline?84:0,MaxHeight=multiline?160:double.PositiveInfinity,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            VerticalContentAlignment=multiline?VerticalAlignment.Top:VerticalAlignment.Center,TextAlignment=TextAlignment.Left };
        Add(name,description,editor,()=>editor.Text,s=>editor.Text=read(s),s=>write(s,editor.Text));
    }

    private void Range(string name,string description,double min,double max,double tick,Func<Settings,double> read,Action<Settings,double> write,Func<double,string> format)
    {
        var slider=new Slider { Minimum=min,Maximum=max,TickFrequency=tick,IsSnapToTickEnabled=true,VerticalAlignment=VerticalAlignment.Center };
        AutomationProperties.SetName(slider,name); AutomationProperties.SetHelpText(slider,description);
        var value=new TextBlock { HorizontalAlignment=HorizontalAlignment.Right,Foreground=Brush("Muted"),Margin=new(0,0,0,2) };
        var editor=new StackPanel();editor.Children.Add(value);editor.Children.Add(slider);
        Add(name,description,editor,()=>format(slider.Value),s=> { double v=read(s);slider.Value=double.IsFinite(v)?v:min;value.Text=format(slider.Value); },s=>write(s,slider.Value));
        slider.ValueChanged+=(_,_)=> { value.Text=format(slider.Value);Changed(); };
    }

    private void Reload(Settings settings)
    {
        autoSave.Stop();
        loading=true;
        try {
            foreach(var row in rows)row.Load(settings);
            bool? enabled=readStartup();startup.IsChecked=enabled??false;startup.IsEnabled=enabled.HasValue;
            if(!enabled.HasValue)startup.ToolTip="현재 실행 환경에서 시작 프로그램 설정에 접근할 수 없습니다.";
            foreach(var row in rows)row.Baseline=row.Read();
        }
        finally { loading=false; }
        Filter();
    }

    private void Changed(bool defer=false)
    {
        if(loading || saving)return;
        autoSave.Stop();
        ShowStatus(HasChanges ? "자동 저장 중…" : "설정은 변경하면 자동으로 저장됩니다.");
        Filter();
        if(loadSettings==null || !HasChanges)return;
        if(defer)autoSave.Start();else TrySave();
    }

    private void Search_Changed(object sender,TextChangedEventArgs e)
    {
        if(Groups==null)return;
        Filter();SettingsScroll.ScrollToTop();
    }

    private void Filter()
    {
        string[] terms=SearchInput.Text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        int count=0;
        foreach(var group in groups) {
            bool any=false;Highlight(group.Heading,group.Name,terms);
            foreach(var row in group.Rows) {
                string value=row.Read(), haystack=row.Name+"\n"+row.Description+"\n"+value;
                bool match=row.Available?.Invoke()!=false && terms.All(term=>haystack.Contains(term,StringComparison.OrdinalIgnoreCase));
                row.Host.Visibility=match?Visibility.Visible:Visibility.Collapsed;
                Highlight(row.Label,row.Name,terms);Highlight(row.Detail,row.Description,terms);Highlight(row.Summary,value,terms);
                row.Detail.Visibility=terms.Any(term=>row.Description.Contains(term,StringComparison.OrdinalIgnoreCase))?Visibility.Visible:Visibility.Collapsed;
                row.Summary.Visibility=terms.Any(term=>value.Contains(term,StringComparison.OrdinalIgnoreCase))?Visibility.Visible:Visibility.Collapsed;
                if(match){any=true;count++;}
            }
            group.Host.Visibility=any?Visibility.Visible:Visibility.Collapsed;
        }
        EmptyMessage.Visibility=count==0?Visibility.Visible:Visibility.Collapsed;
    }

    private void Highlight(TextBlock target,string text,string[] terms)
    {
        target.Inlines.Clear();
        if(terms.Length==0){target.Inlines.Add(new Run(text));return;}
        var pattern=string.Join("|",terms.OrderByDescending(term=>term.Length).Select(Regex.Escape));
        int offset=0;
        foreach(Match match in Regex.Matches(text,pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)) {
            if(match.Index>offset)target.Inlines.Add(new Run(text[offset..match.Index]));
            target.Inlines.Add(new Run(match.Value) { Background=Brush("Mint"),Foreground=Brush("OnAccent"),FontWeight=FontWeights.SemiBold });
            offset=match.Index+match.Length;
        }
        if(offset<text.Length)target.Inlines.Add(new Run(text[offset..]));
    }

    private void ShowStatus(string text,bool error=false)
    {
        Status.Text=text;Status.Foreground=Brush(error?"Danger":"Muted");
        RetryButton.Visibility=error && HasChanges ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Save_Click(object sender,RoutedEventArgs e) => TrySave();

    internal bool TrySave()
    {
        autoSave.Stop();
        if(saving)return false;
        if(!HasChanges)return true;
        if(loadSettings==null || saveSettings==null){ShowStatus("설정을 저장할 수 없습니다.",true);return false;}
        saving=true;
        try {
            var next=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(loadSettings()))!;
            var changed=rows.Where(row=>row.Changed).Select(row=>(Row:row,Value:row.Read())).ToArray();
            foreach(var item in changed)item.Row.Write(next);
            SettingsEditing.Validate(next);
            if(!saveSettings(next)){ShowStatus("자동 저장하지 못했습니다. 변경 내용을 유지했습니다. 다시 시도해 주세요.",true);return false;}
            var startupRow=rows.Single(row=>row.Name=="Windows 로그인 시 자동 실행");
            if(startup.IsEnabled && startupRow.Changed)writeStartup?.Invoke(startup.IsChecked==true);
            // Keep the active editor, caret, IME composition and slider drag intact.
            foreach(var item in changed)item.Row.Baseline=item.Value;
            ShowStatus("자동 저장됨");return true;
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception) {
            ShowStatus(ex.Message,true);return false;
        }
        finally { saving=false; }
    }
}
