using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
internal static class LightweightDefaultsTests {
    public static int Main(string[] args){
        string root=Path.GetFullPath(args[0]);var entries=WallpaperLibrary.Builtins(root);
        if(entries.Count!=4||entries.Count(x=>x.Kind=="video")!=1||entries.Count(x=>x.Kind=="gif")!=1||entries.Count(x=>x.Kind=="image")!=2)throw new Exception("Selected wallpaper catalog differs from requested four assets");
        foreach(var item in entries)if(!item.Available||!File.Exists(item.Thumbnail)||!item.Path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!item.Thumbnail.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Default media depends on an external path");
        if(Directory.Exists(Path.Combine(root,"wallpapers","bundled")))throw new Exception("Retired media directory is still shipped");
        foreach(string retired in new[]{"midnight-ridge","quiet-moon","forest-mist","blue-tide","amber-dunes","star-atlas"})if(File.Exists(Path.Combine(root,"wallpapers",retired+".jpg")))throw new Exception("Retired original wallpaper remains in package");
        var rain=entries.Single(x=>x.Kind=="video");if(new FileInfo(rain.Path).Length!=101871488)throw new Exception("4K original rain video was replaced with a smaller conversion");
        string state=Path.Combine(Path.GetTempPath(),"CodexHub-Defaults-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(state);try{var library=new WallpaperLibrary(state);library.Applied(new WallpaperEntry{Id="builtin-midnight-ridge",Name="旧内置",Source="builtin",Path=Path.Combine(state,"old.jpg"),Kind="image"});library.Applied(new WallpaperEntry{Id="local-own",Name="本地导入",Source="local",Path=Path.Combine(state,"own.jpg"),Kind="image"});library.Applied(new WallpaperEntry{Id=rain.Id,Name=rain.Name,Source="builtin",Path=Path.Combine(state,"old-rain.mp4"),Kind="video"});library.RemapBuiltins(entries);if(library.Snapshot().Any(x=>x.Id=="builtin-midnight-ridge")||!library.Snapshot().Any(x=>x.Id=="local-own")||library.Snapshot().Single(x=>x.Id==rain.Id).Path!=rain.Path)throw new Exception("Built-in migration removed local imports or failed to update history");}finally{File.Delete(Path.Combine(state,"wallpaper-library.json"));if(File.Exists(Path.Combine(state,"wallpaper-library.json.bak")))File.Delete(Path.Combine(state,"wallpaper-library.json.bak"));Directory.Delete(state);}
        Console.WriteLine(new JavaScriptSerializer().Serialize(new{pass=true,defaults=4,videos=1,gifs=1,original4k=true,sourceIndependent=true,thumbnailsPresent=true,retiredAssetsAbsent=true,historyMigrated=true,localImportsPreserved=true}));return 0;
    }
}
