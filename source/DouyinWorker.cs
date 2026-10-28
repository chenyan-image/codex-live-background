using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal sealed class SparkWorkerRequest {public string Operation,Query;public long Window;public SparkNode Node;}
internal sealed class SparkWorkerReply {
    public string Error,ErrorKind;public long Window;public Rectangle Bounds;public bool Foreground,Ready,SearchComplete;
    public SparkNode MessageButton,SearchBox,Composer,Back,Emoji,Spark;public Rectangle ListBounds,ChatBounds;public string HeaderName;
    public List<SparkNode> Contacts=new List<SparkNode>(),Nodes=new List<SparkNode>();public string[] SentMarkers=new string[0];
    public static SparkWorkerReply From(SparkView view){return new SparkWorkerReply{Window=view.Window.ToInt64(),Bounds=view.Bounds,Foreground=view.Foreground,Ready=view.Ready,SearchComplete=view.SearchComplete,MessageButton=view.MessageButton,SearchBox=view.SearchBox,Composer=view.Composer,Back=view.Back,Emoji=view.Emoji,Spark=view.Spark,ListBounds=view.ListBounds,ChatBounds=view.ChatBounds,HeaderName=view.HeaderName,Contacts=view.Contacts,Nodes=view.Nodes,SentMarkers=view.SentMarkers.ToArray()};}
    public SparkView View(){return new SparkView{Window=new IntPtr(Window),Bounds=Bounds,Foreground=Foreground,Ready=Ready,SearchComplete=SearchComplete,MessageButton=MessageButton,SearchBox=SearchBox,Composer=Composer,Back=Back,Emoji=Emoji,Spark=Spark,ListBounds=ListBounds,ChatBounds=ChatBounds,HeaderName=HeaderName,Contacts=Contacts??new List<SparkNode>(),Nodes=Nodes??new List<SparkNode>(),SentMarkers=new HashSet<string>(SentMarkers??new string[0])};}
}
// UIA providers can block inside an RPC that CancellationToken cannot interrupt.
// Only an owned short-lived helper performs those calls. No helper is left
// running after timeout/cancel, and a later request starts with a clean process.
internal sealed class SparkWorkerClient {
    readonly string program;readonly int timeout;readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
    internal Action<int> Started {get;set;}public SparkWorkerClient(string executable=null,int timeoutMilliseconds=15000){program=executable??Assembly.GetExecutingAssembly().Location;timeout=timeoutMilliseconds;}
    public async Task<SparkWorkerReply> Request(SparkWorkerRequest request,CancellationToken stop){
        await gate.WaitAsync(stop);try{return await Task.Run(()=>Execute(request,stop));}finally{gate.Release();}
    }
    SparkWorkerReply Execute(SparkWorkerRequest request,CancellationToken stop){
        stop.ThrowIfCancellationRequested();string directory=Path.Combine(Path.GetTempPath(),"CodexHub-Douyin-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        string input=Path.Combine(directory,"request.json"),output=Path.Combine(directory,"reply.json"),phase=Path.Combine(directory,"phase.txt");var json=new JavaScriptSerializer{MaxJsonLength=2*1024*1024};Process child=null;
        try{
            File.WriteAllText(input,json.Serialize(request),new UTF8Encoding(false));
            child=new Process{StartInfo=new ProcessStartInfo(program,"--douyin-uia \""+input+"\" \""+output+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(program)}};
            child.Start();if(Started!=null)Started(child.Id);var clock=Stopwatch.StartNew();
            while(!File.Exists(output)&&!child.WaitForExit(75)){stop.ThrowIfCancellationRequested();if(clock.ElapsedMilliseconds>timeout){string stage=File.Exists(phase)?File.ReadAllText(phase):"启动读取";throw new TimeoutException("抖音控件读取超时（"+stage+"），本次读取已结束，可重新点击开始。");}}
            stop.ThrowIfCancellationRequested();if(!File.Exists(output)||new FileInfo(output).Length>2*1024*1024)throw new IOException("抖音控件读取未返回有效结果，请重新点击开始。");
            var reply=json.Deserialize<SparkWorkerReply>(File.ReadAllText(output,Encoding.UTF8));if(reply==null)throw new IOException("抖音控件读取结果为空。");
            if(reply.Error!=null){if(reply.ErrorKind=="focus")throw new SparkFocusLost();if(reply.ErrorKind=="contact")throw new SparkContactError(reply.Error);throw new InvalidOperationException(reply.Error);}return reply;
        }finally{
            if(child!=null){try{if(!child.HasExited){child.Kill();child.WaitForExit(2000);}}catch(InvalidOperationException){}finally{child.Dispose();}}
            // Remove only the two files created for this exact request. No recursive cleanup.
            try{if(File.Exists(input))File.Delete(input);if(File.Exists(output))File.Delete(output);if(File.Exists(output+".writing"))File.Delete(output+".writing");if(File.Exists(phase))File.Delete(phase);if(Directory.GetFileSystemEntries(directory).Length==0)Directory.Delete(directory);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
internal static class SparkWorkerHost {
    static Action<string> report;internal static void Phase(string phase){if(report!=null)report(phase);}
    public static int Run(string input,string output){
        WindowsUiaRuntime.Initialize();
        report=stage=>File.WriteAllText(Path.Combine(Path.GetDirectoryName(input),"phase.txt"),stage);Phase("启动读取");
        SparkWorkerReply reply=null;var worker=new Thread(delegate(){try{
            if(new FileInfo(input).Length>65536)throw new InvalidDataException("控件请求过大。");var request=new JavaScriptSerializer().Deserialize<SparkWorkerRequest>(File.ReadAllText(input,Encoding.UTF8));if(request==null)throw new InvalidDataException("控件请求为空。");
            reply=DouyinDesktop.ExecuteWorker(request);
        }catch(Exception error){reply=new SparkWorkerReply{Error=error.Message,ErrorKind=error is SparkFocusLost?"focus":error is SparkContactError?"contact":"error"};}});
        worker.SetApartmentState(ApartmentState.MTA);worker.Start();worker.Join();
        File.WriteAllText(output+".writing",new JavaScriptSerializer{MaxJsonLength=2*1024*1024}.Serialize(reply),new UTF8Encoding(false));File.Move(output+".writing",output);return 0;
    }
}
