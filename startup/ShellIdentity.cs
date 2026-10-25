using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;

namespace CodexBackgroundShell {
 public static class Identity {
  [StructLayout(LayoutKind.Sequential)]struct PropertyKey {public Guid Format;public uint Id;public PropertyKey(uint id){Format=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");Id=id;}}
  [StructLayout(LayoutKind.Explicit,Size=24)]struct Variant {
   [FieldOffset(0)]public ushort Type;[FieldOffset(8)]public IntPtr Pointer;
   public static Variant Text(string value){return new Variant{Type=31,Pointer=Marshal.StringToCoTaskMemUni(value)};}
  }
  [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface Store {
   [PreserveSig]int GetCount(out uint count);[PreserveSig]int GetAt(uint index,out PropertyKey key);
   [PreserveSig]int GetValue(ref PropertyKey key,out Variant value);[PreserveSig]int SetValue(ref PropertyKey key,ref Variant value);[PreserveSig]int Commit();
  }
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHGetPropertyStoreFromParsingName(string path,IntPtr context,uint flags,ref Guid iid,out Store store);
  [DllImport("shell32.dll")]static extern int SHGetPropertyStoreForWindow(IntPtr window,ref Guid iid,out Store store);
  [DllImport("ole32.dll")]static extern int PropVariantClear(ref Variant value);
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern void SHChangeNotify(uint action,uint flags,string first,IntPtr second);
  static Guid StoreId {get{return new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");}}
  public static bool Supported(string id){return id!=null&&Regex.IsMatch(id,@"\AOpenAI\.Codex(?:Beta)?_2p2nqsd0c76g0!App\z");}
  static string Read(Store store,uint id){var key=new PropertyKey(id);Variant value;Marshal.ThrowExceptionForHR(store.GetValue(ref key,out value));try{return value.Type==31?Marshal.PtrToStringUni(value.Pointer):null;}finally{PropVariantClear(ref value);}}
  static void Write(Store store,uint id,string text){var key=new PropertyKey(id);var value=text==null?new Variant():Variant.Text(text);try{Marshal.ThrowExceptionForHR(store.SetValue(ref key,ref value));}finally{PropVariantClear(ref value);}}
  public static string ReadShortcut(string path){var iid=StoreId;Store store;Marshal.ThrowExceptionForHR(SHGetPropertyStoreFromParsingName(Path.GetFullPath(path),IntPtr.Zero,0,ref iid,out store));try{return Read(store,5);}finally{Marshal.ReleaseComObject(store);}}
  public static void SetShortcut(string path,string id){
   if(!Supported(id)||!String.Equals(Path.GetExtension(path),".lnk",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Unsupported shortcut identity");
   var iid=StoreId;Store store;Marshal.ThrowExceptionForHR(SHGetPropertyStoreFromParsingName(Path.GetFullPath(path),IntPtr.Zero,2,ref iid,out store));try{Write(store,5,id);Marshal.ThrowExceptionForHR(store.Commit());}finally{Marshal.ReleaseComObject(store);}
  }
  public static void NotifyShortcut(string path){SHChangeNotify(0x2000,0x5,Path.GetFullPath(path),IntPtr.Zero);}
  public static string ReadWindow(IntPtr window,uint property){var iid=StoreId;Store store;Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window,ref iid,out store));try{return Read(store,property);}finally{Marshal.ReleaseComObject(store);}}
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct StartupInfo {
   public int Size;public string Reserved,Desktop,Title;public uint X,Y,Width,Height,Columns,Rows,Fill,Flags;public ushort Show,ReservedBytes;public IntPtr ReservedPointer,Input,Output,Error;
  }
  [StructLayout(LayoutKind.Sequential)]struct ProcessInfo {public IntPtr Process,Thread;public uint ProcessId,ThreadId;}
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateProcess(string application,StringBuilder command,IntPtr processSecurity,IntPtr threadSecurity,bool inherit,uint flags,IntPtr environment,string directory,ref StartupInfo startup,out ProcessInfo process);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
  static string Quote(string value){var b=new StringBuilder("\"");int slashes=0;foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='"')b.Append('\\',slashes*2+1).Append(c);else b.Append('\\',slashes).Append(c);slashes=0;}return b.Append('\\',slashes*2).Append('"').ToString();}
  public static int Launch(string executable,string arguments,string id,string profile){
   if(!Supported(id)||!File.Exists(executable))throw new ArgumentException("Unsupported application launch");
   var environment=new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(DictionaryEntry pair in Environment.GetEnvironmentVariables())environment[(string)pair.Key]=(string)pair.Value;
   if(profile!=null)environment["CODEX_ELECTRON_USER_DATA_PATH"]=profile;var block=new StringBuilder();foreach(var pair in environment)block.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');block.Append('\0');
   IntPtr memory=Marshal.StringToHGlobalUni(block.ToString());var startup=new StartupInfo{Size=Marshal.SizeOf(typeof(StartupInfo)),Title=id,Flags=0x1000};ProcessInfo created;
   try{if(!CreateProcess(executable,new StringBuilder(Quote(executable)+" "+arguments),IntPtr.Zero,IntPtr.Zero,false,0x400,memory,Path.GetDirectoryName(executable),ref startup,out created))throw new Win32Exception();try{return (int)created.ProcessId;}finally{CloseHandle(created.Thread);CloseHandle(created.Process);}}finally{Marshal.FreeHGlobal(memory);}
  }
 }
}
