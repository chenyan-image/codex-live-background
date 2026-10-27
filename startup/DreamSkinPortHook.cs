using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Collections.Generic;
using System.Reflection;

[assembly: AssemblyTitle("Codex Background Startup")]
[assembly: AssemblyProduct("CodexHub")]
[assembly: AssemblyCompany("codex-live-background contributors")]
[assembly: AssemblyVersion("1.7.0.20")]
[assembly: AssemblyFileVersion("1.7.0.20")]

[ComImport,Guid("F27C3930-8029-4AD1-94E3-3DBA417810C1"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDreamSkinPackageDebug {
  [PreserveSig] int EnableDebugging([MarshalAs(UnmanagedType.LPWStr)] string package,[MarshalAs(UnmanagedType.LPWStr)] string debugger,IntPtr environment);
  [PreserveSig] int DisableDebugging([MarshalAs(UnmanagedType.LPWStr)] string package);
}

// Windows calls this helper before a newly activated packaged app starts.
// Only that suspended activation is replaced; existing app sessions are left alone.
internal static class DreamSkinPortHook {
  [StructLayout(LayoutKind.Sequential)] struct BasicInformation { public IntPtr Reserved1,Peb,Reserved2,Reserved3,ProcessId,Reserved4; }
  [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
  [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenThread(uint access,bool inherit,uint tid);
  [DllImport("kernel32.dll",SetLastError=true)] static extern uint ResumeThread(IntPtr thread);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateProcess(IntPtr process,uint code);
  [DllImport("kernel32.dll")] static extern uint GetProcessIdOfThread(IntPtr thread);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref int size);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateFile(string path,uint access,uint sharing,IntPtr security,uint creation,uint flags,IntPtr template);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetFinalPathNameByHandle(IntPtr file,StringBuilder path,uint capacity,uint flags);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern int GetPackageFullName(IntPtr process,ref uint size,StringBuilder name);
  // Resolve registered package locations, including Store apps moved to another drive.
  // https://learn.microsoft.com/windows/win32/api/appmodel/nf-appmodel-getpackagepathbyfullname
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] static extern int GetPackagePathByFullName(string package,ref uint size,StringBuilder path);
  [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
  [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr process,int kind,out BasicInformation data,int size,out int returned);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool ReadProcessMemory(IntPtr process,IntPtr address,byte[] buffer,IntPtr size,out IntPtr read);
  [DllImport("shell32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CommandLineToArgvW(string command,out int count);
  [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
  [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr instance,string name,uint type,int width,int height,uint flags);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out IntPtr result);
  static IntPtr largeIcon,smallIcon;
  static void SetWindowIcons(IntPtr window){
    if(window==IntPtr.Zero)return;string icon=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"CodexDreamSkin\icons\codex-official.ico");if(!File.Exists(icon))return;
    if(largeIcon==IntPtr.Zero)largeIcon=LoadImage(IntPtr.Zero,icon,1,64,64,0x10);if(smallIcon==IntPtr.Zero)smallIcon=LoadImage(IntPtr.Zero,icon,1,16,16,0x10);
    IntPtr result;if(largeIcon!=IntPtr.Zero)SendMessageTimeout(window,0x80,(IntPtr)1,largeIcon,2,100,out result);if(smallIcon!=IntPtr.Zero)SendMessageTimeout(window,0x80,IntPtr.Zero,smallIcon,2,100,out result);
  }
  static void RefreshWindowIcons(){
    if(!File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"CodexDreamSkin\icons\codex-official.ico")))return;
    foreach(var app in Process.GetProcessesByName("ChatGPT"))using(app)try{if(app.MainWindowHandle==IntPtr.Zero)continue;IntPtr process=OpenProcess(0x1010,false,app.Id);if(process==IntPtr.Zero)continue;try{Verify(process);string command=CommandLine(process);if(command.IndexOf("--type=",StringComparison.OrdinalIgnoreCase)<0&&command.IndexOf("--remote-debugging-port=9335",StringComparison.OrdinalIgnoreCase)>=0)SetWindowIcons(app.MainWindowHandle);}catch{}finally{CloseHandle(process);}}catch{}
  }
  static readonly string Root=AppDomain.CurrentDomain.BaseDirectory;
  static void Log(string message){try{File.AppendAllText(Path.Combine(Root,"startup.log"),DateTime.UtcNow.ToString("o")+" "+message+Environment.NewLine);}catch{}}
  static string Image(IntPtr process){int size=32768;var path=new StringBuilder(size);if(!QueryFullProcessImageName(process,0,path,ref size))throw new Win32Exception();return path.ToString();}
  static string FinalPath(string path){
    IntPtr file=CreateFile(path,0,7,IntPtr.Zero,3,0,IntPtr.Zero);if(file==new IntPtr(-1))throw new Win32Exception();
    try{var result=new StringBuilder(32768);uint size=GetFinalPathNameByHandle(file,result,32768,0);if(size==0||size>=32768)throw new Win32Exception();return result.ToString();}finally{CloseHandle(file);}
  }
  static bool SameUser(IntPtr process){IntPtr token;if(!OpenProcessToken(process,8,out token))return false;try{using(var user=new WindowsIdentity(token)){return user.User.Value==WindowsIdentity.GetCurrent().User.Value;}}finally{CloseHandle(token);}}
  static string PackageExecutable(string package){
    if(!Regex.IsMatch(package,@"^OpenAI\.Codex_\d+\.\d+\.\d+\.\d+_x64__2p2nqsd0c76g0$"))throw new Exception("Unexpected package");
    uint size=0;int result=GetPackagePathByFullName(package,ref size,null);
    if(result!=122||size<2||size>32768)throw new Exception("Registered package path unavailable");
    var path=new StringBuilder((int)size);result=GetPackagePathByFullName(package,ref size,path);
    if(result!=0)throw new Win32Exception(result);
    string executable=Path.Combine(path.ToString(),"app","ChatGPT.exe");
    if(!File.Exists(executable))throw new Exception("Registered app executable unavailable");
    return executable;
  }
  static string Verify(IntPtr process){
    uint size=0;GetPackageFullName(process,ref size,null);if(size<1||size>512)throw new Exception("Package identity unavailable");
    var name=new StringBuilder((int)size);if(GetPackageFullName(process,ref size,name)!=0)throw new Exception("Package identity unavailable");
    string package=name.ToString();if(!Regex.IsMatch(package,@"^OpenAI\.Codex_\d+\.\d+\.\d+\.\d+_x64__2p2nqsd0c76g0$"))throw new Exception("Unexpected package");
    string exe=PackageExecutable(package);
    if(!SameUser(process)||!String.Equals(FinalPath(Image(process)),FinalPath(exe),StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected executable or user");
    return exe;
  }
  static byte[] Read(IntPtr process,IntPtr address,int size){var data=new byte[size];IntPtr count;if(!ReadProcessMemory(process,address,data,new IntPtr(size),out count)||count.ToInt64()!=size)throw new Win32Exception();return data;}
  static string CommandLine(IntPtr process){
    BasicInformation basic;int returned;if(NtQueryInformationProcess(process,0,out basic,Marshal.SizeOf(typeof(BasicInformation)),out returned)!=0||basic.Peb==IntPtr.Zero)throw new Exception("Startup arguments unavailable");
    IntPtr parameters=new IntPtr(BitConverter.ToInt64(Read(process,IntPtr.Add(basic.Peb,0x20),8),0));
    byte[] descriptor=Read(process,IntPtr.Add(parameters,0x70),16);int length=BitConverter.ToUInt16(descriptor,0);
    if(length<2||length>60000||(length&1)!=0)throw new Exception("Unexpected startup arguments");
    return Encoding.Unicode.GetString(Read(process,new IntPtr(BitConverter.ToInt64(descriptor,8)),length));
  }
  static string Quote(string value){
    var result=new StringBuilder("\"");int slashes=0;
    foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='\"'){result.Append('\\',slashes*2+1).Append(c);}else{result.Append('\\',slashes).Append(c);}slashes=0;}
    return result.Append('\\',slashes*2).Append('"').ToString();
  }
  static string OriginalArguments(string command){
    int count;IntPtr argv=CommandLineToArgvW(command,out count);if(argv==IntPtr.Zero)throw new Win32Exception();
    try{var result=new StringBuilder();for(int i=1;i<count;i++){if(result.Length>0)result.Append(' ');result.Append(Quote(Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv,i*IntPtr.Size))));}return result.ToString();}finally{LocalFree(argv);}
  }
  static bool FocusExistingMain(int pid,string executable){
    foreach(var candidate in Process.GetProcessesByName("ChatGPT")){
      using(candidate){if(candidate.Id==pid)continue;IntPtr other=OpenProcess(0x1010,false,candidate.Id);if(other==IntPtr.Zero)continue;
        try{if(!SameUser(other)||!String.Equals(FinalPath(Image(other)),FinalPath(executable),StringComparison.OrdinalIgnoreCase))continue;
          string command=CommandLine(other);
          if(command.IndexOf("--type=",StringComparison.OrdinalIgnoreCase)<0){
            IntPtr window=candidate.MainWindowHandle;
            if(window==IntPtr.Zero)continue;
            SetWindowIcons(window);
            if(IsIconic(window))ShowWindow(window,9);
            SetForegroundWindow(window);return true;
          }
        }catch{throw new Exception("Existing app cannot be inspected safely");}finally{CloseHandle(other);}}
    }return false;
  }
  static Process StartSkinMonitor(){
    string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
    var start=new ProcessStartInfo(powershell,"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy RemoteSigned -File "+Quote(Path.Combine(Root,"monitor-skin.ps1")));
    start.UseShellExecute=false;start.CreateNoWindow=true;start.WindowStyle=ProcessWindowStyle.Hidden;
    return Process.Start(start);
  }
  static string InstalledPackage(){
    string script=Path.Combine(Directory.GetParent(Root.TrimEnd(Path.DirectorySeparatorChar)).FullName,@"engine\scripts\query-codex-package.ps1");
    string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
    var start=new ProcessStartInfo(powershell,"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File "+Quote(script));
    start.UseShellExecute=false;start.CreateNoWindow=true;start.RedirectStandardOutput=true;start.RedirectStandardError=true;
    using(var probe=Process.Start(start)){
      if(!probe.WaitForExit(15000)){probe.Kill();throw new Exception("Package refresh timed out");}
      string package=probe.StandardOutput.ReadToEnd().Trim();
      if(probe.ExitCode!=0||!Regex.IsMatch(package,@"^OpenAI\.Codex_\d+\.\d+\.\d+\.\d+_x64__2p2nqsd0c76g0$"))throw new Exception("Official Codex registration unavailable");
      return package;
    }
  }
  static void Guard(){
    using(var mutex=new Mutex(false,"Local\\CodexDreamSkin.PortGuard."+WindowsIdentity.GetCurrent().User.Value)){
      bool acquired=false;try{try{acquired=mutex.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}if(!acquired)return;
        object settings=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("B1AEC16F-2383-4852-B0E9-8F0B1DC66B4D")));
        var registered=new HashSet<string>();DateTime nextRefresh=DateTime.MinValue,nextMonitor=DateTime.MinValue,nextIcon=DateTime.MinValue;Process monitor=null;
        var follow=CodexFollow.StartEmbedded(Directory.GetParent(Root.TrimEnd(Path.DirectorySeparatorChar)).FullName,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexDreamSkin"));
        try{
          while(!File.Exists(Path.Combine(Root,"stop-guard"))){
            if(DateTime.UtcNow>=nextIcon){RefreshWindowIcons();nextIcon=DateTime.UtcNow.AddSeconds(5);}
            if(monitor!=null&&monitor.HasExited){
              Log("Skin monitor exited with code "+monitor.ExitCode+"; retrying in 10 seconds");
              monitor.Dispose();monitor=null;nextMonitor=DateTime.UtcNow.AddSeconds(10);
            }
            if(monitor==null&&DateTime.UtcNow>=nextMonitor){
              try{
                monitor=StartSkinMonitor();
                Log("Skin monitor started PID "+monitor.Id);
              }catch(Exception error){Log("Skin monitor launch: "+error.Message);nextMonitor=DateTime.UtcNow.AddSeconds(10);}
            }
            if(DateTime.UtcNow>=nextRefresh){
              try{
                string package=InstalledPackage();
                if(!registered.Contains(package)){
                  int result=((IDreamSkinPackageDebug)settings).EnableDebugging(package,Quote(Path.Combine(Root,"DreamSkinPortHook.exe")),IntPtr.Zero);
                  if(result!=0)Marshal.ThrowExceptionForHR(result);registered.Add(package);Log("Startup guard registered "+package);
                }
                File.WriteAllText(Path.Combine(Root,"guard-state.json"),"{\"pid\":"+Process.GetCurrentProcess().Id+",\"package\":\""+package+"\",\"registered\":true,\"followEmbedded\":true,\"time\":\""+DateTime.UtcNow.ToString("o")+"\"}");
                nextRefresh=DateTime.UtcNow.AddMinutes(2);
              }catch(Exception error){Log("Startup guard refresh: "+error.Message);nextRefresh=DateTime.UtcNow.AddSeconds(15);}
            }
            Thread.Sleep(1000);
          }
        }finally{
          follow.Dispose();foreach(string package in registered)((IDreamSkinPackageDebug)settings).DisableDebugging(package);
          Marshal.ReleaseComObject(settings);
          if(monitor!=null)monitor.Dispose();
          File.WriteAllText(Path.Combine(Root,"guard-state.json"),"{\"registered\":false,\"stopped\":true}");
        }
      }catch(Exception error){Log("Startup guard failed: "+error.Message);}finally{if(acquired)mutex.ReleaseMutex();}
    }
  }
  static void EnsureGuard(){
    if(File.Exists(Path.Combine(Root,"stop-guard")))return;
    try{using(var existing=Mutex.OpenExisting("Local\\CodexDreamSkin.PortGuard."+WindowsIdentity.GetCurrent().User.Value))return;}catch(WaitHandleCannotBeOpenedException){}
    var info=new ProcessStartInfo(Path.Combine(Root,"DreamSkinPortHook.exe"),"--guard"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Root};
    using(var child=Process.Start(info)){if(child==null)throw new Exception("Background guard could not start");}
  }
  static void Main(string[] args){
    if(args.Length==1&&args[0]=="--guard"){Guard();return;}
    if(args.Length==1&&args[0]=="--ensure"){EnsureGuard();return;}
    if(args.Length==1&&args[0]=="--launch"){
      try{if(File.Exists(Path.Combine(Root,"stop-guard")))throw new Exception("背景服务正在更新，请稍后重试。");EnsureGuard();string executable=PackageExecutable(InstalledPackage());if(FocusExistingMain(0,executable))return;
        using(var mutex=new Mutex(false,"Local\\CodexDreamSkin.PortStartup."+WindowsIdentity.GetCurrent().User.Value)){
          bool owns=false;try{try{owns=mutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}if(!owns)return;
            var listener=new TcpListener(IPAddress.Loopback,9335);try{listener.Start();}finally{listener.Stop();}
            string profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"CodexDreamSkin\cdp-profile");Directory.CreateDirectory(profile);
            CodexBackgroundShell.Identity.Launch(executable,"--remote-debugging-address=127.0.0.1 --remote-debugging-port=9335 --user-data-dir="+Quote(profile),"OpenAI.Codex_2p2nqsd0c76g0!App",profile);
          }finally{if(owns)mutex.ReleaseMutex();}
        }
      }catch(Exception error){Log("Shortcut launch failed: "+error.Message);Environment.ExitCode=1;}return;
    }
    int pid=0;uint tid=0;for(int i=0;i+1<args.Length;i++){if(args[i]=="-p")Int32.TryParse(args[++i],out pid);else if(args[i]=="-tid")UInt32.TryParse(args[++i],out tid);}
    if(pid<1||tid<1)return;
    IntPtr thread=OpenThread(0x802,false,tid),process=IntPtr.Zero;bool valid=thread!=IntPtr.Zero&&GetProcessIdOfThread(thread)==pid,retired=false;
    try{
      if(!valid)throw new Exception("Activation thread mismatch");
      process=OpenProcess(0x1011,false,pid);if(process==IntPtr.Zero)throw new Win32Exception();
      string executable=Verify(process),command=CommandLine(process);
      EnsureGuard();
      if(command.IndexOf("--type=",StringComparison.OrdinalIgnoreCase)>=0||command.IndexOf("--remote-debugging-",StringComparison.OrdinalIgnoreCase)>=0)return;
      if(FocusExistingMain(pid,executable)){retired=TerminateProcess(process,0);Log("Existing app window activated; new activation "+pid);return;}
      using(var mutex=new Mutex(false,"Local\\CodexDreamSkin.PortStartup."+WindowsIdentity.GetCurrent().User.Value)){
        bool acquired=false;try{try{acquired=mutex.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}if(!acquired)return;
          var reservation=new TcpListener(IPAddress.Loopback,9335);try{reservation.Start();}finally{reservation.Stop();}
          string profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"CodexDreamSkin\cdp-profile");
          Directory.CreateDirectory(profile);
          int launched=CodexBackgroundShell.Identity.Launch(executable,OriginalArguments(command)+" --remote-debugging-address=127.0.0.1 --remote-debugging-port=9335 --user-data-dir="+Quote(profile),"OpenAI.Codex_2p2nqsd0c76g0!App",profile);
          retired=TerminateProcess(process,0);if(!retired)throw new Win32Exception();Log("Prepared port 9335; app PID "+launched);
        }finally{if(acquired)mutex.ReleaseMutex();}
      }
    }catch(Exception error){Log("Ordinary startup retained: "+error.Message);}
    finally{if(valid&&!retired)ResumeThread(thread);if(thread!=IntPtr.Zero)CloseHandle(thread);if(process!=IntPtr.Zero)CloseHandle(process);}
  }
}
