using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class WindowIconTests {
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr first,IntPtr second);
    [STAThread]public static int Main(string[] args){
        string folder=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,@"..\icons"));Directory.CreateDirectory(folder);File.Copy(args[1],Path.Combine(folder,"codex-official.ico"));
        // Exercise the shipped native icon setter on this test's own hidden window.
        // No Codex windows, startup entries or service processes are touched.
        var type=Assembly.LoadFrom(args[0]).GetType("DreamSkinPortHook",true);
        var setter=type.GetMethod("SetWindowIcons",BindingFlags.Static|BindingFlags.NonPublic);
        using(var form=new Form{ShowInTaskbar=false}){
            setter.Invoke(null,new object[]{form.Handle});
            IntPtr large=SendMessage(form.Handle,0x7f,(IntPtr)1,IntPtr.Zero),small=SendMessage(form.Handle,0x7f,IntPtr.Zero,IntPtr.Zero);
            if(large==IntPtr.Zero||small==IntPtr.Zero)throw new Exception("Window icon was not assigned");
            if(Icon.FromHandle(large).Width!=64||Icon.FromHandle(small).Width!=16)throw new Exception("Window icon sizes incorrect");
        }
        Console.WriteLine("{\"pass\":true,\"nativeWindowIcon\":true,\"large64\":true,\"small16\":true,\"target\":\"own-hidden-test-window\"}");return 0;
    }
}
