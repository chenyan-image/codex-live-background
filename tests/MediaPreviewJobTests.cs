using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using System.Collections.Generic;

internal static class MediaPreviewJobTests {
    static bool Alive(int pid){try{using(var p=Process.GetProcessById(pid))return !p.HasExited;}catch(ArgumentException){return false;}}
    public static int Main(string[] args){
        string root=args[0],state=Path.Combine(root,"state");Directory.CreateDirectory(Path.Combine(root,@"engine\scripts"));
        string script="import fs from 'node:fs';import{spawn}from'node:child_process';const r=JSON.parse(fs.readFileSync(process.argv[2],'utf8'));const child=spawn(process.execPath,['-e','setInterval(()=>{},1000)'],{windowsHide:true,stdio:'ignore'});fs.writeFileSync(r.stateRoot+'/worker.json',JSON.stringify({parent:process.pid,child:child.pid}));console.log('DREAM_PROGRESS:started');setInterval(()=>{},1000);";
        File.WriteAllText(Path.Combine(root,@"engine\scripts\preview-media.mjs"),script);
        using(var ready=new ManualResetEvent(false))using(var job=new MediaPreviewJob()){
            job.Start(root,state,Path.Combine(root,"fixture.mp4"),message=>ready.Set(),(file,error)=>{});
            if(!ready.WaitOne(10000))throw new Exception("Preview fixture did not start");
            var pids=new JavaScriptSerializer().Deserialize<Dictionary<string,int>>(File.ReadAllText(Path.Combine(state,"worker.json")));
            if(!Alive(pids["parent"])||!Alive(pids["child"]))throw new Exception("Preview descendants did not start");
            job.Dispose();var watch=Stopwatch.StartNew();while((Alive(pids["parent"])||Alive(pids["child"]))&&watch.ElapsedMilliseconds<5000)Thread.Sleep(40);
            if(Alive(pids["parent"])||Alive(pids["child"]))throw new Exception("Preview cancellation left a worker running");
            while(Directory.GetFiles(Path.Combine(state,"requests")).Length>0&&watch.ElapsedMilliseconds<5000)Thread.Sleep(40);
            if(Directory.GetFiles(Path.Combine(state,"requests")).Length>0)throw new Exception("Preview request was not cleaned up");
            Console.WriteLine("{\"pass\":true,\"previewParentStopped\":true,\"descendantStopped\":true,\"requestRemoved\":true}");
        }
        return 0;
    }
}
