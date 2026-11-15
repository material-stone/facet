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
    // Side tabs in display order; null draws a divider between tab bunches.
    private static readonly (string Name, string[] Groups)?[] Sections = [
        ("일반", ["앱 시작","오버레이"]), ("탭", ["탭"]), ("단축키", ["단축키"]), null,
        ("스킨", ["스킨"]), ("플러그인", ["플러그인"]), null,
        ("데이터", ["데이터"])];
    private readonly Dictionary<string, RadioButton> sectionTabs = [];
    private string section = "일반";
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
        BuildSections();
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
        SizeChanged += (_,e) => Arrange(e.NewSize.Width < 600);
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
    internal PackagesPanel PluginPackages { get; } = new(PackageKind.Plugin);
    internal SkinsPanel SkinPanel { get; } = new();
    // After a package install, so the skin list shows it without reopening the tab.
    internal void ReloadSkins() { if (loadSettings != null && !HasChanges) Reload(loadSettings()); }

    private void BuildRows()
    {
        Group("앱 시작");
        Toggle("직접 실행 시 오버레이 표시", "앱을 직접 실행하거나 다시 실행할 때 오버레이를 엽니다. 단축키로 여는 동작은 그대로 사용할 수 있습니다.", s => s.ShowOverlayOnLaunch, (s,v) => s.ShowOverlayOnLaunch=v);
        startup = Toggle("Windows 로그인 시 자동 실행", "Windows에 로그인하면 오버레이를 열지 않고 트레이에서 앱을 시작합니다.", _ => false, (_,_) => { });

        Group("탭");
        Range("격자 칸 크기", "위젯을 배치하는 칸 한 변의 크기입니다(100% 확대 기준). 칸이 커지면 위젯도 함께 커집니다.", WorkspaceGrid.MinCellSize, WorkspaceGrid.MaxCellSize, 4, s=>WorkspaceGrid.CellSize(s), (s,v)=>s.GridCellSize=(int)v, v=>$"{v:0} px");
        var gridStyles = new ComboBox { ItemsSource=WorkspaceGrid.Styles.Select(style=>new { style.Id, style.Name }).ToList(), DisplayMemberPath="Name", SelectedValuePath="Id" };
        Add("격자 모양", "위젯 편집 중 빈 칸을 보여 주는 방식입니다. 점은 칸 모서리, 선은 칸 경계, 십자는 모서리에 + 표시를 그립니다.", gridStyles,
            ()=>WorkspaceGrid.Styles.FirstOrDefault(style=>style.Id==gridStyles.SelectedValue as string).Name ?? "",
            s=>gridStyles.SelectedValue=WorkspaceGrid.Style(s), s=>s.GridStyle=gridStyles.SelectedValue as string ?? WorkspaceGrid.Dots);
        gridStyles.SelectionChanged += (_,_)=>Changed();
        Add("탭 구성", "설정 외의 탭을 추가·삭제하고 순서·표시·이름을 바꿉니다. 위젯 배치는 각 탭의 위젯 편집 버튼에서 바꿉니다. 변경은 바로 저장됩니다.", WorkspacePanel, ()=>"", _=>{}, _=>{});

        Group("스킨");
        var skins = SkinPanel;
        Add("사용할 스킨", "앱의 색, 배경, 위젯 틀을 함께 바꿉니다. 고르면 바로 적용됩니다. 아래 목록에서 공개된 스킨을 설치하거나 zip 파일로 설치할 수 있습니다.", skins,
            ()=>Design.Skins.Options.FirstOrDefault(option=>option.Id==skins.SelectedSkin)?.Name ?? "",
            s=>skins.Reload(s.Skin),
            s=>s.Skin=skins.SelectedSkin,bare:true);
        // Only the chosen skin's own options appear, right under the list.
        var skinArea=new StackPanel();
        var skinBoxes=new List<(Design.ThemeOption Option,CheckBox Box)>();
        string ChosenSkin()=>skins.SelectedSkin;
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
        Add("스킨 옵션", "고른 스킨이 제공하는 옵션입니다. 옵션이 있는 스킨에서만 나타나고, 바꾸면 바로 적용됩니다.", skinArea,
            ()=>string.Join(" · ",skinBoxes.Select(item=>item.Option.Name+(item.Box.IsChecked==true ? " 켬" : " 끔"))),
            ShowSkinOptions,
            s=>{
                var next=new Dictionary<string,bool>(s.SkinOptions ?? []);
                foreach(var (option,box) in skinBoxes) next[Design.ThemeVisuals.OptionKey(ChosenSkin(),option.Id)]=box.IsChecked==true;
                s.SkinOptions=next;
            });
        var skinOptionsRow=rows[^1];
        skinOptionsRow.Available=()=>skinBoxes.Count>0;
        skins.SelectionChanged += ()=>{
            if(!loading) { ShowSkinOptions(loadSettings?.Invoke() ?? new Settings());skinOptionsRow.Baseline=skinOptionsRow.Read(); }
            Changed();
        };

        Group("오버레이");
        Range("투명도", "오버레이 배경의 투명도입니다. 0%는 완전 불투명, 100%는 완전 투명입니다. 글자와 입력창은 유지합니다.", 0, 100, 1, OverlayAppearance.Transparency, (s,v)=>s.OverlayTransparencyPercent=v, v=>$"{v:0}%");
        var colors = new OverlayColorPalette();
        Add("배경색", "오버레이 배경색을 색상 팔레트에서 선택합니다. 투명도와 별도로 저장됩니다.", colors,
            ()=>colors.SelectedColor, s=>colors.SelectedColor=s.OverlayBackgroundColor, s=>s.OverlayBackgroundColor=colors.SelectedColor);
        colors.SelectionChanged += (_,_)=>Changed();
        Range("나타나는 시간", "오버레이가 나타나는 데 걸리는 시간입니다. 0ms는 즉시 표시합니다.", 0, 2000, 20, s=>s.OverlayShowMilliseconds, (s,v)=>s.OverlayShowMilliseconds=(int)v, v=>$"{v:0} ms");
        Range("사라지는 시간", "오버레이가 사라지는 데 걸리는 시간입니다. 0ms는 즉시 숨깁니다.", 0, 2000, 20, s=>s.OverlayHideMilliseconds, (s,v)=>s.OverlayHideMilliseconds=(int)v, v=>$"{v:0} ms");

        Group("플러그인");
        Add("설치된 플러그인", "켜고 끄기, 플러그인별 설정, 다시 불러오기, 삭제를 합니다. 켠 플러그인의 위젯은 각 탭의 위젯 편집에서 배치합니다. 바뀐 내용은 바로 저장됩니다.", PluginPanel, ()=>"", _=>{}, _=>{}, wide:true);
        Add("플러그인 받기", "공개 목록에서 플러그인을 골라 설치하거나 zip 파일로 설치합니다. 플러그인은 Facet과 같은 권한으로 실행되니 믿을 수 있는 것만 설치하세요.", PluginPackages, ()=>"", _=>{}, _=>{}, wide:true);

        Group("단축키");
        Text("오버레이 열기·닫기", "오버레이를 열고 닫는 전역 단축키입니다. 예: Win+Alt+O", s=>s.OverlayHotkey, (s,v)=>s.OverlayHotkey=v.Trim());
        Text("빠른 실행", "프로그램 이름을 입력해 바로 실행하는 창을 여는 단축키입니다. 예: Win+Alt+Space", s=>s.QuickLaunchHotkey, (s,v)=>s.QuickLaunchHotkey=v.Trim());
        Text("Facet 종료", "Facet을 완전히 종료하는 단축키입니다. 예: Win+Alt+End", s=>s.ExitHotkey, (s,v)=>s.ExitHotkey=v.Trim());

        Group("데이터");
        Add("백업", "일정·설정·플러그인 기록을 zip 파일 하나로 저장합니다. 같은 Windows 계정에서만 복원할 수 있습니다.", BackupEditor(), ()=>"", _=>{}, _=>{});
        Add("복원", "백업 파일을 고르면 Facet을 다시 시작할 때 그 시점으로 되돌립니다. 지금 기록은 옆 폴더에 남겨 둡니다.", RestoreEditor(), ()=>"", _=>{}, _=>{});


    }

    private void BuildSections()
    {
        var order=Sections.Where(item=>item!=null).SelectMany(item=>item!.Value.Groups).ToList();
        foreach(var group in groups.OrderBy(group=>order.IndexOf(group.Name)).ToArray()) { Groups.Children.Remove(group.Host);Groups.Children.Add(group.Host); }
        foreach(var item in Sections) {
            if(item is not { } entry) { SectionNav.Children.Add(new Border { Height=1,Background=Brush("Edge"),Margin=new(4,6,4,10) });continue; }
            var tab=new RadioButton { Content=entry.Name,GroupName="SettingsSections",Style=(Style)FindResource("SideTab"),IsChecked=entry.Name==section };
            AutomationProperties.SetName(tab,entry.Name);
            tab.Checked+=(_,_)=>ShowSection(entry.Name);
            sectionTabs[entry.Name]=tab;SectionNav.Children.Add(tab);
        }
    }

    // Picking a tab leaves search so the tab's own rows show.
    internal void ShowSection(string name)
    {
        if(!sectionTabs.TryGetValue(name,out var tab))return;
        section=name;
        if(tab.IsChecked!=true)tab.IsChecked=true;
        if(SearchInput.Text.Length>0)SearchInput.Text="";else Filter();
        SettingsScroll.ScrollToTop();
    }

    internal string CurrentSection => section;

    // A narrow panel moves the side tabs into one row above the settings so rows keep their width.
    private bool? narrow;
    private void Arrange(bool compact)
    {
        if(narrow==compact)return;
        narrow=compact;
        Grid.SetRowSpan(SectionFrame,compact?1:3);Grid.SetColumnSpan(SectionFrame,compact?2:1);
        SectionFrame.BorderThickness=compact?new(0,0,0,1):new(0,0,1,0);
        SectionFrame.Padding=compact?new(0,0,0,6):new(0,0,16,0);SectionFrame.Margin=compact?new(0,0,0,8):new(0,0,20,0);
        foreach(var element in new FrameworkElement[]{SettingsScroll,StatusBar}) { Grid.SetColumn(element,compact?0:1);Grid.SetColumnSpan(element,compact?2:1); }
        Grid.SetRow(SettingsScroll,compact?1:0);Grid.SetRowSpan(SettingsScroll,compact?1:2);
        DockPanel.SetDock(SearchInput,compact?Dock.Left:Dock.Top);
        SearchInput.Width=compact?150:double.NaN;SearchInput.VerticalAlignment=compact?VerticalAlignment.Center:VerticalAlignment.Stretch;SearchInput.Margin=compact?new(0,0,12,0):new(0,0,0,16);
        SectionNav.Orientation=compact?Orientation.Horizontal:Orientation.Vertical;
        SectionScroll.HorizontalScrollBarVisibility=compact?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled;
        SectionScroll.VerticalScrollBarVisibility=compact?ScrollBarVisibility.Disabled:ScrollBarVisibility.Auto;
        foreach(var child in SectionNav.Children.OfType<FrameworkElement>()) {
            if(child is Border divider) { divider.Width=compact?1:double.NaN;divider.Height=compact?double.NaN:1;divider.Margin=compact?new(4,6,8,10):new(4,6,4,10); }
            else child.Margin=compact?new(0,0,4,0):new(0,0,0,4);
        }
    }

    private FrameworkElement BackupEditor()
    {
        var status=new TextBlock { FontSize=12,Foreground=Brush("Muted"),TextWrapping=TextWrapping.Wrap,Margin=new(0,6,0,0) };
        var make=new Button { Content="지금 백업하기",Margin=new(0,0,8,0) };
        var open=new Button { Content="백업 폴더 열기" };
        make.Click+=(_,_)=>{
            if(app==null) return;
            try {
                var (zip,skipped)=DataBackup.Create(app.Store.Root,DataBackup.DefaultFolder);
                status.Text="저장했습니다: "+zip+(skipped>0 ? $" (사용 중인 파일 {skipped}개 제외)" : "");
            }
            catch(Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text="백업하지 못했습니다: "+ex.Message; }
        };
        open.Click+=(_,_)=>{ Directory.CreateDirectory(DataBackup.DefaultFolder); Process.Start(new ProcessStartInfo(DataBackup.DefaultFolder){ UseShellExecute=true })?.Dispose(); };
        var buttons=new WrapPanel();buttons.Children.Add(make);buttons.Children.Add(open);
        var panel=new StackPanel();panel.Children.Add(buttons);panel.Children.Add(status);
        return panel;
    }

    private FrameworkElement RestoreEditor()
    {
        var status=new TextBlock { FontSize=12,Foreground=Brush("Muted"),TextWrapping=TextWrapping.Wrap,Margin=new(0,6,0,0) };
        var pick=new Button { Content="백업 파일 고르기…",Margin=new(0,0,8,0) };
        var quit=new Button { Content="Facet 종료",Visibility=Visibility.Collapsed };
        pick.Click+=(_,_)=>{
            if(app==null) return;
            var dialog=new Microsoft.Win32.OpenFileDialog { Filter="Facet 백업 (*.zip)|*.zip",InitialDirectory=Directory.Exists(DataBackup.DefaultFolder) ? DataBackup.DefaultFolder : "" };
            if(dialog.ShowDialog()!=true) return;
            try {
                DataBackup.Stage(app.Store.Root,dialog.FileName);
                status.Text="준비했습니다. Facet을 종료하고 다시 실행하면 이 백업으로 복원됩니다.";quit.Visibility=Visibility.Visible;
            }
            catch(Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { status.Text="복원할 수 없습니다: "+ex.Message; }
        };
        quit.Click+=(_,_)=>app?.Exit();
        var buttons=new WrapPanel();buttons.Children.Add(pick);buttons.Children.Add(quit);
        var panel=new StackPanel();panel.Children.Add(buttons);panel.Children.Add(status);
        return panel;
    }

    private void Group(string name)
    {
        var host=new StackPanel { Margin=new(0,0,0,20) };
        var heading=new TextBlock { FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,8) };
        host.Children.Add(heading);
        host.Children.Add(new Border { Height=1,Background=Brush("Edge"),Margin=new(0,0,0,4) });
        Groups.Children.Add(host); groups.Add((host,heading,name,[]));
    }

    // bare hides the name so a self-describing panel (the skin list) takes the whole row; the name stays for search and automation.
    private void Add(string name,string description,FrameworkElement editor,Func<string> read,Action<Settings> load,Action<Settings> write,bool wide=false,bool bare=false)
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
        right.Children.Add(editor);right.Children.Add(summary);
        // A wide editor spans the row under the label and keeps its first line free for right-aligned commands.
        if(wide || bare) Grid.SetColumnSpan(right,2); else Grid.SetColumn(right,1);
        if(bare) left.Visibility=Visibility.Collapsed;
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
        var shown=Sections.FirstOrDefault(item=>item?.Name==section)?.Groups ?? [];
        foreach(var group in groups) {
            if(terms.Length==0 && !shown.Contains(group.Name)) { group.Host.Visibility=Visibility.Collapsed;continue; }
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
