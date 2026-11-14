using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Facet;

internal static partial class Program
{
    private const string WeatherJson = """{"current":{"time":1792801800,"temperature_2m":14.5,"relative_humidity_2m":90,"weather_code":3,"is_day":1}}""";

    private static void CompleteWeatherTask(Task task)
    {
        var timeout=Stopwatch.StartNew();
        while(!task.IsCompleted && timeout.ElapsedMilliseconds<35000) Pump(10);
        if(!task.IsCompleted) throw new TimeoutException("Weather check did not complete.");
        task.GetAwaiter().GetResult();
    }

    private static Rect[] WeatherLayoutBounds(OverlayDashboardView view)
    {
        view.Measure(new Size(800,360));view.Arrange(new Rect(0,0,800,360));view.UpdateLayout();
        return new[]{"WeatherRefresh","WeatherGroup","WeatherIcon","LocationText","ConditionText","TemperatureText","HumidityGroup","WeatherStatusHost","ClockGroup","AgendaSection"}
            .Select(name=> {var element=(FrameworkElement)view.FindName(name);return element.TransformToAncestor(view).TransformBounds(new Rect(0,0,element.ActualWidth,element.ActualHeight));}).ToArray();
    }

    private sealed class WeatherHttpHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) => send(request,cancellationToken);
    }

    private static void CheckWeather(string qa)
    {
        var location=new WeatherLocation(45.12346,-75.65432,"Testtown");
        var sample=WeatherClient.ParseWeather(WeatherJson,location);
        bool Rejects(Action action) {
            try { action(); return false; }
            catch(Exception ex) when(ex is JsonException or InvalidDataException or WeatherLocationUnavailableException) { return true; }
        }
        Assert(location.Area=="Testtown" && sample.Temperature==14.5 && sample.Humidity==90 && sample.Description=="흐림","weather parser keeps current temperature humidity and condition");
        var zero=WeatherClient.ParseWeather(WeatherJson.Replace("14.5","0").Replace(":90",":0"),WindowsWeatherLocation.FromCoordinates(0,0));
        Assert(zero.Location.Latitude==0 && zero.Location.Longitude==0 && zero.Temperature==0 && zero.Humidity==0,"zero coordinates temperature and humidity are valid values");
        var unknown=WeatherClient.ParseWeather(WeatherJson.Replace("14.5","null").Replace(":90",":null").Replace("\"weather_code\":3","\"weather_code\":null"),location);
        Assert(unknown.Temperature==null && unknown.Humidity==null && unknown.Icon==WeatherIconKind.Unknown,"missing weather remains unknown without zero conversion or guessed icon");
        Assert(new[]{double.NaN,double.PositiveInfinity,-91,91}.All(latitude=>Rejects(()=>WindowsWeatherLocation.FromCoordinates(latitude,0))) && Rejects(()=>WindowsWeatherLocation.FromCoordinates(0,181)),"invalid Windows coordinates are rejected before weather requests");
        var seoul=WeatherRegions.All.Single(region=>region.DisplayName=="서울특별시");
        Assert(WeatherRegions.All.Count==245 && WeatherRegions.Options[0].Id=="" && WeatherRegions.All.Select(r=>r.Id).Distinct().Count()==245 && seoul.Latitude is >37 and <38 && seoul.Longitude is >126 and <128,"bundled Korean regions load validated unique coordinates and a Windows automatic option");
        int automaticCalls=0;
        Task<WeatherLocation> Auto(CancellationToken token) {automaticCalls++;return Task.FromResult(location);}
        var manualLocation=WeatherRegions.ResolveAsync(seoul.Id,CancellationToken.None,Auto);CompleteWeatherTask(manualLocation);
        Assert(automaticCalls==0 && manualLocation.Result.Area=="서울특별시" && manualLocation.Result.Latitude==seoul.Latitude,"selected region bypasses Windows and supplies its Korean name and coordinates");
        var autoLocation=WeatherRegions.ResolveAsync("",CancellationToken.None,Auto);CompleteWeatherTask(autoLocation);
        Assert(automaticCalls==1 && autoLocation.Result==location && KoreanPlaces.Display(WindowsWeatherLocation.FromCoordinates(0,0))=="현재 위치","automatic weather keeps supplied coordinates and leaves unmatched locations neutral");
        foreach(var point in new[]{(37.5665,126.9780,"서울특별시"),(37.4979,127.0276,"서울특별시"),
            (37.4201,127.1262,"경기도"),(35.1796,129.0756,"부산광역시"),(37.4563,126.7052,"인천광역시"),
            (36.3504,127.3845,"대전광역시"),(36.4800,127.2890,"세종특별자치시"),(33.4996,126.5312,"제주특별자치도")}) {
            var named=WindowsWeatherLocation.FromCoordinates(point.Item1,point.Item2);
            Assert(named.Area==point.Item3 && named.CountryCode=="KR" && named.Latitude==point.Item1 && named.Longitude==point.Item2,
                "Windows coordinate containment supplies Korean province without moving the weather point: "+point.Item3);
        }
        double[][][] withHole=[[[0,0],[10,0],[10,10],[0,10],[0,0]],[[3,3],[7,3],[7,7],[3,7],[3,3]]];
        Assert(WeatherAdministrativeRegions.ContainsPolygon(withHole,1,1) && !WeatherAdministrativeRegions.ContainsPolygon(withHole,5,5) &&
            !WeatherAdministrativeRegions.ContainsPolygon(withHole,5,11) && WeatherAdministrativeRegions.ContainsPolygon(withHole,5,0),
            "administrative containment includes outer edges and excludes holes and exterior points");
        var reversed=withHole.Select(ring=>ring.Reverse().ToArray()).ToArray();
        Assert(WeatherAdministrativeRegions.ContainsPolygon(reversed,1,1) && !WeatherAdministrativeRegions.ContainsPolygon(reversed,5,5),"region containment is independent of polygon winding direction");
        Assert(Rejects(()=>WeatherRegions.ResolveAsync("missing-region",CancellationToken.None,Auto)),"unknown saved region never silently selects a different city");
        Assert(new[]{"[]","{}",WeatherJson.Replace(":90",":101"),WeatherJson.Replace(":90",":1.5"),WeatherJson.Replace(":1792801800",":null"),WeatherJson.Replace("\"is_day\":1","\"is_day\":2"),new string(' ',65537)}.All(json=>Rejects(()=>WeatherClient.ParseWeather(json,location))),"weather rejects malformed out of range fractional missing timestamp and oversized responses");

        Assert((sample with {Code=0,IsDay=true}).Icon==WeatherIconKind.Sunny && (sample with {Code=0,IsDay=false}).Icon==WeatherIconKind.ClearNight && (sample with {Code=2,IsDay=false}).Icon==WeatherIconKind.PartlyCloudyNight,"clear and partly cloudy icons distinguish day and night");
        Assert(new[]{51,53,55,56,57,61,63,65,66,67,80,81,82}.All(code=>(sample with {Code=code}).Icon==WeatherIconKind.Rain) && new[]{71,73,75,77,85,86}.All(code=>(sample with {Code=code}).Icon==WeatherIconKind.Snow) && new[]{95,96,99}.All(code=>(sample with {Code=code}).Icon==WeatherIconKind.Thunder),"WMO drizzle rain snow and thunder codes select their corresponding icons");
        var gallery=new System.Windows.Controls.Primitives.UniformGrid {Columns=3,Width=600,Background=(Brush)Application.Current.FindResource("Panel")};
        foreach(var kind in Enum.GetValues<WeatherIconKind>().Where(k=>k!=WeatherIconKind.Unknown)) {
            var bitmap=(BitmapSource)WeatherIcons.Load(kind)!;
            var bgra=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);
            var pixels=new byte[bgra.PixelWidth*bgra.PixelHeight*4];bgra.CopyPixels(pixels,bgra.PixelWidth*4,0);
            Assert(bitmap.IsFrozen && bitmap.PixelWidth==168 && pixels[3]==0 && pixels[^1]==0 && Enumerable.Range(0,pixels.Length/4).Any(i=>pixels[i*4+3]>0),"generated weather icon loads frozen with real alpha and visible pixels: "+kind);
            var cell=new StackPanel {Width=200,Height=140,Margin=new Thickness(0,8,0,0)};
            cell.Children.Add(new Image {Source=bitmap,Width=84,Height=84,Stretch=Stretch.Uniform});
            cell.Children.Add(new TextBlock {Text=kind.ToString(),TextAlignment=TextAlignment.Center,Margin=new Thickness(0,8,0,0)});gallery.Children.Add(cell);
        }
        Assert(WeatherIcons.Load(WeatherIconKind.Unknown)==null,"unknown weather never receives a misleading condition icon");
        gallery.Measure(new Size(600,double.PositiveInfinity));gallery.Arrange(new Rect(gallery.DesiredSize));gallery.UpdateLayout();
        SaveWeatherImage(gallery,new Size(600,gallery.ActualHeight),Path.Combine(qa,"weather-icons.png"));

        var requested=new List<Uri>();bool onlyGets=true;
        using(var http=new HttpClient(new WeatherHttpHandler((request,token)=> {
            token.ThrowIfCancellationRequested();requested.Add(request.RequestUri!);
            onlyGets &= request.Method==HttpMethod.Get && request.Content==null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent(WeatherJson,Encoding.UTF8,"application/json")});
        }))) {
            var previous=CultureInfo.CurrentCulture;
            try {
                CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
                var task=new WeatherClient(http,_=>Task.FromResult(location)).FetchAsync(CancellationToken.None);CompleteWeatherTask(task);
                Assert(task.Result.Temperature==14.5 && requested.Count==1 && onlyGets && requested[0].Host=="api.open-meteo.com" && requested[0].Scheme=="https" && requested[0].Query.Contains("latitude=45.1235&longitude=-75.6543") && requested[0].Query.Contains("temperature_unit=celsius") && !requested[0].Query.Contains("Testtown"),"weather client sends one invariant coordinate GET and never performs an IP lookup");
                int before=requested.Count;bool failed=false;
                try {CompleteWeatherTask(new WeatherClient(http,_=>Task.FromException<WeatherLocation>(new WeatherLocationUnavailableException("설정에서 지역을 선택해 주세요."))).FetchAsync(CancellationToken.None));}
                catch(WeatherLocationUnavailableException) {failed=true;}
                Assert(failed && requested.Count==before,"denied or missing Windows location never contacts weather or IP services");
            }
            finally {CultureInfo.CurrentCulture=previous;}
        }
        int calls=0;bool fail=false;var now=DateTimeOffset.Now;
        var view=new OverlayDashboardView(_=> {calls++;return fail?Task.FromException<WeatherSnapshot>(new HttpRequestException("offline")):Task.FromResult(sample);});
        TextBlock Text(string name)=>(TextBlock)view.FindName(name);
        CompleteWeatherTask(view.RefreshWeatherAsync(now));CompleteWeatherTask(view.RefreshWeatherAsync(now.AddMinutes(9)));
        Assert(calls==1 && Text("TemperatureText").Text=="14.5°C" && Text("HumidityText").Text=="90%" && ((Image)view.FindName("WeatherIcon")).Visibility==Visibility.Visible,"weather view displays response and caches it for ten minutes");
        fail=true;CompleteWeatherTask(view.RefreshWeatherAsync(now.AddMinutes(10)));CompleteWeatherTask(view.RefreshWeatherAsync(now.AddMinutes(14)));
        Assert(calls==2 && Text("TemperatureText").Text=="14.5°C" && ((Run)view.FindName("WeatherStatus")).Text.Contains("연결 대기"),"failed refresh preserves prior weather timestamp and backs off for five minutes");
        fail=false;CompleteWeatherTask(view.RefreshWeatherAsync(now.AddMinutes(15)));
        Assert(calls==3 && !((Run)view.FindName("WeatherStatus")).Text.Contains("대기"),"weather refresh recovers after failure backoff");

        var initialFailure=new OverlayDashboardView(_=>Task.FromException<WeatherSnapshot>(new HttpRequestException("offline")));
        CompleteWeatherTask(initialFailure.RefreshWeatherAsync(now));
        Assert(((TextBlock)initialFailure.FindName("TemperatureText")).Text=="--°C" && ((Image)initialFailure.FindName("WeatherIcon")).Visibility==Visibility.Hidden && ((ProgressBar)initialFailure.FindName("WeatherLoading")).Visibility==Visibility.Collapsed,"first network failure preserves unknown weather without fake values and clears loading");
        var missingLocation=new OverlayDashboardView(_=>Task.FromException<WeatherSnapshot>(new WeatherLocationUnavailableException("설정에서 날씨 지역을 선택해 주세요.")));
        CompleteWeatherTask(missingLocation.RefreshWeatherAsync(now));
        Assert(((Run)missingLocation.FindName("WeatherStatus")).Text.Contains("지역을 선택") && ((TextBlock)missingLocation.FindName("TemperatureText")).Text=="--°C" && ((Button)missingLocation.FindName("WeatherRefresh")).IsEnabled,"unavailable Windows location shows settings guidance without fabricated weather and permits retry");

        var startupGate=new TaskCompletionSource<WeatherSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);CancellationToken startupToken=default;
        var startup=new OverlayDashboardView(token=> {startupToken=token;return startupGate.Task;});
        var initialBounds=WeatherLayoutBounds(startup);
        var startupTask=startup.RefreshWeatherOnLaunchAsync();
        Assert(((ProgressBar)startup.FindName("WeatherLoading")).Visibility==Visibility.Visible && ((TextBlock)startup.FindName("WeatherStatusHost")).Opacity==1,"startup immediately displays progress without hiding the original weather status");
        Assert(initialBounds.SequenceEqual(WeatherLayoutBounds(startup)),"initial weather loading keeps weather clock and agenda size and positions unchanged");
        startup.PauseUpdates();Assert(!startupToken.IsCancellationRequested,"hidden startup keeps its initial weather request running");
        startupGate.SetResult(sample);CompleteWeatherTask(startupTask);
        Assert(((TextBlock)startup.FindName("TemperatureText")).Text=="14.5°C" && ((ProgressBar)startup.FindName("WeatherLoading")).Visibility==Visibility.Collapsed,"hidden startup completes weather and clears loading state");

        var relaunchGate=new TaskCompletionSource<WeatherSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);int relaunchCalls=0;
        var relaunch=new OverlayDashboardView(_=>++relaunchCalls==1?Task.FromResult(sample):relaunchGate.Task);
        CompleteWeatherTask(relaunch.RefreshWeatherAsync(now));var previousStatus=((Run)relaunch.FindName("WeatherStatus")).Text;
        var relaunchTask=relaunch.RefreshWeatherOnLaunchAsync();
        CompleteWeatherTask(relaunch.RefreshWeatherOnLaunchAsync());
        Assert(relaunchCalls==2 && !relaunchTask.IsCompleted && ((TextBlock)relaunch.FindName("TemperatureText")).Text=="14.5°C" && ((Run)relaunch.FindName("WeatherStatus")).Text==previousStatus && ((TextBlock)relaunch.FindName("WeatherStatusHost")).Opacity==1 && ((ProgressBar)relaunch.FindName("WeatherLoading")).Visibility==Visibility.Visible,"relaunch preserves visible observation time shows only progress and merges concurrent requests");
        relaunchGate.SetResult(sample with {Temperature=23});CompleteWeatherTask(relaunchTask);
        Assert(((TextBlock)relaunch.FindName("TemperatureText")).Text=="23°C" && ((ProgressBar)relaunch.FindName("WeatherLoading")).Visibility==Visibility.Collapsed,"relaunch replaces weather after successful refresh");

        var shutdownGate=new TaskCompletionSource<WeatherSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);CancellationToken shutdownToken=default;
        var shutdown=new OverlayDashboardView(token=> {shutdownToken=token;return shutdownGate.Task;});
        var shutdownTask=shutdown.RefreshWeatherOnLaunchAsync();shutdown.PauseUpdates(cancelStartup:true);
        Assert(shutdownToken.IsCancellationRequested && ((ProgressBar)shutdown.FindName("WeatherLoading")).Visibility==Visibility.Collapsed,"shutdown cancels startup weather and clears loading state");
        shutdownGate.SetResult(sample);CompleteWeatherTask(shutdownTask);
        Assert(((TextBlock)shutdown.FindName("TemperatureText")).Text=="--°C","late startup response after shutdown does not update weather");

        var retryGate=new TaskCompletionSource<WeatherSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);int retryCalls=0;
        var retry=new OverlayDashboardView(_=>++retryCalls==1?Task.FromException<WeatherSnapshot>(new HttpRequestException("offline")):retryGate.Task);
        CompleteWeatherTask(retry.RefreshWeatherAsync(now));var retryTask=retry.RefreshWeatherOnLaunchAsync();
        Assert(retryCalls==2 && !retryTask.IsCompleted,"relaunch bypasses failed weather retry backoff");
        retryGate.SetException(new HttpRequestException("offline"));CompleteWeatherTask(retryTask);
        Assert(((ProgressBar)retry.FindName("WeatherLoading")).Visibility==Visibility.Collapsed && !((Run)retry.FindName("WeatherStatus")).Text.Contains("불러오는"),"failed startup refresh clears loading and reports retry state");

        var manualGate=new TaskCompletionSource<WeatherSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);int manualCalls=0;
        var manual=new OverlayDashboardView(_=>++manualCalls switch {
            1=>Task.FromResult(sample), 2=>manualGate.Task,
            3=>Task.FromException<WeatherSnapshot>(new HttpRequestException("offline")), _=>Task.FromResult(sample)
        });
        var refresh=(Button)manual.FindName("WeatherRefresh");
        var manualLoading=(ProgressBar)manual.FindName("WeatherLoading");
        CompleteWeatherTask(manual.RefreshWeatherAsync(now));var manualBounds=WeatherLayoutBounds(manual);
        refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(manualBounds.SequenceEqual(WeatherLayoutBounds(manual)),"manual weather refresh keeps compact hit area icon text clock and agenda stationary");
        Assert(manualCalls==2 && !refresh.IsEnabled && manualLoading.Visibility==Visibility.Visible && ((TextBlock)manual.FindName("TemperatureText")).Text=="14.5°C","clicking weather bypasses fresh cache and disables refresh while preserving weather");
        for(int i=0;i<10;i++) refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        CompleteWeatherTask(manual.RefreshWeatherAsync(now.AddMinutes(11)));CompleteWeatherTask(manual.RefreshWeatherOnLaunchAsync());
        Assert(manualCalls==2,"repeated weather clicks and automatic startup refreshes never duplicate a pending request");
        manualGate.SetResult(sample with {Temperature=23});
        var manualWait=Stopwatch.StartNew();while(manualLoading.Visibility==Visibility.Visible && manualWait.ElapsedMilliseconds<3000) Pump(10);
        Assert(refresh.IsEnabled && manualLoading.Visibility==Visibility.Collapsed && ((TextBlock)manual.FindName("TemperatureText")).Text=="23°C","successful manual refresh updates weather and enables the next click");
        refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(manualCalls==3 && refresh.IsEnabled && ((Run)manual.FindName("WeatherStatus")).Text.Contains("연결 대기"),"failed manual refresh enables retry and displays connection status");
        refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(manualCalls==4 && refresh.IsEnabled && !((Run)manual.FindName("WeatherStatus")).Text.Contains("대기"),"weather click can retry immediately during failure backoff");

        var stableGate=new TaskCompletionSource<WeatherSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stable=new OverlayDashboardView(_=>stableGate.Task);
        stable.ApplyWeather(sample with {Temperature=0,Humidity=0,Code=0});
        var stableBounds=WeatherLayoutBounds(stable);var stableTask=stable.RefreshWeatherAsync(now);
        Assert(stableBounds.SequenceEqual(WeatherLayoutBounds(stable)),"short zero temperature and humidity never expand to fit loading message or progress");
        stableGate.SetResult(sample with {Temperature=0,Humidity=0,Code=0});CompleteWeatherTask(stableTask);
        Assert(stableBounds.SequenceEqual(WeatherLayoutBounds(stable)) && ((TextBlock)stable.FindName("WeatherStatusHost")).Opacity==1 && ((ProgressBar)stable.FindName("WeatherLoading")).Visibility==Visibility.Collapsed,"unchanged weather response retains status without shifting layout");

        var gate=new TaskCompletionSource<WeatherSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);int pendingCalls=0;CancellationToken captured=default;
        var newer=sample with {Temperature=23,Code=0};
        var cancellable=new OverlayDashboardView(token=> {captured=token;pendingCalls++;return pendingCalls==1?gate.Task:Task.FromResult(newer);});
        var first=cancellable.RefreshWeatherAsync(now);CompleteWeatherTask(cancellable.RefreshWeatherAsync(now));
        Assert(pendingCalls==1 && !first.IsCompleted,"weather refresh keeps a single in flight request");
        cancellable.PauseUpdates();Assert(captured.IsCancellationRequested && ((ProgressBar)cancellable.FindName("WeatherLoading")).Visibility==Visibility.Collapsed && ((Button)cancellable.FindName("WeatherRefresh")).IsEnabled,"hiding dashboard cancels its regular in flight request clears loading and enables refresh");
        CompleteWeatherTask(cancellable.RefreshWeatherAsync(now));gate.SetResult(sample);CompleteWeatherTask(first);
        Assert(pendingCalls==2 && ((TextBlock)cancellable.FindName("TemperatureText")).Text=="23°C","late cancelled response cannot replace a newer display session");

        var previousRegionGate=new TaskCompletionSource<WeatherSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);CancellationToken previousRegionToken=default;int regionCalls=0;
        var locationChange=new OverlayDashboardView(token=> {regionCalls++;if(regionCalls==1) {previousRegionToken=token;return previousRegionGate.Task;}return Task.FromResult(sample with {Location=seoul.Location});});
        var previousRegionTask=locationChange.RefreshWeatherAsync(now);locationChange.ReloadWeatherLocation();
        Assert(previousRegionToken.IsCancellationRequested && regionCalls==2 && ((TextBlock)locationChange.FindName("LocationText")).Text=="서울특별시","changing location cancels the old request and refreshes the selected region immediately");
        previousRegionGate.SetResult(sample);CompleteWeatherTask(previousRegionTask);
        Assert(((TextBlock)locationChange.FindName("LocationText")).Text=="서울특별시","late weather from the prior location cannot overwrite the newly selected region");

        var preview=new OverlayTabs();var previewView=(OverlayDashboardView)preview.FindName("Dashboard");previewView.UpdateClock(new DateTime(2026,10,24,9,8,7));previewView.ApplyWeather(sample);
        foreach(var size in new[]{new Size(1440,900),new Size(800,600),new Size(480,360)}) {
            preview.Measure(size);preview.Arrange(new Rect(size));preview.UpdateLayout();
            SaveWeatherImage(preview,size,Path.Combine(qa,$"dashboard-weather-{size.Width}.png"));
        }
    }

    private static void SaveWeatherImage(Visual view,Size size,string path)
    {
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(size.Width),(int)Math.Ceiling(size.Height),96,96,PixelFormats.Pbgra32);bitmap.Render(view);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);png.Save(output);
    }

    private static void CheckLiveWeather(string qa)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var task=Task.Run(()=>new WeatherClient().FetchAsync(timeout.Token));CompleteWeatherTask(task);
        var result=task.Result;
        Assert(result.Temperature!=null && result.Humidity!=null && result.Icon!=WeatherIconKind.Unknown,"live Windows location and weather response load through the application client");
        var preview=new OverlayTabs();((OverlayDashboardView)preview.FindName("Dashboard")).ApplyWeather(result);
        var size=new Size(1440,900);preview.Measure(size);preview.Arrange(new Rect(size));preview.UpdateLayout();
        SaveWeatherImage(preview,size,Path.Combine(qa,"dashboard-weather-live.png"));
        Console.WriteLine($"Live weather: {result.Description}, {result.Temperature:0.#} C, humidity {result.Humidity}%, icon {result.Icon}.");
    }
}
