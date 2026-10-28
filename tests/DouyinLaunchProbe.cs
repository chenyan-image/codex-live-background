using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
// Read-only integration probe: launches/restores the app and reads controls.
// It never searches a contact, clicks a chat, or sends a message.
internal static class DouyinLaunchProbe {
    [STAThread]static int Main(string[] args){if(args.Length==3&&args[0]=="--douyin-uia")return SparkWorkerHost.Run(args[1],args[2]);
        var clock=Stopwatch.StartNew();try{
            if(args.Length==2&&args[0]=="--only-read"){var window=DouyinDesktop.FindOpenWindow();if(window==IntPtr.Zero)throw new Exception("抖音主窗口尚未打开");var snapshot=new SparkWorkerClient().Request(new SparkWorkerRequest{Operation="inspect",Window=window.ToInt64()},CancellationToken.None).GetAwaiter().GetResult();File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{pass=snapshot.Ready,ready=snapshot.Ready,foreground=snapshot.Foreground,milliseconds=clock.ElapsedMilliseconds,realMessagesSent=0}));return snapshot.Ready?0:1;}
            string executable=DouyinInstall.Find(null);var launcher=new DouyinAppLauncher(executable);launcher.EnsureReady(s=>{},CancellationToken.None).GetAwaiter().GetResult();
            var desktop=new DouyinDesktop();SparkView view=null;string firstError=null;try{view=desktop.Read(CancellationToken.None).GetAwaiter().GetResult();}catch(Exception error){firstError=error.Message;view=desktop.Read(CancellationToken.None).GetAwaiter().GetResult();}
            File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{pass=view.Ready&&view.Foreground,launchedOrRestored=true,ready=view.Ready,foreground=view.Foreground,messageButton=view.MessageButton!=null,list=view.HasList,chat=view.Composer!=null,contacts=view.Contacts.Count,firstReadError=firstError,milliseconds=clock.ElapsedMilliseconds,realMessagesSent=0}));return view.Ready&&view.Foreground?0:1;
        }catch(Exception error){File.WriteAllText(args.Length==2?args[1]:args[0],new JavaScriptSerializer().Serialize(new{pass=false,error=error.ToString(),milliseconds=clock.ElapsedMilliseconds,realMessagesSent=0}));return 1;}
    }
}
