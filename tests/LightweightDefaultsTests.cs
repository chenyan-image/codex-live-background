using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
internal static class LightweightDefaultsTests {
    public static int Main(string[] args){
        string root=Path.GetFullPath(args[0]);var entries=WallpaperLibrary.Builtins(root);
        if(entries.Count!=6||entries.Any(x=>x.Animated))throw new Exception("Lightweight release must contain only six original defaults");
        foreach(var item in entries)if(!item.Available||!File.Exists(item.Thumbnail)||!item.Path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!item.Thumbnail.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Default media depends on an external path");
        if(Directory.Exists(Path.Combine(root,"wallpapers","bundled")))throw new Exception("Retired media directory is still shipped");
        Console.WriteLine(new JavaScriptSerializer().Serialize(new{pass=true,defaults=6,videos=0,sourceIndependent=true,thumbnailsPresent=true,retiredAssetsAbsent=true}));return 0;
    }
}
