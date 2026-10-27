using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Let child panels yield the resize band to the top-level window. Otherwise a
// wide hit zone on the form still receives only its one-pixel uncovered edge.
internal sealed class WindowResizeBridge : IDisposable {
    readonly Form form;readonly Dictionary<Control,EdgeWindow> children=new Dictionary<Control,EdgeWindow>();bool disposed;
    public WindowResizeBridge(Form owner){form=owner;Bind(owner);form.Disposed+=delegate{Dispose();};}
    void Bind(Control control){if(disposed)return;if(control!=form&&!children.ContainsKey(control)){var edge=new EdgeWindow(form,control);children[control]=edge;control.Disposed+=delegate{edge.Dispose();children.Remove(control);};}control.ControlAdded+=Added;foreach(Control child in control.Controls)Bind(child);}
    void Added(object sender,ControlEventArgs e){Bind(e.Control);}
    public void Dispose(){if(disposed)return;disposed=true;foreach(var child in children.Values.ToArray())child.Dispose();children.Clear();}
    sealed class EdgeWindow : NativeWindow,IDisposable {
        readonly Form form;readonly Control control;
        public EdgeWindow(Form owner,Control child){form=owner;control=child;control.HandleCreated+=Created;control.HandleDestroyed+=Destroyed;if(control.IsHandleCreated)AssignHandle(control.Handle);}
        void Created(object sender,EventArgs e){if(Handle==IntPtr.Zero)AssignHandle(control.Handle);}
        void Destroyed(object sender,EventArgs e){if(Handle!=IntPtr.Zero)ReleaseHandle();}
        protected override void WndProc(ref Message message){if(message.Msg==0x84&&form.WindowState==FormWindowState.Normal&&!form.IsDisposed){var point=form.PointToClient(new Point(unchecked((short)(long)message.LParam),unchecked((short)((long)message.LParam>>16))));if(WindowFrame.ResizeHit(form,point)!=0){message.Result=(IntPtr)(-1);return;}}base.WndProc(ref message);}
        public void Dispose(){control.HandleCreated-=Created;control.HandleDestroyed-=Destroyed;if(Handle!=IntPtr.Zero)ReleaseHandle();}
    }
}
