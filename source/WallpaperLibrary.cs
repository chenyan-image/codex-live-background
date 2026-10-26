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
    public string Id,Name,Path,Thumbnail,Source,Kind,LastUsedUtc,AddedUtc,Note;public BuiltinRemoteAsset Remote;
    public bool Available {get{return File.Exists(Path);}}
    public bool Animated {get{return Kind=="video"||Kind=="gif";}}
}
internal sealed class LibraryIndex {
    public int Schema=1;
    public List<WallpaperEntry> Entries=new List<WallpaperEntry>();
    public List<string> SteamRoots=new List<string>();
    public bool MigratedCurrentTheme;
}
internal sealed class BuiltinWallpaperCatalog {public BuiltinWallpaperItem[] items{get;set;}}
internal sealed class BuiltinWallpaperItem {public string id{get;set;}public string name{get;set;}public string file{get;set;}public string thumbnail{get;set;}public string note{get;set;}public BuiltinRemoteAsset remote{get;set;}}
internal sealed class BuiltinRemoteAsset {public string file{get;set;}public string url{get;set;}public long bytes{get;set;}public string sha256{get;set;}}
internal sealed class WallpaperLibrary {
    public const int MaxNameCodePoints=4096;
    const int MaxIndexBytes=64*1024*1024;
    public static string ValidateName(string value){
        if(String.IsNullOrWhiteSpace(value))throw new InvalidDataException("请输入壁纸名称。");
        int count=0;for(int i=0;i<value.Length;i++){
            char c=value[i];if(c<0x20||c==0x7f)throw new InvalidDataException("壁纸名称不能包含换行或控制字符。");
            if(Char.IsHighSurrogate(c)){if(i+1>=value.Length||!Char.IsLowSurrogate(value[i+1]))throw new InvalidDataException("壁纸名称包含无效字符。");i++;}else if(Char.IsLowSurrogate(c))throw new InvalidDataException("壁纸名称包含无效字符。");
            if(++count>MaxNameCodePoints)throw new InvalidDataException("壁纸名称最多支持 4096 个 Unicode 字符。");
        }
        return value.Trim();
    }
    readonly string file;readonly object gate=new object();
    LibraryIndex index=new LibraryIndex();
    public string Warning="";
    public WallpaperLibrary(string directory){file=System.IO.Path.Combine(directory,"wallpaper-library.json");Load();if(index.Entries.RemoveAll(IsRetiredSeed)>0){index.MigratedCurrentTheme=true;Save();}}
    public static bool IsRetiredSeed(WallpaperEntry entry){return entry!=null&&entry.Source=="recovered"&&entry.Name=="纸上微光"&&System.IO.Path.GetFileName(entry.Path).Equals("dream-reference.jpg",StringComparison.OrdinalIgnoreCase)&&System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(entry.Path)).Equals("active-theme",StringComparison.OrdinalIgnoreCase);}
    void Load(){
        if(!File.Exists(file))return;
        try{
            if(new FileInfo(file).Length>MaxIndexBytes)throw new InvalidDataException("Index too large");
            index=new JavaScriptSerializer{MaxJsonLength=MaxIndexBytes}.Deserialize<LibraryIndex>(File.ReadAllText(file,Encoding.UTF8));
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
    public void RemapBuiltins(IEnumerable<WallpaperEntry> builtins){lock(gate){
        var currentList=builtins.ToArray();var ids=new HashSet<string>(currentList.Select(v=>v.Id),StringComparer.OrdinalIgnoreCase);bool changed=index.Entries.RemoveAll(v=>v.Source=="builtin"&&!ids.Contains(v.Id))>0;
        foreach(var current in currentList){var saved=index.Entries.FirstOrDefault(x=>x.Source=="builtin"&&x.Id==current.Id);if(saved==null)continue;bool retainVideo=current.Remote!=null&&saved.Kind=="video"&&saved.Available;string path=retainVideo?saved.Path:current.Path,kind=retainVideo?saved.Kind:current.Kind;bool remoteChanged=(saved.Remote==null)!=(current.Remote==null)||(saved.Remote!=null&&current.Remote!=null&&saved.Remote.url!=current.Remote.url);
            if(saved.Path!=path||saved.Thumbnail!=current.Thumbnail||saved.Kind!=kind||saved.Note!=current.Note||saved.Name!=current.Name||remoteChanged){saved.Path=path;saved.Thumbnail=current.Thumbnail;saved.Kind=kind;saved.Note=current.Note;saved.Name=current.Name;saved.Remote=current.Remote;changed=true;}}
        if(changed)Save();
    }}
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
            var entry=new WallpaperEntry{Id=Identity(path),Path=path,Name=ValidateName(String.IsNullOrWhiteSpace(name)?System.IO.Path.GetFileNameWithoutExtension(path):name),Source=source,Kind=MediaKind(path),Thumbnail=thumbnail,Note=note,AddedUtc=DateTime.UtcNow.ToString("o")};
            index.Entries.Add(entry);Save();return entry;
        }
    }
    public WallpaperEntry AddLocalBatch(IEnumerable<string> paths){
        var incoming=paths.Select(path=>System.IO.Path.GetFullPath(path)).Distinct(StringComparer.OrdinalIgnoreCase).Select(path=>new WallpaperEntry{Id=Identity(path),Path=path,Name=ValidateName(System.IO.Path.GetFileNameWithoutExtension(path)),Source="local",Kind=MediaKind(path),Note="本地文件引用；不会复制或删除原文件",AddedUtc=DateTime.UtcNow.ToString("o")}).ToArray();
        if(incoming.Any(item=>item.Kind==null))throw new InvalidDataException("仅支持视频、GIF 和图片。");
        lock(gate){var added=incoming.Where(item=>!index.Entries.Any(old=>old.Id==item.Id)).ToArray();if(index.Entries.Count+added.Length>2000)throw new InvalidDataException("壁纸记录已满，请移除不再需要的记录。");index.Entries.AddRange(added);try{if(added.Length>0)Save();}catch{foreach(var item in added)index.Entries.Remove(item);throw;}return incoming.Length==0?null:index.Entries.First(item=>item.Id==incoming.Last().Id);}
    }
    public void Applied(WallpaperEntry entry){string name=ValidateName(entry.Name);lock(gate){var saved=index.Entries.FirstOrDefault(x=>x.Id==entry.Id);if(saved==null){if(index.Entries.Count>=2000)throw new InvalidDataException("壁纸记录已满。");index.Entries.Add(entry);saved=entry;}saved.Name=name;saved.LastUsedUtc=DateTime.UtcNow.ToString("o");Save();}}
    public void RememberPreview(WallpaperEntry entry,string thumbnail){lock(gate){entry.Thumbnail=thumbnail;Save();}}
    public void Forget(string id){lock(gate){index.Entries.RemoveAll(x=>x.Id==id);Save();}}
    void Save(){
        string directory=System.IO.Path.GetDirectoryName(file);Directory.CreateDirectory(directory);string temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{string json=new JavaScriptSerializer{MaxJsonLength=MaxIndexBytes}.Serialize(index);if(Encoding.UTF8.GetByteCount(json)>MaxIndexBytes)throw new InvalidDataException("壁纸记录已达到容量上限。");File.WriteAllText(temporary,json,new UTF8Encoding(false));if(File.Exists(file))File.Replace(temporary,file,file+".bak",true);else File.Move(temporary,file);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static List<WallpaperEntry> Builtins(string root){
        var result=new List<WallpaperEntry>();string folder=System.IO.Path.Combine(root,"wallpapers");
        string catalog=System.IO.Path.Combine(folder,"catalog.json");if(!File.Exists(catalog))return result;if(new FileInfo(catalog).Length>65536)throw new InvalidDataException("内置壁纸清单过大。");var data=new JavaScriptSerializer().Deserialize<BuiltinWallpaperCatalog>(File.ReadAllText(catalog));if(data==null||data.items==null||data.items.Length>32)throw new InvalidDataException("内置壁纸清单无效。");
        foreach(var item in data.items){if(item==null||String.IsNullOrWhiteSpace(item.id)||!Regex.IsMatch(item.id,@"^[a-z0-9-]{1,80}$")||new[]{item.file,item.thumbnail}.Any(n=>String.IsNullOrWhiteSpace(n)||n!=System.IO.Path.GetFileName(n)||n.IndexOfAny(new[]{'/','\\',':'})>=0))throw new InvalidDataException("内置壁纸路径无效。");string path=System.IO.Path.Combine(folder,item.file),thumbnail=System.IO.Path.Combine(folder,item.thumbnail);if(File.Exists(path)&&File.Exists(thumbnail)&&MediaKind(path)!=null)result.Add(new WallpaperEntry{Id="builtin-"+item.id,Name=ValidateName(item.name),Path=path,Thumbnail=thumbnail,Source="builtin",Kind=MediaKind(path),Note=item.note,Remote=item.remote});}
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
                    result.Entries.Add(new WallpaperEntry{Id=id,Name=WallpaperLibrary.ValidateName(title),Path=path,Thumbnail=thumb,Source="steam",Kind=WallpaperLibrary.MediaKind(path),Note=scene?"场景预览图 · 不包含原场景动画":"Wallpaper Engine · 使用原文件"});
                }catch{result.Invalid++;}
            }}catch{result.Invalid++;}
        }
        return result;
    }
}
