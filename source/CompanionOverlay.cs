using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Each popup owns its pixels and geometry; the pet's body window stays fixed.
internal sealed class PetOverlay : Form {
    [StructLayout(LayoutKind.Sequential)]struct PointI{public int X,Y;public PointI(int x,int y){X=x;Y=y;}}
    [StructLayout(LayoutKind.Sequential)]struct SizeI{public int W,H;public SizeI(int w,int h){W=w;H=h;}}
    [StructLayout(LayoutKind.Sequential,Pack=1)]struct Blend{public byte Op,Flags,Alpha,Format;}
    [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr value);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll",SetLastError=true)]static extern bool UpdateLayeredWindow(IntPtr window,IntPtr screen,ref PointI destination,ref SizeI size,IntPtr source,ref PointI origin,uint key,ref Blend blend,uint flags);
    [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]static extern bool GetPhysicalCursorPos(out PointI point);
    readonly bool clickThrough;public event Action<Point> Clicked;
    public PetOverlay(bool through=false){clickThrough=through;ShowInTaskbar=false;FormBorderStyle=FormBorderStyle.None;TopMost=true;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;Text="CodexHub · 浮层";}
    protected override bool ShowWithoutActivation{get{return true;}}
    protected override CreateParams CreateParams{get{var value=base.CreateParams;value.ExStyle=(value.ExStyle|0x80000|0x80|0x08000000|(clickThrough?0x20:0))&~0x40000;return value;}}
    public static int Buttons{get{return ((GetAsyncKeyState(1)&0x8000)!=0?1:0)|((GetAsyncKeyState(2)&0x8000)!=0?2:0);}}
    public static Point Pointer{get{PointI point;return GetPhysicalCursorPos(out point)?new Point(point.X,point.Y):Cursor.Position;}}
    public void Present(Bitmap pixels,Point point,float opacity){ClientSize=pixels.Size;Location=point;Push(Handle,pixels,point,(byte)Math.Max(0,Math.Min(255,(int)(opacity*255))));if(!Visible)Show();}
    public static void Push(IntPtr handle,Bitmap image,Point location,byte alpha=255){IntPtr screen=GetDC(IntPtr.Zero),dc=CreateCompatibleDC(screen),bitmap=IntPtr.Zero,old=IntPtr.Zero;try{bitmap=image.GetHbitmap(Color.FromArgb(0));old=SelectObject(dc,bitmap);var point=new PointI(location.X,location.Y);var origin=new PointI(0,0);var size=new SizeI(image.Width,image.Height);var blend=new Blend{Alpha=alpha,Format=1};if(!UpdateLayeredWindow(handle,screen,ref point,ref size,dc,ref origin,0,ref blend,2))throw new Win32Exception();}finally{if(old!=IntPtr.Zero)SelectObject(dc,old);if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);DeleteDC(dc);ReleaseDC(IntPtr.Zero,screen);}}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left&&Clicked!=null)Clicked(e.Location);}
    protected override void WndProc(ref Message message){if(message.Msg==0x21){message.Result=(IntPtr)3;return;}if(clickThrough&&message.Msg==0x84){message.Result=(IntPtr)(-1);return;}base.WndProc(ref message);}
}
