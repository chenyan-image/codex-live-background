using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal sealed class SparkContact {
    public string Id=Guid.NewGuid().ToString("N"),Name="",Status="待处理",Detail="",LastAttemptDay="",LastSentDay="";
    public bool Enabled=true;
}
internal sealed class SparkSettings {
    public int Version=1;public bool AutoStart=false;public string DouyinPath="";public List<SparkContact> Contacts=new List<SparkContact>();
    public static string NameKey(string text){return (text??"").Normalize(NormalizationForm.FormKC).Replace('\u00a0',' ').Trim();}
    public void Validate(){
        if(Version!=1||Contacts==null||Contacts.Count>200)throw new InvalidDataException("续火花名单格式无效，最多支持 200 人。");
        var ids=new HashSet<string>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in Contacts){if(item==null||String.IsNullOrEmpty(item.Id)||!ids.Add(item.Id))throw new InvalidDataException("名单编号重复。");item.Name=NameKey(item.Name);if(item.Name.Length==0||item.Name.Length>512||item.Name.Any(Char.IsControl)||!names.Add(item.Name))throw new InvalidDataException("请填写不重复的联系人名称（最多 512 字符）。");}
    }
}
internal sealed class SparkStore {
    readonly string path;public string Warning;
    public SparkStore(string state){path=Path.Combine(state,"douyin-spark.json");}
    public SparkSettings Load(){
        if(!File.Exists(path))return new SparkSettings();
        try{if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException("名单文件过大。");var value=new JavaScriptSerializer().Deserialize<SparkSettings>(File.ReadAllText(path,Encoding.UTF8));if(value==null)throw new InvalidDataException("名单为空。");value.Validate();return value;}
        catch(Exception error){Warning="名单未载入："+error.Message+" 原文件已保留。";return new SparkSettings();}
    }
    public void Save(SparkSettings settings){
        if(Warning!=null)throw new IOException(Warning);settings.Validate();Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{byte[] bytes=new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(settings));using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
internal sealed class SparkNode {
    public string Key,Name,Id,Kind,Value;public Rectangle Bounds;public bool Enabled=true;
    public Point Center {get{return new Point(Bounds.Left+Bounds.Width/2,Bounds.Top+Bounds.Height/2);}}
}
internal sealed class SparkView {
    public IntPtr Window;public Rectangle Bounds;public bool Foreground,Ready,SearchComplete;
    public List<SparkNode> Nodes=new List<SparkNode>();
    public SparkNode MessageButton,SearchBox,Composer,Back,Emoji,Spark;
    public Rectangle ListBounds,ChatBounds;public string HeaderName;
    public List<SparkNode> Contacts=new List<SparkNode>();public HashSet<string> SentMarkers=new HashSet<string>();
    public bool HasList {get{return SearchBox!=null&&!ListBounds.IsEmpty;}}
    public bool IsChat(string name){return Composer!=null&&String.Equals(SparkSettings.NameKey(HeaderName),SparkSettings.NameKey(name),StringComparison.OrdinalIgnoreCase);}
}
internal interface ISparkDesktop {
    Task<SparkView> Read(CancellationToken stop);
    Task Click(SparkNode node,CancellationToken stop);
    Task Search(SparkNode field,string name,CancellationToken stop);
}
internal interface ISparkSearchEditor {
    bool TryDirect(string value);void Focus();bool Focused {get;}
    void Type(string value);bool TryRead(out string value);
}
internal static class SparkSearchInput {
    public static void Replace(ISparkSearchEditor editor,string value,Action pause){
        if(editor.TryDirect(value))for(int i=0;i<3;i++){string actual;if(editor.TryRead(out actual)&&SparkSettings.NameKey(actual)==value)return;pause();}
        editor.Focus();bool focused=false;for(int i=0;i<5;i++){if(editor.Focused){focused=true;break;}pause();if(i==2)editor.Focus();}
        if(!focused)throw new SparkContactError("未确认消息搜索框焦点，已停止输入。");editor.Type(value);
        for(int i=0;i<8;i++){string actual;if(editor.TryRead(out actual)&&SparkSettings.NameKey(actual)==value)return;pause();}
        throw new SparkContactError("搜索文字尚未确认，未执行发送，请检查抖音消息搜索框。");
    }
}
internal interface ISparkGuide {
    Task WalkTo(Point point,CancellationToken stop);
    void End();
}
internal sealed class SparkContactError : Exception {public SparkContactError(string message):base(message){}}
internal sealed class SparkFocusLost : Exception {public SparkFocusLost():base("抖音已切到后台或窗口已变化，任务已停止。"){} }
internal sealed class SparkReadDiagnostic {
    public string Stage="",Backend="unknown";public bool QueryMatches,HasList,Ready,SearchComplete;public int ExactCount,Total;public long Milliseconds;
}
internal sealed class SparkRunDiagnostic {
    public int Version=1;public string StartedUtc="",FinishedUtc="",Outcome="running",Stage="initial";public bool InspectOnly;public long ElapsedMilliseconds;public List<SparkReadDiagnostic> Reads=new List<SparkReadDiagnostic>();
}
internal sealed class SparkRunner {
    readonly ISparkDesktop desktop;readonly ISparkGuide guide;readonly Action<SparkSettings> save;readonly Func<DateTime> clock;readonly Func<int,CancellationToken,Task> wait;
    public event Action Changed;public string State="等待开始";public bool Running {get;private set;}public bool InspectOnly {get;private set;}
    IntPtr window;string day,currentQuery="",stage="initial";Stopwatch runClock;SparkRunDiagnostic diagnostic;public long ElapsedMilliseconds {get;private set;}public string DiagnosticDirectory {get;set;}
    public SparkRunner(ISparkDesktop ui,ISparkGuide pet,Action<SparkSettings> persist,Func<DateTime> now=null,Func<int,CancellationToken,Task> delay=null){desktop=ui;guide=pet;save=persist;clock=now??(()=>DateTime.Now);wait=delay??((ms,ct)=>Task.Delay(ms,ct));}
    void Notify(string state){State=state;if(Changed!=null)Changed();}
    internal void Report(string state){Notify(state);}
    static bool QueryMatches(SparkView view,string query){return view.HasList&&String.Equals(SparkSettings.NameKey(view.SearchBox.Value),SparkSettings.NameKey(query),StringComparison.OrdinalIgnoreCase);}
    static SparkNode[] ExactResults(SparkView view,string query){return view.Contacts.Where(n=>String.Equals(SparkSettings.NameKey(n.Name),SparkSettings.NameKey(query),StringComparison.OrdinalIgnoreCase)).ToArray();}
    void Record(SparkView view){
        if(diagnostic==null)return;var nodes=view.Nodes.Concat(view.Contacts).Concat(new[]{view.SearchBox,view.MessageButton,view.Composer,view.Emoji}).Where(n=>n!=null).ToArray();
        string backend=nodes.Any(n=>(n.Key??"").StartsWith("v:",StringComparison.Ordinal))?"visual":nodes.Length>0?"uia":"unknown";
        diagnostic.Reads.Add(new SparkReadDiagnostic{Stage=stage,Backend=backend,QueryMatches=QueryMatches(view,currentQuery),ExactCount=ExactResults(view,currentQuery).Length,Total=view.Contacts.Count,HasList=view.HasList,Ready=view.Ready,SearchComplete=view.SearchComplete,Milliseconds=runClock==null?0:runClock.ElapsedMilliseconds});
        if(diagnostic.Reads.Count>160)diagnostic.Reads.RemoveAt(0);
    }
    void SaveDiagnostic(){
        if(diagnostic==null||String.IsNullOrEmpty(DiagnosticDirectory))return;
        string temporary=null;try{
            Directory.CreateDirectory(DiagnosticDirectory);string path=Path.Combine(DiagnosticDirectory,"douyin-last-run.json");temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(diagnostic),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
        }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(NotSupportedException){}catch(System.Security.SecurityException){}
        finally{if(temporary!=null)try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    async Task<SparkView> Read(CancellationToken stop){stop.ThrowIfCancellationRequested();var view=await desktop.Read(stop);Record(view);if(!view.Foreground||window!=IntPtr.Zero&&view.Window!=window)throw new SparkFocusLost();if(!view.Ready)throw new InvalidOperationException("未能读取抖音消息控件，请稍后重试；未将名单标记为无此人。");return view;}
    async Task<SparkView> Until(Func<SparkView,bool> ready,CancellationToken stop){var elapsed=Stopwatch.StartNew();for(int i=0;i<20&&elapsed.ElapsedMilliseconds<8000;i++){var view=await Read(stop);if(ready(view))return view;await wait(180,stop);}throw new SparkContactError("页面响应超时，本人已跳过。");}
    async Task Click(SparkNode node,CancellationToken stop){if(node==null)throw new SparkContactError("未找到所需控件，本人已跳过。");await guide.WalkTo(node.Center,stop);await desktop.Click(node,stop);await wait(160,stop);}
    async Task<SparkView> OpenList(CancellationToken stop){
        var view=await Read(stop);if(view.HasList)return view;
        if(view.Back!=null){await Click(view.Back,stop);return await Until(v=>v.HasList,stop);}
        await Click(view.MessageButton,stop);view=await Read(stop);
        if(view.Back!=null){await Click(view.Back,stop);return await Until(v=>v.HasList,stop);}
        return await Until(v=>v.HasList,stop);
    }
    void SetResult(SparkSettings settings,SparkContact contact,string status,string detail){contact.Status=status;contact.Detail=detail;save(settings);Notify(contact.Name+" · "+status);}
    public async Task Run(SparkSettings settings,bool inspect,CancellationToken stop){
        if(Running)throw new InvalidOperationException("已有续火花任务在运行。");settings.Validate();var elapsed=Stopwatch.StartNew();runClock=elapsed;diagnostic=new SparkRunDiagnostic{StartedUtc=DateTime.UtcNow.ToString("o"),InspectOnly=inspect};currentQuery="";stage="initial";ElapsedMilliseconds=0;Running=true;InspectOnly=inspect;window=IntPtr.Zero;day=clock().ToString("yyyy-MM-dd");Notify(inspect?"检查名单（不会发送）":"正在准备抖音续火花");
        try{
            var initial=await Read(stop);window=initial.Window;var contacts=settings.Contacts.Where(c=>c.Enabled).ToArray();
            foreach(var contact in contacts){
                stop.ThrowIfCancellationRequested();day=clock().ToString("yyyy-MM-dd");if(!inspect&&(contact.LastSentDay==day||contact.LastAttemptDay==day)){Notify(contact.Name+" · 今天已处理，跳过");continue;}
                try{
                    contact.Status="查找联系人";contact.Detail="";Notify("正在查找："+contact.Name);
                    currentQuery=contact.Name;stage="open-list";var view=await OpenList(stop);stage="search-input";await desktop.Search(view.SearchBox,contact.Name,stop);
                    // Wait for the search provider to settle. A transient empty list is not proof of absence.
                    stage="search-ready";await wait(320,stop);view=await Until(v=>QueryMatches(v,contact.Name)&&(v.SearchComplete||ExactResults(v,contact.Name).Length>0),stop);
                    int matches=0;SparkNode chosen=null;string signature=null;int stable=0;
                    stage="search-stable";
                    for(int i=0;i<6;i++){
                        if(i>0)view=await Read(stop);if(!QueryMatches(view,contact.Name)){stable=0;signature=null;await wait(180,stop);continue;}
                        var found=ExactResults(view,contact.Name);
                        // Only exact-name multiplicity determines a positive
                        // match. OCR snippets and backend-specific boxes may
                        // change without changing the verified recipient.
                        // Absence still needs a completed, stable result set.
                        string next=found.Length>0?"exact:"+found.Length:"absent:"+String.Join("|",view.Contacts.Select(n=>SparkSettings.NameKey(n.Name)).OrderBy(n=>n,StringComparer.Ordinal));
                        bool eligible=found.Length>0||view.SearchComplete;stable=eligible?(next==signature?stable+1:1):0;signature=next;matches=found.Length;chosen=found.FirstOrDefault();
                        if(stable>=2)break;await wait(180,stop);
                    }
                    if(stable<2)throw new SparkContactError("搜索结果尚未稳定，已跳过。");
                    if(matches==0){SetResult(settings,contact,"无此人","未在当前消息搜索结果中找到完全相同的名字。");continue;}
                    if(matches>1){SetResult(settings,contact,"同名待确认","搜索出现多个同名联系人，请先在抖音设置唯一备注。");continue;}
                    stage="open-chat";await Click(chosen,stop);stage="chat-ready";view=await Until(v=>v.IsChat(contact.Name),stop);
                    if(view.Spark==null){stage="open-emoji";await Click(view.Emoji,stop);stage="emoji-ready";view=await Until(v=>v.IsChat(contact.Name)&&v.Spark!=null,stop);}
                    if(inspect){SetResult(settings,contact,"检查通过","已找到此人和续火花表情，检查模式未发送。");continue;}
                    Notify("正在续火花："+contact.Name);await guide.WalkTo(view.Spark.Center,stop);
                    // Re-read identity and target after the guide movement; never dispatch using an old chat.
                    stage="send-recheck";view=await Read(stop);if(!view.IsChat(contact.Name)||view.Spark==null)throw new SparkContactError("当前对话已改变，已跳过。");
                    day=clock().ToString("yyyy-MM-dd");var before=new HashSet<string>(view.SentMarkers);contact.LastAttemptDay=day;contact.Status="发送待确认";contact.Detail="已登记本次操作；结果未确认时当天不自动重发。";
                    save(settings); // A failed durable write prevents the external click.
                    stage="send-click";await desktop.Click(view.Spark,stop);
                    bool confirmed=false;
                    stage="receipt";
                    for(int i=0;i<3;i++){await wait(180,stop);view=await Read(stop);if(!view.IsChat(contact.Name))break;if(view.Spark==null&&view.SentMarkers.Except(before).Any()){confirmed=true;break;}}
                    if(confirmed){contact.LastSentDay=day;SetResult(settings,contact,"已续火花","已检测到当前对话新增的续火花消息。");}
                    else SetResult(settings,contact,"已点击·待确认","未读到明确发送回执；请在抖音核对，当天不自动重发。");
                }catch(SparkContactError error){SetResult(settings,contact,"已跳过",error.Message);}
                await wait(160,stop);
            }
            diagnostic.Outcome="complete";Notify((inspect?"名单检查完成，未发送消息。":"本轮已完成；结果见名单。今天已处理的人不会重复发送。")+" 用时 "+elapsed.Elapsed.TotalSeconds.ToString("F1")+" 秒。");
        }catch(OperationCanceledException){diagnostic.Outcome="cancelled";Notify("任务已停止；已登记的发送不会自动重试。");}
        catch(Exception error){diagnostic.Outcome="error:"+error.GetType().Name;Notify("任务已停止："+error.Message);}
        finally{ElapsedMilliseconds=elapsed.ElapsedMilliseconds;diagnostic.FinishedUtc=DateTime.UtcNow.ToString("o");diagnostic.ElapsedMilliseconds=ElapsedMilliseconds;diagnostic.Stage=stage;SaveDiagnostic();guide.End();Running=false;InspectOnly=false;if(Changed!=null)Changed();}
    }
}
