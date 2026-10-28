using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Diagnostics;
using System.Reflection;

internal static class DouyinSparkTests {
    static int checks;static void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
    sealed class Guide : ISparkGuide {
        public int Walks,Ends;public Action<int> BeforeWalk;
        public Task WalkTo(Point point,CancellationToken stop){stop.ThrowIfCancellationRequested();Walks++;if(BeforeWalk!=null)BeforeWalk(Walks);stop.ThrowIfCancellationRequested();return Task.FromResult(true);}
        public void End(){Ends++;}
    }
    sealed class Launcher : ISparkAppLauncher {
        public int Calls;public Action Open;
        public Task EnsureReady(Action<string> progress,CancellationToken stop){stop.ThrowIfCancellationRequested();Calls++;progress("正在打开抖音…");if(Open!=null)Open();stop.ThrowIfCancellationRequested();return Task.FromResult(true);}
    }
    sealed class SearchEditor : ISparkSearchEditor {
        public bool Direct,IgnoreDirect,CanFocus=true,Readable=true;public int FocusCalls,Typed,FocusChecks,FocusAfterChecks;public string Value="old";
        public bool TryDirect(string value){if(Direct&&!IgnoreDirect)Value=value;return Direct;}
        public void Focus(){FocusCalls++;}public bool Focused{get{return CanFocus&&++FocusChecks>=FocusAfterChecks;}}
        public void Type(string value){Typed++;Value=value;}public bool TryRead(out string value){value=Value;return Readable;}
    }
    sealed class Desktop : ISparkDesktop {
        public bool List,Emoji,Foreground=true,Ready=true,Unconfirmed,ChangedHeader,SearchUnsettled;public string Query="",Selected;public int Sends,Reads,Searches,SearchReads;public Action BeforeSend;public Action<SparkView,int> SearchSnapshot;public readonly List<string> SendNames=new List<string>();public readonly Dictionary<string,int> People=new Dictionary<string,int>{{"A",1},{"B",1},{"Duplicate",2}};
        SparkNode Node(string key,string name=""){return new SparkNode{Key=key,Name=name,Kind="Text",Bounds=new Rectangle(20,20,30,30),Value=Query};}
        public Task<SparkView> Read(CancellationToken stop){
            stop.ThrowIfCancellationRequested();Reads++;var view=new SparkView{Window=new IntPtr(12),Foreground=Foreground,Ready=Ready,MessageButton=Node("messages"),SearchComplete=!SearchUnsettled};
            if(List){view.SearchBox=Node("search");view.ListBounds=new Rectangle(1,1,300,500);int count;if(People.TryGetValue(Query,out count))for(int i=0;i<count;i++)view.Contacts.Add(Node("person"+i,Query));}
            if(List&&Searches>0){SearchReads++;if(SearchSnapshot!=null)SearchSnapshot(view,SearchReads);}
            if(Selected!=null){view.HeaderName=ChangedHeader?"Wrong":Selected;view.Composer=Node("composer");view.Back=Node("back");view.Emoji=Node("emoji");if(Emoji)view.Spark=Node("spark","续火花");if(Sends>0&&!Unconfirmed)view.SentMarkers.Add("sent"+Sends);}
            return Task.FromResult(view);
        }
        public Task Search(SparkNode field,string name,CancellationToken stop){stop.ThrowIfCancellationRequested();Searches++;SearchReads=0;Query=name;return Task.FromResult(true);}
        public Task Click(SparkNode node,CancellationToken stop){
            stop.ThrowIfCancellationRequested();if(node.Key=="messages"||node.Key=="back"){List=true;Selected=null;Emoji=false;}else if(node.Key.StartsWith("person")){Selected=Query;List=false;Emoji=false;}else if(node.Key=="emoji")Emoji=true;else if(node.Key=="spark"){if(BeforeSend!=null)BeforeSend();Sends++;SendNames.Add(Selected);Emoji=false;}
            return Task.FromResult(true);
        }
    }
    static SparkSettings Settings(params string[] names){return new SparkSettings{Contacts=names.Select(n=>new SparkContact{Name=n}).ToList()};}
    static SparkRunner Runner(Desktop desktop,Guide guide,Action<SparkSettings> persist=null){return new SparkRunner(desktop,guide,persist??(s=>{}),()=>new DateTime(2026,10,28,12,0,0),(ms,stop)=>{stop.ThrowIfCancellationRequested();return Task.FromResult(true);});}
    static async Task Run(string output){
        var ui=new Desktop();var guide=new Guide();var settings=Settings("A","Missing","B","Duplicate");int saves=0;
        var runner=Runner(ui,guide,s=>{s.Validate();saves++;});ui.BeforeSend=()=>Check(saves>0&&settings.Contacts.First(c=>c.Name==ui.Selected).LastAttemptDay=="2026-10-28","journal is flushed before send");
        await runner.Run(settings,false,CancellationToken.None);
        Check(ui.Sends==2&&String.Join(",",ui.SendNames)=="A,B","one complete pass in list order");Check(settings.Contacts[0].Status=="已续火花","verified success");Check(settings.Contacts[1].Status=="无此人","missing exact friend");Check(settings.Contacts[3].Status=="同名待确认","duplicate names skipped");Check(guide.Ends==1&&!runner.Running,"guide released on completion");
        await runner.Run(settings,false,CancellationToken.None);Check(ui.Sends==2,"daily duplicate suppression");
        settings=Settings("A");ui=new Desktop();guide=new Guide();runner=Runner(ui,guide);await runner.Run(settings,true,CancellationToken.None);Check(ui.Sends==0&&settings.Contacts[0].Status=="检查通过","inspection never sends");Check(settings.Contacts[0].LastAttemptDay=="","inspection does not claim daily send");
        settings=Settings("A");ui=new Desktop{Unconfirmed=true};runner=Runner(ui,new Guide());await runner.Run(settings,false,CancellationToken.None);Check(settings.Contacts[0].Status=="已点击·待确认","uncertain delivery is not presented as success");await runner.Run(settings,false,CancellationToken.None);Check(ui.Sends==1,"uncertain delivery is not retried");
        settings=Settings("A");ui=new Desktop();runner=Runner(ui,new Guide(),s=>{throw new IOException("disk full");});await runner.Run(settings,false,CancellationToken.None);Check(ui.Sends==0&&runner.State.Contains("disk full"),"persist failure blocks dispatch");
        settings=Settings("A");ui=new Desktop{Foreground=false};guide=new Guide();runner=Runner(ui,guide);await runner.Run(settings,false,CancellationToken.None);Check(ui.Sends==0&&guide.Ends==1,"non-Douyin focus blocks all sends and releases guide");
        settings=Settings("A");ui=new Desktop{Ready=false};runner=Runner(ui,new Guide());await runner.Run(settings,false,CancellationToken.None);Check(ui.Sends==0&&settings.Contacts[0].Status!="无此人","unavailable UI does not mean missing friend");
        settings=Settings("Missing");ui=new Desktop{SearchUnsettled=true};runner=Runner(ui,new Guide());await runner.Run(settings,false,CancellationToken.None);Check(ui.Sends==0&&settings.Contacts[0].Status=="已跳过","unsettled empty search is skipped");
        settings=Settings("A");ui=new Desktop{SearchSnapshot=(v,n)=>{v.SearchComplete=n%2==0;v.Contacts[0].Key="person"+(n%2);v.Contacts[0].Bounds=new Rectangle(100,100,n%2==0?60:14,18);v.Contacts.Add(new SparkNode{Key="noise"+n,Name="unrelated snippet "+n,Kind="Text"});}};runner=Runner(ui,new Guide());await runner.Run(settings,true,CancellationToken.None);Check(ui.Sends==0&&settings.Contacts[0].Status=="检查通过"&&ui.SearchReads==2,"two exact names settle despite alternating backend bounds and unrelated OCR snippets");
        settings=Settings("A");ui=new Desktop{SearchSnapshot=(v,n)=>{if(n>=2)v.Contacts.Add(new SparkNode{Key="person1",Name="A",Kind="Text",Bounds=new Rectangle(100,160,60,18)});v.Contacts.Add(new SparkNode{Key="noise"+n,Name="unrelated "+n,Kind="Text"});}};runner=Runner(ui,new Guide());await runner.Run(settings,true,CancellationToken.None);Check(ui.Sends==0&&settings.Contacts[0].Status=="同名待确认"&&ui.SearchReads>=3,"changing exact multiplicity resets stability and duplicates are never opened");
        settings=Settings("Missing");ui=new Desktop{SearchSnapshot=(v,n)=>{v.SearchComplete=false;v.Contacts.Add(new SparkNode{Key="noise"+n,Name="partial "+n,Kind="Text"});}};runner=Runner(ui,new Guide());await runner.Run(settings,true,CancellationToken.None);Check(ui.Sends==0&&settings.Contacts[0].Status=="已跳过","absence still requires completed search evidence");
        settings=Settings("A");ui=new Desktop{SearchSnapshot=(v,n)=>{if(n==2)v.SearchBox.Value="unrelated query";}};runner=Runner(ui,new Guide());await runner.Run(settings,true,CancellationToken.None);Check(ui.Sends==0&&settings.Contacts[0].Status=="检查通过"&&ui.SearchReads==4,"temporary query mismatch resets exact stability before recovery");
        settings=Settings("PrivateContactForDiagnostic");ui=new Desktop();ui.People[settings.Contacts[0].Name]=1;ui.SearchSnapshot=(v,n)=>{v.MessageButton.Key=n%2==0?"v:messages":"messages";v.Contacts.Add(new SparkNode{Key="noise"+n,Name="private chat content "+n,Kind="Text"});};runner=Runner(ui,new Guide());string diagnosticDirectory=Path.Combine(output,"diagnostic-profile");runner.DiagnosticDirectory=diagnosticDirectory;await runner.Run(settings,true,CancellationToken.None);string diagnosticText=File.ReadAllText(Path.Combine(diagnosticDirectory,"douyin-last-run.json"));var diagnostic=new JavaScriptSerializer().Deserialize<SparkRunDiagnostic>(diagnosticText);Check(diagnostic.Outcome=="complete"&&diagnostic.Reads.Any(v=>v.Stage=="search-stable"&&v.QueryMatches&&v.ExactCount==1&&v.Total==2)&&diagnostic.Reads.Any(v=>v.Backend=="visual"),"last-run diagnostics include stage query counts and detected backend");Check(!diagnosticText.Contains("PrivateContactForDiagnostic")&&!diagnosticText.Contains("private chat content"),"diagnostics exclude contact names and chat contents");
        var cancel=new CancellationTokenSource();settings=Settings("A");ui=new Desktop();guide=new Guide{BeforeWalk=i=>{if(i==3)cancel.Cancel();}};runner=Runner(ui,guide);await runner.Run(settings,false,cancel.Token);Check(ui.Sends==0&&guide.Ends==1&&!runner.Running,"cancel during guide walk");
        settings=Settings("A");ui=new Desktop();guide=new Guide{BeforeWalk=i=>{if(i==4)ui.ChangedHeader=true;}};runner=Runner(ui,guide);await runner.Run(settings,false,CancellationToken.None);Check(ui.Sends==0,"recipient identity is rechecked after walking");
        settings=Settings("A","B");settings.Contacts[0].Enabled=false;ui=new Desktop();runner=Runner(ui,new Guide());await runner.Run(settings,false,CancellationToken.None);Check(ui.Sends==1&&ui.SendNames[0]=="B","disabled contact skipped");
        settings=Settings("Ａ","A");bool invalid=false;try{settings.Validate();}catch(InvalidDataException){invalid=true;}Check(invalid,"normalized duplicate rejected");Check(SparkSettings.NameKey(" 章若楠\u00a0boy ")=="章若楠 boy","nonbreaking space normalized");
        string state=Path.Combine(output,"state");Directory.CreateDirectory(state);string untouched=Path.Combine(state,"pet-settings.json");File.WriteAllText(untouched,"preserve original settings");var store=new SparkStore(state);settings=Settings("联系人（中文）🔥");settings.AutoStart=true;settings.Contacts[0].LastAttemptDay="2026-10-28";store.Save(settings);var loaded=new SparkStore(state).Load();Check(loaded.AutoStart&&loaded.Contacts[0].Name==settings.Contacts[0].Name&&loaded.Contacts[0].LastAttemptDay=="2026-10-28","persistent unicode and daily journal");Check(File.ReadAllText(untouched)=="preserve original settings","unrelated configuration unchanged");Check(Directory.GetFiles(state,"*.tmp").Length==0,"atomic writer cleaned own temp file");
        File.WriteAllText(Path.Combine(state,"douyin-spark.json"),"broken");store=new SparkStore(state);store.Load();invalid=false;try{store.Save(Settings("A"));}catch(IOException){invalid=true;}Check(invalid&&File.ReadAllText(Path.Combine(state,"douyin-spark.json"))=="broken","invalid configuration is preserved");
        Locator();
        var beforeVisual=new SparkNode{Key="v:person:0",Name="A",Kind="Text",Bounds=new Rectangle(100,100,14,18)};
        var nativePerson=new SparkNode{Key="42.123",Name="A",Kind="Text",Bounds=new Rectangle(99,100,60,18)};
        var switched=new SparkView{Contacts=new List<SparkNode>{nativePerson}};
        Check(DouyinDesktop.CurrentTarget(switched,beforeVisual)==nativePerson,"visual and native contact keys can change without losing the observed target");
        var visualAgain=new SparkView{Contacts=new List<SparkNode>{beforeVisual}};Check(DouyinDesktop.CurrentTarget(visualAgain,nativePerson)==beforeVisual,"native full text can revalidate to the overlapping visual character");
        var ambiguousClick=new SparkView{Contacts=new List<SparkNode>{beforeVisual,nativePerson},Nodes=new List<SparkNode>{beforeVisual}};Check(DouyinDesktop.CurrentTarget(ambiguousClick,beforeVisual)==null,"new duplicate during final contact revalidation blocks the click");
        nativePerson.Bounds=new Rectangle(100,160,60,18);Check(DouyinDesktop.CurrentTarget(switched,beforeVisual)==null,"moved contact still fails revalidation");
        ui=new Desktop{Foreground=false};guide=new Guide();settings=Settings("A");runner=Runner(ui,guide);var launch=new Launcher{Open=()=>{ui.Foreground=true;}};await SparkStartFlow.Run(launch,runner,settings,true,CancellationToken.None);Check(launch.Calls==1&&settings.Contacts[0].Status=="检查通过"&&ui.Sends==0,"start launches and foregrounds Douyin before inspecting");
        ui=new Desktop();settings=Settings("A");runner=Runner(ui,new Guide());launch=new Launcher{Open=()=>{throw new FileNotFoundException("not installed");}};bool missing=false;try{await SparkStartFlow.Run(launch,runner,settings,false,CancellationToken.None);}catch(FileNotFoundException){missing=true;}Check(missing&&ui.Reads==0&&ui.Sends==0,"missing Douyin stops before UI operations");
        var startCancel=new CancellationTokenSource();ui=new Desktop();runner=Runner(ui,new Guide());launch=new Launcher{Open=()=>startCancel.Cancel()};bool cancelled=false;try{await SparkStartFlow.Run(launch,runner,Settings("A"),false,startCancel.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled&&ui.Reads==0,"stop during launch prevents the run");
        Check(DouyinInstall.Executable("\"E:\\我的软件 (x64)\\抖音\\douyin.exe\",0")==@"E:\我的软件 (x64)\抖音\douyin.exe","quoted custom drive and unicode install path");
        Check(DouyinInstall.Executable(@"C:\Program Files\ByteDance\douyin\douyin.exe --from=desktop")==@"C:\Program Files\ByteDance\douyin\douyin.exe","registry icon command parsed without executing arguments");
        Check(DouyinInstall.Executable(@"C:\抖音\uninstall.exe")==null&&DouyinInstall.Executable(@"douyin.exe")==null,"uninstaller and relative command rejected");
        await WorkerRecovery();
        var editor=new SearchEditor{Direct=false};SparkSearchInput.Replace(editor,"冰月一十",()=>{});Check(editor.Typed==1&&editor.FocusCalls==1&&editor.Value=="冰月一十","no value pattern falls back to focused keyboard input");
        editor=new SearchEditor{Direct=true};SparkSearchInput.Replace(editor,"备注🔥",()=>{});Check(editor.Typed==0&&editor.Value=="备注🔥","working direct input retained");
        editor=new SearchEditor{Direct=true,IgnoreDirect=true};SparkSearchInput.Replace(editor,"中文 ABC",()=>{});Check(editor.Typed==1&&editor.Value=="中文 ABC","ignored direct input retries by keyboard");
        editor=new SearchEditor{CanFocus=false};bool blocked=false;try{SparkSearchInput.Replace(editor,"A",()=>{});}catch(SparkContactError){blocked=true;}Check(blocked&&editor.Typed==0,"wrong focus prevents all keyboard typing");
        editor=new SearchEditor{FocusAfterChecks=4};int focusWaits=0;SparkSearchInput.Replace(editor,"delayed search",()=>focusWaits++);Check(editor.Typed==1&&editor.Value=="delayed search"&&editor.FocusCalls==2&&focusWaits==3,"delayed search focus is retried before any keyboard input");
        editor=new SearchEditor{FocusAfterChecks=6};blocked=false;try{SparkSearchInput.Replace(editor,"A",()=>{});}catch(SparkContactError){blocked=true;}Check(blocked&&editor.Typed==0&&editor.FocusChecks==5,"focus delay beyond the bounded retries does not type into another element");
        editor=new SearchEditor{Readable=false};blocked=false;try{SparkSearchInput.Replace(editor,"A",()=>{});}catch(SparkContactError){blocked=true;}Check(blocked,"unreadable search echo is not assumed successful");
    }
    static bool Gone(int id){try{using(var process=Process.GetProcessById(id))return process.HasExited;}catch(ArgumentException){return true;}}
    static async Task WorkerRecovery(){
        var ids=new List<int>();var worker=new SparkWorkerClient(Assembly.GetExecutingAssembly().Location,1200);worker.Started=id=>ids.Add(id);bool timedOut=false;
        try{await worker.Request(new SparkWorkerRequest{Operation="probe-hang"},CancellationToken.None);}catch(TimeoutException){timedOut=true;}
        Check(timedOut&&ids.Count==1&&Gone(ids[0]),"timed-out UIA helper is terminated");
        var reply=await worker.Request(new SparkWorkerRequest{Operation="probe-ok"},CancellationToken.None);Check(reply.Ready&&ids.Count==2&&Gone(ids[1]),"next read works after a timed-out read");
        var cancel=new CancellationTokenSource();worker.Started=id=>{ids.Add(id);cancel.Cancel();};bool cancelled=false;try{await worker.Request(new SparkWorkerRequest{Operation="probe-hang"},cancel.Token);}catch(OperationCanceledException){cancelled=true;}
        Check(cancelled&&Gone(ids.Last()),"cancel terminates only the owned helper");worker.Started=id=>ids.Add(id);reply=await worker.Request(new SparkWorkerRequest{Operation="probe-ok"},CancellationToken.None);Check(reply.Ready&&Gone(ids.Last()),"cancel does not leave the reader locked");
    }
    static int WorkerProbe(string input,string output){var request=new JavaScriptSerializer().Deserialize<SparkWorkerRequest>(File.ReadAllText(input));if(request.Operation=="probe-hang"){Thread.Sleep(60000);return 1;}File.WriteAllText(output,new JavaScriptSerializer().Serialize(new SparkWorkerReply{Ready=true,Foreground=true,Window=12}));return 0;}
    static SparkNode N(string key,string name,string kind,int x,int y,int w,int h,string id="",string value=""){return new SparkNode{Key=key,Name=name,Kind=kind,Id=id,Value=value,Bounds=new Rectangle(x,y,w,h)};}
    static void Locator(){
        var nodes=new List<SparkNode>{N("message","消息","Text",970,22,36,18),N("search","搜索","Edit",834,60,246,32,value:"A"),N("avatar","","ListItem",838,112,38,38),N("name","A","Text",886,114,90,21),N("status","重燃中 2/3","Text",978,114,78,18),N("body","A","Text",100,300,100,20)};
        var view=SparkLocator.Describe(new IntPtr(12),new Rectangle(0,0,1200,800),true,nodes);Check(view.HasList&&view.SearchComplete,"message search located");Check(view.Contacts.Count==1&&view.Contacts[0].Name=="A","only list names considered");
        var messageButton=N("open-result","发消息","Button",1020,114,66,24);nodes.Add(messageButton);view=SparkLocator.Describe(new IntPtr(12),new Rectangle(0,0,1200,800),true,nodes);Check(view.Contacts.Count==1&&view.Contacts[0].Name=="A"&&view.Contacts[0].Bounds==messageButton.Bounds,"search result keeps full recipient identity and clicks its send-message button");nodes.Remove(messageButton);
        nodes.RemoveAll(n=>n.Key=="avatar"||n.Key=="name"||n.Key=="status");view=SparkLocator.Describe(new IntPtr(12),new Rectangle(0,0,1200,800),true,nodes);Check(!view.SearchComplete,"empty transient list is not settled");nodes.Add(N("empty","暂无搜索结果","Text",870,200,180,20));view=SparkLocator.Describe(new IntPtr(12),new Rectangle(0,0,1200,800),true,nodes);Check(view.SearchComplete&&view.Contacts.Count==0,"explicit empty result");
        nodes=new List<SparkNode>{N("header","","Image",1002,70,18,18,"ei-conversation-header-title-bar"),N("title","A","Text",916,70,35,18),N("edit","","Group",850,744,180,35,value:"发送消息 ​"),N("folder","","Image",1040,750,24,24),N("emoji","","Image",1082,750,24,24),N("send","","Image",1120,750,24,24),N("spark","续火花","Group",844,520,65,80),N("fake","续火花","Text",120,230,90,20)};
        view=SparkLocator.Describe(new IntPtr(12),new Rectangle(0,0,1200,800),true,nodes);Check(view.IsChat("A")&&!view.IsChat("B"),"chat header identity");Check(view.Emoji!=null&&view.Emoji.Center.X==1094,"composer-relative emoji target");Check(view.Spark!=null&&view.Spark.Key=="spark","named picker tile only");Check(view.SentMarkers.Count==0,"picker and unrelated text are not a receipt");
        nodes.Add(N("sent","[续火花]","Text",1020,400,90,24));view=SparkLocator.Describe(new IntPtr(12),new Rectangle(0,0,1200,800),true,nodes);Check(view.SentMarkers.Contains("sent"),"explicit message receipt marker");
        nodes.Add(N("floating-feed","","Image",1154,770,24,24));view=SparkLocator.Describe(new IntPtr(12),new Rectangle(0,0,1200,800),true,nodes);Check(view.Emoji!=null&&view.Emoji.Center.X==1094,"floating notification beside input must not turn send arrow into emoji");
        nodes.Add(N("send-inner-arrow","","Image",1125,755,13,13));view=SparkLocator.Describe(new IntPtr(12),new Rectangle(0,0,1200,800),true,nodes);Check(view.Emoji!=null&&view.Emoji.Center.X==1094,"send icon child is not another toolbar button");
        var shifted=nodes.Select(n=>new SparkNode{Key=n.Key,Name=n.Name,Kind=n.Kind,Id=n.Id,Value=n.Value,Bounds=new Rectangle(n.Bounds.X-1920,n.Bounds.Y+160,n.Bounds.Width,n.Bounds.Height)}).ToList();view=SparkLocator.Describe(new IntPtr(12),new Rectangle(-1920,160,1200,800),true,shifted);Check(view.IsChat("A")&&view.Emoji.Center.X==-826,"negative-coordinate monitor layout");
        var scaled=nodes.Select(n=>new SparkNode{Key=n.Key,Name=n.Name,Kind=n.Kind,Id=n.Id,Value=n.Value,Bounds=new Rectangle(n.Bounds.X*2,n.Bounds.Y*2,n.Bounds.Width*2,n.Bounds.Height*2)}).ToList();view=SparkLocator.Describe(new IntPtr(12),new Rectangle(0,0,2400,1600),true,scaled,2);Check(view.IsChat("A")&&view.Emoji.Center.X==2188,"200 percent display scaling");
        nodes.Add(N("search-under-chat","搜索","Edit",850,110,230,28,value:"A"));view=SparkLocator.Describe(new IntPtr(12),new Rectangle(0,0,1200,800),true,nodes);Check(!view.HasList&&view.Back!=null,"hidden list under chat does not hijack the workflow");
    }
    static int Main(string[] args){if(args.Length==3&&args[0]=="--douyin-uia")return WorkerProbe(args[1],args[2]);try{Directory.CreateDirectory(args[0]);Run(args[0]).GetAwaiter().GetResult();File.WriteAllText(Path.Combine(args[0],"result.json"),new JavaScriptSerializer().Serialize(new{pass=true,checks=checks,realMessagesSent=0}));Console.WriteLine("PASS "+checks+" checks; no real messages sent");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
