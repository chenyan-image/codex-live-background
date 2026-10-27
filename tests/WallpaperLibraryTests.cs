using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

internal static class WallpaperLibraryTests {
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static string Project(string workshop,string id,object project){string folder=Path.Combine(workshop,id);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"project.json"),new JavaScriptSerializer().Serialize(project),Encoding.UTF8);return folder;}
    public static int Main(string[] args){
        string sandbox=args[0];Directory.CreateDirectory(sandbox);int cases=0;
        string media=Path.Combine(sandbox,"wallpaper.jpg");File.WriteAllText(media,"source unchanged");
        var library=new WallpaperLibrary(Path.Combine(sandbox,"state"));var entry=library.Add(media,"测试","local",null,null);library.Add(media.ToUpperInvariant(),"duplicate","local",null,null);Require(library.Snapshot().Count==1,"Duplicate reference");cases++;
        Require(String.IsNullOrEmpty(entry.LastUsedUtc),"Import entered successful-use history");library.Applied(entry);Require(!String.IsNullOrEmpty(new WallpaperLibrary(Path.Combine(sandbox,"state")).Snapshot()[0].LastUsedUtc),"History was not persisted");cases++;
        library.Forget(entry.Id);Require(File.ReadAllText(media)=="source unchanged"&&library.Snapshot().Count==0,"Forget changed source file");cases++;
        entry=library.Add(media,"missing","local",null,null);File.Move(media,media+".moved");Require(!entry.Available&&new WallpaperLibrary(Path.Combine(sandbox,"state")).Snapshot().Count==1,"Missing file history lost");cases++;
        string corrupt=Path.Combine(sandbox,"corrupt");Directory.CreateDirectory(corrupt);File.WriteAllText(Path.Combine(corrupt,"wallpaper-library.json"),"{broken");var recovered=new WallpaperLibrary(corrupt);Require(recovered.Warning.Length>0&&Directory.GetFiles(corrupt,"*.damaged-*").Length==1,"Corrupt data not backed up");cases++;
        string steam=Path.Combine(sandbox,"Steam"),secondary=Path.Combine(sandbox,"SecondLibrary"),workshop=Path.Combine(secondary,@"steamapps\workshop\content\431960");Directory.CreateDirectory(Path.Combine(steam,"steamapps"));Directory.CreateDirectory(workshop);
        File.WriteAllText(Path.Combine(steam,@"steamapps\libraryfolders.vdf"),"\"libraryfolders\" { \"1\" { \"path\" \""+secondary.Replace("\\","\\\\")+"\" } }");Require(SteamWallpapers.DiscoverRoots(new[]{steam}).Contains(secondary,StringComparer.OrdinalIgnoreCase),"Secondary Steam library missed");cases++;
        string video=Project(workshop,"1",new{title="动态项目",type="video",file="movie.mp4",preview="preview.jpg"});File.WriteAllText(Path.Combine(video,"movie.mp4"),"video fixture");File.WriteAllText(Path.Combine(video,"preview.jpg"),"preview fixture");
        string image=Project(workshop,"2",new{title="图片项目",type="video",file="wall.jpg",preview="preview.jpg"});File.WriteAllText(Path.Combine(image,"wall.jpg"),"image fixture");
        string scene=Project(workshop,"3",new{title="场景项目",type="scene",file="scene.json",preview="preview.jpg"});File.WriteAllText(Path.Combine(scene,"scene.pkg"),"never execute");File.WriteAllText(Path.Combine(scene,"preview.jpg"),"scene preview");
        string traversal=Project(workshop,"4",new{title="unsafe",type="video",file="..\\..\\..\\outside.mp4"});File.WriteAllText(Path.Combine(sandbox,"outside.mp4"),"must not be imported");
        string malformed=Path.Combine(workshop,"5");Directory.CreateDirectory(malformed);File.WriteAllText(Path.Combine(malformed,"project.json"),"{bad");
        var result=SteamWallpapers.Scan(new[]{steam,secondary,secondary},()=>false);Require(result.Entries.Count==3&&result.WorkshopRoots.Count==1,"Steam entries incorrect");cases++;
        Require(result.Entries.Single(x=>x.Name=="动态项目").Kind=="video"&&result.Entries.Single(x=>x.Name=="图片项目").Kind=="image","Media types incorrect");cases++;
        Require(result.Unsupported==1&&result.Entries.Single(x=>x.Name=="场景项目").Note.StartsWith("场景预览图"),"Scene was presented as original animation");cases++;
        Require(!result.Entries.Any(x=>x.Name=="unsafe")&&SteamWallpapers.Inside(traversal,"..\\..\\..\\outside.mp4")==null,"Project path escaped workshop item");cases++;
        Require(result.Invalid==1,"Malformed project not isolated");cases++;
        Require(SteamWallpapers.Scan(new[]{secondary},()=>true).Entries.Count==0,"Scan cancellation failed");cases++;
        var built=new WallpaperEntry{Id="builtin-ridge",Source="builtin",Path=Path.Combine(sandbox,"old-install.jpg"),Name="builtin",Kind="image"};library.Applied(built);library.RemapBuiltins(new[]{new WallpaperEntry{Id=built.Id,Source="builtin",Path=Path.Combine(video,"preview.jpg")}});Require(library.Snapshot().Single(x=>x.Id==built.Id).Available,"Built-in history broke after install-directory change");cases++;
        string package=Path.Combine(sandbox,"installed"),bundle=Path.Combine(package,"wallpapers","bundled");Directory.CreateDirectory(bundle);
        foreach(string id in new[]{"midnight-ridge","quiet-moon","forest-mist","blue-tide","amber-dunes","star-atlas"})File.WriteAllText(Path.Combine(package,"wallpapers",id+".jpg"),"generated fixture");
        foreach(string file in new[]{"water.mp4","water.gif","eyes.mp4","eyes.jpg","zero.jpg"})File.WriteAllText(Path.Combine(bundle,file),"independent installed fixture");
        File.WriteAllText(Path.Combine(bundle,"catalog.json"),new JavaScriptSerializer().Serialize(new{schema=1,wallpapers=new[]{new{id="water",name="水中会说话的眼睛",file="water.mp4",thumbnail="water.gif",note="内置动态"},new{id="eyes",name="会说话的眼睛2",file="eyes.mp4",thumbnail="eyes.jpg",note="内置动态"},new{id="zero",name="零二",file="zero.jpg",thumbnail="zero.jpg",note="内置静态"}}}),Encoding.UTF8);
        var defaults=WallpaperLibrary.Builtins(package);Require(defaults.Count==0,"Retired six defaults were reintroduced without the selected catalog");Require(File.Exists(Path.Combine(bundle,"water.mp4")),"Catalog filtering deleted source media");cases++;
        if(args.Length>1){var live=SteamWallpapers.Scan(SteamWallpapers.DiscoverRoots(new string[0]),()=>false);Console.WriteLine(new JavaScriptSerializer().Serialize(new{liveSteam=true,roots=live.WorkshopRoots.Count,wallpapers=live.Entries.Count,videos=live.Entries.Count(x=>x.Kind=="video"),images=live.Entries.Count(x=>!x.Animated),scenePreviews=live.Unsupported,invalid=live.Invalid}));}
        Console.WriteLine(new JavaScriptSerializer().Serialize(new{pass=true,cases=cases,history=true,sourcePreserved=true,secondarySteamLibrary=true,sceneLabeled=true,traversalRejected=true,malformedIsolated=true,cancellable=true}));return 0;
    }
}
