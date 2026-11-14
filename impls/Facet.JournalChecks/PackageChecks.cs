using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Facet;
using Facet.Design;

internal static partial class Program
{
    private static void CheckPackages(string qa)
    {
        string root=Path.Combine(qa,"packages-"+Guid.NewGuid().ToString("N")),data=Path.Combine(root,"data"),work=Path.Combine(root,"work");
        Directory.CreateDirectory(work);
        string Zip(string name,params (string Path,string Text)[] files) {
            string file=Path.Combine(work,name);
            using var zip=ZipFile.Open(file,ZipArchiveMode.Create);
            foreach(var (path,text) in files) { using var writer=new StreamWriter(zip.CreateEntry(path).Open());writer.Write(text); }
            return file;
        }
        bool Refused(string file,string? expected=null) {
            try { Packages.Install(file,data);return false; }
            catch(InvalidDataException ex) { return expected==null || ex.Message.Contains(expected); }
        }

        // Skin manifests: data only, and every token and slot validated.
        var tokens=new ResourceDictionary { Source=new Uri("pack://application:,,,/Facet;component/Design/Tokens.xaml") };
        Assert(tokens.Keys.OfType<string>().Where(key=>tokens[key] is SolidColorBrush).ToHashSet().SetEquals(ExternalSkins.ColorTokens),"external skins can set exactly the app's color tokens");
        Assert(ExternalSkins.Parse("{\"id\":\"a\",\"name\":\"A\",\"colors\":{\"Bg\":\"#102030\"}}","x") is { Id:"a" },"a valid skin manifest parses");
        Assert(ExternalSkins.Parse("{\"id\":\"../a\",\"name\":\"A\"}","x")==null && ExternalSkins.Parse("{\"id\":\"a\"}","x")==null && ExternalSkins.Parse("nope","x")==null,"a bad id, a missing name and broken JSON are refused");
        Assert(ExternalSkins.Parse("{\"id\":\"a\",\"name\":\"A\",\"colors\":{\"Nope\":\"#102030\"}}","x")==null && ExternalSkins.Parse("{\"id\":\"a\",\"name\":\"A\",\"colors\":{\"Bg\":\"red\"}}","x")==null,"unknown tokens and non-hex colors are refused");
        Assert(ExternalSkins.Parse("{\"id\":\"a\",\"name\":\"A\",\"theme\":{\"background\":\"../x.png\"}}","x")==null,"theme assets cannot leave the skin folder");

        // Installing and discovering.
        string skinJson="{\"id\":\"aurora\",\"name\":\"오로라\",\"version\":\"1.0.0\",\"colors\":{\"Bg\":\"#102030\",\"Ink\":\"#FFFFFF\"}}";
        Assert(Packages.Install(Zip("skin.zip",("skin.json",skinJson)),data).Kind==PackageKind.Skin && File.Exists(Path.Combine(data,"skins","aurora","skin.json")),"a skin package installs into the skins folder");
        var skinsBefore=Skins.Options;
        Skins.UseFolder(Path.Combine(data,"skins"));
        Assert(Skins.Resolve("aurora")=="aurora" && Skins.Load("aurora")["Bg"] is SolidColorBrush { Color:var bg } && bg==Color.FromRgb(0x10,0x20,0x30) && Skins.LoadVisuals("aurora")==null,"an installed skin is selectable and supplies its tokens");
        Assert(Skins.Load("aurora").Contains(SystemColors.WindowTextBrushKey) && !Skins.Load("aurora").Contains("Panel"),"a skin overrides only what it sets, plus the matching system colors");
        Directory.CreateDirectory(Path.Combine(data,"skins","broken"));File.WriteAllText(Path.Combine(data,"skins","broken","skin.json"),"nope");
        Directory.CreateDirectory(Path.Combine(data,"skins","clash"));File.WriteAllText(Path.Combine(data,"skins","clash","skin.json"),"{\"id\":\"light\",\"name\":\"가짜\"}");
        Assert(Skins.Rescan().Count(option=>option.Id=="aurora")==1 && Skins.Options.All(option=>option.Name!="가짜") && Skins.Options.All(option=>option.Id!="broken"),"a broken folder is skipped and a built-in id cannot be replaced");

        // A theme with an image, written the way an author would.
        string imageFolder=Path.Combine(data,"skins","framed");Directory.CreateDirectory(imageFolder);
        var pixels=new byte[16*16*4];var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(16,16,96,96,PixelFormats.Bgra32,null,pixels,64)));
        using(var png=File.Create(Path.Combine(imageFolder,"frame.png"))) encoder.Save(png);
        File.WriteAllText(Path.Combine(imageFolder,"skin.json"),"{\"id\":\"framed\",\"name\":\"틀\",\"theme\":{\"frame\":\"frame.png\",\"frameSlice\":4,\"stars\":3}}");
        Skins.Rescan();
        Assert(Skins.LoadVisuals("framed") is { Frame:not null,Stars:3 },"an external theme loads images from its folder");
        File.WriteAllText(Path.Combine(imageFolder,"skin.json"),"{\"id\":\"framed\",\"name\":\"틀\",\"theme\":{\"frame\":\"frame.png\",\"frameSlice\":8}}");
        Skins.Rescan();
        Assert(Skins.LoadVisuals("framed")==null,"a frame too small for its slice keeps tokens only");
        File.Delete(Path.Combine(imageFolder,"frame.png"));
        Assert(Skins.LoadVisuals("framed")==null,"a missing image keeps tokens only");

        // The packaged skins in registry/skins: every former built-in skin is now data the app validates (ADR 0100).
        string repository=AppContext.BaseDirectory;
        while(!Directory.Exists(Path.Combine(repository,"registry","skins"))) repository=Path.GetDirectoryName(repository) ?? throw new DirectoryNotFoundException("registry/skins");
        ExternalSkin Packaged(string id)=>ExternalSkins.Read(Path.Combine(repository,"registry","skins",id)) ?? throw new InvalidDataException(id);
        Assert(new[]{"space","voxel","midnight"}.All(id=>Packaged(id).Id==id && Packaged(id).Colors.Count==ExternalSkins.ColorTokens.Count),"each packaged skin parses with its folder id and sets every color token");
        Assert(ExternalSkins.Visuals(Packaged("space")) is { Voyage:true,HudWidgets:true,Stars:500,PanelMarkLength:12,TabCornerRadius:0 } spaceVisuals && spaceVisuals.NebulaSpeed>0 && spaceVisuals.Options.Single().Id==ThemeVisuals.ReticleOption,"the space package keeps the voyage, nebula, stars, frame marks and reticle");
        Assert(ExternalSkins.Visuals(Packaged("voxel")) is { WorldPageHtml:{ Length:>1000 },SoftHud:true } && Packaged("voxel").Theme is { BodyFont:not null,HeadingFont:not null } && ExternalSkins.Visuals(Packaged("midnight"))==null,"the voxel package carries its world page and fonts and midnight only colors");

        // Unsafe packages leave nothing behind.
        Assert(Refused(Zip("slip.zip",("skin.json",skinJson),("../../evil.txt","x")),"안전하지 않은") && !File.Exists(Path.Combine(root,"evil.txt")),"a path leaving the package is refused");
        Assert(Refused(Zip("device.zip",("skin.json",skinJson),("con.txt","x")),"안전하지 않은") && Refused(Zip("stream.zip",("skin.json",skinJson),("a.txt:b","x")),"안전하지 않은"),"device names and alternate streams are refused");
        Assert(Refused(Zip("code.zip",("skin.json",skinJson),("run.dll","x")),"넣을 수 없는"),"a skin cannot carry code");
        Assert(Refused(Zip("none.zip",("readme.txt","x"))) && Refused(Zip("both.zip",("skin.json",skinJson),("plugin.json","{}"))),"a package has exactly one valid manifest");
        Assert(Refused(Zip("noimage.zip",("skin.json","{\"id\":\"m\",\"name\":\"M\",\"theme\":{\"background\":\"gone.png\"}}")),"파일이 없습니다"),"a skin must carry the images it names");
        string garbage=Path.Combine(work,"garbage.zip");File.WriteAllText(garbage,"not a zip");
        Assert(Refused(garbage,"손상"),"a broken zip reports a message");
        Assert(!Directory.GetDirectories(data,".package-staging-*").Any(),"staging is cleaned up after failures");

        // Replacing.
        string pluginJson(string version)=>"{\"id\":\"my.plugin\",\"name\":\"내 플러그인\",\"version\":\""+version+"\",\"assembly\":\"My.dll\"}";
        Assert(Refused(Zip("noasm.zip",("plugin.json",pluginJson("1.0.0")))),"a plugin must carry its assembly");
        Packages.Install(Zip("p1.zip",("plugin.json",pluginJson("1.0.0")),("My.dll","one")),data);
        string renamed=Path.Combine(data,"plugins","renamed-by-user");Directory.Move(Path.Combine(data,"plugins","my.plugin"),renamed);
        Packages.Install(Zip("p2.zip",("wrapped/plugin.json",pluginJson("2.0.0")),("wrapped/My.dll","two")),data);
        Assert(File.ReadAllText(Path.Combine(renamed,"My.dll"))=="two" && !Directory.Exists(Path.Combine(data,"plugins","my.plugin")),"an update replaces the folder that holds the same id, and one wrapping folder is accepted");
        Directory.CreateDirectory(Path.Combine(data,"plugins","taken"));File.WriteAllText(Path.Combine(data,"plugins","taken","mine.txt"),"keep");
        Assert(Refused(Zip("taken.zip",("plugin.json","{\"id\":\"taken\",\"name\":\"T\",\"assembly\":\"T.dll\"}"),("T.dll","x")),"다른 폴더") && File.Exists(Path.Combine(data,"plugins","taken","mine.txt")),"an unrelated folder is never overwritten");
        using(File.Open(Path.Combine(renamed,"My.dll"),FileMode.Open,FileAccess.Read,FileShare.None))
            Assert(Refused(Zip("p3.zip",("plugin.json",pluginJson("3.0.0")),("My.dll","three"))),"a locked plugin reports a message");
        Assert(File.ReadAllText(Path.Combine(renamed,"My.dll"))=="two","a failed update keeps the old plugin");

        // Registry.
        string sha=new('a',64),url="https://github.com/"+PackageRegistry.Repository+"/releases/download/skin-a-1/a.zip";
        string Row(string rowUrl,string rowSha)=>"{\"kind\":\"skin\",\"id\":\"a\",\"name\":\"A\",\"version\":\"1\",\"author\":\"x\",\"url\":\""+rowUrl+"\",\"sha256\":\""+rowSha+"\"}";
        Assert(PackageRegistry.Parse("{\"packages\":["+Row(url,sha)+"]}").Count==1,"a valid registry row parses");
        Assert(PackageRegistry.Parse("{\"packages\":["+Row("https://evil.example/a.zip",sha)+","+Row(url,"zz")+","+Row(url.Replace("skin-a-1","../x"),sha)+",1]}").Count==0,"rows with another host, a bad hash or a traversal are dropped");
        Assert(PackageRegistry.IsNewer("1.10.0","1.9.0") && !PackageRegistry.IsNewer("1.0.0","1.0.0") && !PackageRegistry.IsNewer("1.0.0","2.0.0") && PackageRegistry.IsNewer("beta","alpha"),"versions compare numerically where they can");
        string good=Zip("reg.zip",("skin.json","{\"id\":\"reg\",\"name\":\"R\",\"version\":\"1\"}"));
        string goodSha;using(var stream=File.OpenRead(good)) goodSha=Convert.ToHexStringLower(SHA256.HashData(stream));
        RegistryEntry Entry(string id,string hash)=>new(PackageKind.Skin,id,"R","1","x","",url,hash);
        bool VerifyRefused(RegistryEntry entry) { try { PackageRegistry.InstallVerified(entry,good,data);return false; } catch(InvalidDataException) { return true; } }
        Assert(VerifyRefused(Entry("reg",sha)) && !Directory.Exists(Path.Combine(data,"skins","reg")),"a download with another hash is not installed");
        Assert(VerifyRefused(Entry("other",goodSha)) && !Directory.Exists(Path.Combine(data,"skins","reg")),"a package that is not what the list says is not installed");
        Assert(PackageRegistry.InstallVerified(Entry("reg",goodSha),good,data).Id=="reg","a matching download installs");

        // Back to the built-in list for the checks that follow.
        Assert(skinsBefore.All(option=>Skins.Options.Any(now=>now.Id==option.Id)),"built-in skins stay available");
        Skins.UseFolder(Path.Combine(root,"none"));
    }
}
