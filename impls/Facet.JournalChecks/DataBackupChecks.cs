using System.IO;
using System.IO.Compression;
using Facet;

internal static partial class Program
{
    // A backup holds the records folder; restoring swaps it in at the next start and keeps the folder it replaced.
    private static void CheckDataBackup(string qa)
    {
        string root=Path.Combine(qa,"backup-"+Guid.NewGuid().ToString("N")),data=Path.Combine(root,"Facet"),folder=Path.Combine(root,"saved");
        Directory.CreateDirectory(Path.Combine(data,"plugin-data"));Directory.CreateDirectory(Path.Combine(data,"backups"));
        File.WriteAllText(Path.Combine(data,"records.dat"),"one");File.WriteAllText(Path.Combine(data,"plugin-data","p.dat"),"plugin");
        File.WriteAllText(Path.Combine(data,"backups","old.bak"),"skip");File.WriteAllText(Path.Combine(data,"records.dat.tmp"),"skip");
        var (zip,skipped)=DataBackup.Create(data,folder);
        using(var archive=ZipFile.OpenRead(zip)) Assert(skipped==0 && archive.Entries.Select(e=>e.FullName).Order().SequenceEqual(["plugin-data/p.dat","records.dat"]),"a backup holds records and plugin data, not backups or temporary files");
        File.WriteAllText(Path.Combine(data,"records.dat"),"two");
        Assert(DataBackup.Apply(data)==null,"nothing is restored without a staged backup");
        DataBackup.Stage(data,zip);
        Assert(DataBackup.IsStaged(data) && File.ReadAllText(Path.Combine(data,"records.dat"))=="two","staging leaves the running records alone");
        string? kept=DataBackup.Apply(data);
        Assert(kept!=null && File.ReadAllText(Path.Combine(data,"records.dat"))=="one" && File.ReadAllText(Path.Combine(kept,"records.dat"))=="two" && !DataBackup.IsStaged(data),"the next start restores the backup and keeps the replaced folder");
        string other=Path.Combine(root,"other.zip");using(var archive=ZipFile.Open(other,ZipArchiveMode.Create)) archive.CreateEntry("readme.txt");
        bool rejected=false;try{DataBackup.Stage(data,other);}catch(InvalidDataException){rejected=true;}
        Assert(rejected && !DataBackup.IsStaged(data),"a zip without records is not a backup");
        string evil=Path.Combine(root,"evil.zip");using(var archive=ZipFile.Open(evil,ZipArchiveMode.Create)){archive.CreateEntry("records.dat");archive.CreateEntry("../escape.txt");}
        rejected=false;try{DataBackup.Stage(data,evil);}catch(InvalidDataException){rejected=true;}
        Assert(rejected && !File.Exists(Path.Combine(root,"escape.txt")) && !DataBackup.IsStaged(data),"a backup cannot write outside its folder");
        Directory.Delete(root,true);
    }
}