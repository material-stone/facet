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
        // The weather pictures moved with the dashboard face to the weather plugin (ADR 0101).

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
        // The dashboard weather face moved to the weather plugin (ADR 0101); the client stays for the world backdrop and the weather tool.
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
        Console.WriteLine($"Live weather: {result.Description}, {result.Temperature:0.#} C, humidity {result.Humidity}%, icon {result.Icon}.");
    }
}
