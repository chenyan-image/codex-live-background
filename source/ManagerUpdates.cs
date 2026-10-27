using System;using System.Diagnostics;using System.Drawing;using System.IO;using System.Linq;using System.Threading;using System.Windows.Forms;
internal sealed class ProgramUpdateButton:ActionButton {public bool UpdateAvailable;protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(UpdateAvailable)using(var fill=new SolidBrush(Color.FromArgb(246,81,81)))e.Graphics.FillEllipse(fill,Width-15,6,7,7);}}
internal sealed class LocalProgramNotes {
    public string Tag,Version,RelativePath,Sha256,Body,Transaction;
    public long Bytes;
    internal const int NotesLimit=131072;
    static System.Web.Script.Serialization.JavaScriptSerializer Json{get{return new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=4*1024*1024};}}
    static bool Regular(string path){return File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0;}
    static object Value(object value,string key){var map=value as System.Collections.Generic.Dictionary<string,object>;object found;return map!=null&&map.TryGetValue(key,out found)?found:null;}
    static string Normalize(string path){return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);}
    static bool SameRoot(string left,string right){return !String.IsNullOrWhiteSpace(left)&&Normalize(left).Equals(Normalize(right),StringComparison.OrdinalIgnoreCase);}
    static string ReadText(string path,int limit){if(!Regular(path)||new FileInfo(path).Length>limit)throw new InvalidDataException("本地更新说明记录不可用。");return File.ReadAllText(path,System.Text.Encoding.UTF8);}
    static bool SameFile(SoftwareFile file,string path){return file!=null&&file.bytes>=0&&Regular(path)&&new FileInfo(path).Length==file.bytes&&SoftwareUpdate.Digest(file.sha256)&&SoftwareUpdate.Hash(path).Equals(file.sha256,StringComparison.OrdinalIgnoreCase);}
    static SoftwareFile Entry(SoftwareManifest manifest,string path){var matches=(manifest.files??new SoftwareFile[0]).Where(file=>file!=null&&String.Equals((file.path??"").Replace('\\','/'),path,StringComparison.OrdinalIgnoreCase)).ToArray();return matches.Length==1?matches[0]:null;}
    internal static LocalProgramNotes Read(string root,Version current){try{
        if(current==null)return null;
        var manifest=Json.Deserialize<SoftwareManifest>(ReadText(Path.Combine(root,"manifest.json"),4*1024*1024));Version version;
        if(manifest==null||!System.Version.TryParse(manifest.version,out version)||version!=current||manifest.files==null||manifest.files.Length>10000)return null;
        string docs=Path.Combine(root,"docs");if(!Directory.Exists(docs)||(File.GetAttributes(docs)&FileAttributes.ReparsePoint)!=0)return null;
        LocalProgramNotes selected=null;
        foreach(var file in manifest.files.Where(file=>file!=null&&System.Text.RegularExpressions.Regex.IsMatch((file.path??"").Replace('\\','/'),@"^docs/release-[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?(?:-preview\.[0-9]+)?\.md$" )).Take(128)){
            string relative=file.path.Replace('\\','/'),path=SoftwareUpdate.Inside(root,relative);if(file.bytes>NotesLimit||!SameFile(file,path))continue;
            string text=ReadText(path,NotesLimit),tag="v"+Path.GetFileNameWithoutExtension(path).Substring("release-".Length);
            var heading=System.Text.RegularExpressions.Regex.Match(text,@"\A\s*#\s+(v[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?(?:-preview\.[0-9]+)?)\s*(?:\r?\n|$)");
            var internalVersion=System.Text.RegularExpressions.Regex.Match(text,@"(?m)^\s*内部版本\s*[：:]\s*([0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?)(?=[。\s]|$)");Version parsed,tagged;
            if(!heading.Success||heading.Groups[1].Value!=tag||!internalVersion.Success||!System.Version.TryParse(internalVersion.Groups[1].Value,out parsed)||parsed!=current||!System.Version.TryParse(tag.Substring(1).Split('-')[0],out tagged)||tagged.Major!=current.Major||tagged.Minor!=current.Minor||tagged.Build!=current.Build)continue;
            string body=Summarize(text);if(String.IsNullOrWhiteSpace(body))continue;if(selected!=null)return null;
            selected=new LocalProgramNotes{Tag=tag,Version=current.ToString(),RelativePath=relative,Sha256=file.sha256,Bytes=file.bytes,Body=body};
        }return selected;
    }catch{return null;}}
    static string Summarize(string text){
        var lines=text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(line=>line.Trim()).Where(line=>line.Length>0).ToArray();
        var selected=lines.Where(line=>line.StartsWith("- ")||line.StartsWith("* ")).Take(10).Select(line=>"• "+line.Substring(2)).ToArray();
        if(selected.Length==0)selected=lines.Where(line=>!line.StartsWith("#")&&!line.StartsWith("内部版本")).Take(3).ToArray();
        string body=String.Join(Environment.NewLine,selected);body=System.Text.RegularExpressions.Regex.Replace(body,@"\[([^\]]+)\]\([^\)]+\)","$1").Replace("**","").Replace("`","");
        return body.Length>2400?body.Substring(0,2399)+"…":body;
    }
    internal string Section(bool completed){return (completed?"本次程序更新已完成":"当前版本更新内容")+"（"+Tag+" · 内部版本 "+Version+"）"+Environment.NewLine+Body;}
    internal static string CurrentLog(string root,string caption,Version current){var notes=Read(root,current);return "未发现可用更新。当前："+(caption??"CodexHub").Replace("CodexHub · ","")+"（内部版本："+current+"）。"+Environment.NewLine+Environment.NewLine+(notes==null?"当前版本更新内容：本地更新说明暂不可用。":notes.Section(false));}
    static string Marker(string state){return Path.Combine(state,"updates","last-shown-update.json");}
    static bool AlreadyShown(string state,string root,LocalProgramNotes notes){try{var record=Json.DeserializeObject(ReadText(Marker(state),4096));return SameRoot(Convert.ToString(Value(record,"root")),root)&&Convert.ToString(Value(record,"version"))==notes.Version&&Convert.ToString(Value(record,"sha256")).Equals(notes.Sha256,StringComparison.OrdinalIgnoreCase);}catch{return false;}}
    internal static LocalProgramNotes Pending(string root,string state,Version current){try{
        var notes=Read(root,current);string updates=Path.Combine(state,"updates");if(notes==null||AlreadyShown(state,root,notes)||!Directory.Exists(updates)||(File.GetAttributes(updates)&FileAttributes.ReparsePoint)!=0)return null;
        var installed=Json.Deserialize<SoftwareManifest>(ReadText(Path.Combine(root,"manifest.json"),4*1024*1024));var program=Entry(installed,"CodexLiveBackground.exe");if(!SameFile(program,Path.Combine(root,"CodexLiveBackground.exe")))return null;
        foreach(string folder in Directory.EnumerateDirectories(updates,"rollback-*").Take(256).OrderByDescending(path=>Directory.GetLastWriteTimeUtc(path)).Take(32))try{
            if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)continue;
            var result=Json.DeserializeObject(ReadText(Path.Combine(folder,"result.json"),131072));if(!Object.Equals(Value(result,"pass"),true)||Convert.ToString(Value(result,"version"))!=notes.Version)continue;
            var changed=Value(result,"changed") as object[];if(changed==null||!changed.Any(path=>String.Equals(Convert.ToString(path).Replace('\\','/'),notes.RelativePath,StringComparison.OrdinalIgnoreCase))||!changed.Any(path=>String.Equals(Convert.ToString(path),"CodexLiveBackground.exe",StringComparison.OrdinalIgnoreCase)))continue;
            // Existing updater receipts do not record a root. Its backed-up
            // native installation marker supplies ownership without changing
            // the updater protocol or guessing from a rollback folder name.
            string previous=Path.Combine(folder,"previous"),incoming=Path.Combine(folder,"incoming");if(!Directory.Exists(previous)||!Directory.Exists(incoming)||(File.GetAttributes(previous)&FileAttributes.ReparsePoint)!=0||(File.GetAttributes(incoming)&FileAttributes.ReparsePoint)!=0)continue;
            var owner=Json.DeserializeObject(ReadText(Path.Combine(previous,".codex-background-install.json"),2*1024*1024));if(Convert.ToString(Value(owner,"productId"))!="codex-live-background/exe"||!SameRoot(Convert.ToString(Value(owner,"root")),root))continue;
            var manifest=Json.Deserialize<SoftwareManifest>(ReadText(Path.Combine(incoming,"update-manifest.json"),1024*1024));if(manifest==null||manifest.product!="CodexHub-program-update"||manifest.version!=notes.Version)continue;
            var noteFile=Entry(manifest,notes.RelativePath);var exe=Entry(manifest,"CodexLiveBackground.exe");if(noteFile==null||noteFile.bytes!=notes.Bytes||!String.Equals(noteFile.sha256,notes.Sha256,StringComparison.OrdinalIgnoreCase)||exe==null||exe.bytes!=program.bytes||!String.Equals(exe.sha256,program.sha256,StringComparison.OrdinalIgnoreCase))continue;
            notes.Transaction=Path.GetFileName(folder);return notes;
        }catch{}
        return null;
    }catch{return null;}}
    internal static void MarkShown(string root,string state,LocalProgramNotes notes){
        if(notes==null||String.IsNullOrEmpty(notes.Transaction))return;
        string folder=Path.Combine(state,"updates");Directory.CreateDirectory(folder);if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("更新说明记录目录不可用。");string marker=Marker(state);if(File.Exists(marker)&&!Regular(marker))throw new InvalidDataException("更新说明记录不可用。");
        SoftwareUpdate.JsonFile(marker,new{root=Normalize(root),version=notes.Version,sha256=notes.Sha256,transaction=notes.Transaction,shownUtc=DateTime.UtcNow.ToString("o")});
    }
}

internal sealed partial class Manager {
    bool updateNotesReading,updateNotesReadStarted,retainingUpdateNotes;LocalProgramNotes pendingInstalledUpdate;string retainedUpdateNotes;DateTime retainUpdateNotesUntil;
    bool updatingSoftware,dailyUpdateStarted,checkingSoftwareUpdate;SoftwareRelease availableSoftwareUpdate;
    readonly ProgramUpdateButton updateSoftware=new ProgramUpdateButton();readonly System.Windows.Forms.Timer dailyUpdateTimer=new System.Windows.Forms.Timer{Interval=8000};
    void StartDailyUpdates(){if(dailyUpdateStarted||!companionLive)return;dailyUpdateStarted=true;PrepareInstalledUpdateNotes();dailyUpdateTimer.Tick+=delegate{if(updateNotesReading||busy||checkingStatus||updatingSoftware||petImporting||ImportingWallpapers||downloadingBuiltin||!Enabled||OwnedForms.Length>0||HubPopupMenu.AnyOpen)return;TryShowInstalledUpdateNotes();dailyUpdateTimer.Stop();CheckSoftwareUpdate();};dailyUpdateTimer.Start();}
    void SetSoftwareUpdateAvailable(SoftwareRelease release){availableSoftwareUpdate=release;updateSoftware.UpdateAvailable=release!=null;updateSoftware.AccessibleDescription=release==null?"未发现可用更新":"发现新版本 "+release.tag+"，点击下载并更新";updateSoftware.Invalidate();}
    void CheckSoftwareUpdate(){if(checkingSoftwareUpdate||updatingSoftware)return;checkingSoftwareUpdate=true;ImportWork.Start(delegate{try{var release=SoftwareUpdate.FindRelease(GetType().Assembly.GetName().Version);Ui(delegate{SetSoftwareUpdateAvailable(release);if(release!=null){retainedUpdateNotes=null;log.Text="发现新版本 "+release.tag+"。点击带红点的更新软件按钮后安装；素材和设置保留。";}});}catch(Exception error){Ui(delegate{log.Text="启动更新检查未完成："+error.Message+"；可点击更新软件重试。";});}finally{checkingSoftwareUpdate=false;}});}
    void BuildUpdateButton(){updateSoftware.Text="更新软件";updateSoftware.AccessibleName="更新软件，保留素材与设置";updateSoftware.Click+=delegate{BeginSoftwareUpdate();};commands.Controls.Add(updateSoftware);servicePage.VisibleChanged+=delegate{TryShowInstalledUpdateNotes();};VisibleChanged+=delegate{TryShowInstalledUpdateNotes();};log.TextChanged+=delegate{KeepStartupUpdateNotes();};}
    void PrepareInstalledUpdateNotes(){
        if(updateNotesReadStarted||!companionLive)return;updateNotesReadStarted=updateNotesReading=true;
        ImportWork.Start(delegate{var notes=LocalProgramNotes.Pending(root,stateDirectory,GetType().Assembly.GetName().Version);Ui(delegate{updateNotesReading=false;pendingInstalledUpdate=notes;TryShowInstalledUpdateNotes();});});
    }
    void TryShowInstalledUpdateNotes(){if(pendingInstalledUpdate==null||IsDisposed||!Visible||!servicePage.Visible||busy||checkingStatus||updatingSoftware||petImporting||ImportingWallpapers||downloadingBuiltin)return;DisplayInstalledUpdateNotes();}
    void DisplayInstalledUpdateNotes(){
        if(pendingInstalledUpdate==null)return;retainedUpdateNotes=pendingInstalledUpdate.Section(true);retainUpdateNotesUntil=DateTime.UtcNow.AddSeconds(60);KeepStartupUpdateNotes();AcknowledgeInstalledUpdateNotes();
    }
    void AcknowledgeInstalledUpdateNotes(){var notes=pendingInstalledUpdate;pendingInstalledUpdate=null;if(notes!=null)ImportWork.Start(delegate{try{LocalProgramNotes.MarkShown(root,stateDirectory,notes);}catch{}});}
    void KeepStartupUpdateNotes(){
        if(retainingUpdateNotes||String.IsNullOrEmpty(retainedUpdateNotes))return;
        if(DateTime.UtcNow>=retainUpdateNotesUntil){retainedUpdateNotes=null;return;}
        if(log.Text.Contains(retainedUpdateNotes))return;retainingUpdateNotes=true;try{log.AppendText((log.TextLength==0?"":Environment.NewLine+Environment.NewLine)+retainedUpdateNotes);}finally{retainingUpdateNotes=false;}
    }
    void BeginSoftwareUpdate(){
        if(PreviewNativeActionBlocked())return;
        if(updatingSoftware||busy)return;if(petImporting||ImportingWallpapers||downloadingBuiltin){log.Text="素材仍在准备，请在完成后更新程序。";return;}updatingSoftware=true;retainedUpdateNotes=null;SetBusyControls(true);updateSoftware.Enabled=false;string caption=Text;log.Text="正在检查 GitHub 新版本，仅更新程序，保留素材和个人设置…";
        ImportWork.Start(delegate{try{var current=GetType().Assembly.GetName().Version;var release=availableSoftwareUpdate??SoftwareUpdate.FindRelease(current);if(release==null){string message=LocalProgramNotes.CurrentLog(root,caption,current);Ui(delegate{SetSoftwareUpdateAvailable(null);log.Text=message;AcknowledgeInstalledUpdateNotes();updatingSoftware=false;SetBusyControls(false);updateSoftware.Enabled=true;});return;}string archive=SoftwareUpdate.Download(release,stateDirectory,message=>Ui(delegate{log.Text=message;}));var verified=SoftwareUpdate.VerifyArchive(archive,release.sha256,Path.Combine(stateDirectory,"updates",release.version,"verified-"+Guid.NewGuid().ToString("N")));if(verified.version!=release.version)throw new InvalidDataException("更新包版本与 GitHub 清单不一致。");
            var request=new SoftwareUpdateRequest{Root=root,Archive=archive,Sha256=release.sha256,StateRoot=stateDirectory,ManagerPid=Process.GetCurrentProcess().Id,ManagerStarted=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,ResumeArguments=Environment.GetCommandLineArgs().Skip(1).ToArray(),RequestExit=true};string host=SoftwareUpdate.StageHost(root,request);Ui(delegate{LaunchSoftwareUpdate(host);});
        }catch(Exception error){Ui(delegate{log.Text="更新未完成："+error.Message+"\r\n现有程序、素材和设置均未改动。";updatingSoftware=false;SetBusyControls(false);updateSoftware.Enabled=true;});}});
    }
    void LaunchSoftwareUpdate(string host){try{log.Text="校验完成，正在更新程序；完成后自动重新打开。";var started=Process.Start(new ProcessStartInfo(host,"--apply "+SoftwareUpdate.Quote(Path.Combine(Path.GetDirectoryName(host),"request.json"))+" --owner "+SoftwareUpdate.Quote(System.Security.Principal.WindowsIdentity.GetCurrent().User.Value)){UseShellExecute=SoftwareUpdate.NeedsElevation(root),Verb="runas",WorkingDirectory=Path.GetDirectoryName(host)});ThreadPool.QueueUserWorkItem(delegate{using(started){started.WaitForExit();}Ui(delegate{log.Text="更新程序已退出，当前管理器仍在运行。请查看更新窗口的提示后重试。";updatingSoftware=false;SetBusyControls(false);updateSoftware.Enabled=true;});});}catch(Exception error){log.Text="更新未启动："+error.Message;updatingSoftware=false;SetBusyControls(false);updateSoftware.Enabled=true;}}
}