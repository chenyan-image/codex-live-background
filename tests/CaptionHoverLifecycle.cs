using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class CaptionHoverLifecycleTest {
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Pump(){for(int i=0;i<8;i++){Application.DoEvents();Thread.Sleep(10);}}
    static Button Find(Control root,string name){foreach(Control child in root.Controls){var button=child as Button;if(button!=null&&button.AccessibleName==name)return button;var nested=Find(child,name);if(nested!=null)return nested;}return null;}
    static Color Pixel(Control control,string file){using(var image=new Bitmap(control.Width,control.Height)){control.DrawToBitmap(image,control.ClientRectangle);if(file!=null)image.Save(file);return image.GetPixel(3,3);}}
    static void Enter(Control button){button.GetType().GetMethod("OnMouseEnter",Private).Invoke(button,new object[]{EventArgs.Empty});}
    static void Leave(Control button){button.GetType().GetMethod("OnMouseLeave",Private).Invoke(button,new object[]{EventArgs.Empty});}
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    [STAThread] static int Main(string[] args){
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        var errors=new List<string>();Application.ThreadException+=delegate(object sender,System.Threading.ThreadExceptionEventArgs e){errors.Add(e.Exception.ToString());};
        try{
            using(var form=(Form)Activator.CreateInstance(typeof(Manager),Private,null,new object[]{false},null)){
                form.Show();Pump();typeof(Manager).GetMethod("InitializeTray",Private).Invoke(form,null);
                var trayMenu=(ContextMenuStrip)typeof(Manager).GetField("trayMenu",Private).GetValue(form);
                var close=Find(form,"关闭");var minimize=Find(form,"最小化");Require(close!=null&&minimize!=null,"Caption controls missing");
                Leave(close);var normal=Pixel(close,Path.Combine(Path.GetDirectoryName(args[0]),"close-normal.png"));
                for(int i=0;i<5;i++){
                    Enter(close);var hovered=Pixel(close,null);Require(hovered.R>150&&hovered.G<80,"Close hover no longer provides red feedback");
                    // Hide inside the same click callback as a real close click.
                    // Windows need not deliver MouseLeave while hiding its HWND.
                    close.PerformClick();Pump();Require(!form.Visible&&!form.ShowInTaskbar,"Close did not hide manager to tray");
                    ((ToolStripMenuItem)trayMenu.Items[0]).PerformClick();Pump();
                    var restored=Pixel(close,i==0?Path.Combine(Path.GetDirectoryName(args[0]),"close-restored.png"):null);
                    Require(restored.ToArgb()==normal.ToArgb(),"Close button retained red hover after restoring from tray: "+restored);
                    Enter(minimize);minimize.PerformClick();Pump();((ToolStripMenuItem)trayMenu.Items[0]).PerformClick();Pump();
                    Require(Pixel(minimize,null).ToArgb()==normal.ToArgb(),"Minimize button retained hover after tray restore");
                }
                Enter(close);Leave(close);Require(Pixel(close,null).ToArgb()==normal.ToArgb(),"MouseLeave failed to clear hover");
                Require(errors.Count==0,String.Join("\n",errors.ToArray()));
            }
            File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{pass=true,trayRestoreCycles=10,closeRedOnlyWhileHovered=true,closeAndMinimizeReset=true,unhandledExceptions=errors.Count,realServicesModified=false,resizeAndSliderRepeated=false}));return 0;
        }catch(Exception error){File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{pass=false,error=error.ToString()}));return 1;}
    }
}
