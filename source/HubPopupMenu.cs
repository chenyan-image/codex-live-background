using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// ToolStrip's normal AutoClose does not see clicks in other applications when
// its owner is a non-activating pet window. Observe those clicks only while a
// popup is open; never capture input, activate an owner, or install a hook.
internal sealed class HubContextMenu : ContextMenuStrip {
    protected override CreateParams CreateParams {get{var value=base.CreateParams;value.ExStyle=(value.ExStyle|0x80|0x08000000)&~0x40000;return value;}}
}
internal static class HubPopupMenu {
    static readonly Dictionary<ContextMenuStrip,Session> sessions=new Dictionary<ContextMenuStrip,Session>();
    static readonly PopupRenderer renderer=new PopupRenderer();
    static int openCount;
    public static bool AnyOpen {get{return Thread.VolatileRead(ref openCount)>0;}}
    public static void Apply(ContextMenuStrip menu){
        if(menu==null)throw new ArgumentNullException("menu");if(menu.IsDisposed)return;
        Session value;if(sessions.TryGetValue(menu,out value)){value.StyleTree(menu);return;}
        value=new Session(menu);sessions.Add(menu,value);
    }
    static GraphicsPath Round(RectangleF bounds,float radius){
        var path=new GraphicsPath();float diameter=Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height));
        if(diameter<=0){path.AddRectangle(bounds);return path;}
        path.AddArc(bounds.Left,bounds.Top,diameter,diameter,180,90);path.AddArc(bounds.Right-diameter,bounds.Top,diameter,diameter,270,90);path.AddArc(bounds.Right-diameter,bounds.Bottom-diameter,diameter,diameter,0,90);path.AddArc(bounds.Left,bounds.Bottom-diameter,diameter,diameter,90,90);path.CloseFigure();return path;
    }
    static readonly Color Background=Color.FromArgb(15,15,16),Foreground=Color.FromArgb(242,242,244),Muted=Color.FromArgb(132,136,144),Border=Color.FromArgb(41,42,46),Selected=Color.FromArgb(40,43,49),Accent=Color.FromArgb(142,188,247);
    sealed class PopupColors : ProfessionalColorTable {
        public override Color ToolStripDropDownBackground {get{return Background;}}
        public override Color MenuBorder {get{return Border;}}
        public override Color MenuItemSelected {get{return Selected;}}
        public override Color MenuItemSelectedGradientBegin {get{return Selected;}}
        public override Color MenuItemSelectedGradientEnd {get{return Selected;}}
        public override Color MenuItemPressedGradientBegin {get{return Selected;}}
        public override Color MenuItemPressedGradientMiddle {get{return Selected;}}
        public override Color MenuItemPressedGradientEnd {get{return Selected;}}
        public override Color MenuItemBorder {get{return Selected;}}
        public override Color ImageMarginGradientBegin {get{return Background;}}
        public override Color ImageMarginGradientMiddle {get{return Background;}}
        public override Color ImageMarginGradientEnd {get{return Background;}}
        public override Color SeparatorDark {get{return Border;}}
        public override Color SeparatorLight {get{return Border;}}
        public override Color CheckBackground {get{return Background;}}
        public override Color CheckSelectedBackground {get{return Selected;}}
        public override Color CheckPressedBackground {get{return Selected;}}
    }
    sealed class PopupRenderer : ToolStripProfessionalRenderer {
        public PopupRenderer():base(new PopupColors()){RoundedEdges=false;}
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){
            var saved=e.Graphics.Save();e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=Round(new RectangleF(0,0,e.ToolStrip.Width,e.ToolStrip.Height),9))using(var brush=new SolidBrush(Background))e.Graphics.FillPath(brush,shape);e.Graphics.Restore(saved);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){
            if(e.ToolStrip.Width<2||e.ToolStrip.Height<2)return;var saved=e.Graphics.Save();e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=Round(new RectangleF(.5F,.5F,e.ToolStrip.Width-1,e.ToolStrip.Height-1),8.5F))using(var pen=new Pen(Border))e.Graphics.DrawPath(pen,shape);e.Graphics.Restore(saved);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e){}
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){
            if(!e.Item.Enabled||(!e.Item.Selected&&!e.Item.Pressed))return;var saved=e.Graphics.Save();e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=Round(new RectangleF(2,1,Math.Max(1,e.Item.Width-4),Math.Max(1,e.Item.Height-2)),5))using(var brush=new SolidBrush(Selected))e.Graphics.FillPath(brush,shape);e.Graphics.Restore(saved);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=e.Item.Enabled?Foreground:Muted;base.OnRenderItemText(e);}
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e){using(var pen=new Pen(Border))e.Graphics.DrawLine(pen,10,e.Item.Height/2,Math.Max(10,e.Item.Width-10),e.Item.Height/2);}
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e){e.ArrowColor=e.Item.Enabled?Accent:Muted;base.OnRenderArrow(e);}
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e){
            var saved=e.Graphics.Save();e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;var box=e.ImageRectangle;float x=box.Left+box.Width*.2F,y=box.Top+box.Height*.5F;
            using(var pen=new Pen(e.Item.Enabled?Accent:Muted,1.7F)){pen.StartCap=pen.EndCap=LineCap.Round;e.Graphics.DrawLines(pen,new[]{new PointF(x,y),new PointF(x+box.Width*.22F,y+box.Height*.22F),new PointF(x+box.Width*.66F,y-box.Height*.3F)});}e.Graphics.Restore(saved);
        }
    }
    sealed class Session : IDisposable {
        [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
        [StructLayout(LayoutKind.Sequential)]struct NativeRect {public int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)]struct NativePoint {public int X,Y;}
        [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out NativeRect bounds);
        [DllImport("user32.dll")]static extern bool GetPhysicalCursorPos(out NativePoint point);
        [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
        [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern IntPtr SetWindowLongPtr(IntPtr window,int index,IntPtr value);
        readonly ContextMenuStrip root;readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=20};readonly Font font=new Font("Microsoft YaHei UI",9F,FontStyle.Regular,GraphicsUnit.Point);
        readonly HashSet<ToolStripDropDown> styled=new HashSet<ToolStripDropDown>();readonly HashSet<ToolStripMenuItem> items=new HashSet<ToolStripMenuItem>();bool open,disposed;int previousButtons;
        public Session(ContextMenuStrip menu){root=menu;StyleTree(menu);menu.Opening+=delegate{StyleTree(root);};menu.Opened+=delegate{Begin();};menu.Closed+=delegate{End();};menu.VisibleChanged+=delegate{if(menu.Visible)Begin();else End();};menu.Disposed+=delegate{Dispose();};timer.Tick+=delegate{Poll();};}
        public void StyleTree(ToolStripDropDown menu){
            if(disposed||menu.IsDisposed)return;menu.BackColor=Background;menu.ForeColor=Foreground;menu.Font=font;menu.Renderer=renderer;menu.AutoClose=true;menu.DropShadowEnabled=true;menu.Padding=new Padding(5,5,5,5);menu.ShowItemToolTips=false;
            var dropdown=menu as ToolStripDropDownMenu;if(dropdown!=null){dropdown.ShowImageMargin=false;dropdown.ShowCheckMargin=true;}
            if(styled.Add(menu)){menu.HandleCreated+=delegate{KeepBackground(menu);};menu.SizeChanged+=delegate{RoundWindow(menu);};menu.Opened+=delegate{KeepBackground(menu);RoundWindow(menu);};menu.ItemAdded+=delegate{StyleTree(menu);};}
            foreach(ToolStripItem item in menu.Items){item.ForeColor=Foreground;item.BackColor=Background;item.Font=font;
                if(item is ToolStripSeparator){item.Margin=new Padding(2,3,2,3);continue;}
                item.Padding=new Padding(7,4,7,4);item.Margin=new Padding(1,0,1,0);var parent=item as ToolStripMenuItem;
                if(parent!=null&&items.Add(parent))parent.DropDownOpening+=delegate{StyleTree(parent.DropDown);};
                if(parent!=null&&parent.HasDropDownItems)StyleTree(parent.DropDown);
            }
            RoundWindow(menu);
        }
        static void KeepBackground(ToolStripDropDown menu){if(menu.IsDisposed||!menu.IsHandleCreated)return;long style=GetWindowLongPtr(menu.Handle,-20).ToInt64();SetWindowLongPtr(menu.Handle,-20,new IntPtr((style|0x80|0x08000000)&~0x40000));}
        static void RoundWindow(ToolStripDropDown menu){if(menu.IsDisposed||menu.Width<2||menu.Height<2)return;using(var path=Round(new RectangleF(0,0,menu.Width,menu.Height),9)){var old=menu.Region;menu.Region=new Region(path);if(old!=null)old.Dispose();}}
        static int Buttons(out bool recent){int down=0;recent=false;int[] keys={1,2,4,5,6};for(int i=0;i<keys.Length;i++){short state=GetAsyncKeyState(keys[i]);if((state&0x8000)!=0)down|=1<<i;if((state&1)!=0)recent=true;}return down;}
        void Begin(){if(disposed||open)return;bool ignored;previousButtons=Buttons(out ignored);open=true;Interlocked.Increment(ref openCount);timer.Start();}
        void End(){if(!open)return;open=false;timer.Stop();Interlocked.Decrement(ref openCount);}
        bool ContainsPointer(ToolStripDropDown menu,Point pointer){if(menu.IsDisposed||!menu.Visible)return false;NativeRect box;if(GetWindowRect(menu.Handle,out box)&&pointer.X>=box.Left&&pointer.X<box.Right&&pointer.Y>=box.Top&&pointer.Y<box.Bottom)return true;foreach(ToolStripItem item in menu.Items){var parent=item as ToolStripMenuItem;if(parent!=null&&parent.HasDropDownItems&&parent.DropDown.Visible&&ContainsPointer(parent.DropDown,pointer))return true;}return false;}
        void Poll(){
            if(disposed||root.IsDisposed||!root.Visible){End();return;}bool recent;int buttons=Buttons(out recent);bool pressed=(buttons&~previousButtons)!=0||recent;previousButtons=buttons;
            if(pressed){IntPtr old=IntPtr.Zero;try{old=SetThreadDpiAwarenessContext(new IntPtr(-4));NativePoint point;if(GetPhysicalCursorPos(out point)&&!ContainsPointer(root,new Point(point.X,point.Y)))root.Close(ToolStripDropDownCloseReason.AppClicked);}finally{if(old!=IntPtr.Zero)SetThreadDpiAwarenessContext(old);}}
        }
        public void Dispose(){if(disposed)return;End();disposed=true;timer.Dispose();sessions.Remove(root);styled.Clear();items.Clear();font.Dispose();}
    }
}
