using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Web.Script.Serialization;
internal static class TypeFilterLifecycleTest {
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
 [STAThread]static int Main(string[] args){
  Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  var errors=new List<string>();Application.ThreadException+=delegate(object s,System.Threading.ThreadExceptionEventArgs e){errors.Add(e.Exception.ToString());};
  try{
   int clicks=0,changes=0;ContextMenuStrip owned=null;
   using(var host=new Form{ShowInTaskbar=false,Opacity=0,ClientSize=new Size(300,160)}){
    var filter=new TypeFilter{Location=new Point(10,10),Size=new Size(150,35)};filter.Items.AddRange(new object[]{"All","Animated","Static"});filter.SelectedIndexChanged+=delegate{changes++;};host.Controls.Add(filter);
    var field=typeof(TypeFilter).GetField("menu",BindingFlags.NonPublic|BindingFlags.Instance);owned=(ContextMenuStrip)field.GetValue(filter);owned.Opacity=0;host.Show();Application.DoEvents();
    for(int cycle=0;cycle<24;cycle++){
     filter.PerformClick();Application.DoEvents();ContextMenuStrip menu=owned;
     // Inspect only this fixture's popup; no desktop input or other app is used.
     if(menu==null)throw new Exception("Filter popup is not owned for its lifetime");
     int index=cycle%3;var item=menu.Items[index];var bounds=item.Bounds;int x=bounds.X+Math.Min(10,bounds.Width/2),y=bounds.Y+bounds.Height/2;var point=new IntPtr((y<<16)|(x&0xffff));
     SendMessage(menu.Handle,0x201,new IntPtr(1),point);SendMessage(menu.Handle,0x202,IntPtr.Zero,point);Application.DoEvents();clicks++;
     if(filter.SelectedIndex!=index)throw new Exception("Selected filter did not update");
     if(menu.IsDisposed)throw new Exception("Popup disposed during framework item-close handling");if(menu.Visible)menu.Close(ToolStripDropDownCloseReason.ItemClicked);Application.DoEvents();
     filter.PerformClick();Application.DoEvents();menu.Close(ToolStripDropDownCloseReason.Keyboard);Application.DoEvents();
     if(menu.IsDisposed||filter.IsDisposed)throw new Exception("Cancelled popup disposed its owner");
    }
    filter.PerformClick();Application.DoEvents();filter.Dispose();Application.DoEvents();if(!owned.IsDisposed)throw new Exception("Popup leaked after owner disposal");host.Close();
   }
   if(errors.Count>0)throw new Exception(String.Join("\n",errors.ToArray()));if(changes<20)throw new Exception("Selection events were lost");
   File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{pass=true,selectionClicks=clicks,keyboardDismissals=24,selectionChanges=changes,ownerDisposal=true,unhandledExceptions=errors.Count,resizeAndSliderRepeated=false}));return 0;
  }catch(Exception e){File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{pass=false,error=e.ToString()}));return 1;}
 }
}
