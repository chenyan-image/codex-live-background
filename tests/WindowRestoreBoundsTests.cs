using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Compiled only in this fixture. The production manager stays unchanged.
internal sealed partial class Manager {
    protected override bool ShowWithoutActivation {get{return true;}}
}
internal static class WindowRestoreBoundsTests {
    [StructLayout(LayoutKind.Sequential)]struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect bounds);
    [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out Rect bounds);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowStyle(IntPtr window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern IntPtr SetWindowStyle(IntPtr window,int index,IntPtr style);
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr value,IntPtr other);
    static int checks;
    // WinForms may rewrite ExStyle during minimize/handle recreation. Keep
    // these test-only windows nonactivating across those rewrites as well.
    sealed class NonactivatingFixture : NativeWindow,IDisposable {
        readonly Form owner;
        public NonactivatingFixture(Form form){owner=form;owner.HandleCreated+=Created;owner.HandleDestroyed+=Destroyed;Created(owner,EventArgs.Empty);}
        void Created(object sender,EventArgs e){if(Handle==IntPtr.Zero)AssignHandle(owner.Handle);SetWindowStyle(Handle,-20,new IntPtr(GetWindowStyle(Handle,-20).ToInt64()|0x08000000|0x80|0x20));}
        void Destroyed(object sender,EventArgs e){if(Handle!=IntPtr.Zero)ReleaseHandle();}
        protected override void WndProc(ref Message m){if(m.Msg==0x7C&&unchecked((int)m.WParam.ToInt64())==-20&&m.LParam!=IntPtr.Zero)Marshal.WriteInt32(m.LParam,4,Marshal.ReadInt32(m.LParam,4)|0x08000000|0x80|0x20);base.WndProc(ref m);}
        public void Dispose(){owner.HandleCreated-=Created;owner.HandleDestroyed-=Destroyed;Destroyed(owner,EventArgs.Empty);}
    }
    static void Need(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    static void Pump(){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<75){Application.DoEvents();Thread.Sleep(5);}}
    static object Check(Form form,string stage,Rectangle expected){
        Pump();Rect window=new Rect(),client=new Rect();
        Need(GetWindowRect(form.Handle,out window)&&GetClientRect(form.Handle,out client),"Native geometry unavailable");
        var actual=new Rectangle(window.Left,window.Top,window.Right-window.Left,window.Bottom-window.Top);
        Need(actual==expected,stage+": restored/maximized bounds drifted: "+actual+" / "+expected);
        Need(form.Bounds==actual&&form.ClientSize==new Size(client.Right-client.Left,client.Bottom-client.Top),stage+": managed and native geometry differ");
        Need(form.ClientSize==actual.Size,stage+": border consumes full-window client pixels");
        Need(form.Controls[0].Bounds==form.DisplayRectangle,stage+": client fill leaves uncovered area");
        Need((GetWindowStyle(form.Handle,-16).ToInt64()&0x40000)!=0,"Native resizing style was lost");
        Need(form.Opacity==0&&!form.ShowInTaskbar,"Fixture must stay transparent and out of the taskbar");
        Need(GetForegroundWindow()!=form.Handle,stage+": transparent fixture took foreground focus");
        return new{stage,x=actual.X,y=actual.Y,width=actual.Width,height=actual.Height};
    }
    [STAThread]static int Main(string[] args){try{
        SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        IntPtr foreground=GetForegroundWindow();var results=new List<object>();
        foreach(var screen in Screen.AllScreens){
            var work=screen.WorkingArea;
            using(var form=(Form)Activator.CreateInstance(typeof(Manager),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{false},null)){
                form.ShowInTaskbar=false;form.Opacity=0;form.StartPosition=FormStartPosition.Manual;
                form.ClientSize=new Size(Math.Min(1310,work.Width-100),Math.Min(816,work.Height-100));
                form.Location=new Point(work.Left+50,work.Top+50);
                SetWindowStyle(form.Handle,-20,new IntPtr(GetWindowStyle(form.Handle,-20).ToInt64()|0x08000000|0x80|0x20));
                var parameters=(CreateParams)typeof(Manager).GetProperty("CreateParams",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form,null);
                Need((parameters.Style&0x40000)==0,"Managed client-size calculations still include a native sizing frame");
                using(var noActivation=new NonactivatingFixture(form)){
                form.Show();Pump();Rectangle original=form.Bounds;var records=new List<object>{Check(form,"normal",original)};
                var toggle=typeof(Manager).GetMethod("ToggleMaximize",BindingFlags.Instance|BindingFlags.NonPublic);
                for(int cycle=1;cycle<=10;cycle++){
                    toggle.Invoke(form,null);records.Add(Check(form,"title-max-"+cycle,work));
                    toggle.Invoke(form,null);records.Add(Check(form,"title-restore-"+cycle,original));
                }
                for(int cycle=1;cycle<=3;cycle++){
                    SendMessage(form.Handle,0x112,(IntPtr)0xF030,IntPtr.Zero);records.Add(Check(form,"system-max-"+cycle,work));
                    SendMessage(form.Handle,0x112,(IntPtr)0xF120,IntPtr.Zero);records.Add(Check(form,"system-restore-"+cycle,original));
                }
                for(int cycle=1;cycle<=3;cycle++){
                    form.Hide();form.ShowInTaskbar=true;form.ShowInTaskbar=false;
                    SetWindowStyle(form.Handle,-20,new IntPtr(GetWindowStyle(form.Handle,-20).ToInt64()|0x08000000|0x80|0x20));
                    form.Show();records.Add(Check(form,"hidden-handle-restore-"+cycle,original));
                    toggle.Invoke(form,null);records.Add(Check(form,"hidden-restore-max-"+cycle,work));
                    form.Hide();form.Show();records.Add(Check(form,"hidden-max-show-"+cycle,work));
                    toggle.Invoke(form,null);records.Add(Check(form,"hidden-max-restore-"+cycle,original));
                }
                results.Add(new{screen=screen.DeviceName,records});form.Close();Pump();
                }
            }
        }
        bool focusUnchanged=foreground==GetForegroundWindow();
        File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{pass=true,checks,results,focusUnchanged,transparentFixtures=true,liveWindowsModified=false,liveSettingsChanged=false}));return 0;
    }catch(Exception error){File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{pass=false,checks,error=error.ToString()}));return 1;}}
}
