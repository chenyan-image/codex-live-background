using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
internal static class InstallPathProbe {
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CommandLineToArgvW(string text,out int count);
    [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr pointer);
    static int checks;
    static void Need(bool pass,string message){if(!pass)throw new Exception(message);checks++;}
    static object Invoke(Type type,string name,params object[] args){return type.GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Invoke(null,args);}
    static int Main(string[] args){try{
        var setup=Assembly.LoadFrom(args[0]).GetType("Setup",true);var manager=Assembly.LoadFrom(args[1]);var update=manager.GetType("SoftwareUpdate",true);var uninstall=Assembly.LoadFrom(args[2]).GetType("NativeUninstall",true);
        for(char letter='A';letter<='Z';letter++){
            string root=letter+":\\";
            Need(String.Equals((string)Invoke(setup,"InstallPath",letter+":"),root,StringComparison.OrdinalIgnoreCase),"Bare drive rejected: "+letter);
            Need((string)Invoke(update,"RootPath",root)==root,"Updater root became relative");
            Need((string)Invoke(uninstall,"RootPath",root)==root,"Uninstaller root became relative");
        }
        foreach(string path in new[]{@"E:\",@"C:\客户 软件 & 任意名称",@"D:\test's folder\背景 #1",@"E:\[方括号] %TEMP% !"}){
            Need((string)Invoke(setup,"InstallPath",path)==path,"Valid folder name changed");
            string quote=(string)Invoke(uninstall,"Quote",path);int count;IntPtr pointer=CommandLineToArgvW("probe.exe "+quote+" --relocated",out count);
            try{Need(count==3&&Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer,IntPtr.Size))==path,"Trailing slash/Unicode quote was split");}finally{LocalFree(pointer);}
        }
        Console.WriteLine("{\"pass\":true,\"checks\":"+checks+",\"allDriveLettersNormalized\":true,\"realInstallationModified\":false}");return 0;
    }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
