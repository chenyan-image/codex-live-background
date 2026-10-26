using System;using System.Collections;using System.Collections.Generic;using System.IO;using System.Security.Cryptography;using System.Web.Script.Serialization;

// Copy only the selected catalog. Original Steam files remain untouched.
internal static class BuiltinWallpapers {
    static string Inside(string directory,string name){if(String.IsNullOrWhiteSpace(name)||name!=Path.GetFileName(name)||name.IndexOfAny(new[]{'/','\\',':'})>=0)throw new InvalidDataException("内置壁纸路径无效。");return Path.Combine(directory,name);}
    public static void Generate(string destination,string source){
        string catalog=Path.Combine(source,"catalog.json");if(!File.Exists(catalog)||new FileInfo(catalog).Length>65536)throw new InvalidDataException("缺少内置壁纸清单。");var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(catalog));Directory.CreateDirectory(destination);var copied=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(Dictionary<string,object> item in (IEnumerable)data["items"]){foreach(string key in new[]{"file","thumbnail"}){string name=Convert.ToString(item[key]);if(!copied.Add(name))continue;string input=Inside(source,name);long bytes=Convert.ToInt64(item[key=="file"?"bytes":"thumbnailBytes"]);string hash;using(var stream=File.OpenRead(input))using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");if(new FileInfo(input).Length!=bytes||!String.Equals(hash,Convert.ToString(item[key=="file"?"sha256":"thumbnailSha256"]),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("内置壁纸校验失败："+name);File.Copy(input,Inside(destination,name));}}
        File.Copy(catalog,Path.Combine(destination,"catalog.json"));File.Copy(Path.Combine(source,"NOTICE.md"),Path.Combine(destination,"NOTICE.md"));
    }
    [STAThread]public static void Main(string[] args){Generate(args[0],args[1]);}
}
