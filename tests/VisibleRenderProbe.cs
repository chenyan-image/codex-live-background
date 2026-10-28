using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
internal static class VisibleRenderProbe {
    [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
    [STAThread]static int Main(){
        SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        var primary=Screen.PrimaryScreen;var left=Screen.AllScreens.Where(s=>s.Bounds.Right<=primary.Bounds.Left).OrderByDescending(s=>s.Bounds.Right).FirstOrDefault();
        if(left==null)return 2;
        using(var form=(Form)Activator.CreateInstance(typeof(Manager),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{false},null)){
            form.Text="CodexHub · 窗口修复检查";form.StartPosition=FormStartPosition.Manual;
            form.MinimumSize=new Size(Math.Min(1090,left.WorkingArea.Width-64),Math.Min(720,left.WorkingArea.Height-64));
            form.ClientSize=new Size(Math.Min(1310,left.WorkingArea.Width-64),Math.Min(816,left.WorkingArea.Height-64));
            form.Location=new Point(left.WorkingArea.Left+32,left.WorkingArea.Top+32);
            Application.Run(form);
        }
        return 0;
    }
}
