using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

// Shared by the manager and the updater's lightweight --follow mode. This mode
// never initializes Forms, the pet renderer, image caches or account readers.
internal static class CodexPresence {
    [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}
    delegate bool WindowCallback(IntPtr window,IntPtr data);
    [DllImport("user32.dll")]static extern bool EnumWindows(WindowCallback callback,IntPtr data);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out Rect rect);
    [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int kind,out int value,int size);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetPackageFullName(IntPtr process,ref uint size,StringBuilder name);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetPackagePathByFullName(string name,ref uint size,StringBuilder path);
    static readonly Regex OfficialPackage=new Regex(@"^OpenAI\.Codex(?:Beta)?_\d+\.\d+\.\d+\.\d+_x64__2p2nqsd0c76g0$");
    internal static bool MatchesRegisteredImage(string executable,string installed,string package){return OfficialPackage.IsMatch(package)&&String.Equals(Path.GetFullPath(executable),Path.GetFullPath(Path.Combine(installed,@"app\ChatGPT.exe")),StringComparison.OrdinalIgnoreCase);}
    internal static bool OfficialProcess(Process process){
        if(process.SessionId!=Process.GetCurrentProcess().SessionId)return false;
        uint size=0;GetPackageFullName(process.Handle,ref size,null);string package=null;
        if(size>1&&size<512){var name=new StringBuilder((int)size);if(GetPackageFullName(process.Handle,ref size,name)==0)package=name.ToString();}
        string image=process.MainModule.FileName;
        if(String.IsNullOrEmpty(package)){var app=new DirectoryInfo(Path.GetDirectoryName(image));if(app.Parent==null)return false;package=app.Parent.Name;}
        if(!OfficialPackage.IsMatch(package))return false;uint length=0;GetPackagePathByFullName(package,ref length,null);if(length<2||length>32768)return false;var installed=new StringBuilder((int)length);
        return GetPackagePathByFullName(package,ref length,installed)==0&&MatchesRegisteredImage(image,installed.ToString(),package);
    }
    internal static bool VisibleFor(HashSet<uint> trusted){bool found=false;if(trusted.Count==0)return false;EnumWindows((window,data)=>{
        if(!IsWindowVisible(window)||IsIconic(window))return true;uint owner;GetWindowThreadProcessId(window,out owner);if(!trusted.Contains(owner))return true;
        int cloaked;if(DwmGetWindowAttribute(window,14,out cloaked,4)==0&&cloaked!=0)return true;Rect client;if(!GetClientRect(window,out client)||client.Right<240||client.Bottom<300)return true;found=true;return false;
    },IntPtr.Zero);return found;}
    public static bool Visible(){var trusted=new HashSet<uint>();foreach(var process in Process.GetProcessesByName("ChatGPT"))using(process)try{if(process.MainWindowHandle!=IntPtr.Zero&&OfficialProcess(process))trusted.Add((uint)process.Id);}catch(Win32Exception){}catch(InvalidOperationException){}catch(ArgumentException){}return VisibleFor(trusted);}
}

internal sealed class CodexFollowPolicy {
    double hiddenSince=-1,nextLaunch;bool manualPause;
    public bool ShouldExit(bool enabled,bool visible,bool busy,double now){if(!enabled||visible){hiddenSince=-1;return false;}if(hiddenSince<0)hiddenSince=now;return !busy&&now-hiddenSince>=.8;}
    public bool ShouldLaunch(bool visible,bool running,bool manualExit,bool maintenance,double now){if(manualExit)manualPause=true;if(!visible){manualPause=false;return false;}if(running||manualPause||maintenance||now<nextLaunch)return false;nextLaunch=now+3;return true;}
}

internal static class CodexFollow {
    const string RunPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string RunName="CodexHubFollow";
    static readonly string Sid=WindowsIdentity.GetCurrent().User.Value;
    static string Identity(string root,string state){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).TrimEnd('\\').ToUpperInvariant()+"|"+Path.GetFullPath(state).TrimEnd('\\').ToUpperInvariant()))).Replace("-","");}
    static string Name(string kind,string root,string state){return "Local\\CodexHub.Follow."+kind+"."+Sid+"."+Identity(root,state);}
    static bool Exists(string name){try{using(var mutex=Mutex.OpenExisting(name))return true;}catch(WaitHandleCannotBeOpenedException){return false;}}
    sealed class Pause:IDisposable {readonly EventWaitHandle handle;public Pause(string name){handle=new EventWaitHandle(false,EventResetMode.ManualReset,name);handle.Set();}public void Dispose(){handle.Reset();handle.Dispose();}}
    public static IDisposable Suspend(string root,string state){return new Pause(Name("Maintenance",root,state));}
    static bool Paused(string root,string state){try{using(var handle=EventWaitHandle.OpenExisting(Name("Maintenance",root,state)))return handle.WaitOne(0);}catch(WaitHandleCannotBeOpenedException){return false;}}
    internal static string Command(string root,string state){return SoftwareUpdate.Quote(Path.Combine(root,"CodexHubUpdater.exe"))+" --follow "+SoftwareUpdate.Quote(Path.GetFullPath(state));}
    internal static bool Enabled(string state){try{string file=Path.Combine(state,"pet-settings.json");if(!File.Exists(file))return false;using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(stream,Encoding.UTF8)){if(stream.Length>65536)return false;var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());object value;return data!=null&&data.TryGetValue("FollowCodex",out value)&&Object.Equals(value,true);}}catch(IOException){return false;}catch(ArgumentException){return false;}catch(UnauthorizedAccessException){return false;}}
    static bool OwnRegistration(string command){var match=Regex.Match(command??"",@"^""([^""\r\n]+\\CodexHubUpdater\.exe)"" --follow ""[^""\r\n]+""$",RegexOptions.IgnoreCase);if(!match.Success)return false;try{string root=Path.GetDirectoryName(match.Groups[1].Value),file=Path.Combine(root,".codex-background-install.json");if(!File.Exists(file))return false;var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(file));return Convert.ToString(data["productId"])=="codex-live-background/exe"&&String.Equals(Path.GetFullPath(Convert.ToString(data["root"])).TrimEnd('\\'),Path.GetFullPath(root).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase);}catch{return false;}}
    public static void Configure(string root,string state,bool enabled){
        string expected=Command(root,state);using(var key=Registry.CurrentUser.CreateSubKey(RunPath)){string old=Convert.ToString(key.GetValue(RunName));if(enabled){if(!File.Exists(Path.Combine(root,"CodexHubUpdater.exe")))throw new IOException("缺少自动启停组件，请更新软件。");if(old.Length>0&&!old.Equals(expected,StringComparison.OrdinalIgnoreCase)&&!OwnRegistration(old))throw new IOException("自动启停登记不属于已验证安装，未覆盖。");key.SetValue(RunName,expected,RegistryValueKind.String);}else if(old.Equals(expected,StringComparison.OrdinalIgnoreCase))key.DeleteValue(RunName,false);}
        if(enabled)Ensure(root,state);else RequestStop(root,state);
    }
    public static void Ensure(string root,string state){if(!Enabled(state)||Maintenance(root,state)||Exists(Name("Watch",root,state)))return;using(var stop=new EventWaitHandle(false,EventResetMode.ManualReset,Name("Stop",root,state)))stop.Reset();var start=new ProcessStartInfo(Path.Combine(root,"CodexHubUpdater.exe"),"--follow "+SoftwareUpdate.Quote(Path.GetFullPath(state))){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=root};using(var process=Process.Start(start)){} }
    public static void RequestStop(string root,string state){try{using(var stop=EventWaitHandle.OpenExisting(Name("Stop",root,state)))stop.Set();}catch(WaitHandleCannotBeOpenedException){}}
    public static void ManualExit(string root,string state){using(var signal=new EventWaitHandle(false,EventResetMode.AutoReset,Name("Manual",root,state)))using(var ack=new EventWaitHandle(false,EventResetMode.AutoReset,Name("ManualAck",root,state))){ack.Reset();signal.Set();Ensure(root,state);ack.WaitOne(3000);}}
    public static void StopAndWait(string root,string state){RequestStop(root,state);string expected=Path.GetFullPath(Path.Combine(root,"CodexHubUpdater.exe"));int self=Process.GetCurrentProcess().Id;foreach(var process in Process.GetProcessesByName("CodexHubUpdater"))using(process)try{if(process.Id==self||process.SessionId!=Process.GetCurrentProcess().SessionId||!process.MainModule.FileName.Equals(expected,StringComparison.OrdinalIgnoreCase))continue;if(!process.WaitForExit(4000))throw new IOException("自动启停组件尚未退出，程序未替换。");}catch(Win32Exception){throw new IOException("无法验证自动启停组件，程序未替换。");}catch(InvalidOperationException){}}
    static bool Maintenance(string root,string state){return Paused(root,state)||Exists("Local\\CodexLiveBackground.NativeLifecycle."+Sid)||Exists("Local\\CodexLiveBackground.Setup."+Sid);}
    static bool RegisteredHere(string root,string state){using(var key=Registry.CurrentUser.OpenSubKey(RunPath)){return key!=null&&String.Equals(Convert.ToString(key.GetValue(RunName)),Command(root,state),StringComparison.OrdinalIgnoreCase);}}
    static bool ManagerRunning(){return Exists("Local\\CodexLiveBackground.Manager."+Sid);}
    public static int Run(string state){string root=AppDomain.CurrentDomain.BaseDirectory;state=Path.GetFullPath(state);if(!Enabled(state)||!File.Exists(Path.Combine(root,"CodexLiveBackground.exe")))return 0;
        using(var mutex=new Mutex(false,Name("Watch",root,state))){bool owns=false;try{try{owns=mutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}if(!owns)return 0;
            using(var stop=new EventWaitHandle(false,EventResetMode.ManualReset,Name("Stop",root,state)))using(var manual=new EventWaitHandle(false,EventResetMode.AutoReset,Name("Manual",root,state)))using(var ack=new EventWaitHandle(false,EventResetMode.AutoReset,Name("ManualAck",root,state))){var policy=new CodexFollowPolicy();var clock=Stopwatch.StartNew();while(!stop.WaitOne(0)&&Enabled(state)&&RegisteredHere(root,state)){
                bool visible=CodexPresence.Visible(),manualExit=manual.WaitOne(0),launch=policy.ShouldLaunch(visible,ManagerRunning(),manualExit,Maintenance(root,state),clock.Elapsed.TotalSeconds);if(manualExit)ack.Set();if(launch)try{using(var process=Process.Start(new ProcessStartInfo(Path.Combine(root,"CodexLiveBackground.exe"),"--follow-start"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=root})){} }catch(IOException){}catch(Win32Exception){}
                if(stop.WaitOne(750))break;
            }}return 0;
        }finally{if(owns)mutex.ReleaseMutex();}}
    }
}
