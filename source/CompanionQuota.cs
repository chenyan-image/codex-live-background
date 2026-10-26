using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// The quota CLI and image-conversion children belong to this manager only.
// Closing the job also handles an interrupted manager or forced uninstall.
internal sealed class PetProcessScope : IDisposable {
    [StructLayout(LayoutKind.Sequential)]struct Basic {public long ProcessTime,JobTime;public uint Flags;public UIntPtr Min,Max;public uint Processes;public UIntPtr Affinity;public uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)]struct Counters {public ulong A,B,C,D,E,F;}
    [StructLayout(LayoutKind.Sequential)]struct Limits {public Basic Basic;public Counters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateJobObject(IntPtr attrs,string name);
    [DllImport("kernel32.dll")]static extern bool SetInformationJobObject(IntPtr job,int kind,ref Limits limits,int size);
    [DllImport("kernel32.dll")]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    IntPtr job;public readonly Process Process;
    public PetProcessScope(ProcessStartInfo info){
        job=CreateJobObject(IntPtr.Zero,null);if(job==IntPtr.Zero)throw new IOException("无法隔离宠物组件进程。");var limits=new Limits();limits.Basic.Flags=0x2000;
        if(!SetInformationJobObject(job,9,ref limits,Marshal.SizeOf(limits))){CloseHandle(job);job=IntPtr.Zero;throw new IOException("无法管理宠物组件进程。");}
        Process=new Process{StartInfo=info};try{Process.Start();if(!AssignProcessToJobObject(job,Process.Handle))throw new IOException("无法关联宠物组件进程。");}catch{try{if(Process.Id>0&&!Process.HasExited)Process.Kill();}catch{}Dispose();throw;}
    }
    public void Cancel(){IntPtr handle=Interlocked.Exchange(ref job,IntPtr.Zero);if(handle!=IntPtr.Zero)CloseHandle(handle);}
    public void Dispose(){Cancel();if(Process!=null)Process.Dispose();}
}

internal sealed class PetQuotaWindow {
    public double? Remaining;public long? ResetsAt;public int Minutes;
    public string Label {get{return Minutes==10080?"本周":Minutes==300?"5 小时":Minutes>0?Minutes>=1440?(Minutes/1440D).ToString("0.#")+" 天":(Minutes/60D).ToString("0.#")+" 小时":"短周期";}}
    public string Percent {get{return Remaining.HasValue?Remaining.Value.ToString("0.#",CultureInfo.InvariantCulture)+"%":"未知";}}
    public string ResetText {get{return ResetAfter(DateTimeOffset.Now);}}
    public string ResetAfter(DateTimeOffset now){if(!ResetsAt.HasValue)return "重置时间未知";try{var left=DateTimeOffset.FromUnixTimeSeconds(ResetsAt.Value)-now;return left.TotalSeconds<=0?"等待额度更新":left.TotalDays>=1?left.Days+"天"+left.Hours+"时后重置":left.TotalHours>=1?left.Hours+"时"+left.Minutes+"分后重置":Math.Max(0,left.Minutes)+"分"+Math.Max(0,left.Seconds)+"秒后重置";}catch(ArgumentOutOfRangeException){return "重置时间未知";}}
}
internal sealed class PetQuotaSnapshot {
    public PetQuotaWindow Short,Week;public DateTime CheckedUtc;
    public bool Stale {get{return (DateTime.UtcNow-CheckedUtc).TotalMinutes>5;}}
    internal static object Get(object value,string key){var data=value as Dictionary<string,object>;object result;return data!=null&&data.TryGetValue(key,out result)?result:null;}
    internal static double? Number(object value){if(value==null||value is bool||value is string)return null;try{double number=Convert.ToDouble(value,CultureInfo.InvariantCulture);return Double.IsNaN(number)||Double.IsInfinity(number)?null:(double?)number;}catch{return null;}}
    static PetQuotaWindow Window(object value){
        if(value==null)return null;var used=Number(Get(value,"usedPercent"));var reset=Number(Get(value,"resetsAt"));var minutes=Number(Get(value,"windowDurationMins"));
        return new PetQuotaWindow{Remaining=used.HasValue?(double?)Math.Max(0,Math.Min(100,100-used.Value)):null,ResetsAt=reset.HasValue&&reset.Value>0&&reset.Value<253402300799L?(long?)reset.Value:null,Minutes=minutes.HasValue&&minutes>0&&minutes<int.MaxValue?(int)minutes.Value:0};
    }
    public static PetQuotaSnapshot Parse(object data){
        object bucket=Get(Get(data,"rateLimitsByLimitId"),"codex")??Get(data,"rateLimits");
        if(bucket==null)throw new InvalidDataException("当前账号尚未返回 Codex 额度。");
        string id=Convert.ToString(Get(bucket,"limitId"));if(!String.IsNullOrEmpty(id)&&id!="codex")throw new InvalidDataException("未返回 Codex 对应的额度窗口。");
        var primary=Window(Get(bucket,"primary"));var secondary=Window(Get(bucket,"secondary"));
        if(primary!=null&&primary.Minutes==10080){var swap=primary;primary=secondary;secondary=swap;}
        return new PetQuotaSnapshot{Short=primary,Week=secondary,CheckedUtc=DateTime.UtcNow};
    }
}
internal sealed class PetQuotaReader : IDisposable {
    readonly CancellationTokenSource shutdown=new CancellationTokenSource();
    public static string FindCli(){
        string configured=Environment.GetEnvironmentVariable("CODEX_COMPANION_CLI");if(!String.IsNullOrWhiteSpace(configured)&&File.Exists(configured))return Path.GetFullPath(configured);
        foreach(var process in Process.GetProcessesByName("ChatGPT"))using(process)try{
            string exe=process.MainModule.FileName;
            if(exe.IndexOf(@"\WindowsApps\OpenAI.Codex",StringComparison.OrdinalIgnoreCase)>=0){string candidate=Path.Combine(Path.GetDirectoryName(exe),@"resources\codex.exe");if(File.Exists(candidate))return candidate;}
        }catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
        string cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"OpenAI\Codex\bin");
        if(Directory.Exists(cache)){string direct=Path.Combine(cache,"codex.exe");if(File.Exists(direct))return direct;foreach(string directory in Directory.GetDirectories(cache).OrderByDescending(x=>x,StringComparer.OrdinalIgnoreCase)){string file=Path.Combine(directory,"codex.exe");if(File.Exists(file))return file;}}
        foreach(string folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(';'))try{string file=Path.Combine(folder.Trim('"'),"codex.exe");if(File.Exists(file))return file;}catch(ArgumentException){}
        throw new FileNotFoundException("请先打开并登录 Codex，再刷新额度。");
    }
    public async Task<PetQuotaSnapshot> ReadAsync(){
        shutdown.Token.ThrowIfCancellationRequested();string executable=FindCli();
        using(var scope=new PetProcessScope(new ProcessStartInfo(executable,"app-server"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8}))
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token)){
            var process=scope.Process;timeout.CancelAfter(20000);process.ErrorDataReceived+=delegate{};process.BeginErrorReadLine();
            try{
                await Send(process,new{id=1,method="initialize",@params=new{clientInfo=new{name="codex_desktop_pet",title="CodexHub",version="1.7.0-preview.3"}}}).ConfigureAwait(false);
                await Response(process,1,timeout.Token).ConfigureAwait(false);
                await Send(process,new{method="initialized"}).ConfigureAwait(false);
                await Send(process,new{id=2,method="account/rateLimits/read"}).ConfigureAwait(false);
                return PetQuotaSnapshot.Parse(await Response(process,2,timeout.Token).ConfigureAwait(false));
            }catch(OperationCanceledException){if(shutdown.IsCancellationRequested)throw;throw new TimeoutException("额度读取超时，请稍后刷新。");}
            finally{try{if(!process.HasExited)process.Kill();process.WaitForExit(1500);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
        }
    }
    static async Task Send(Process process,object value){await process.StandardInput.WriteLineAsync(new JavaScriptSerializer().Serialize(value)).ConfigureAwait(false);await process.StandardInput.FlushAsync().ConfigureAwait(false);}
    static async Task<object> Response(Process process,int id,CancellationToken token){
        while(true){token.ThrowIfCancellationRequested();var line=process.StandardOutput.ReadLineAsync();if(await Task.WhenAny(line,Task.Delay(Timeout.Infinite,token)).ConfigureAwait(false)!=line)throw new OperationCanceledException(token);string text=await line.ConfigureAwait(false);if(text==null)throw new IOException("Codex 额度连接已中断。");if(text.Length>2*1024*1024)throw new InvalidDataException("额度响应过大。");object data;try{data=new JavaScriptSerializer{MaxJsonLength=2*1024*1024}.DeserializeObject(text);}catch(ArgumentException){continue;}
            if(PetQuotaSnapshot.Number(PetQuotaSnapshot.Get(data,"id"))!=id)continue;
            if(PetQuotaSnapshot.Get(data,"error")!=null)throw new IOException("无法读取额度，请检查 Codex 登录和网络。");
            return PetQuotaSnapshot.Get(data,"result");
        }
    }
    public void Dispose(){shutdown.Cancel();}
}
