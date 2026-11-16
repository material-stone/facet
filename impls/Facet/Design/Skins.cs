namespace Facet.Design;

internal sealed record SkinOption(string Id,string Name,bool HasVisuals=false,ExternalSkin? External=null);

internal static class Skins
{
    internal const string DefaultId="default";
    // Only the default (dark) skin is built in; every other skin is a package (ADR 0100).
    private static readonly SkinOption[] BuiltIn=[new(DefaultId,"기본")];
    // Extra package folders read after the data folder; checks use it to reach registry/skins.
    internal static readonly List<string> SharedFolders=[];
    private static string? externalRoot;
    internal static IReadOnlyList<SkinOption> Options { get; private set; }=BuiltIn;
    // Set with the tokens at startup and read when windows and tiles are built.
    internal static ThemeVisuals? Visuals { get; private set; }
    private static ResourceDictionary? applied;
    internal static void UseFolder(string root) { externalRoot=root;Rescan(); }
    // Skin folders can change while the app runs. A built-in id always wins.
    internal static IReadOnlyList<SkinOption> Rescan()
    {
        var external=(externalRoot==null ? [] : ExternalSkins.Discover(externalRoot)).Concat(SharedFolders.SelectMany(ExternalSkins.Discover)).DistinctBy(skin=>skin.Id).ToList();
        return Options=[..BuiltIn,..external.Where(skin=>BuiltIn.All(option=>option.Id!=skin.Id)).Select(skin=>new SkinOption(skin.Id,skin.Name,skin.Theme!=null,skin))];
    }
    internal static string Resolve(string? id)=>Options.FirstOrDefault(option=>option.Id==id)?.Id ?? DefaultId;
    private static string Folder(string id)=>$"{char.ToUpperInvariant(id[0])}{id[1..]}";
    internal static ResourceDictionary Load(string id)=>Options.First(option=>option.Id==id).External is { } external ? ExternalSkins.Dictionary(external) : new() { Source=new Uri($"pack://application:,,,/Facet;component/Design/Skins/{Folder(id)}.xaml") };
    internal static ThemeVisuals? LoadVisuals(string id)
    {
        var option=Options.First(option=>option.Id==id);
        return option.External is { } external ? ExternalSkins.Visuals(external) : option.HasVisuals ? ThemeVisuals.Load(Folder(id)) : null;
    }
    // Must run before the first window: StaticResource references keep the brush they first resolved.
    // The default skin is Tokens.xaml itself; a later merged dictionary overrides its keys.
    internal static void Apply(Application application,string? id)
    {
        string skin=Resolve(id);
        if(applied!=null) application.Resources.MergedDictionaries.Remove(applied);
        applied=skin!=DefaultId ? Load(skin) : null;
        if(applied!=null) application.Resources.MergedDictionaries.Add(applied);
        Visuals=LoadVisuals(skin);
        if(Visuals is { PanelMarkLength:>0 }) ThemePanels.Register();
        application.Resources[Facet.Plugins.WidgetTheme.HudKey]=Visuals is { HudWidgets:true };
        application.Resources[Facet.Plugins.WidgetTheme.SoftHudKey]=Visuals is { SoftHud:true };
        application.Resources[Facet.Plugins.WidgetTheme.CatHudKey]=Visuals is { CatHud:true };
    }
    // Styles already built keep the brushes they resolved, so a running switch also needs fresh shared styles and fresh views.
    internal static void Switch(Application application,string? id)
    {
        var dictionaries=application.Resources.MergedDictionaries;
        int components=dictionaries.ToList().FindIndex(dictionary=>dictionary.Source?.OriginalString.EndsWith("Components.xaml",StringComparison.Ordinal)==true);
        Apply(application,id);
        if(components>=0) dictionaries[components]=new ResourceDictionary { Source=new Uri("pack://application:,,,/Facet;component/Design/Components.xaml") };
    }
}
