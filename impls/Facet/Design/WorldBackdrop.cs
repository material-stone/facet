using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Facet.Design;

// The world page's weather states. Pure so the mapping can be checked without a window.
internal static class WorldWeather
{
    internal static string Key(int? code)=>code switch {
        0 => "clear",
        1 or 2 => "partly",
        3 => "cloud",
        45 or 48 => "fog",
        71 or 73 or 75 or 77 or 85 or 86 => "snow",
        51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 or 95 or 96 or 99 => "rain",
        _ => "partly"
    };
}

// A layered WPF window cannot show an HWND child, so the world lives in its own window kept right below the overlay.
// It never takes input or focus: the overlay catches every click and forwards "next" with where it was pressed.
internal sealed class WorldBackdrop : IDisposable
{
    private const int GwlExStyle=-20,WsExNoActivate=0x08000000,WsExToolWindow=0x80,WsExTransparent=0x20;
    private const uint SwpNoActivate=0x10,SwpNoOwnerZOrder=0x200;
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }

    private static readonly TimeSpan WeatherInterval=TimeSpan.FromMinutes(30);
    // Enforced by the host, so a skin page cannot opt out. The page text follows this, and a later policy can only narrow it.
    private const string Lock="<!doctype html><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data: blob:; font-src data:; media-src data: blob:; worker-src blob:\">\n";
    // A dead proxy and no direct UDP also stop what the policy does not cover, such as WebRTC.
    private const string NoNetwork="--proxy-server=http://127.0.0.1:9 --proxy-bypass-list=<-loopback> --webrtc-ip-handling-policy=disable_non_proxied_udp --force-webrtc-ip-handling-policy";
    // The window never takes input, so the page never gets a user gesture; without this Chromium keeps its AudioContext suspended.
    internal const string Autoplay="--autoplay-policy=no-user-gesture-required";
    internal const string BrowserArguments=NoNetwork+" "+Autoplay;
    private readonly string page;
    internal Action<string>? ProbeForCheck;
    internal string? DataFolderForCheck;
    internal Exception? FailureForCheck;
    private readonly string[] scenes;
    private readonly Func<CancellationToken,Task<WeatherSnapshot>> fetchWeather;
    private readonly Window window;
    private readonly WebView2 view=new() { DefaultBackgroundColor=System.Drawing.Color.Black };
    private readonly DispatcherTimer weatherTimer=new() { Interval=WeatherInterval };
    private readonly CancellationTokenSource closing=new();
    private bool ready,requestedVisible,disposed;
    private string weather="partly";
    internal event Action? Failed;
    // The page's { type:'action' } message; returns the actionResult to send back (ADR 0110).
    internal Func<string,Task<string>>? ActionRequested;
    private string? widgets;
    private string? options;
    internal string? OptionsForCheck => options;

    internal WorldBackdrop(string page,string[] scenes,Func<CancellationToken,Task<WeatherSnapshot>> fetchWeather)
    {
        this.page=page;this.scenes=scenes;this.fetchWeather=fetchWeather;
        window=new Window {
            WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Topmost=true,
            Focusable=false,Background=Brushes.Black,Title="Facet world",Content=view,Left=-32000,Top=-32000,Width=1,Height=1
        };
        window.SourceInitialized+=(_,_) => {
            var handle=new WindowInteropHelper(window).Handle;
            long style=GetWindowLongPtr(handle,GwlExStyle).ToInt64();
            SetWindowLongPtr(handle,GwlExStyle,new IntPtr(style|WsExNoActivate|WsExToolWindow|WsExTransparent));
        };
        weatherTimer.Tick+=(_,_) => _=RefreshWeatherAsync();
    }

    // Starts only when first shown, so a skin that is never opened costs no browser process.
    internal void SetVisible(bool visible,Window overlay)
    {
        if(disposed) return;
        requestedVisible=visible;
        if(!visible) { weatherTimer.Stop();window.Hide();Post(new { visible=false });return; }
        window.Show();
        Place(overlay);
        if(view.CoreWebView2==null && !starting) { starting=true;_=StartAsync(); }
        Post(new { visible=true });
        _=RefreshWeatherAsync();weatherTimer.Start();
    }
    private bool starting;

    // Called again whenever the overlay's topmost state or screen changes: the world must stay directly under it.
    internal void Place(Window overlay)
    {
        if(disposed || !window.IsVisible) return;
        window.Topmost=overlay.Topmost;
        var handle=new WindowInteropHelper(overlay).Handle;
        if(!GetWindowRect(handle,out var rect)) return;
        SetWindowPos(new WindowInteropHelper(window).Handle,handle,rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top,SwpNoActivate|SwpNoOwnerZOrder);
    }
    // x and y are the click as fractions of the overlay, so a page can react where it was pressed (ADR 0107).
    internal void Next(double x,double y)=>Post(new { next=true,x=Math.Round(Math.Clamp(x,0,1),4),y=Math.Round(Math.Clamp(y,0,1),4) });
    // The skin-drawn widgets of the shown tab; an unchanged list is not sent again, and a page that becomes ready gets the latest.
    internal void Widgets(string message)
    {
        if(message==widgets) return;
        widgets=message;PostJson(message);
    }
    // The skin's own options as { options: { id: bool } }, resent when they change and when the page becomes ready.
    internal void Options(IReadOnlyDictionary<string,bool> values)
    {
        string message=JsonSerializer.Serialize(new { options=values });
        if(message==options) return;
        options=message;PostJson(message);
    }
    internal void Pointer(string message)=>PostJson(message);

    private async Task StartAsync()
    {
        try {
            string data=DataFolderForCheck ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Facet","world-webview");
            var environment=await CoreWebView2Environment.CreateAsync(userDataFolder:data,options:new CoreWebView2EnvironmentOptions(additionalBrowserArguments:BrowserArguments));
            if(disposed) return;
            await view.EnsureCoreWebView2Async(environment);
            if(disposed) return;
            var core=view.CoreWebView2;
            core.Settings.AreDevToolsEnabled=false;core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreDefaultScriptDialogsEnabled=false;
            core.Settings.AreHostObjectsAllowed=false;core.Settings.IsStatusBarEnabled=false;core.Settings.IsZoomControlEnabled=false;core.Settings.AreBrowserAcceleratorKeysEnabled=false;
            // Only the skin's page may load.
            core.NavigationStarting+=(_,e) => { if(!e.Uri.StartsWith("data:",StringComparison.Ordinal) && !e.Uri.StartsWith("about:",StringComparison.Ordinal)) e.Cancel=true; };
            core.NewWindowRequested+=(_,e) => e.Handled=true;
            core.WebMessageReceived+=(_,e) => Receive(e.WebMessageAsJson);
            core.ProcessFailed+=(_,_) => Fail();
            core.NavigateToString(Lock+page);
        }
        catch(Exception ex) when(ex is WebView2RuntimeNotFoundException or COMException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { FailureForCheck=ex;Fail(); }
    }
    private void Receive(string json)
    {
        string? type;
        try {
            using var message=JsonDocument.Parse(json);type=message.RootElement.GetProperty("type").GetString();
            if(type=="probe") { ProbeForCheck?.Invoke(message.RootElement.GetProperty("message").GetString() ?? "");return; }
        }
        catch(Exception ex) when(ex is JsonException or KeyNotFoundException or InvalidOperationException) { return; }
        if(type=="loaded") view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { scenes }));
        else if(type=="ready") { ready=true;Post(new { weather,visible=requestedVisible });if(options!=null) PostJson(options);if(widgets!=null) PostJson(widgets);ProbeForCheck?.Invoke("ready"); }
        else if(type=="action" && ActionRequested is { } act) _=ReplyAsync(act,json);
        else if(type=="failed") Fail();
    }
    private void Post(object message)=>PostJson(JsonSerializer.Serialize(message));
    private void PostJson(string json)
    {
        if(ready && !disposed && view.CoreWebView2!=null) view.CoreWebView2.PostWebMessageAsJson(json);
    }
    private async Task ReplyAsync(Func<string,Task<string>> act,string json)
    {
        try { PostJson(await act(json)); }
        catch(Exception ex) when(ex is not OutOfMemoryException) { }
    }
    private async Task RefreshWeatherAsync()
    {
        try {
            var snapshot=await fetchWeather(closing.Token);
            weather=WorldWeather.Key(snapshot.Code);
            Post(new { weather });
        }
        // Weather is decoration here: a failed fetch keeps the sky it has.
        catch(Exception ex) when(ex is not OutOfMemoryException) { }
    }
    private void Fail()
    {
        if(disposed) return;
        Dispose();
        Failed?.Invoke();
    }
    public void Dispose()
    {
        if(disposed) return;
        disposed=true;
        weatherTimer.Stop();closing.Cancel();
        window.Close();view.Dispose();closing.Dispose();
    }
}
