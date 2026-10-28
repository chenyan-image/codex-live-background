using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;
internal static class WindowFillTests {
    [StructLayout(LayoutKind.Sequential)]struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out Rect bounds);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    static int checks;
    static void Need(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    static object Check(Form form,string stage){
        Application.DoEvents();form.PerformLayout();Rect native;Need(GetClientRect(form.Handle,out native),"Native client bounds unavailable");
        var actual=new Size(native.Right-native.Left,native.Bottom-native.Top);
        Need(form.ClientSize==actual,stage+": native="+actual+", managed="+form.ClientSize);
        var shell=form.Controls[0];Need(shell.Bounds==form.DisplayRectangle,stage+": Dock.Fill no longer covers the complete client area: "+shell.Bounds+" / "+form.DisplayRectangle);
        Need(!form.Visible,stage+": geometry check unexpectedly displayed a window");
        return new{stage,actualWidth=actual.Width,actualHeight=actual.Height,fillWidth=shell.Width,fillHeight=shell.Height};
    }
    [STAThread]static int Main(string[] args){try{
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        var assembly=Assembly.LoadFile(Path.GetFullPath(args[0]));var type=assembly.GetType("Manager",true);var results=new List<object>();
        using(var form=(Form)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{false},null)){
            form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-30000,-30000);var handle=form.Handle;
            results.Add(Check(form,"created"));
            foreach(var size in new[]{new Size(1310,816),new Size(1600,1000),new Size(2560,1392),new Size(1920,1080),new Size(900,600),new Size(1310,816)}){
                form.ClientSize=size;results.Add(Check(form,"client-size-"+size.Width+"x"+size.Height));
            }
            foreach(float scale in new[]{1.25F,1.5F,2F}){form.Scale(new SizeF(scale,scale));results.Add(Check(form,"scale-"+scale));}
            form.Size=new Size(1500,1000);results.Add(Check(form,"window-size"));
            Need(SetWindowPos(handle,IntPtr.Zero,0,0,1800,1100,0x0016),"Native resize failed");results.Add(Check(form,"native-size"));
            form.WindowState=FormWindowState.Maximized;results.Add(Check(form,"hidden-maximized"));form.WindowState=FormWindowState.Normal;results.Add(Check(form,"hidden-restored"));
        }
        File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{pass=true,checks,results,liveWindowsModified=false,liveSettingsChanged=false}));return 0;
    }catch(Exception error){File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{pass=false,checks,error=error.ToString()}));return 1;}}
}
