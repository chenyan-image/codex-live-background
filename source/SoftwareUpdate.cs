using System;using System.Collections;using System.Collections.Generic;using System.Diagnostics;using System.Drawing;using System.IO;using System.IO.Compression;using System.Linq;using System.Net;using System.Security.Cryptography;using System.Text;using System.Threading;using System.Web.Script.Serialization;using System.Windows.Forms;using Microsoft.Win32;

internal sealed class SoftwareFile {public string path{get;set;}public long bytes{get;set;}public string sha256{get;set;}}
internal sealed class SoftwareManifest {public string product{get;set;}public string version{get;set;}public SoftwareFile[] files{get;set;}}
internal sealed class SoftwareRelease {public string version{get;set;}public string tag{get;set;}public string url{get;set;}public string sha256{get;set;}public long bytes{get;set;}}
internal sealed class SoftwareUpdateRequest {public string Root{get;set;}public string Archive{get;set;}public string Sha256{get;set;}public string StateRoot{get;set;}public int ManagerPid{get;set;}public long ManagerStarted{get;set;}public string[] ResumeArguments{get;set;}public bool RequestExit{get;set;}}
internal sealed class UpdateHttpFailure:IOException {
    internal readonly string Host;internal readonly int Status;internal readonly DateTime RetryUtc;
    internal UpdateHttpFailure(string host,int status,DateTime retry,Exception inner=null):base("HTTP "+status+" ("+host+")",inner){Host=host;Status=status;RetryUtc=retry;}
}
internal sealed class UpdateLookupState {
    internal readonly object Gate=new object();internal bool HasCache;internal SoftwareRelease Cached;internal DateTime CachedUntilUtc,ApiRetryUtc;internal int ApiStatus;
}
internal static class SoftwareUpdate {
    public const string Repository="chenyan-image/codex-live-background-pet";
    internal const string LegacyRepository="chenyan-image/codex-live-background";
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=8*1024*1024};
    static string Slash(string value){return value.Replace('\\','/');}
    public static bool Allowed(string value){
        if(String.IsNullOrEmpty(value)||value.Contains(":")||value.StartsWith("/")||value.Contains("\\"))return false;string[] parts=value.Split('/');if(parts.Any(p=>p==".."||p=="."||p.Length==0))return false;
        string extension=Path.GetExtension(value).ToLowerInvariant();
        if(parts.Length==1)return value=="CodexLiveBackground.exe"||value=="CodexHubUpdater.exe"||value=="卸载 CodexHub.exe"||extension==".ps1"||new[]{"service-compatibility.json","legacy-managers.json","historical-install-files.json","cloud-pets.json","legacy-msi-products.json","README.md","CHANGELOG.md","NOTICE.md","LICENSE","THIRD_PARTY_NOTICES.md","使用说明.txt"}.Contains(value);
        if(parts.Length==2&&parts[0]=="source")return extension==".cs"||extension==".ps1"||value=="source/app.manifest";
        if(parts.Length==3&&parts[0]=="engine"&&(parts[1]=="scripts"||parts[1]=="assets"))return new[]{".mjs",".js",".ps1",".css",".json"}.Contains(extension);
        return parts.Length==2&&parts[0]=="docs"&&extension==".md";
    }
    public static string Hash(string path){using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
    public static bool Digest(string value){return value!=null&&value.Length==64&&value.All(Uri.IsHexDigit);}
    public static Uri ReleaseUri(string value){Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.Host!="github.com"||uri.UserInfo.Length>0||uri.Fragment.Length>0||!(uri.AbsolutePath.StartsWith("/"+Repository+"/releases/download/",StringComparison.Ordinal)||uri.AbsolutePath.StartsWith("/"+LegacyRepository+"/releases/download/",StringComparison.Ordinal)))throw new InvalidDataException("更新来源不是本项目 GitHub 发行页。");return uri;}
    static string ReleasePrefix(Uri uri){return "https://github.com/"+(uri.AbsolutePath.StartsWith("/"+Repository+"/",StringComparison.Ordinal)?Repository:LegacyRepository)+"/releases/download/";}
    internal static bool SameReleaseLocation(string left,string right){try{var a=ReleaseUri(left);var b=ReleaseUri(right);return a.AbsoluteUri.Substring(ReleasePrefix(a).Length).Equals(b.AbsoluteUri.Substring(ReleasePrefix(b).Length),StringComparison.Ordinal);}catch(InvalidDataException){return false;}}
    internal const string StableChannel="https://raw.githubusercontent.com/"+Repository+"/main/updates/stable.json";
    internal const string PreviewChannel=StableChannel; // Retain the internal name for older callers/tests.
    internal const string ReleasesApi="https://api.github.com/repos/"+Repository+"/releases?per_page=15";
    static readonly UpdateLookupState Lookup=new UpdateLookupState();
    internal static DateTime RetryTime(DateTime now,string retryAfter,string remaining,string reset){
        DateTime next=DateTime.MinValue;long seconds;DateTimeOffset date;
        if(Int64.TryParse(retryAfter,out seconds)&&seconds>=0&&seconds<=86400)next=now.AddSeconds(seconds);
        else if(DateTimeOffset.TryParse(retryAfter,out date))next=date.UtcDateTime;
        if(remaining=="0"&&Int64.TryParse(reset,out seconds))try{var value=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(seconds);if(value>next)next=value;}catch(ArgumentOutOfRangeException){}
        if(next<=now)next=now.AddMinutes(1);return next>now.AddDays(1)?now.AddDays(1):next;
    }
    static string GetText(string url,int max){
        var request=(HttpWebRequest)WebRequest.Create(url);request.UserAgent="CodexHub-Updater";request.Timeout=request.ReadWriteTimeout=20000;
        WebResponse response;
        try{response=request.GetResponse();}catch(WebException error){using(var failed=error.Response as HttpWebResponse){int status=failed==null?0:(int)failed.StatusCode;DateTime retry=DateTime.MinValue;if((status==403||status==429)&&request.Address.Host=="api.github.com")retry=RetryTime(DateTime.UtcNow,failed.Headers["Retry-After"],failed.Headers["X-RateLimit-Remaining"],failed.Headers["X-RateLimit-Reset"]);throw new UpdateHttpFailure(request.Address.Host,status,retry,error);}}
        using(response)using(var source=response.GetResponseStream())using(var target=new MemoryStream()){byte[] buffer=new byte[16384];int count;while((count=source.Read(buffer,0,buffer.Length))>0){if(target.Length+count>max)throw new InvalidDataException("更新清单过大。");target.Write(buffer,0,count);}return Encoding.UTF8.GetString(target.ToArray());}
    }
    static object Get(object value,string key){var dict=value as Dictionary<string,object>;object result;return dict!=null&&dict.TryGetValue(key,out result)?result:null;}
    internal static SoftwareRelease ValidateRelease(SoftwareRelease info){
        Version version;System.Text.RegularExpressions.Match tag;
        if(info==null||!Version.TryParse(info.version,out version)||version.Build<0||!Digest(info.sha256)||info.bytes<1||info.bytes>64*1024*1024||String.IsNullOrEmpty(info.tag))throw new InvalidDataException("公开更新频道清单字段无效。");
        tag=System.Text.RegularExpressions.Regex.Match(info.tag,@"^v([0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?)(?:-preview\.[0-9]+)?$");Version tagged;
        if(!tag.Success||!Version.TryParse(tag.Groups[1].Value,out tagged)||tagged.Major!=version.Major||tagged.Minor!=version.Minor||tagged.Build!=version.Build)throw new InvalidDataException("公开更新频道版本与发行标记不一致。");
        var uri=ReleaseUri(info.url);string expected=ReleasePrefix(uri)+info.tag+"/CodexHub-"+info.tag.Substring(1)+"-Update.zip";
        if(!uri.AbsoluteUri.Equals(expected,StringComparison.Ordinal))throw new InvalidDataException("更新包地址与本项目发行标记不一致。");
        return info;
    }
    internal static SoftwareRelease ParseRelease(string text){
        if(text==null||Encoding.UTF8.GetByteCount(text)>131072)throw new InvalidDataException("公开更新频道清单过大或为空。");
        SoftwareRelease info;try{info=Json.Deserialize<SoftwareRelease>(text);}catch(ArgumentException error){throw new InvalidDataException("公开更新频道清单不是有效 JSON。",error);}return ValidateRelease(info);
    }
    static SoftwareRelease ReadApi(Func<string,int,string> fetch){
        string text=fetch(ReleasesApi,8*1024*1024);if(text==null||Encoding.UTF8.GetByteCount(text)>8*1024*1024)throw new InvalidDataException("GitHub 发行清单过大或为空。");
        var data=Json.DeserializeObject(text) as object[];if(data==null)throw new InvalidDataException("GitHub 发行清单无效。");SoftwareRelease selected=null;
        foreach(object release in data){string tag=Convert.ToString(Get(release,"tag_name"));if(Object.Equals(Get(release,"draft"),true)||Object.Equals(Get(release,"prerelease"),true)||tag.IndexOf("-preview.",StringComparison.Ordinal)>=0)continue;var assets=Get(release,"assets") as object[];object manifest=(assets??new object[0]).FirstOrDefault(a=>Convert.ToString(Get(a,"name"))=="CodexHub-Update.json");if(manifest==null)continue;
            var manifestUri=ReleaseUri(Convert.ToString(Get(manifest,"browser_download_url")));string expected=ReleasePrefix(manifestUri)+tag+"/CodexHub-Update.json";if(!manifestUri.AbsoluteUri.Equals(expected,StringComparison.Ordinal))throw new InvalidDataException("GitHub 更新清单地址与发行标记不一致。");
            var info=ParseRelease(fetch(manifestUri.AbsoluteUri,131072));if(info.tag!=tag)throw new InvalidDataException("GitHub 更新清单与发行标记不一致。");object archive=(assets??new object[0]).FirstOrDefault(a=>SameReleaseLocation(Convert.ToString(Get(a,"browser_download_url")),info.url)&&Convert.ToInt64(Get(a,"size"))==info.bytes);if(archive==null)throw new InvalidDataException("更新包与发行清单不一致。");string digest=Convert.ToString(Get(archive,"digest"));if(digest.StartsWith("sha256:")&&!digest.Substring(7).Equals(info.sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("GitHub 更新包校验值不一致。");
            if(selected==null||Version.Parse(info.version)>Version.Parse(selected.version))selected=info;
        }return selected;
    }
    static SoftwareRelease Newer(SoftwareRelease info,Version current){if(info==null||info.tag.IndexOf("-preview.",StringComparison.Ordinal)>=0||Version.Parse(info.version)<=current)return null;return new SoftwareRelease{version=info.version,tag=info.tag,url=info.url,sha256=info.sha256,bytes=info.bytes};}
    static IOException Backoff(UpdateLookupState state){return new IOException("GitHub 更新接口暂时限制请求（HTTP "+state.ApiStatus+"），下次可在 "+state.ApiRetryUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")+" 后重试。公开频道当前也无法连接；未更改现有程序。");}
    public static SoftwareRelease FindRelease(Version current){ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;return FindRelease(current,GetText,()=>DateTime.UtcNow,Lookup);}
    internal static SoftwareRelease FindRelease(Version current,Func<string,int,string> fetch,Func<DateTime> clock,UpdateLookupState state){
        if(current==null)throw new ArgumentNullException("current");lock(state.Gate){DateTime now=clock().ToUniversalTime();if(state.HasCache&&now<state.CachedUntilUtc)return Newer(state.Cached,current);SoftwareRelease latest;
            try{latest=ParseRelease(fetch(PreviewChannel,131072));}
            catch(UpdateHttpFailure){
                if(now<state.ApiRetryUtc)throw Backoff(state);
                try{latest=ReadApi(fetch);state.ApiRetryUtc=DateTime.MinValue;state.ApiStatus=0;}
                catch(UpdateHttpFailure error){if(error.Host=="api.github.com"&&(error.Status==403||error.Status==429)){state.ApiRetryUtc=error.RetryUtc>now?error.RetryUtc:now.AddMinutes(1);state.ApiStatus=error.Status;throw Backoff(state);}throw new IOException("GitHub 更新检查暂时不可用："+error.Message,error);}
            }
            state.Cached=latest;state.HasCache=true;state.CachedUntilUtc=clock().ToUniversalTime().AddMinutes(5);return Newer(latest,current);
        }
    }
    public static string Download(SoftwareRelease release,string state,Action<string> report){
        ValidateRelease(release);var request=(HttpWebRequest)WebRequest.Create(ReleaseUri(release.url));request.UserAgent="CodexHub-Updater";request.Timeout=request.ReadWriteTimeout=30000;
        using(var response=request.GetResponse())using(var input=response.GetResponseStream())return DownloadStream(release,state,report,input);
    }
    internal static string DownloadStream(SoftwareRelease release,string state,Action<string> report,Stream input){
        ValidateRelease(release);string folder=Path.Combine(state,"updates",release.version);Directory.CreateDirectory(folder);string path=Path.Combine(folder,"program-"+Guid.NewGuid().ToString("N")+".zip");
        try{using(var output=File.Create(path)){byte[] buffer=new byte[65536];int count;long total=0;var clock=Stopwatch.StartNew();long last=-250;while((count=input.Read(buffer,0,buffer.Length))>0){total+=count;if(total>release.bytes)throw new InvalidDataException("更新包大小超出清单。");output.Write(buffer,0,count);if(clock.ElapsedMilliseconds-last>=250){last=clock.ElapsedMilliseconds;report("正在下载程序更新 "+Math.Min(100,total*100/release.bytes)+"% · 素材和设置保留");}}if(total!=release.bytes)throw new InvalidDataException("程序更新下载不完整。");}if(!Hash(path).Equals(release.sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("程序更新校验失败。");return path;}catch{if(File.Exists(path))File.Delete(path);throw;}
    }
    public static string Inside(string root,string relative){if(String.IsNullOrEmpty(relative)||Path.IsPathRooted(relative)||relative.Contains(":"))throw new InvalidDataException("更新路径无效。");string full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar))),prefix=Path.GetFullPath(root).TrimEnd('\\')+"\\";if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新路径超出程序目录。");string cursor=full;while(cursor!=null&&cursor.Length>=prefix.Length-1){if((File.Exists(cursor)||Directory.Exists(cursor))&&(File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("更新路径包含目录链接。");cursor=Path.GetDirectoryName(cursor);}return full;}
    internal static string RootPath(string value){string root=Path.GetFullPath(value);return root.Equals(Path.GetPathRoot(root),StringComparison.OrdinalIgnoreCase)?root:root.TrimEnd('\\');}
    internal static bool NeedsElevation(string root){
        if(new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))return false;
        try{using(var file=new FileStream(Path.Combine(RootPath(root),".codexhub-write-"+Guid.NewGuid().ToString("N")),FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.DeleteOnClose)){}return false;}catch(UnauthorizedAccessException){return true;}
    }
    public static SoftwareManifest VerifyArchive(string archive,string expected,string stage){
        if(!Digest(expected)||!Hash(archive).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新包校验失败。");Directory.CreateDirectory(stage);SoftwareManifest manifest;
        using(var zip=ZipFile.OpenRead(archive)){
            var entry=zip.Entries.SingleOrDefault(e=>e.FullName=="update-manifest.json");if(entry==null||entry.Length>1024*1024)throw new InvalidDataException("更新包缺少程序清单。");using(var reader=new StreamReader(entry.Open(),Encoding.UTF8))manifest=Json.Deserialize<SoftwareManifest>(reader.ReadToEnd());Version parsed;
            if(manifest==null||manifest.product!="CodexHub-program-update"||!Version.TryParse(manifest.version,out parsed)||manifest.files==null||manifest.files.Length<1||manifest.files.Length>500)throw new InvalidDataException("程序更新清单无效。");
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
            foreach(var item in manifest.files){if(!Allowed(item.path)||!names.Add(item.path)||item.bytes<0||item.bytes>32*1024*1024||!Digest(item.sha256)||(total+=item.bytes)>100*1024*1024)throw new InvalidDataException("更新包包含素材、设置或不允许的文件。");var source=zip.Entries.SingleOrDefault(e=>e.FullName==item.path);if(source==null||source.Length!=item.bytes)throw new InvalidDataException("更新文件长度不一致。");string destination=Inside(stage,item.path);Directory.CreateDirectory(Path.GetDirectoryName(destination));using(var input=source.Open())using(var output=File.Create(destination))input.CopyTo(output);if(!Hash(destination).Equals(item.sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新文件校验失败。");}
            if(zip.Entries.Any(e=>e.FullName!="update-manifest.json"&&!names.Contains(e.FullName)))throw new InvalidDataException("更新包存在未声明文件。");
            if(!names.Contains("CodexLiveBackground.exe"))throw new InvalidDataException("更新包缺少主程序。");
        }return manifest;
    }
    internal static void JsonFile(string path,object value){string temp=path+".update-"+Guid.NewGuid().ToString("N");File.WriteAllText(temp,Json.Serialize(value),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
    public static void Apply(SoftwareUpdateRequest request,Action<string> report,Action<int> afterCopy=null){
        string sid=System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;using(var lifecycle=new Mutex(false,"Local\\CodexLiveBackground.NativeLifecycle."+sid)){bool owns=false;try{try{owns=lifecycle.WaitOne(0);}catch(AbandonedMutexException){owns=true;}if(!owns)throw new IOException("安装、卸载或另一个更新正在进行。");ApplyLocked(request,report,afterCopy);}finally{if(owns)lifecycle.ReleaseMutex();}}
    }
    static void ApplyLocked(SoftwareUpdateRequest request,Action<string> report,Action<int> afterCopy){
        string identity;using(var sha=SHA256.Create())identity=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(request.Root).TrimEnd('\\').ToUpperInvariant()))).Replace("-","");using(var mutex=new Mutex(false,"Local\\CodexHub.SoftwareUpdate."+identity)){bool owns=false;try{try{owns=mutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}if(!owns)throw new IOException("另一个程序更新正在进行。");ApplyCore(request,report,afterCopy);}finally{if(owns)mutex.ReleaseMutex();}}
    }
    static void ApplyCore(SoftwareUpdateRequest request,Action<string> report,Action<int> afterCopy){
        string root=RootPath(request.Root),exe=Inside(root,"CodexLiveBackground.exe");if(!File.Exists(exe)||!File.Exists(Inside(root,"manifest.json"))||(root==Path.GetPathRoot(root)&&!CodexFollow.InstalledHere(root)))throw new InvalidDataException("更新目标不是完整的软件目录。");
        string transaction=Path.Combine(request.StateRoot,"updates","rollback-"+Guid.NewGuid().ToString("N")),stage=Path.Combine(transaction,"incoming"),backup=Path.Combine(transaction,"previous");var manifest=VerifyArchive(request.Archive,request.Sha256,stage);
        var old=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Inside(root,"manifest.json")));Version current;if(!Version.TryParse(Convert.ToString(old["version"]),out current)||Version.Parse(manifest.version)<=current)throw new InvalidDataException("已是当前版本，未覆盖程序。");
        var changed=new List<string>();Action<string,string> replace=(relative,source)=>{string destination=Inside(root,relative),saved=Inside(backup,relative);if(File.Exists(destination)){Directory.CreateDirectory(Path.GetDirectoryName(saved));File.Copy(destination,saved);}changed.Add(relative);Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(source,destination,true);};
        try{
            int count=0;foreach(var file in manifest.files){report("正在更新程序 "+(++count)+" / "+manifest.files.Length);replace(file.path,Inside(stage,file.path));if(afterCopy!=null)afterCopy(count);}
            var inventory=new Dictionary<string,SoftwareFile>(StringComparer.OrdinalIgnoreCase);
            foreach(object item in (IEnumerable)old["files"]){var data=(Dictionary<string,object>)item;string path=Slash(Convert.ToString(data["path"]));inventory[path]=new SoftwareFile{path=path.Replace('/','\\'),bytes=Convert.ToInt64(data["bytes"]),sha256=Convert.ToString(data["sha256"])};}
            foreach(var file in manifest.files)inventory[file.path]=new SoftwareFile{path=file.path.Replace('/','\\'),bytes=file.bytes,sha256=file.sha256};
            string metadata=Path.Combine(transaction,"manifest.json");JsonFile(metadata,new{version=manifest.version,platform="windows-x64",files=inventory.Values.ToArray()});replace("manifest.json",metadata);
            foreach(string name in new[]{".codex-background-install.json","installed.json"}){string path=Inside(root,name);if(!File.Exists(path))continue;var data=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));data["version"]=manifest.version;if(name.StartsWith(".codex")){if(Convert.ToString(data["productId"])!="codex-live-background/exe"||!Path.GetFullPath(Convert.ToString(data["root"])).TrimEnd('\\').Equals(root.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("安装记录归属不一致。");data["files"]=((IEnumerable)data["files"]).Cast<object>().Select(Convert.ToString).Concat(manifest.files.Select(f=>f.path.Replace('/','\\'))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();}string source=Path.Combine(transaction,name);JsonFile(source,data);replace(name,source);}
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLiveBackground",true)){if(key!=null&&String.Equals(Convert.ToString(key.GetValue("InstallLocation")),root,StringComparison.OrdinalIgnoreCase))key.SetValue("DisplayVersion",manifest.version);}
            JsonFile(Path.Combine(transaction,"result.json"),new{pass=true,version=manifest.version,changed=changed.ToArray(),materialsUnchanged=true,settingsUnchanged=true});
        }catch{for(int i=changed.Count-1;i>=0;i--){string target=Inside(root,changed[i]),saved=Inside(backup,changed[i]);if(File.Exists(saved))File.Copy(saved,target,true);else if(File.Exists(target))File.Delete(target);}throw;}
    }
    public static string Quote(string text){var value=new StringBuilder("\"");int slashes=0;foreach(char c in text){if(c=='\\'){slashes++;continue;}if(c=='"')value.Append('\\',slashes*2+1).Append(c);else value.Append('\\',slashes).Append(c);slashes=0;}return value.Append('\\',slashes*2).Append('"').ToString();}
    public static string StageHost(string root,SoftwareUpdateRequest request){string folder=Path.Combine(Path.GetTempPath(),"CodexHubUpdate-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string host=Path.Combine(folder,"CodexHubUpdater.exe");File.Copy(Path.Combine(root,"CodexHubUpdater.exe"),host);JsonFile(Path.Combine(folder,"request.json"),request);return host;}
    public static void Restart(SoftwareUpdateRequest request){using(var started=Process.Start(new ProcessStartInfo(Path.Combine(request.Root,"CodexLiveBackground.exe"),String.Join(" ",(request.ResumeArguments??new string[0]).Select(Quote))){UseShellExecute=false,WorkingDirectory=request.Root})){} }
}

internal static class SoftwareUpdateHost {
    delegate bool WindowCallback(IntPtr window,IntPtr data);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool EnumWindows(WindowCallback callback,IntPtr data);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool PostThreadMessage(uint thread,uint message,IntPtr w,IntPtr l);
    static void NormalExit(int id){uint thread=0;EnumWindows((window,data)=>{uint owner;uint current=GetWindowThreadProcessId(window,out owner);if(owner==(uint)id){thread=current;return false;}return true;},IntPtr.Zero);if(thread==0||!PostThreadMessage(thread,0x12,IntPtr.Zero,IntPtr.Zero))throw new IOException("无法请求管理器正常退出，程序未替换。");}
    [STAThread]static int Main(string[] args){if(args.Length==4&&args[2]=="--owner"){if(args[3]!=System.Security.Principal.WindowsIdentity.GetCurrent().User.Value){MessageBox.Show("Windows 授权切换到了其他账号，请使用原账号确认。","CodexHub");return 1;}args=args.Take(2).ToArray();}if(args.Length==2&&args[0]=="--follow"){try{return CodexFollow.Run(args[1]);}catch{return 1;}}if(args.Length==2&&args[0]=="--finish-guard")return GuardComponents.Run(args[1]);return RunUpdate(args);}
    static int RunUpdate(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);if(args.Length!=2||args[0]!="--apply")return 1;var form=new Form{Text="CodexHub · 程序更新",ClientSize=new Size(510,170),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterScreen,MaximizeBox=false,MinimizeBox=false,ControlBox=false};var text=new Label{Bounds=new Rectangle(24,25,460,65),Text="正在准备程序更新，素材与设置保留…"};var bar=new ProgressBar{Bounds=new Rectangle(24,106,460,18),Style=ProgressBarStyle.Marquee};form.Controls.Add(text);form.Controls.Add(bar);
        form.Shown+=delegate{new Thread(delegate(){SoftwareUpdateRequest request=null;try{request=new JavaScriptSerializer().Deserialize<SoftwareUpdateRequest>(File.ReadAllText(args[1]));if(request==null)throw new InvalidDataException("更新请求无效。");using(var followPause=CodexFollow.Suspend(request.Root,request.StateRoot)){CodexFollow.StopAndWait(request.Root,request.StateRoot);try{using(var process=Process.GetProcessById(request.ManagerPid)){if(process.StartTime.ToUniversalTime().Ticks!=request.ManagerStarted||!process.MainModule.FileName.Equals(Path.Combine(request.Root,"CodexLiveBackground.exe"),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("管理器身份发生变化。");if(request.RequestExit)NormalExit(request.ManagerPid);if(!process.WaitForExit(30000))throw new IOException("管理器尚未正常退出，程序没有替换。");}}catch(ArgumentException){}SoftwareUpdate.Apply(request,message=>form.BeginInvoke((MethodInvoker)delegate{text.Text=message+"\r\n壁纸、宠物、设置保持原样。";}));SoftwareUpdate.Restart(request);form.BeginInvoke((MethodInvoker)delegate{form.Close();});}}catch(Exception error){form.BeginInvoke((MethodInvoker)delegate{form.ControlBox=true;bar.Style=ProgressBarStyle.Blocks;text.Text="更新未完成："+error.Message+"\r\n原有程序已保留，可关闭此窗口后重新打开。";});}}){IsBackground=true}.Start();};Application.Run(form);return 0;
    }
}

internal sealed class GuardSoftwareMaintenance:IDisposable {
    readonly string root,marker;readonly Action resume;bool restart;
    public GuardSoftwareMaintenance(string directory,Action restartAction=null){
        root=Path.GetFullPath(directory);marker=SoftwareUpdate.Inside(root,"startup/stop-guard");resume=restartAction;
        var owned=new List<Process>();foreach(var process in Process.GetProcessesByName("DreamSkinPortHook"))try{if(process.SessionId==Process.GetCurrentProcess().SessionId&&process.MainModule.FileName.Equals(Path.Combine(root,@"startup\DreamSkinPortHook.exe"),StringComparison.OrdinalIgnoreCase))owned.Add(process);else process.Dispose();}catch{process.Dispose();throw new IOException("无法核对后台服务，程序未替换。");}
        if(owned.Count==0)return;
        if(File.Exists(marker)){foreach(var process in owned)process.Dispose();throw new IOException("后台服务正在停止，请稍后更新。");}
        restart=true;File.WriteAllText(marker,"CodexHub program update: cooperative guard restart.");
        try{foreach(var process in owned)using(process)if(!process.WaitForExit(45000))throw new IOException("后台服务尚未退出，程序未替换。");
            string monitor=SoftwareUpdate.Inside(root,"startup/monitor-state.json");if(File.Exists(monitor)){var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(monitor));int pid;if(data!=null&&data.ContainsKey("pid")&&Int32.TryParse(Convert.ToString(data["pid"]),out pid))try{using(var process=Process.GetProcessById(pid))if(process.SessionId==Process.GetCurrentProcess().SessionId&&process.ProcessName.Equals("powershell",StringComparison.OrdinalIgnoreCase)&&!process.WaitForExit(45000))throw new IOException("背景监控尚未退出，程序未替换。");}catch(ArgumentException){}}
        }catch{Dispose();throw;}
    }
    public void Dispose(){if(!restart)return;restart=false;if(File.Exists(marker))File.Delete(marker);if(resume!=null){resume();return;}using(var process=Process.Start(new ProcessStartInfo(Path.Combine(root,@"startup\DreamSkinPortHook.exe"),"--guard"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.Combine(root,"startup")})){} }
}

internal static class GuardComponents {
    [System.Runtime.InteropServices.DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId,uint flags,IntPtr item1,IntPtr item2);
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=8*1024*1024};
    static readonly string[] Metadata={"cloud-pets.json","historical-install-files.json","source/cloud-pets.json","source/historical-install-files.json"};
    static Dictionary<string,object> Read(string path){return Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static byte[] Resource(System.Reflection.Assembly assembly,string name){using(var input=assembly.GetManifestResourceStream(name)){if(input==null||input.Length>2*1024*1024)throw new InvalidDataException("后台组件资源缺失或大小无效。");using(var output=new MemoryStream()){input.CopyTo(output);return output.ToArray();}}}
    internal static bool Required(string root){try{
        if(Metadata.Any(file=>!File.Exists(SoftwareUpdate.Inside(root,file))))return true;
        var resources=System.Reflection.Assembly.GetExecutingAssembly();
        foreach(var item in new[]{Tuple.Create("CodexBackground.ico","CodexHub.AppIcon"),Tuple.Create("source/assets/brand/codexhub.png","CodexHub.Brand")}){
            string file=SoftwareUpdate.Inside(root,item.Item1);if(!File.Exists(file)||!SoftwareUpdate.Hash(file).Equals(Hash(Resource(resources,item.Item2)),StringComparison.OrdinalIgnoreCase))return true;
        }
        if(!CodexFollow.InstalledHere(root))return false;var manifest=Read(SoftwareUpdate.Inside(root,"manifest.json"));Version expected=Version.Parse(Convert.ToString(manifest["version"])),actual;string guard=SoftwareUpdate.Inside(root,"startup/DreamSkinPortHook.exe");return !File.Exists(guard)||!Version.TryParse(FileVersionInfo.GetVersionInfo(guard).FileVersion,out actual)||actual<expected;
    }catch{return true;}}
    internal static void Install(string directory,string state,System.Reflection.Assembly resources=null,Action restartOverride=null,bool manageRegistration=true,Action<int> afterCopy=null){
        string root=SoftwareUpdate.RootPath(directory);if(!File.Exists(SoftwareUpdate.Inside(root,"CodexLiveBackground.exe"))||(root==Path.GetPathRoot(root)&&!CodexFollow.InstalledHere(root)))throw new InvalidDataException("后台组件目标不是完整的软件目录。");
        resources=resources??typeof(SoftwareUpdateHost).Assembly;var manifest=Read(SoftwareUpdate.Inside(root,"manifest.json"));string version=Convert.ToString(manifest["version"]);Version.Parse(version);
        var component=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);byte[] cloud=Resource(resources,"CodexHub.CloudPetCatalog"),history=Resource(resources,"CodexHub.HistoricalInstallCatalog");component["cloud-pets.json"]=component["source/cloud-pets.json"]=cloud;component["historical-install-files.json"]=component["source/historical-install-files.json"]=history;component["CodexBackground.ico"]=Resource(resources,"CodexHub.AppIcon");component["source/assets/brand/codexhub.png"]=Resource(resources,"CodexHub.Brand");
        bool installed=CodexFollow.InstalledHere(root);if(File.Exists(SoftwareUpdate.Inside(root,".codex-background-install.json"))&&!installed)throw new InvalidDataException("安装记录归属不一致。");if(installed){byte[] program=Resource(resources,"CodexHub.GuardProgram");var compatibility=Read(SoftwareUpdate.Inside(root,"service-compatibility.json"));bool trusted=false;foreach(Dictionary<string,object> profile in (IEnumerable)compatibility["profiles"]){if(Convert.ToString(profile["name"])!="packaged-"+version)continue;var hashes=(Dictionary<string,object>)profile["hashes"];trusted=Hash(program).Equals(Convert.ToString(hashes["DreamSkinPortHook.exe"]),StringComparison.OrdinalIgnoreCase);}if(!trusted)throw new InvalidDataException("后台组件与已验证的程序版本不匹配。");
            component["startup/DreamSkinPortHook.exe"]=program;component["startup/DreamSkinPortHook.cs"]=Resource(resources,"CodexHub.GuardSource");component["startup/ShellIdentity.cs"]=Resource(resources,"CodexHub.ShellIdentitySource");component["source/service-compatibility.json"]=File.ReadAllBytes(SoftwareUpdate.Inside(root,"service-compatibility.json"));
        }
        foreach(string path in component.Keys)SoftwareUpdate.Inside(root,path);if(installed&&manageRegistration){CodexFollow.StopAndWait(root,state);CodexFollow.Configure(root,state,false);}
        string previous=Path.Combine(state,"updates","components-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(previous);var changed=new List<string>();Action<string,byte[]> write=(relative,bytes)=>{string target=SoftwareUpdate.Inside(root,relative),saved=SoftwareUpdate.Inside(previous,relative);if(File.Exists(target)){Directory.CreateDirectory(Path.GetDirectoryName(saved));File.Copy(target,saved);}changed.Add(relative);Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllBytes(target,bytes);};
        using(var guard=installed?new GuardSoftwareMaintenance(root,restartOverride):null){try{
            int copied=0;foreach(var item in component){write(item.Key,item.Value);if(afterCopy!=null)afterCopy(++copied);}
            var inventory=new Dictionary<string,SoftwareFile>(StringComparer.OrdinalIgnoreCase);foreach(Dictionary<string,object> item in (IEnumerable)manifest["files"]){string path=Convert.ToString(item["path"]).Replace('\\','/');inventory[path]=new SoftwareFile{path=path.Replace('/','\\'),bytes=Convert.ToInt64(item["bytes"]),sha256=Convert.ToString(item["sha256"])};}
            foreach(var item in component)inventory[item.Key]=new SoftwareFile{path=item.Key.Replace('/','\\'),bytes=item.Value.LongLength,sha256=Hash(item.Value)};manifest["files"]=inventory.Values.ToArray();write("manifest.json",Encoding.UTF8.GetBytes(Json.Serialize(manifest)));
            if(installed){string marker=SoftwareUpdate.Inside(root,".codex-background-install.json");var record=Read(marker);record["files"]=((IEnumerable)record["files"]).Cast<object>().Select(Convert.ToString).Concat(component.Keys.Select(path=>path.Replace('/','\\'))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();write(".codex-background-install.json",Encoding.UTF8.GetBytes(Json.Serialize(record)));}
            SoftwareUpdate.JsonFile(Path.Combine(previous,"result.json"),new{pass=true,version,components=component.Keys.ToArray(),settingsUnchanged=true,materialsUnchanged=true});
            try{SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);}catch(System.Runtime.InteropServices.ExternalException){}
        }catch{for(int i=changed.Count-1;i>=0;i--){string target=SoftwareUpdate.Inside(root,changed[i]),saved=SoftwareUpdate.Inside(previous,changed[i]);if(File.Exists(saved))File.Copy(saved,target,true);else if(File.Exists(target))File.Delete(target);}throw;}}
    }
    internal static string FinishAtStartup(string root){try{if(!Required(root))return null;using(var process=Process.Start(new ProcessStartInfo(Path.Combine(root,"CodexHubUpdater.exe"),"--finish-guard "+SoftwareUpdate.Quote(root)+" --owner "+SoftwareUpdate.Quote(System.Security.Principal.WindowsIdentity.GetCurrent().User.Value)){UseShellExecute=SoftwareUpdate.NeedsElevation(root),Verb="runas",WorkingDirectory=root})){if(!process.WaitForExit(120000)||process.ExitCode!=0||Required(root))return "后台组件更新尚未完成，请在服务页查看提示，必要时用完整 EXE 修复安装。";}return null;}catch(Exception error){return "后台组件未能更新："+error.Message;}}
    internal static int Run(string root){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);int result=0;var form=new Form{Text="CodexHub · 完成组件更新",ClientSize=new Size(510,170),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterScreen,MaximizeBox=false,MinimizeBox=false,ControlBox=false};var text=new Label{Bounds=new Rectangle(24,25,460,65),Text="正在完成后台组件更新，壁纸与设置保留…"};var bar=new ProgressBar{Bounds=new Rectangle(24,106,460,18),Style=ProgressBarStyle.Marquee};form.Controls.Add(text);form.Controls.Add(bar);
        form.Shown+=delegate{new Thread(delegate(){try{string state=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexDreamSkin");using(var pause=CodexFollow.Suspend(root,state)){string sid=System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;using(var mutex=new Mutex(false,"Local\\CodexLiveBackground.NativeLifecycle."+sid)){bool owns=false;try{try{owns=mutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}if(!owns)throw new IOException("另一个安装或更新正在进行。");Install(root,state);}finally{if(owns)mutex.ReleaseMutex();}}}form.BeginInvoke((MethodInvoker)delegate{form.Close();});}catch(Exception error){result=1;form.BeginInvoke((MethodInvoker)delegate{form.ControlBox=true;bar.Style=ProgressBarStyle.Blocks;text.Text="后台组件更新未完成："+error.Message+"\r\n可关闭此窗口，在管理器中继续查看日志。";});}}){IsBackground=true}.Start();};Application.Run(form);return result;
    }
}
