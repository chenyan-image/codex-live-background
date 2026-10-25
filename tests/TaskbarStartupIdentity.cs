using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
internal static class TaskbarStartupIdentityTest {
 [StructLayout(LayoutKind.Sequential)]struct StartupInfo {public int Size;public IntPtr Reserved,Desktop,Title;public uint X,Y,Width,Height,Columns,Rows,Fill,Flags;public ushort Show,ReservedBytes;public IntPtr ReservedPointer,Input,Output,Error;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern void GetStartupInfo(out StartupInfo info);
 [DllImport("shell32.dll")]static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr value);
 [STAThread]static int Main(string[] args){
  if(args.Length>0&&args[0]=="--child"){
   StartupInfo info;GetStartupInfo(out info);IntPtr id;int status=GetCurrentProcessExplicitAppUserModelID(out id);string explicitId=null;try{if(status==0)explicitId=Marshal.PtrToStringUni(id);}finally{if(id!=IntPtr.Zero)Marshal.FreeCoTaskMem(id);}
   File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{title=Marshal.PtrToStringUni(info.Title),flags=info.Flags,explicitId,profile=Environment.GetEnvironmentVariable("CODEX_ELECTRON_USER_DATA_PATH"),literal=args[2]}));Thread.Sleep(50);return 0;
  }
  try{
   string id="OpenAI.Codex_2p2nqsd0c76g0!App",profile=Path.Combine(Path.GetDirectoryName(args[0]),"中文 profile");
   string child=args[0]+".child.json",exe=System.Reflection.Assembly.GetExecutingAssembly().Location;
   int pid=CodexBackgroundShell.Identity.Launch(exe,"--child \""+child+"\" \"中文 argument\"",id,profile);
   using(var process=System.Diagnostics.Process.GetProcessById(pid)){var until=DateTime.UtcNow.AddSeconds(10);while(!File.Exists(child)&&DateTime.UtcNow<until&&!process.HasExited)Thread.Sleep(20);if(!File.Exists(child))throw new Exception("Identity child did not return, exit="+(process.HasExited?process.ExitCode.ToString():"pending"));}
   var result=new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(child));
   if(Convert.ToString(result["title"])!=id||(Convert.ToUInt32(result["flags"])&0x1000)==0||Convert.ToString(result["profile"])!=profile||Convert.ToString(result["literal"])!="中文 argument")throw new Exception("Native identity, arguments or profile environment was changed");
   File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{pass=true,canonicalStartupIdentity=true,unicodeArgumentsAndEnvironment=true,explicitId=result["explicitId"],realCodexStarted=false}));return 0;
  }catch(Exception e){File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{pass=false,error=e.ToString()}));return 1;}
 }
}
