using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;
internal static class WindowBackgroundTest {
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
 static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
 static void Pump(int milliseconds){var until=DateTime.UtcNow.AddMilliseconds(milliseconds);while(DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}}
 static void Require(bool value,string error){if(!value)throw new Exception(error);}
 static Button ButtonIn(Control root,string name){foreach(Control child in root.Controls){var b=child as Button;if(b!=null&&b.AccessibleName==name)return b;var nested=ButtonIn(child,name);if(nested!=null)return nested;}return null;}
 [STAThread]static int Main(string[] args){
  Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  var errors=new List<string>();Application.ThreadException+=delegate(object sender,System.Threading.ThreadExceptionEventArgs e){errors.Add(e.Exception.ToString());};
  try{
   var type=typeof(Manager);int restoreCycles=0,wholePaints=0;
   using(var form=(Form)Activator.CreateInstance(type,Private,null,new object[]{false},null)){
    form.Show();Pump(150);var root=form.Controls[0];var content=(TableLayoutPanel)((TableLayoutPanel)root).GetControlFromPosition(0,1);var canvas=content.GetControlFromPosition(0,0);int paints=0;Rectangle coverage=Rectangle.Empty;
    canvas.Paint+=delegate(object sender,PaintEventArgs e){paints++;coverage=Rectangle.Union(coverage,e.ClipRectangle);};
    var preview=(WallpaperPreview)type.GetField("preview",Private).GetValue(form);preview.SetMedia(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview.gif"));Pump(250);Require(preview.HasPicture,"Fixture GIF did not load");
    for(int i=0;i<6;i++){
     paints=0;coverage=Rectangle.Empty;form.WindowState=FormWindowState.Minimized;Pump(60);form.WindowState=FormWindowState.Normal;form.Show();Pump(150);
     Require(paints>0,"Restore reused the old surface without repainting the content panel");
     Require(coverage.Contains(1,1)&&coverage.Contains(canvas.Width-2,canvas.Height-2),"Restore did not repaint the complete content panel: "+coverage+" / "+canvas.ClientRectangle);
     Require(root.Right==form.ClientSize.Width-form.Padding.Right&&root.Bottom==form.ClientSize.Height-form.Padding.Bottom,"Window content bounds are clipped");
     Require(preview.HasPicture,"Restore lost the selected preview");restoreCycles++;wholePaints+=paints;
    }
    type.GetMethod("InitializeTray",Private).Invoke(form,null);var tray=(NotifyIcon)type.GetField("tray",Private).GetValue(form);var menu=(ContextMenuStrip)type.GetField("trayMenu",Private).GetValue(form);
    Require(tray.Visible&&tray.Icon!=null,"Tray icon was not registered");
    Button minimize=ButtonIn(form,"最小化"),close=ButtonIn(form,"关闭");Require(minimize!=null&&close!=null,"Caption controls missing");
    minimize.PerformClick();Pump(100);Require(!form.Visible&&!form.ShowInTaskbar&&!form.IsDisposed&&tray.Visible,"Minimize did not move manager to tray");
    paints=0;((ToolStripMenuItem)menu.Items[0]).PerformClick();Pump(150);Require(form.Visible&&form.ShowInTaskbar&&paints>0,"Tray Open did not restore and repaint manager");
    close.PerformClick();Pump(80);Require(!form.Visible&&!form.ShowInTaskbar&&!form.IsDisposed,"Close did not keep manager in tray");
    typeof(NotifyIcon).GetMethod("OnDoubleClick",Private).Invoke(tray,new object[]{EventArgs.Empty});Pump(150);Require(form.Visible&&form.ShowInTaskbar,"Tray double-click did not restore manager");
    form.WindowState=FormWindowState.Minimized;Pump(100);Require(!form.Visible&&!form.ShowInTaskbar,"System minimize did not hide taskbar button");
    type.GetMethod("RestoreWindow",Private).Invoke(form,null);Pump(150);Require(form.Visible&&form.WindowState==FormWindowState.Normal&&preview.HasPicture,"Existing-instance activation failed: visible="+form.Visible+", taskbar="+form.ShowInTaskbar+", state="+form.WindowState+", saved="+type.GetField("stateBeforeHide",Private).GetValue(form)+", picture="+preview.HasPicture);
    menu.Opacity=0;menu.Show(form,new Point(20,40));Pump(30);var area=menu.Items[2].Bounds;var point=new IntPtr(((area.Y+area.Height/2)<<16)|(area.X+10));SendMessage(menu.Handle,0x201,new IntPtr(1),point);SendMessage(menu.Handle,0x202,IntPtr.Zero,point);Pump(100);Require(form.IsDisposed&&!tray.Visible&&menu.IsDisposed,"Exit did not release manager and tray icon");
   }
   Require(errors.Count==0,String.Join("\n",errors.ToArray()));File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{pass=true,restoreCycles,wholeContentPaints=wholePaints,animatedPreviewRetained=true,minimizeAndCloseToTray=true,taskbarHidden=true,menuAndDoubleClickRestore=true,existingInstanceRestore=true,explicitExitDisposesTray=true,unhandledExceptions=errors.Count,realServicesModified=false,oldResizeAndSliderTestsRepeated=false}));return 0;
  }catch(Exception e){File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{pass=false,error=e.ToString()}));return 1;}
 }
}
