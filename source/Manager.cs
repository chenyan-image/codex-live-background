using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Windows.Forms;

[assembly: AssemblyTitle("Codex 动态背景")]
[assembly: AssemblyProduct("Codex 动态背景")]
[assembly: AssemblyCompany("codex-live-background contributors")]
[assembly: AssemblyVersion("1.5.4.4")]
[assembly: AssemblyFileVersion("1.5.4.4")]

internal sealed class BufferedTable : TableLayoutPanel {
    public BufferedTable(){DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw|ControlStyles.AllPaintingInWmPaint,true);}
}
internal sealed class BufferedFlow : FlowLayoutPanel {
    public BufferedFlow(){DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw|ControlStyles.AllPaintingInWmPaint,true);}
}
internal static class WindowFrame {
    public static void Update(Form form){
        if(form.IsDisposed||form.Disposing||!form.IsHandleCreated||form.Width<2||form.Height<2||form.WindowState==FormWindowState.Minimized)return;
        Region previous=form.Region;
        if(form.WindowState==FormWindowState.Maximized){if(previous!=null){form.Region=null;previous.Dispose();}return;}
        using(var g=form.CreateGraphics())using(var path=IconAssets.Round(new RectangleF(0,0,form.Width,form.Height),8*g.DpiX/96F))form.Region=new Region(path);
        if(previous!=null)previous.Dispose();
    }
    public static void PaintBorder(Form form,Graphics graphics,Color color){
        using(var pen=new Pen(color)){
            if(form.WindowState==FormWindowState.Maximized){graphics.DrawRectangle(pen,0,0,form.Width-1,form.Height-1);return;}
            graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=IconAssets.Round(new RectangleF(.5F,.5F,form.Width-1,form.Height-1),8*graphics.DpiX/96F))graphics.DrawPath(pen,path);
        }
    }
}
internal enum WindowCaptionKind { Minimize,Maximize,Close }
internal sealed class WindowCaptionButton : Button {
    readonly WindowCaptionKind kind;bool hover,pressed,restore;
    public bool RestoreGlyph {get{return restore;}set{if(restore!=value){restore=value;Invalidate();}}}
    public WindowCaptionButton(WindowCaptionKind value){
        kind=value;Size=new Size(44,32);Margin=Padding.Empty;Padding=Padding.Empty;TabStop=false;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;
        AccessibleName=value==WindowCaptionKind.Minimize?"最小化":value==WindowCaptionKind.Maximize?"最大化或还原":"关闭";
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
    }
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left){pressed=true;Invalidate();}base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;base.OnMouseUp(e);if(!IsDisposed)Invalidate();}
    protected override void OnPaint(PaintEventArgs e){
        Color background=Parent==null?BackColor:Parent.BackColor;
        if(hover)background=kind==WindowCaptionKind.Close?(pressed?Color.FromArgb(164,32,24):Color.FromArgb(196,43,28)):(pressed?Color.FromArgb(45,50,54):Color.FromArgb(35,40,44));
        var g=e.Graphics;g.Clear(background);float scale=g.DpiX/96F,size=10*scale,x=(Width-size)/2F,y=(Height-size)/2F;
        using(var pen=new Pen(hover?Color.FromArgb(245,247,248):Color.FromArgb(176,185,190),scale)){
            g.SmoothingMode=SmoothingMode.None;g.PixelOffsetMode=PixelOffsetMode.Half;
            if(kind==WindowCaptionKind.Minimize)g.DrawLine(pen,x,y+size/2,x+size,y+size/2);
            else if(kind==WindowCaptionKind.Maximize){
                if(restore){g.DrawRectangle(pen,x+3*scale,y,7*scale,7*scale);using(var brush=new SolidBrush(background))g.FillRectangle(brush,x,y+3*scale,7*scale,7*scale);g.DrawRectangle(pen,x,y+3*scale,7*scale,7*scale);}
                else g.DrawRectangle(pen,x,y,9*scale,9*scale);
            }else{g.SmoothingMode=SmoothingMode.AntiAlias;g.DrawLine(pen,x,y,x+9*scale,y+9*scale);g.DrawLine(pen,x+9*scale,y,x,y+9*scale);}
        }
    }
}
internal sealed class Surface : Panel {
    public Surface(){DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw|ControlStyles.AllPaintingInWmPaint,true);BackColor=Color.FromArgb(23,31,35);Padding=new Padding(20);}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(BackColor);base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var p=IconAssets.Round(new RectangleF(.5F,.5F,Width-1,Height-1),14))using(var pen=new Pen(Color.FromArgb(47,59,62)))e.Graphics.DrawPath(pen,p);}
}
internal class ActionButton : Button {
    bool hover; public bool Primary; public bool Destructive;
    public ActionButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;Size=new Size(170,44);Margin=new Padding(0,0,12,0);}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.Clear(Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Color fill=Primary?Color.FromArgb(135,235,194):hover?Color.FromArgb(45,59,62):Color.FromArgb(30,40,44);
        Color text=Primary?Color.FromArgb(12,43,35):Destructive?Color.FromArgb(242,163,158):Color.FromArgb(222,232,232);
        if(!Enabled){fill=Color.FromArgb(35,44,47);text=Color.FromArgb(105,123,125);}
        using(var p=IconAssets.Round(new RectangleF(1,1,Width-2,Height-2),10))using(var brush=new SolidBrush(fill))using(var pen=new Pen(Primary?fill:Color.FromArgb(62,78,80))){e.Graphics.FillPath(brush,p);e.Graphics.DrawPath(pen,p);}
        TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        if(Focused&&ShowFocusCues)using(var ring=IconAssets.Round(new RectangleF(5,5,Width-10,Height-10),6))using(var pen=new Pen(Primary?Color.FromArgb(31,111,86):Color.FromArgb(135,235,194)))e.Graphics.DrawPath(pen,ring);
    }
}
internal sealed class OpacitySlider : Control {
    int minimum=10,maximum=100,current=72;bool dragging;
    public event EventHandler ValueChanged;
    public int Minimum {get{return minimum;}set{minimum=value;if(maximum<=minimum)maximum=minimum+1;Value=current;Invalidate();}}
    public int Maximum {get{return maximum;}set{maximum=Math.Max(minimum+1,value);Value=current;Invalidate();}}
    public int Value {get{return current;}set{int next=Math.Max(minimum,Math.Min(maximum,value));if(next==current)return;current=next;Invalidate();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);AccessibilityNotifyClients(AccessibleEvents.ValueChange,-1);}}
    public OpacitySlider(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable|ControlStyles.Opaque,true);TabStop=true;AccessibleRole=AccessibleRole.Slider;AccessibleName="聊天框背景不透明度";Cursor=Cursors.Hand;}
    void SetFromX(int x){Value=Minimum+(int)Math.Round((x-12)*((Maximum-Minimum)/(double)Math.Max(1,Width-24)));}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;Focus();dragging=true;Capture=true;SetFromX(e.X);}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging)SetFromX(e.X);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left){dragging=false;Capture=false;}}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)dragging=false;}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override bool IsInputKey(Keys key){return key==Keys.Left||key==Keys.Right||key==Keys.Up||key==Keys.Down||key==Keys.Home||key==Keys.End||key==Keys.PageUp||key==Keys.PageDown||base.IsInputKey(key);}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);switch(e.KeyCode){case Keys.Left:case Keys.Down:Value--;break;case Keys.Right:case Keys.Up:Value++;break;case Keys.Home:Value=Minimum;break;case Keys.End:Value=Maximum;break;case Keys.PageUp:Value+=10;break;case Keys.PageDown:Value-=10;break;default:return;}e.Handled=true;e.SuppressKeyPress=true;}
    protected override AccessibleObject CreateAccessibilityInstance(){return new SliderAccess(this);}
    sealed class SliderAccess : ControlAccessibleObject {
        readonly OpacitySlider owner;public SliderAccess(OpacitySlider control):base(control){owner=control;}
        public override string Value {get{return owner.Value+"%";}set{int next;if(int.TryParse(value.TrimEnd('%'),out next))owner.Value=next;}}
        public override string DefaultAction {get{return "调整聊天框背景不透明度";}}
        public override void DoDefaultAction(){owner.Focus();}
    }
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;float x=12+(Width-24)*(Value-Minimum)/(float)(Maximum-Minimum),y=Height/2F;
        using(var track=new Pen(Color.FromArgb(55,77,79),4)){track.StartCap=track.EndCap=LineCap.Round;g.DrawLine(track,12,y,Width-12,y);}
        using(var filled=new Pen(Color.FromArgb(116,217,179),4)){filled.StartCap=filled.EndCap=LineCap.Round;g.DrawLine(filled,12,y,x,y);}
        using(var knob=new SolidBrush(Color.FromArgb(208,255,234)))g.FillEllipse(knob,x-6,y-6,12,12);
        if(Focused&&ShowFocusCues)using(var ring=new Pen(Color.FromArgb(135,235,194)))g.DrawEllipse(ring,x-10,y-10,20,20);
    }
}
internal sealed class WallpaperPreview : Control {
    Image picture;MemoryStream imageBytes;readonly EventHandler animation;int request;
    public bool Animate=true;
    public bool HasPicture {get{return picture!=null;}}
    public WallpaperPreview(){DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw|ControlStyles.AllPaintingInWmPaint,true);animation=delegate{if(Visible&&IsHandleCreated&&!IsDisposed)try{BeginInvoke((MethodInvoker)delegate{if(Visible)Invalidate();});}catch(InvalidOperationException){}};}
    void ClearImage(){if(picture!=null){ImageAnimator.StopAnimate(picture,animation);picture.Dispose();picture=null;}if(imageBytes!=null){imageBytes.Dispose();imageBytes=null;}}
    public void SetMedia(string file){
        int generation=Interlocked.Increment(ref request);ClearImage();Invalidate();
        if(String.IsNullOrEmpty(file)||new[]{".mp4",".webm"}.ContainsExtension(file))return;
        IntPtr ready=Handle;
        // Decode and resize off the UI thread so a large image cannot block Close/resize.
        ThreadPool.QueueUserWorkItem(delegate{
            if(IsDisposed||generation!=request)return;
            Image loaded=null;MemoryStream bytes=null;
            try{if(File.Exists(file)){
                if(Path.GetExtension(file).Equals(".gif",StringComparison.OrdinalIgnoreCase)&&new FileInfo(file).Length<=16*1024*1024){bytes=new MemoryStream(File.ReadAllBytes(file));loaded=Image.FromStream(bytes);if(loaded.Width>1024||loaded.Height>1024){loaded.Dispose();loaded=null;bytes.Dispose();bytes=null;}}
                else using(var source=Image.FromFile(file)){float ratio=Math.Min(512F/source.Width,288F/source.Height);loaded=new Bitmap(Math.Max(1,(int)(source.Width*ratio)),Math.Max(1,(int)(source.Height*ratio)));using(var g=Graphics.FromImage(loaded))g.DrawImage(source,0,0,loaded.Width,loaded.Height);}
            }}catch{if(loaded!=null)loaded.Dispose();loaded=null;if(bytes!=null)bytes.Dispose();bytes=null;}
            Image result=loaded;MemoryStream buffer=bytes;bool queued=false;
            try{if(!IsDisposed&&IsHandleCreated){BeginInvoke((MethodInvoker)delegate{
                if(IsDisposed||generation!=request){if(result!=null)result.Dispose();if(buffer!=null)buffer.Dispose();return;}
                ClearImage();picture=result;imageBytes=buffer;if(Animate&&picture!=null&&ImageAnimator.CanAnimate(picture))ImageAnimator.Animate(picture,animation);Invalidate();
            });queued=true;}}catch(InvalidOperationException){}
            if(!queued){if(result!=null)result.Dispose();if(buffer!=null)buffer.Dispose();}
        });
    }
    protected override void Dispose(bool disposing){if(disposing){Interlocked.Increment(ref request);ClearImage();}base.Dispose(disposing);}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var clip=IconAssets.Round(new RectangleF(0,0,Width-1,Height-1),12)){
            g.SetClip(clip);
            if(picture!=null){ImageAnimator.UpdateFrames(picture);float scale=Math.Max(Width/(float)picture.Width,Height/(float)picture.Height);float w=picture.Width*scale,h=picture.Height*scale;g.DrawImage(picture,(Width-w)/2,(Height-h)/2,w,h);}
            else{
                using(var b=new LinearGradientBrush(ClientRectangle,Color.FromArgb(45,89,91),Color.FromArgb(15,36,44),55F))g.FillRectangle(b,ClientRectangle);
                using(var b=new SolidBrush(Color.FromArgb(124,210,179)))g.FillEllipse(b,Width*.65F,Height*.17F,30,30);
                using(var p=new GraphicsPath())using(var b=new SolidBrush(Color.FromArgb(33,88,84))){p.AddBezier(0,Height*.8F,Width*.26F,Height*.15F,Width*.5F,Height*.9F,Width,Height*.3F);p.AddLine(Width,Height*.3F,Width,Height);p.AddLine(Width,Height,0,Height);p.CloseFigure();g.FillPath(b,p);}
                using(var p=new GraphicsPath())using(var b=new SolidBrush(Color.FromArgb(17,56,58))){p.AddBezier(0,Height*.65F,Width*.4F,Height*1.2F,Width*.5F,Height*.3F,Width,Height*.75F);p.AddLine(Width,Height*.75F,Width,Height);p.AddLine(Width,Height,0,Height);p.CloseFigure();g.FillPath(b,p);}
                using(var f=new Font("Segoe UI",8F,FontStyle.Bold))using(var b=new SolidBrush(Color.FromArgb(163,215,198)))g.DrawString("LOCAL WALLPAPER",f,b,14,Height-27);
            }
            g.ResetClip();
        }
    }
}
internal static class Extensions {public static bool ContainsExtension(this string[] extensions,string file){return Array.IndexOf(extensions,Path.GetExtension(file).ToLowerInvariant())>=0;}}

internal sealed partial class Manager : Form {
    readonly TextBox media=new TextBox();
    readonly TextBox log=new TextBox();
    readonly OpacitySlider opacity=new OpacitySlider();
    readonly Label percentage=new Label(),status=new Label(),wallpaperName=new Label();
    readonly FlowLayoutPanel commands=new BufferedFlow();
    readonly ProgressBar progress=new ProgressBar();
    readonly WallpaperPreview preview=new WallpaperPreview();
    bool busy,closeRequested;volatile bool cancelStatus;string runningAction;int operationGeneration;
    NotifyIcon tray;ContextMenuStrip trayMenu;Icon trayImage;
    bool exitRequested,repaintQueued,nativeWasMinimized,restoringWindow;
    readonly bool liveStatusEnabled;
    FormWindowState stateBeforeHide=FormWindowState.Normal;
    readonly object statusLock=new object();Process statusProcess;
    readonly string root=AppDomain.CurrentDomain.BaseDirectory;
    static readonly Color Muted=Color.FromArgb(146,166,169),Ink=Color.FromArgb(231,240,238),Mint=Color.FromArgb(135,235,194);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageW")] static extern IntPtr SetCueText(IntPtr hwnd,int msg,IntPtr wParam,string text);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr window,IntPtr update,IntPtr region,uint flags);
    Label Caption(string text,float size,Color color){return new Label{Text=text,Font=new Font("Microsoft YaHei UI",size),ForeColor=color,AutoSize=false,Dock=DockStyle.Fill,Margin=Padding.Empty,TextAlign=ContentAlignment.MiddleLeft};}
    Manager(bool checkStatus){ liveStatusEnabled=checkStatus;BuildGallery(checkStatus);if(checkStatus)InitializeTray(); }
    protected override CreateParams CreateParams {get{var value=base.CreateParams;value.ExStyle|=0x02000000;return value;}}
    void Ui(MethodInvoker work){if(IsDisposed||!IsHandleCreated)return;try{BeginInvoke((MethodInvoker)delegate{if(!IsDisposed)work();});}catch(InvalidOperationException){}}
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);try{int dark=1;DwmSetWindowAttribute(Handle,20,ref dark,4);}catch{}WindowFrame.Update(this);}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(Visible&&!restoringWindow&&WindowState!=FormWindowState.Minimized)stateBeforeHide=WindowState;WindowFrame.Update(this);}
    protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(Visible)QueueCompleteRedraw();}
    protected override void OnHandleDestroyed(EventArgs e){repaintQueued=false;base.OnHandleDestroyed(e);}
    void QueueCompleteRedraw(){
        if(repaintQueued||IsDisposed||Disposing||!IsHandleCreated||!Visible||WindowState==FormWindowState.Minimized)return;
        repaintQueued=true;
        try{BeginInvoke((MethodInvoker)delegate{
            repaintQueued=false;if(IsDisposed||Disposing||!IsHandleCreated||!Visible||WindowState==FormWindowState.Minimized)return;
            PerformLayout();if(Controls.Count>0)Controls[0].PerformLayout();WindowFrame.Update(this);
            // Invalidate the complete composited surface after restore, including
            // every child HWND; repainting just the preview leaves a stale buffer.
            Invalidate(true);RedrawWindow(Handle,IntPtr.Zero,IntPtr.Zero,0x0585);
        });}catch(InvalidOperationException){repaintQueued=false;}
    }
    void InitializeTray(){
        if(tray!=null)return;
        trayMenu=new ContextMenuStrip{BackColor=Color.FromArgb(27,32,37),ForeColor=Ink,ShowImageMargin=false};
        trayMenu.Items.Add("打开管理器",null,delegate{RestoreWindow();});trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出管理器",null,delegate{if(!IsDisposed&&!Disposing)BeginInvoke((MethodInvoker)delegate{exitRequested=true;Close();});});
        trayImage=(Icon)Icon.Clone();tray=new NotifyIcon{Icon=trayImage,Text="Codex 动态背景",ContextMenuStrip=trayMenu,Visible=true};
        tray.DoubleClick+=delegate{RestoreWindow();};
    }
    void HideToTray(){
        if(tray==null){WindowState=FormWindowState.Minimized;return;}
        if(WindowState!=FormWindowState.Minimized)stateBeforeHide=WindowState;
        connectionTimer.Stop();CancelPreview();Hide();ShowInTaskbar=false;
    }
    void RestoreWindow(){
        if(IsDisposed||Disposing)return;
        var target=stateBeforeHide==FormWindowState.Minimized?FormWindowState.Normal:stateBeforeHide;
        closeRequested=exitRequested=false;restoringWindow=true;
        try{ShowInTaskbar=true;Show();WindowState=target;Activate();SetForegroundWindow(Handle);}finally{restoringWindow=false;}
        QueueCompleteRedraw();
        if(tray!=null&&liveStatusEnabled){connectionTimer.Start();if(!busy&&!checkingStatus)Run("Connection");}
    }
    protected override void Dispose(bool disposing){
        if(disposing){if(tray!=null){tray.Visible=false;tray.Dispose();tray=null;}if(trayMenu!=null){trayMenu.Dispose();trayMenu=null;}if(trayImage!=null){trayImage.Dispose();trayImage=null;}}
        base.Dispose(disposing);
    }
    void ToggleMaximize(){MaximizedBounds=Screen.FromHandle(Handle).WorkingArea;WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
    void AddTitleBar(){
        var bar=new Panel{Dock=DockStyle.Top,Height=32,BackColor=Color.FromArgb(12,14,17)};
        var controls=new BufferedFlow{Dock=DockStyle.Right,Width=132,FlowDirection=FlowDirection.LeftToRight,WrapContents=false,Margin=Padding.Empty,Padding=Padding.Empty};
        foreach(WindowCaptionKind kind in new[]{WindowCaptionKind.Minimize,WindowCaptionKind.Maximize,WindowCaptionKind.Close}){
            var b=new WindowCaptionButton(kind);
            if(kind==WindowCaptionKind.Minimize)b.Click+=delegate{HideToTray();};else if(kind==WindowCaptionKind.Maximize){b.Click+=delegate{ToggleMaximize();};SizeChanged+=delegate{b.RestoreGlyph=WindowState==FormWindowState.Maximized;};}else b.Click+=delegate{Close();};controls.Controls.Add(b);
        }
        var title=new Label{Text="Codex 动态背景",Font=new Font("Microsoft YaHei UI",8.5F),ForeColor=Muted,TextAlign=ContentAlignment.MiddleLeft,Dock=DockStyle.Fill,Padding=new Padding(15,0,0,0)};
        MouseEventHandler drag=delegate(object sender,MouseEventArgs e){if(e.Button!=MouseButtons.Left)return;if(e.Clicks==2){ToggleMaximize();return;}ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);};title.MouseDown+=drag;bar.MouseDown+=drag;
        bar.Controls.Add(title);bar.Controls.Add(controls);
        var content=Controls[0];Controls.Remove(content);
        var shell=new BufferedTable{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Margin=Padding.Empty,Padding=Padding.Empty};shell.RowStyles.Add(new RowStyle(SizeType.Absolute,32));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        bar.Dock=DockStyle.Fill;bar.Margin=Padding.Empty;shell.Controls.Add(bar,0,0);shell.Controls.Add(content,0,1);Controls.Add(shell);
        Shown+=delegate{ActiveControl=importButton;};
    }
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);WindowFrame.PaintBorder(this,e.Graphics,Color.FromArgb(49,65,69));}
    protected override void WndProc(ref Message m){
        base.WndProc(ref m);
        if(m.Msg==0x0005){
            bool minimized=m.WParam.ToInt32()==1,restored=nativeWasMinimized&&!minimized;nativeWasMinimized=minimized;
            if(minimized&&tray!=null&&!exitRequested&&!restoringWindow){try{BeginInvoke((MethodInvoker)delegate{if(!IsDisposed&&!Disposing&&!exitRequested&&!restoringWindow&&WindowState==FormWindowState.Minimized)HideToTray();});}catch(InvalidOperationException){}}
            if(restored)QueueCompleteRedraw();
        }
        if(m.Msg==0x84&&WindowState==FormWindowState.Normal&&m.Result==(IntPtr)1){
            var point=PointToClient(new Point(unchecked((short)(long)m.LParam),unchecked((short)((long)m.LParam>>16))));int grip=6;
            bool left=point.X<grip,right=point.X>=Width-grip,top=point.Y<grip,bottom=point.Y>=Height-grip;
            if(top&&left)m.Result=(IntPtr)13;else if(top&&right)m.Result=(IntPtr)14;else if(bottom&&left)m.Result=(IntPtr)16;else if(bottom&&right)m.Result=(IntPtr)17;else if(left)m.Result=(IntPtr)10;else if(right)m.Result=(IntPtr)11;else if(top)m.Result=(IntPtr)12;else if(bottom)m.Result=(IntPtr)15;
        }
    }
    Button MakeButton(string title,bool primary,bool destructive,EventHandler action){var b=new ActionButton{Text=title,Primary=primary,Destructive=destructive,AccessibleName=title};b.Click+=action;return b;}
    void LoadSavedTheme(){
        try{
            string active=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"CodexDreamSkin\active-theme");
            var theme=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(active,"theme.json"),Encoding.UTF8));
            object value;if(theme.TryGetValue("name",out value))wallpaperName.Text=Convert.ToString(value);
            if(theme.TryGetValue("art",out value)){
                var art=value as Dictionary<string,object>;object alpha;
                if(art!=null&&art.TryGetValue("surfaceOpacity",out alpha))opacity.Value=Math.Max(opacity.Minimum,Math.Min(opacity.Maximum,(int)Math.Round(Convert.ToDouble(alpha)*100)));
            }
            if(theme.TryGetValue("image",out value)){string file=Path.GetFullPath(Path.Combine(active,Convert.ToString(value)));if(file.StartsWith(active+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){media.Text=file;preview.SetMedia(file);}}
            string previewInfo=Path.Combine(Path.GetDirectoryName(active),"manager-preview.json");
            if(File.Exists(previewInfo)){
                var saved=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(previewInfo,Encoding.UTF8));object id,pathValue,themeId;
                if(saved.TryGetValue("themeId",out id)&&theme.TryGetValue("id",out themeId)&&Equals(id,themeId)&&saved.TryGetValue("path",out pathValue)){
                    string file=Path.GetFullPath(Convert.ToString(pathValue)),cache=Path.Combine(Path.GetDirectoryName(active),"previews")+Path.DirectorySeparatorChar;
                    if(file.StartsWith(cache,StringComparison.OrdinalIgnoreCase))preview.SetMedia(file);
                }
            }
        }catch{}
    }
    void ChooseAndApply(){
        if(busy)return;
        using(var dialog=new OpenFileDialog{Title="选择壁纸并应用",Filter="支持的素材|*.mp4;*.webm;*.gif;*.png;*.jpg;*.jpeg;*.webp|视频|*.mp4;*.webm|GIF 动图|*.gif|图片|*.png;*.jpg;*.jpeg;*.webp",CheckFileExists=true}){
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;media.Text=dialog.FileName;wallpaperName.Text=Path.GetFileNameWithoutExtension(dialog.FileName);preview.SetMedia(dialog.FileName);
        }
        Run("Apply");
    }
    void ConfirmRemoval(){
        if(busy)return;
        if(MessageBox.Show(this,"卸下当前皮肤，恢复原界面？\r\n\r\n后台服务保留，之后可以直接更换壁纸。","卸载皮肤",MessageBoxButtons.OKCancel,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.OK)Run("RemoveSkin");
    }
    void ConfirmServiceRemoval(){if(busy)return;if(MessageBox.Show(this,"彻底卸载背景服务？\r\n\r\n将停止并解除自启动、Codex 启动挂接、后台守护和监控。完整卸载软件请使用安装目录的独立卸载工具。管理器、壁纸与登录资料保留，Codex 聊天不会关闭。","卸载服务",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.OK)Run("RemoveService");}
    static string Quote(string value){var b=new StringBuilder("\"");int slashes=0;foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='"')b.Append('\\',slashes*2+1).Append(c);else b.Append('\\',slashes).Append(c);slashes=0;}return b.Append('\\',slashes*2).Append('"').ToString();}
    static int ServiceCommand(string action){
        try{
            string root=AppDomain.CurrentDomain.BaseDirectory;
            string ps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
            var info=new ProcessStartInfo(ps,"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy RemoteSigned -File "+Quote(Path.Combine(root,"control.ps1"))+" -Action "+action+" -ManagerPid "+Process.GetCurrentProcess().Id+(action=="RemoveService"?" -AllowDisconnected":"")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            using(var child=Process.Start(info)){var output=child.StandardOutput.ReadToEndAsync();var errors=child.StandardError.ReadToEndAsync();WaitForCommand(child,300000,delegate{return false;});string state=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexDreamSkin");Directory.CreateDirectory(state);File.WriteAllText(Path.Combine(state,"installer-service.log"),output.Result+Environment.NewLine+errors.Result);return child.ExitCode;}
        }catch(Exception error){try{string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexDreamSkin");Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"installer-service.log"),error.ToString());}catch{}return 1;}
    }
    static string ExplainLaunchError(Exception error){
        var native=error as System.ComponentModel.Win32Exception;
        if(native!=null&&(native.NativeErrorCode==225||native.NativeErrorCode==226))return "Windows 安全软件已阻止或隔离组件。请记录威胁名称并提交开发者核查；程序未修改防护设置。";
        if(native!=null&&(native.NativeErrorCode==577||native.NativeErrorCode==1260))return "Windows 应用控制阻止了当前组件，需要系统认可的发布者签名或管理员允许的发行版本。程序不会绕过此策略。";
        return error.Message;
    }
    static void WaitForCommand(Process process,int milliseconds,Func<bool> cancelled){
        var watch=Stopwatch.StartNew();
        while(!process.WaitForExit(100)){
            bool cancel=cancelled();if(!cancel&&watch.ElapsedMilliseconds<milliseconds)continue;
            // This is the controller process started here, never a Codex or service process.
            try{process.Kill();}catch(InvalidOperationException){}process.WaitForExit(3000);
            throw new Exception(cancel?"已关闭状态检查。":"操作等待超时。管理器已恢复可用；可查看运行记录后重试。");
        }
    }
    void Run(string action){
        if(busy)return;bool statusOnly=action=="Status"||action=="Connection";if(statusOnly&&checkingStatus)return;int generation=++operationGeneration;cancelStatus=false;
        if(statusOnly)checkingStatus=true;
        if(!statusOnly){CancelPreview();busy=true;runningAction=action;SetBusyControls(true);progress.Visible=true;previewHint.Text=action=="Apply"?"正在应用壁纸，视频不兼容时会自动适配…":"正在处理服务设置…";}
        WallpaperEntry applyingEntry=selected;
        string chosen=selected!=null?selected.Path:media.Text,alpha=(opacity.Value/100M).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if(action!="Connection")log.Text=action=="Install"?"正在装载服务并设置自启动…":action=="Apply"?"正在检查素材并更换壁纸…":action=="RemoveService"?"正在彻底卸载背景服务…":action=="RemoveSkin"?"正在卸下当前皮肤…":"正在检查服务状态…";
        ThreadPool.QueueUserWorkItem(delegate{
            string result;bool success=false;
            try{
                string ps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
                string args="-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy RemoteSigned -File "+Quote(Path.Combine(root,"control.ps1"))+" -Action "+action+" -ManagerPid "+Process.GetCurrentProcess().Id;
                if(action=="Apply")args+=" -Media "+Quote(chosen)+" -Opacity "+alpha;
                var start=new ProcessStartInfo(ps,args){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
                using(var p=Process.Start(start)){
                    if(statusOnly)lock(statusLock)statusProcess=p;
                    var stdout=new StringBuilder();var stderr=new StringBuilder();
                    using(var outputDone=new ManualResetEvent(false))using(var errorDone=new ManualResetEvent(false)){
                    p.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data==null){try{outputDone.Set();}catch(ObjectDisposedException){}return;}if(e.Data.StartsWith("DREAM_STATUS:")){string json=e.Data.Substring(13);Ui(delegate{if(generation==operationGeneration)UpdateConnection(json);});return;}if(e.Data.StartsWith("DREAM_PREVIEW:")){string file=e.Data.Substring(14);Ui(delegate{if(generation==operationGeneration)preview.SetMedia(file);});return;}lock(stdout)stdout.AppendLine(e.Data);Ui(delegate{if(generation==operationGeneration&&busy&&!closeRequested){if(log.TextLength>24000)log.Text=log.Text.Substring(log.TextLength-12000);log.AppendText(Environment.NewLine+e.Data);}});};
                    p.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data==null){try{errorDone.Set();}catch(ObjectDisposedException){}return;}lock(stderr)stderr.AppendLine(e.Data);};
                    p.BeginOutputReadLine();p.BeginErrorReadLine();WaitForCommand(p,statusOnly?30000:action=="Apply"?1800000:300000,delegate{return statusOnly&&cancelStatus;});outputDone.WaitOne(2000);errorDone.WaitOne(2000);success=p.ExitCode==0;
                    lock(stdout)lock(stderr)result=(stdout.ToString()+Environment.NewLine+stderr.ToString()).Trim();
                    }
                    if(result.Length==0)result=success?"操作完成。":"操作失败，请查看使用说明。";
                }
            }catch(Exception error){result=ExplainLaunchError(error);}finally{if(statusOnly){checkingStatus=false;lock(statusLock)statusProcess=null;}}
            if(IsDisposed)return;
            Ui(delegate{
                if(generation!=operationGeneration)return;
                if(action!="Connection")log.Text=(success?"✓ ":"操作未完成：")+result;busy=false;SetBusyControls(false);progress.Visible=false;
                if(closeRequested){Close();return;}
                if(!success){if(statusOnly){status.Text="○  检测暂不可用";status.ForeColor=Muted;}else previewHint.Text="操作未完成，请在服务与设置中查看运行记录："+(result.Length>120?result.Substring(0,120):result);}
                if(success&&action=="Apply")AppliedToHistory(applyingEntry);
                if(success&&(action=="RemoveSkin"||action=="RemoveService"))previewHint.Text=action=="RemoveService"?"背景服务已卸载，壁纸记录和登录资料保留。":"皮肤已卸下，背景服务保留。";
                if(success&&!statusOnly)Run("Connection");
            });
        });
    }
    int TestLifecycle(string reportPath){
        Show();Application.DoEvents();busy=true;runningAction="Apply";Close();Application.DoEvents();
        if(Visible||IsDisposed||!closeRequested)throw new Exception("Busy window did not close immediately");
        busy=false;closeRequested=false;Show();runningAction="Status";busy=true;Close();if(!cancelStatus||Visible)throw new Exception("Status cancellation failed");
        string ps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
        string waitingApp=Application.ExecutablePath;
        bool timedOut=false,cancelled=false;var watch=Stopwatch.StartNew();
        using(var child=Process.Start(new ProcessStartInfo(waitingApp,"--wait-test"){UseShellExecute=false,CreateNoWindow=true})){
            try{WaitForCommand(child,600,delegate{return false;});}catch(Exception){timedOut=child.HasExited;}
        }
        using(var child=Process.Start(new ProcessStartInfo(waitingApp,"--wait-test"){UseShellExecute=false,CreateNoWindow=true})){
            try{WaitForCommand(child,30000,delegate{return true;});}catch(Exception){cancelled=child.HasExited;}
        }
        if(!timedOut||!cancelled||watch.ElapsedMilliseconds>7000)throw new Exception("Command watchdog failed");
        busy=false;Close();File.WriteAllText(reportPath,"{\"pass\":true,\"closeWhileBusy\":true,\"statusCancellation\":true,\"timeout\":true,\"milliseconds\":"+watch.ElapsedMilliseconds+"}");return 0;
    }
    static Button FindButton(Control parent,string name){foreach(Control child in parent.Controls){var button=child as Button;if(button!=null&&button.AccessibleName==name)return button;var nested=FindButton(child,name);if(nested!=null)return nested;}return null;}
    int TestPreview(string image,string reportPath){
        Show();preview.SetMedia(image);Application.DoEvents();var hashes=new HashSet<string>();
        for(int index=0;index<18;index++){
            Thread.Sleep(80);Application.DoEvents();using(var bitmap=new Bitmap(preview.Width,preview.Height))using(var buffer=new MemoryStream()){
                preview.DrawToBitmap(bitmap,preview.ClientRectangle);bitmap.Save(buffer,System.Drawing.Imaging.ImageFormat.Png);using(var sha=System.Security.Cryptography.SHA256.Create())hashes.Add(Convert.ToBase64String(sha.ComputeHash(buffer.ToArray())));
            }
        }
        if(hashes.Count<2)throw new Exception("Animated preview did not change frames");
        File.WriteAllText(reportPath,"{\"pass\":true,\"animated\":true,\"distinctFrames\":"+hashes.Count+"}");return 0;
    }
    int TestChrome(string reportPath){
        Show();for(int i=0;i<30;i++){Thread.Sleep(30);Application.DoEvents();}var minimize=FindButton(this,"最小化");var maximize=FindButton(this,"最大化或还原");var close=FindButton(this,"关闭");
        if(minimize==null||maximize==null||close==null)throw new Exception("Window controls missing");
        minimize.PerformClick();Application.DoEvents();if(WindowState!=FormWindowState.Minimized)throw new Exception("Minimize failed");WindowState=FormWindowState.Normal;
        maximize.PerformClick();Application.DoEvents();if(WindowState!=FormWindowState.Maximized)throw new Exception("Maximize failed");maximize.PerformClick();Application.DoEvents();if(WindowState!=FormWindowState.Normal)throw new Exception("Restore failed");
        int before=opacity.Value;opacity.Value=before-10;if(percentage.Text!=(before-10)+"%")throw new Exception("Opacity feedback failed");
        opacity.Focus();Application.DoEvents();
        Func<int,IntPtr> position=delegate(int x){return (IntPtr)((opacity.Height/2<<16)|(x&0xffff));};
        int middle=opacity.Width/2;SendMessage(opacity.Handle,0x201,(IntPtr)1,position(middle));SendMessage(opacity.Handle,0x202,IntPtr.Zero,position(middle));Application.DoEvents();
        ActiveControl=commands.Controls[0];Application.DoEvents();
        using(var first=new Bitmap(opacity.Width,opacity.Height))using(var last=new Bitmap(opacity.Width,opacity.Height)){
            opacity.DrawToBitmap(first,opacity.ClientRectangle);
            SendMessage(opacity.Handle,0x201,(IntPtr)1,position(12));
            for(int i=0;i<250;i++)SendMessage(opacity.Handle,0x200,(IntPtr)1,position(12+(opacity.Width-24)*((i*37)%101)/100));
            SendMessage(opacity.Handle,0x200,(IntPtr)1,position(middle));SendMessage(opacity.Handle,0x202,IntPtr.Zero,position(middle));ActiveControl=commands.Controls[0];opacity.Refresh();Application.DoEvents();opacity.DrawToBitmap(last,opacity.ClientRectangle);
            for(int y=0;y<first.Height;y++)for(int x=0;x<first.Width;x++)if(first.GetPixel(x,y)!=last.GetPixel(x,y))throw new Exception("Slider redraw residue detected");
        }
        opacity.Focus();SendMessage(opacity.Handle,0x100,(IntPtr)Keys.Home,IntPtr.Zero);if(opacity.Value!=opacity.Minimum)throw new Exception("Slider Home key failed");SendMessage(opacity.Handle,0x100,(IntPtr)Keys.End,IntPtr.Zero);if(opacity.Value!=opacity.Maximum)throw new Exception("Slider End key failed");
        int edge=SendMessage(Handle,0x84,IntPtr.Zero,(IntPtr)((PointToScreen(new Point(1,1)).Y<<16)|(PointToScreen(new Point(1,1)).X&0xffff))).ToInt32();if(edge!=13)throw new Exception("Resize hit test failed");
        if(FormBorderStyle!=FormBorderStyle.None||commands.Controls.Count!=4)throw new Exception("Unexpected chrome or command count");
        Size original=ClientSize;opacity.Value=72;ActiveControl=commands.Controls[0];Application.DoEvents();
        using(var first=new Bitmap(preview.Width,preview.Height))using(var last=new Bitmap(preview.Width,preview.Height)){
            preview.DrawToBitmap(first,preview.ClientRectangle);
            for(int i=0;i<80;i++){ClientSize=new Size(original.Width+(i*47)%330,original.Height+(i*29)%160);Application.DoEvents();}
            ClientSize=original;Application.DoEvents();preview.DrawToBitmap(last,preview.ClientRectangle);
            for(int y=0;y<first.Height;y++)for(int x=0;x<first.Width;x++)if(first.GetPixel(x,y)!=last.GetPixel(x,y))throw new Exception("Preview resize residue detected");
        }
        File.WriteAllText(reportPath,"{\"pass\":true,\"minimize\":true,\"maximize\":true,\"restore\":true,\"resize\":true,\"resizeCycles\":80,\"resizeNoTrails\":true,\"opacityFeedback\":true,\"sliderDragEvents\":250,\"sliderNoTrails\":true,\"sliderKeyboard\":true,\"commands\":4}");close.PerformClick();if(!IsDisposed)throw new Exception("Close failed");return 0;
    }
    [STAThread]static int Main(string[] args){
        if(args.Length==1&&args[0]=="--msi-action-probe")return 0;
        if(args.Length==1&&args[0]=="--wait-test"){Thread.Sleep(30000);return 0;}
        if(args.Length==1&&(args[0]=="--service-install"||args[0]=="--service-remove"))return ServiceCommand(args[0]=="--service-install"?"Install":"RemoveService");
        SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        bool smoke=args.Length>=2&&args[0]=="--smoke-test",uiTest=args.Length==2&&args[0]=="--ui-test",previewTest=args.Length==3&&args[0]=="--preview-test",lifecycle=args.Length==2&&args[0]=="--lifecycle-test",galleryTest=args.Length==2&&args[0]=="--gallery-test",steamScrollTest=args.Length==2&&args[0]=="--steam-scroll-test";
        bool test=smoke||uiTest||previewTest||lifecycle||galleryTest||steamScrollTest;Mutex instance=null;bool owns=false;
        try{
        if(!test){try{using(var setup=Mutex.OpenExisting("Local\\CodexLiveBackground.Setup."+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value)){MessageBox.Show("安装或卸载正在进行，请等待完成。","Codex 动态背景");return 0;}}catch(WaitHandleCannotBeOpenedException){}instance=new Mutex(false,"Local\\CodexLiveBackground.Manager."+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value);try{owns=instance.WaitOne(0);}catch(AbandonedMutexException){owns=true;}
            if(!owns){try{using(var activate=EventWaitHandle.OpenExisting("Local\\CodexLiveBackground.Activate."+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value))activate.Set();}catch(WaitHandleCannotBeOpenedException){}return 0;}}
        using(var form=new Manager(!test)){
            if(steamScrollTest)return form.TestSteamScroll(args[1]);
            if(galleryTest)return form.TestGallery(args[1]);
            if(lifecycle)return form.TestLifecycle(args[1]);
            if(previewTest)return form.TestPreview(args[1],args[2]);
            if(uiTest)return form.TestChrome(args[1]);
            if(smoke){if(args.Length==4)form.ClientSize=new Size(int.Parse(args[2]),int.Parse(args[3]));form.Show();for(int i=0;i<50;i++){Thread.Sleep(30);Application.DoEvents();}using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(args[1]);}return form.commands.Controls.Count==4?0:1;}
            using(var activate=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\CodexLiveBackground.Activate."+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value))using(var timer=new System.Windows.Forms.Timer{Interval=250}){
                timer.Tick+=delegate{if(activate.WaitOne(0))form.RestoreWindow();};timer.Start();Application.Run(form);
            }
        }
        return 0;
        }catch(Exception error){if(test){File.WriteAllText(args[args.Length-1],new JavaScriptSerializer().Serialize(new{pass=false,error=error.ToString()}));return 1;}MessageBox.Show(error.Message,"Codex 动态背景");return 1;}
        finally{if(instance!=null){if(owns)instance.ReleaseMutex();instance.Dispose();}}
    }
}
