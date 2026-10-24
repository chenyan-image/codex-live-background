using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

internal sealed class WallpaperEntry {
    public string Id,Name,Path,Thumbnail,Source,Kind,LastUsedUtc,AddedUtc,Note;
    public bool Available {get{return File.Exists(Path);}}
    public bool Animated {get{return Kind=="video"||Kind=="gif";}}
}
internal sealed class LibraryIndex {
    public int Schema=1;
    public List<WallpaperEntry> Entries=new List<WallpaperEntry>();
    public List<string> SteamRoots=new List<string>();
    public bool MigratedCurrentTheme;
}
internal sealed class WallpaperLibrary {
    readonly string file;readonly object gate=new object();
    LibraryIndex index=new LibraryIndex();
    public string Warning="";
    public WallpaperLibrary(string directory){file=System.IO.Path.Combine(directory,"wallpaper-library.json");Load();}
    void Load(){
        if(!File.Exists(file))return;
        try{
            if(new FileInfo(file).Length>4*1024*1024)throw new InvalidDataException("Index too large");
            index=new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Deserialize<LibraryIndex>(File.ReadAllText(file,Encoding.UTF8));
            if(index==null||index.Schema!=1||index.Entries==null||index.Entries.Count>2000)throw new InvalidDataException("Invalid library schema");
            index.Entries=index.Entries.Where(x=>x!=null&&!String.IsNullOrWhiteSpace(x.Path)&&System.IO.Path.IsPathRooted(x.Path)&&!String.IsNullOrWhiteSpace(x.Id)).GroupBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
            if(index.SteamRoots==null)index.SteamRoots=new List<string>();
        }catch(Exception){
            // Preserve the damaged file for recovery; never replace it with an empty library.
            string backup=file+".damaged-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");File.Copy(file,backup,false);
            index=new LibraryIndex();Warning="壁纸记录损坏，已保留备份并创建新记录。";
        }
    }
    public List<WallpaperEntry> Snapshot(){lock(gate)return new List<WallpaperEntry>(index.Entries);}
    public bool CurrentThemeMigrated {get{lock(gate)return index.MigratedCurrentTheme;}}
    public void FinishCurrentThemeMigration(){lock(gate){index.MigratedCurrentTheme=true;Save();}}
    public void RemapBuiltins(IEnumerable<WallpaperEntry> builtins){lock(gate){bool changed=false;foreach(var current in builtins){var saved=index.Entries.FirstOrDefault(x=>x.Source=="builtin"&&x.Id==current.Id);if(saved!=null&&!String.Equals(saved.Path,current.Path,StringComparison.OrdinalIgnoreCase)){saved.Path=current.Path;saved.Thumbnail=current.Path;changed=true;}}if(changed)Save();}}
    public List<string> SteamRoots(){lock(gate)return new List<string>(index.SteamRoots);}
    public void AddSteamRoot(string root){root=System.IO.Path.GetFullPath(root);lock(gate){if(!index.SteamRoots.Contains(root,StringComparer.OrdinalIgnoreCase))index.SteamRoots.Add(root);Save();}}
    public static string Identity(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(path).ToUpperInvariant()))).Replace("-","").ToLowerInvariant();}
    public static string MediaKind(string path){string ext=System.IO.Path.GetExtension(path).ToLowerInvariant();return ext==".mp4"||ext==".webm"?"video":ext==".gif"?"gif":new[]{".jpg",".jpeg",".png",".webp"}.Contains(ext)?"image":null;}
    public WallpaperEntry Add(string path,string name,string source,string thumbnail,string note){
        path=System.IO.Path.GetFullPath(path);if(MediaKind(path)==null)throw new InvalidDataException("仅支持视频、GIF 和图片。");
        lock(gate){
            var old=index.Entries.FirstOrDefault(x=>String.Equals(x.Path,path,StringComparison.OrdinalIgnoreCase));
            if(old!=null)return old;
            if(index.Entries.Count>=2000)throw new InvalidDataException("壁纸记录已满，请移除不再需要的记录。");
            var entry=new WallpaperEntry{Id=Identity(path),Path=path,Name=String.IsNullOrWhiteSpace(name)?System.IO.Path.GetFileNameWithoutExtension(path):name,Source=source,Kind=MediaKind(path),Thumbnail=thumbnail,Note=note,AddedUtc=DateTime.UtcNow.ToString("o")};
            index.Entries.Add(entry);Save();return entry;
        }
    }
    public void Applied(WallpaperEntry entry){lock(gate){var saved=index.Entries.FirstOrDefault(x=>x.Id==entry.Id);if(saved==null){if(index.Entries.Count>=2000)throw new InvalidDataException("壁纸记录已满。");index.Entries.Add(entry);saved=entry;}saved.LastUsedUtc=DateTime.UtcNow.ToString("o");Save();}}
    public void RememberPreview(WallpaperEntry entry,string thumbnail){lock(gate){entry.Thumbnail=thumbnail;Save();}}
    public void Forget(string id){lock(gate){index.Entries.RemoveAll(x=>x.Id==id);Save();}}
    void Save(){
        string directory=System.IO.Path.GetDirectoryName(file);Directory.CreateDirectory(directory);string temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(index),new UTF8Encoding(false));if(File.Exists(file))File.Replace(temporary,file,file+".bak",true);else File.Move(temporary,file);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static List<WallpaperEntry> Builtins(string root){
        var result=new List<WallpaperEntry>();string folder=System.IO.Path.Combine(root,"wallpapers");
        string[] ids={"midnight-ridge","quiet-moon","forest-mist","blue-tide","amber-dunes","star-atlas"};
        string[] names={"午夜山脊","静海月色","雾林深处","深蓝潮汐","琥珀沙丘","暗夜星图"};
        for(int i=0;i<ids.Length;i++){string path=System.IO.Path.Combine(folder,ids[i]+".jpg");if(File.Exists(path))result.Add(new WallpaperEntry{Id="builtin-"+ids[i],Name=names[i],Path=path,Thumbnail=path,Source="builtin",Kind="image",Note="内置原创背景 · 2560 × 1440"});}
        return result;
    }
}

internal sealed class SteamScanResult {
    public List<WallpaperEntry> Entries=new List<WallpaperEntry>();
    public List<string> WorkshopRoots=new List<string>();
    public int Unsupported,Invalid;public bool Truncated;
}
internal static class SteamWallpapers {
    static bool Regular(string path,bool directory){try{var attributes=File.GetAttributes(path);return (attributes&FileAttributes.ReparsePoint)==0&&((attributes&FileAttributes.Directory)!=0)==directory;}catch{return false;}}
    static bool PlainTree(string directory){try{for(string p=System.IO.Path.GetFullPath(directory);p!=null;p=System.IO.Path.GetDirectoryName(p))if(!Regular(p,true))return false;return true;}catch{return false;}}
    static string Text(string path,int limit){if(!PlainTree(System.IO.Path.GetDirectoryName(path))||!Regular(path,false)||new FileInfo(path).Length>limit)return "";return File.ReadAllText(path,Encoding.UTF8);}
    public static string Inside(string directory,string relative){
        if(String.IsNullOrWhiteSpace(relative)||System.IO.Path.IsPathRooted(relative))return null;
        try{
            string root=System.IO.Path.GetFullPath(directory).TrimEnd('\\')+"\\",path=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,relative));
            if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!Regular(path,false))return null;
            for(string parent=System.IO.Path.GetDirectoryName(path);parent!=null&&parent.Length>=root.Length-1;parent=System.IO.Path.GetDirectoryName(parent))if(!Regular(parent,true))return null;
            return path;
        }catch{return null;}
    }
    static string Value(Dictionary<string,object> project,string key){object value;return project.TryGetValue(key,out value)&&value is string?(string)value:"";}
    public static IEnumerable<string> DiscoverRoots(IEnumerable<string> extra){
        var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Action<string> add=delegate(string p){try{if(!String.IsNullOrWhiteSpace(p)&&Directory.Exists(p))roots.Add(System.IO.Path.GetFullPath(p));}catch{}};
        if(extra!=null)foreach(string p in extra)add(p);
        foreach(string registry in new[]{@"HKEY_CURRENT_USER\Software\Valve\Steam",@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam"})try{add(Convert.ToString(Registry.GetValue(registry,"SteamPath",null)));add(Convert.ToString(Registry.GetValue(registry,"InstallPath",null)));}catch{}
        add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));
        foreach(var drive in DriveInfo.GetDrives())try{if(drive.DriveType==DriveType.Fixed&&drive.IsReady){add(System.IO.Path.Combine(drive.RootDirectory.FullName,"Steam"));add(System.IO.Path.Combine(drive.RootDirectory.FullName,"SteamLibrary"));}}catch{}
        foreach(string root in roots.ToArray()){
            string vdf=Text(System.IO.Path.Combine(root,@"steamapps\libraryfolders.vdf"),2*1024*1024);
            foreach(Match match in Regex.Matches(vdf,"\"path\"\\s*\"((?:\\\\.|[^\"])*)\"",RegexOptions.IgnoreCase))add(match.Groups[1].Value.Replace("\\\\","\\").Replace("\\\"","\""));
        }
        return roots.Take(64).ToArray();
    }
    public static SteamScanResult Scan(IEnumerable<string> roots,Func<bool> cancel){
        var result=new SteamScanResult();var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int visited=0;
        foreach(string root in roots){
            if(cancel())break;
            string workshop=System.IO.Path.GetFileName(root)=="431960"?root:System.IO.Path.Combine(root,@"steamapps\workshop\content\431960");
            if(!PlainTree(workshop)||result.WorkshopRoots.Contains(workshop,StringComparer.OrdinalIgnoreCase))continue;
            result.WorkshopRoots.Add(workshop);
            try{foreach(string folder in Directory.EnumerateDirectories(workshop)){
                if(cancel())return result;if(++visited>5000){result.Truncated=true;return result;}if(!Regular(folder,true))continue;
                try{
                    var json=Text(System.IO.Path.Combine(folder,"project.json"),2*1024*1024);
                    var project=String.IsNullOrWhiteSpace(json)?new Dictionary<string,object>():new JavaScriptSerializer{MaxJsonLength=2*1024*1024}.Deserialize<Dictionary<string,object>>(json);
                    if(project==null){result.Invalid++;continue;}
                    string type=Value(project,"type"),title=Value(project,"title"),path=Inside(folder,Value(project,"file")),thumb=Inside(folder,Value(project,"preview"));
                    if(String.IsNullOrWhiteSpace(title))title=System.IO.Path.GetFileName(folder);
                    if(thumb==null)foreach(string name in new[]{"preview.jpg","preview.png","preview.gif","preview.webp"}){thumb=Inside(folder,name);if(thumb!=null)break;}
                    bool scene=type.Equals("scene",StringComparison.OrdinalIgnoreCase)||type.Equals("web",StringComparison.OrdinalIgnoreCase)||type.Equals("application",StringComparison.OrdinalIgnoreCase);
                    if(scene){result.Unsupported++;path=thumb;}
                    if(path==null||WallpaperLibrary.MediaKind(path)==null){
                        if(!scene&&String.IsNullOrEmpty(type))path=Directory.EnumerateFiles(folder).Take(64).FirstOrDefault(x=>Regular(x,false)&&WallpaperLibrary.MediaKind(x)!=null&&!System.IO.Path.GetFileName(x).StartsWith("preview",StringComparison.OrdinalIgnoreCase));
                        else path=null;
                    }
                    if(path==null||WallpaperLibrary.MediaKind(path)==null)continue;
                    string id=WallpaperLibrary.Identity(path);if(!ids.Add(id))continue;
                    result.Entries.Add(new WallpaperEntry{Id=id,Name=title.Length>160?title.Substring(0,160):title,Path=path,Thumbnail=thumb,Source="steam",Kind=WallpaperLibrary.MediaKind(path),Note=scene?"场景预览图 · 不包含原场景动画":"Wallpaper Engine · 使用原文件"});
                }catch{result.Invalid++;}
            }}catch{result.Invalid++;}
        }
        return result;
    }
}
