using System.IO;

using Facet;
using Facet.Core;

internal static partial class Program
{
    private static void CheckPortablePlugins(string qa)
    {
        string root=Path.Combine(qa,"portable-"+Guid.NewGuid().ToString("N")),package=Path.Combine(root,"package"),data=Path.Combine(root,"data");
        string bundled=Path.Combine(package,PortablePlugins.Folder),plugins=Path.Combine(data,"plugins");
        void Write(string path,string text){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,text);}
        Write(Path.Combine(bundled,"sample","plugin.dll"),"one");Write(Path.Combine(bundled,"sample","Tools","tool.exe"),"tool");
        Directory.CreateDirectory(data);
        Assert(PortablePlugins.Install(package,data)==null && !Directory.Exists(plugins),"a folder without a package id installs nothing");
        Write(Path.Combine(bundled,PortablePlugins.PackageFile),"1");
        Assert(PortablePlugins.Install(null,data)==null && !Directory.Exists(plugins),"a release without a package installs nothing");
        Assert(PortablePlugins.Install(package,data)==null && File.ReadAllText(Path.Combine(plugins,"sample","Tools","tool.exe"))=="tool","the first start copies bundled plugins with their subfolders");
        Write(Path.Combine(plugins,"mine","plugin.dll"),"mine");
        Directory.Delete(Path.Combine(plugins,"sample"),true);
        Assert(PortablePlugins.Install(package,data)==null && !Directory.Exists(Path.Combine(plugins,"sample")),"a plugin the user removed stays removed for the same package");
        Write(Path.Combine(bundled,"sample","plugin.dll"),"two");Write(Path.Combine(bundled,PortablePlugins.PackageFile),"2");
        Assert(PortablePlugins.Install(package,data)==null && File.ReadAllText(Path.Combine(plugins,"sample","plugin.dll"))=="two" && File.Exists(Path.Combine(plugins,"mine","plugin.dll")),"a new package replaces its plugins and keeps the user's own");
        Assert(!Directory.GetDirectories(data,".plugin-staging-*").Any(),"staging is cleaned up");
        using(File.Open(Path.Combine(plugins,"sample","plugin.dll"),FileMode.Open,FileAccess.Read,FileShare.None)){
            Write(Path.Combine(bundled,PortablePlugins.PackageFile),"3");
            Assert(PortablePlugins.Install(package,data)!=null,"a locked plugin reports a message instead of stopping the start");
        }
        Assert(File.Exists(Path.Combine(plugins,"sample","plugin.dll")) && PortablePlugins.Install(package,data)==null && File.ReadAllText(Path.Combine(plugins,PortablePlugins.MarkerFile))=="3","the failed package retries on the next start");
        string Manifest(string id,string assembly)=>"{\"id\":\""+id+"\",\"name\":\"N\",\"assembly\":\""+assembly+"\"}";
        void Plugin(string folder,string id,string assembly,string text){Write(Path.Combine(folder,"plugin.json"),Manifest(id,assembly));Write(Path.Combine(folder,assembly),text);}
        string deploy=Path.Combine(root,"deploy");
        Plugin(Path.Combine(plugins,"old"),"my.old","Teum.Old.dll","old");Plugin(Path.Combine(deploy,"plugins","old"),"my.old","Facet.Old.dll","new");
        Plugin(Path.Combine(plugins,PluginIds.RenamedPrefix+"clock"),PluginIds.RenamedPrefix+"clock","Facet.Clock.dll","old");Plugin(Path.Combine(deploy,"plugins","clock"),"facet.clock","Facet.Clock.dll","new");
        Plugin(Path.Combine(plugins,"orphan"),"my.orphan","Teum.Orphan.dll","keep");
        Plugin(Path.Combine(plugins,"other"),"my.other","Teum.Other.dll","keep");Plugin(Path.Combine(deploy,"plugins","other"),"different.id","Facet.Other.dll","no");
        Assert(PortablePlugins.UpgradeRenamed(deploy,data)==null && File.ReadAllText(Path.Combine(plugins,"old","Facet.Old.dll"))=="new" && !File.Exists(Path.Combine(plugins,"old","Teum.Old.dll")),"a plugin built before the rename is replaced from the deployment");
        Assert(File.ReadAllText(Path.Combine(plugins,"clock","Facet.Clock.dll"))=="new" && !Directory.Exists(Path.Combine(plugins,PluginIds.RenamedPrefix+"clock")),"a plugin with an old first-party id moves to the current plugin's folder");
        Assert(File.Exists(Path.Combine(plugins,"orphan","Teum.Orphan.dll")) && File.Exists(Path.Combine(plugins,"other","Teum.Other.dll")),"a plugin with no replacement of the same id stays as it was");
    }
}
