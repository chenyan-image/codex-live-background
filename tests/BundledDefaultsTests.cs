using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
internal static class BundledDefaultsTests {
    public static int Main(string[] args){
        string root=Path.GetFullPath(args[0]);var entries=WallpaperLibrary.Builtins(root);
        if(entries.Count!=9||entries.Count(x=>x.Animated)!=2)throw new Exception("Release must contain nine defaults and two videos");
        foreach(var item in entries)if(!item.Available||!File.Exists(item.Thumbnail)||!item.Path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!item.Thumbnail.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Default media depends on an external path");
        if(entries.Single(x=>x.Id=="builtin-zero-two").Animated)throw new Exception("Scene preview was mislabeled as animation");
        if(entries.Where(x=>x.Animated).Any(x=>x.Note.IndexOf("4K",StringComparison.Ordinal)<0))throw new Exception("Bundled video details missing");
        Console.WriteLine(new JavaScriptSerializer().Serialize(new{pass=true,defaults=9,videos=2,sourceIndependent=true,thumbnailsPresent=true,sceneStatic=true}));return 0;
    }
}
