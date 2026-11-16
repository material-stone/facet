using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Facet.Plugins;

namespace Facet.Design;

// One skin-drawn widget as the world page sees it: overlay window pixels, the widget's base size and its state (ADR 0109).
internal sealed record SkinWidgetFrame(string Id,string Type,Rect Bounds,int BaseWidth,int BaseHeight,string StateJson);

// The widget layer protocol between the app and a skin's world page. Pure, so it can be checked without a window.
internal static class SkinWidgets
{
    internal const int MaxArgsBytes=8192,MaxStateBytes=65536,MaxResultBytes=65536;
    private static readonly Regex ActionName=new("^[a-z][A-Za-z0-9_-]{0,31}$",RegexOptions.CultureInvariant);

    // Board pixels to a board view's own pixels, the same transform OverlayWorkspaceView applies (origin at the view's middle plus the pan).
    internal static Rect BoardToView(double x,double y,double width,double height,double viewWidth,double viewHeight,double panX,double panY,double zoom)=>
        new(viewWidth/2+panX+x*zoom,viewHeight/2+panY+y*zoom,width*zoom,height*zoom);

    // A point in a tile's own pixels to the widget's base-size coordinates, which is what the page draws in.
    internal static Point ToBase(Point local,Size tile,int baseWidth,int baseHeight)=>
        new(tile.Width>0 ? local.X*baseWidth/tile.Width : 0,tile.Height>0 ? local.Y*baseHeight/tile.Height : 0);

    // State that is not one JSON object, or too large, is sent as null so a broken plugin cannot break the page's message.
    internal static string? CleanState(string? json)
    {
        if(json==null || Encoding.UTF8.GetByteCount(json)>MaxStateBytes) return null;
        try { using var document=JsonDocument.Parse(json);return document.RootElement.ValueKind==JsonValueKind.Object ? json : null; }
        catch(JsonException) { return null; }
    }

    private static double Round(double value)=>Math.Round(value,1);

    internal static string WidgetsMessage(IEnumerable<SkinWidgetFrame> frames)
    {
        using var buffer=new MemoryStream();
        using(var writer=new Utf8JsonWriter(buffer)) {
            writer.WriteStartObject();writer.WriteStartArray("widgets");
            foreach(var frame in frames) {
                writer.WriteStartObject();
                writer.WriteString("id",frame.Id);writer.WriteString("type",frame.Type);
                writer.WriteNumber("x",Round(frame.Bounds.X));writer.WriteNumber("y",Round(frame.Bounds.Y));
                writer.WriteNumber("width",Round(frame.Bounds.Width));writer.WriteNumber("height",Round(frame.Bounds.Height));
                writer.WriteNumber("baseWidth",frame.BaseWidth);writer.WriteNumber("baseHeight",frame.BaseHeight);
                writer.WritePropertyName("state");
                if(CleanState(frame.StateJson) is { } state) writer.WriteRawValue(state,skipInputValidation:true); else writer.WriteNullValue();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    // kind is move, down, up, wheel or leave; button is 0 left, 1 middle, 2 right; delta is wheel notches times 120.
    internal static string PointerMessage(string id,string kind,Point at,int delta=0,int button=0)=>
        JsonSerializer.Serialize(new { pointer=new { id,kind,x=Round(at.X),y=Round(at.Y),delta,button } });

    // Runs a page's { type:'action', id, name, args } on the widget it names. The reply is always an actionResult;
    // an id that is not a skin-drawn widget of the shown tab, a bad name or bad arguments never reach a plugin.
    internal static async Task<string> RouteAction(string json,Func<string,IWidgetState?> find)
    {
        string id="",name="";
        string Reply(bool ok,string? result,string? error)
        {
            using var buffer=new MemoryStream();
            using(var writer=new Utf8JsonWriter(buffer)) {
                writer.WriteStartObject();writer.WriteStartObject("actionResult");
                writer.WriteString("id",id);writer.WriteString("name",name);writer.WriteBoolean("ok",ok);
                writer.WritePropertyName("result");
                if(CleanResult(result) is { } clean) writer.WriteRawValue(clean,skipInputValidation:true); else writer.WriteNullValue();
                if(error!=null) writer.WriteString("error",error);
                writer.WriteEndObject();writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
        string args;
        try {
            using var message=JsonDocument.Parse(json);
            var root=message.RootElement;
            if(root.TryGetProperty("id",out var idValue) && idValue.ValueKind==JsonValueKind.String) id=idValue.GetString()!;
            if(root.TryGetProperty("name",out var nameValue) && nameValue.ValueKind==JsonValueKind.String) name=nameValue.GetString()!;
            if(!ActionName.IsMatch(name)) return Reply(false,null,"bad-name");
            if(!root.TryGetProperty("args",out var argsValue) || argsValue.ValueKind==JsonValueKind.Null) args="{}";
            else if(argsValue.ValueKind!=JsonValueKind.Object) return Reply(false,null,"bad-args");
            else args=argsValue.GetRawText();
        }
        catch(JsonException) { return Reply(false,null,"bad-message"); }
        if(Encoding.UTF8.GetByteCount(args)>MaxArgsBytes) return Reply(false,null,"bad-args");
        if(find(id) is not { } widget) return Reply(false,null,"unknown-widget");
        try { return Reply(true,await widget.Act(name,args),null); }
        catch(Exception ex) when(ex is ArgumentException or JsonException or FormatException or InvalidOperationException) { return Reply(false,null,"refused"); }
        // Plugin code can throw anything; the page only learns that it failed.
        catch(Exception ex) when(ex is not OutOfMemoryException) { return Reply(false,null,"failed"); }
    }

    private static string? CleanResult(string? json)
    {
        if(json==null || Encoding.UTF8.GetByteCount(json)>MaxResultBytes) return null;
        try { using var _=JsonDocument.Parse(json);return json; }
        catch(JsonException) { return null; }
    }
}
