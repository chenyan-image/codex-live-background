using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Collections.Generic;

// Only owns the preview Node process and its FFmpeg descendants, never Codex or the service.
internal sealed class MediaPreviewJob : IDisposable {
    [StructLayout(LayoutKind.Sequential)]struct BasicLimit {public long PerProcessTime,PerJobTime;public uint Flags;public UIntPtr MinWorkingSet,MaxWorkingSet;public uint ActiveProcessLimit;public UIntPtr Affinity;public uint PriorityClass,SchedulingClass;}
    [StructLayout(LayoutKind.Sequential)]struct IoCounters {public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)]struct ExtendedLimit {public BasicLimit Basic;public IoCounters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateJobObject(IntPtr attributes,string name);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(IntPtr job,int info,ref ExtendedLimit limits,int size);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    readonly object gate=new object();IntPtr job;volatile bool cancelled;
    public void Start(string root,string state,string media,Action<string> progress,Action<string,string> complete){
        ThreadPool.QueueUserWorkItem(delegate{
            string request=null;
            try{
                string directory=Path.Combine(state,"requests");Directory.CreateDirectory(directory);request=Path.Combine(directory,"gallery-preview-"+Guid.NewGuid().ToString("N")+".json");
                File.WriteAllText(request,new JavaScriptSerializer().Serialize(new{media=Path.GetFullPath(media),stateRoot=state}),new UTF8Encoding(false));
                var info=new ProcessStartInfo(Path.Combine(root,@"engine\runtime\node\node.exe"),Quote(Path.Combine(root,@"engine\scripts\preview-media.mjs"))+" "+Quote(request)){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
                using(var child=new Process{StartInfo=info}){
                    lock(gate){
                        if(cancelled)return;job=CreateJobObject(IntPtr.Zero,null);if(job==IntPtr.Zero)throw new Exception("无法创建预览任务。");
                        var limits=new ExtendedLimit();limits.Basic.Flags=0x2000;
                        if(!SetInformationJobObject(job,9,ref limits,Marshal.SizeOf(limits)))throw new Exception("无法隔离预览任务。");
                        child.Start();if(!AssignProcessToJobObject(job,child.Handle)){try{child.Kill();}catch{}throw new Exception("无法管理预览进程。");}
                    }
                    var errors=child.StandardError.ReadToEndAsync();string result=null,line;
                    while((line=child.StandardOutput.ReadLine())!=null){if(line.StartsWith("DREAM_PROGRESS:"))progress(line.Substring(15));else if(line.StartsWith("DREAM_RESULT:")){var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(line.Substring(13));object value;if(data.TryGetValue("path",out value))result=Convert.ToString(value);}}
                    child.WaitForExit();if(!cancelled)complete(child.ExitCode==0?result:null,child.ExitCode==0?null:errors.Result.Trim());
                }
            }catch(Exception error){if(!cancelled)complete(null,error.Message);}
            finally{lock(gate){if(job!=IntPtr.Zero){CloseHandle(job);job=IntPtr.Zero;}}if(request!=null)try{File.Delete(request);}catch{}}
        });
    }
    static string Quote(string value){return "\""+value.Replace("\"","\\\"")+"\"";}
    public void Dispose(){lock(gate){cancelled=true;if(job!=IntPtr.Zero){CloseHandle(job);job=IntPtr.Zero;}}}
}
