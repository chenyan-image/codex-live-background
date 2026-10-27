using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class PetQuotaRefreshTests {
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static int checks;
    static void Need(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static T Field<T>(object value,string name){return (T)value.GetType().GetField(name,Private).GetValue(value);}
    static void Until(Func<bool> condition,string message,int timeout=6000){var clock=Stopwatch.StartNew();while(!condition()&&clock.ElapsedMilliseconds<timeout){Application.DoEvents();Thread.Sleep(5);}Need(condition(),message);}
    static void Pump(int milliseconds){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<milliseconds){Application.DoEvents();Thread.Sleep(5);}}
    static string[] ReadLog(string file){if(!File.Exists(file))return new string[0];using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(stream))return reader.ReadToEnd().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);}
    static int Requests(string file){return ReadLog(file).Count(line=>line=="account/rateLimits/read");}
    static PetRefreshAnimation Animation(PetWindow window){return Field<PetRefreshAnimation>(Field<PetPainter>(window,"painter"),"quotaRefresh");}
    static PetWindow HiddenWindow(PetPack pack,PetPreferences preferences,string root){
        var window=new PetWindow(pack,preferences,root);Field<System.Windows.Forms.Timer>(window,"timer").Stop();
        Need(!window.Visible&&!window.ShowInTaskbar,"Test pet unexpectedly became visible");return window;
    }
    static int FakeCli(){
        string file=Environment.GetEnvironmentVariable("CODEXHUB_QUOTA_TEST_LOG"),mode=Environment.GetEnvironmentVariable("CODEXHUB_QUOTA_TEST_MODE");
        var json=new JavaScriptSerializer();string line;
        while((line=Console.ReadLine())!=null){
            var request=json.Deserialize<Dictionary<string,object>>(line);object methodValue;if(!request.TryGetValue("method",out methodValue))continue;string method=Convert.ToString(methodValue);
            using(var stream=new FileStream(file,FileMode.Append,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete))using(var writer=new StreamWriter(stream))writer.WriteLine(method);
            if(method=="initialize")Console.WriteLine(json.Serialize(new{id=1,result=new{}}));
            else if(method=="account/rateLimits/read"){
                Thread.Sleep(mode=="cancel"?5000:300);
                if(mode=="error")Console.WriteLine(json.Serialize(new{id=2,error=new{code=-32001,message="fixture error"}}));
                else Console.WriteLine(json.Serialize(new{id=2,result=new{rateLimitsByLimitId=new{codex=new{limitId="codex",primary=new{usedPercent=24,windowDurationMins=300,resetsAt=1900000000},secondary=new{usedPercent=31,windowDurationMins=10080,resetsAt=1900500000}}}}}));
            }
            Console.Out.Flush();
        }return 0;
    }
    [STAThread]static int Main(string[] args){
        if(args.Length==1&&args[0]=="app-server")return FakeCli();
        string report=args[1],scratch=Path.Combine(Path.GetDirectoryName(report),"quota-state-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
        string[] keys={"CODEX_COMPANION_CLI","CODEX_HOME","CODEXHUB_QUOTA_TEST_LOG","CODEXHUB_QUOTA_TEST_MODE"};var old=keys.ToDictionary(key=>key,key=>Environment.GetEnvironmentVariable(key));
        try{
            string root=args[0],file=Path.Combine(scratch,"requests.log");Environment.SetEnvironmentVariable(keys[0],Assembly.GetExecutingAssembly().Location);Environment.SetEnvironmentVariable(keys[1],Path.Combine(scratch,"empty-codex-home"));Environment.SetEnvironmentVariable(keys[2],file);Environment.SetEnvironmentVariable(keys[3],"success");
            Application.EnableVisualStyles();using(var ui=new Form()){
                IntPtr handle=ui.Handle;SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                var pack=PetPack.Discover(root,scratch).First(p=>p.Id=="deepseek"&&p.Available);
                using(var controller=new PetController(ui,root,scratch,true)){
                    var window=HiddenWindow(pack,controller.Preferences,root);controller.Window=window;
                    Need(!Field<System.Windows.Forms.Timer>(controller,"timer").Enabled&&!Field<System.Windows.Forms.Timer>(controller,"activityTimer").Enabled,"Unstarted controller starts live timers");
                    var previous=new PetQuotaSnapshot{CheckedUtc=DateTime.UtcNow,Short=new PetQuotaWindow{Remaining=12,Minutes=300}};controller.Quota=previous;window.Quota=previous;
                    int changed=0;controller.Changed+=delegate{changed++;};var start=Stopwatch.StartNew();controller.Refresh();long returnedMs=start.ElapsedMilliseconds;
                    Need(Field<bool>(controller,"checking")&&window.QuotaRefreshing&&Animation(window).Waiting,"Refresh lacks immediate busy/animation state");
                    for(int i=0;i<12;i++)controller.Refresh();
                    Until(()=>Requests(file)==1,"Fake quota request was not issued");Need(Field<bool>(controller,"checking"),"Delayed request finished too early to verify single-flight");
                    Until(()=>!Field<bool>(controller,"checking"),"Success refresh never completes");
                    Need(Requests(file)==1,"Concurrent refreshes sent duplicate requests");Need(!window.QuotaRefreshing&&!Animation(window).Waiting,"Success left the refresh spinning");
                    Need(!Object.ReferenceEquals(previous,controller.Quota)&&Object.ReferenceEquals(controller.Quota,window.Quota),"Success does not propagate the new quota snapshot");
                    Need(controller.Quota.Short.Remaining==76&&controller.Quota.Week.Remaining==69,"Success parsed the wrong quota percentages");Need(controller.QuotaStatus.StartsWith("更新于 ")&&window.QuotaStatus==controller.QuotaStatus,"Success status was not propagated");
                    double now=Field<Stopwatch>(window,"watch").Elapsed.TotalSeconds;double settled=Field<double>(Animation(window),"finished");Need(Animation(window).At(settled+.3).CheckOpacity==1,"Successful refresh never shows its check mark");Need(Animation(window).At(settled+2).CheckOpacity==0,"Success check does not return to the refresh arrow");
                    Need(!window.Visible&&!ui.Visible,"Success test displayed a real window");
                    var success=controller.Quota;Environment.SetEnvironmentVariable(keys[3],"error");int count=Requests(file);controller.Refresh();Need(window.QuotaRefreshing&&Animation(window).Waiting,"Failure retry does not start a fresh animation");
                    Until(()=>Requests(file)==count+1,"Error scenario was not requested");Until(()=>!Field<bool>(controller,"checking"),"Failure refresh never completes");
                    Need(Requests(file)==2,"Failure sent unexpected extra reads");Need(Object.ReferenceEquals(success,controller.Quota)&&Object.ReferenceEquals(success,window.Quota),"Failure replaced the last valid quota");
                    Need(controller.QuotaStatus.Contains("无法读取额度")&&controller.QuotaStatus==window.QuotaStatus,"Failure message did not reach the window");Need(!window.QuotaRefreshing&&!Animation(window).Waiting,"Failure left the refresh spinning");
                    now=Field<Stopwatch>(window,"watch").Elapsed.TotalSeconds;foreach(double age in new[]{0D,.5,1,2,3})Need(Animation(window).At(now+age).CheckOpacity==0,"Failed refresh falsely shows success");Need(changed==4,"Expected one busy and one completion event for each refresh");
                    string[] methods=ReadLog(file);Need(methods.Count(value=>value=="initialize")==2&&methods.Count(value=>value=="initialized")==2,"Fake server did not exercise the full production protocol");
                    Need(returnedMs<1000,"Refresh blocked the UI while waiting for server data");
                }
                string cancelState=Path.Combine(scratch,"cancel");Environment.SetEnvironmentVariable(keys[3],"cancel");var pending=new PetController(ui,root,cancelState,true);var pet=HiddenWindow(pack,pending.Preferences,root);pending.Window=pet;
                var oldQuota=new PetQuotaSnapshot{CheckedUtc=DateTime.UtcNow,Short=new PetQuotaWindow{Remaining=44,Minutes=300}};pending.Quota=oldQuota;pet.Quota=oldQuota;var animation=Animation(pet);int notifications=0;pending.Changed+=delegate{notifications++;};int before=Requests(file);
                try{
                    pending.Refresh();Until(()=>Requests(file)==before+1,"Cancellation request never reached the fake server");Need(pet.QuotaRefreshing&&Field<bool>(pending,"checking"),"Cancel scenario was not in flight");
                    pending.Dispose();Until(()=>!Field<bool>(pending,"checking"),"Disposing did not cancel the in-flight quota reader");Pump(30);
                    Need(pending.Window==null&&pet.IsDisposed,"Disposing leaves the pet alive");Need(Object.ReferenceEquals(pending.Quota,oldQuota),"Cancelled read changed existing quota");Need(notifications==1,"Cancelled read published a completion/success event after disposal");
                    Need(animation.At(100).CheckOpacity==0,"Disposed refresh falsely shows a success check");Need(Requests(file)==3,"Cancellation caused another request");
                }finally{pending.Dispose();}
            }
            File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{pass=true,checks,singleFlight=true,successCheck=true,failurePreservesSnapshot=true,cancelNeverConfirms=true,realCodexOrNetworkTouched=false,visibleWindowsCreated=false}));return 0;
        }catch(Exception error){File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{pass=false,checks,error=error.ToString()}));return 1;}
        finally{foreach(string key in keys)Environment.SetEnvironmentVariable(key,old[key]);}
    }
}
