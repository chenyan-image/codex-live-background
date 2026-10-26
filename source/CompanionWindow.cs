using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal sealed class PetSurface {
    public Rectangle Bounds;public IntPtr Window;
    public PetSurface(Rectangle bounds,IntPtr window){Bounds=bounds;Window=window;}
}
internal static class CodexPetSurface {
    [StructLayout(LayoutKind.Sequential)]struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct Point {public int X,Y;}
    delegate bool EnumWindow(IntPtr window,IntPtr data);
    [DllImport("user32.dll")]static extern bool EnumWindows(EnumWindow callback,IntPtr data);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint id);
    [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr window,ref Point point);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetPackageFullName(IntPtr process,ref uint size,StringBuilder name);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetPackagePathByFullName(string name,ref uint size,StringBuilder path);
    [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attr,out int value,int size);
    static readonly Regex OfficialPackage=new Regex(@"^OpenAI\.Codex(?:Beta)?_\d+\.\d+\.\d+\.\d+_x64__2p2nqsd0c76g0$");
    internal static bool MatchesRegisteredImage(string executable,string installed,string fullName){return OfficialPackage.IsMatch(fullName)&&String.Equals(Path.GetFullPath(executable),Path.GetFullPath(Path.Combine(installed,@"app\ChatGPT.exe")),StringComparison.OrdinalIgnoreCase);}
    static bool OfficialProcess(Process process){
        uint size=0;GetPackageFullName(process.Handle,ref size,null);string fullName=null;
        if(size>1&&size<512){var name=new StringBuilder((int)size);if(GetPackageFullName(process.Handle,ref size,name)==0)fullName=name.ToString();}
        string executable=process.MainModule.FileName;
        // Official unpackaged launches (including a debugging-port launch) have
        // no process package identity. Resolve the package with Windows instead
        // of trusting the executable name or a versioned shortcut path alone.
        if(String.IsNullOrEmpty(fullName)){var app=new DirectoryInfo(Path.GetDirectoryName(executable));if(app.Parent==null)return false;fullName=app.Parent.Name;}
        if(!OfficialPackage.IsMatch(fullName))return false;uint length=0;GetPackagePathByFullName(fullName,ref length,null);if(length<2||length>32768)return false;var installed=new StringBuilder((int)length);
        return GetPackagePathByFullName(fullName,ref length,installed)==0&&MatchesRegisteredImage(executable,installed.ToString(),fullName);
    }
    public static PetSurface Find(){
        PetSurface selected=null;IntPtr foreground=GetForegroundWindow();var trusted=new HashSet<uint>();
        foreach(var process in Process.GetProcessesByName("ChatGPT"))using(process)try{
            if(process.MainWindowHandle!=IntPtr.Zero&&OfficialProcess(process))trusted.Add((uint)process.Id);
        }catch(Win32Exception){}catch(InvalidOperationException){}catch(ArgumentException){}
        if(trusted.Count==0)return null;
        EnumWindows(delegate(IntPtr window,IntPtr unused){
            if(!IsWindowVisible(window)||IsIconic(window))return true;uint pid;GetWindowThreadProcessId(window,out pid);
            if(!trusted.Contains(pid))return true;
            try{
                int cloaked;if(DwmGetWindowAttribute(window,14,out cloaked,4)==0&&cloaked!=0)return true;
                Rect client;var origin=new Point();if(!GetClientRect(window,out client)||!ClientToScreen(window,ref origin))return true;
                var bounds=new Rectangle(origin.X,origin.Y,client.Right,client.Bottom);if(bounds.Width<240||bounds.Height<300)return true;
                if(selected==null||window==foreground)selected=new PetSurface(bounds,window);
            }catch(Win32Exception){}catch(InvalidOperationException){}catch(ArgumentException){}return true;
        },IntPtr.Zero);return selected;
    }
    public static bool ForegroundAllows(PetSurface surface){
        if(surface==null)return false;IntPtr foreground=GetForegroundWindow();if(foreground==surface.Window)return true;uint id;GetWindowThreadProcessId(foreground,out id);return id==(uint)Process.GetCurrentProcess().Id;
    }
}
internal sealed class PetLayout {
    public readonly Size Size;public readonly RectangleF Quota,Sprite;public readonly float PetScale,CapsuleScale,QuotaOffset;
    public PetLayout(bool expanded,float petScale=1,float capsuleScale=1){
        PetScale=Math.Max(.05F,petScale);CapsuleScale=Math.Max(.05F,capsuleScale);
        int width=(int)Math.Ceiling(Math.Max(280*CapsuleScale,208*PetScale));QuotaOffset=(width-280*CapsuleScale)/2;
        Quota=new RectangleF(QuotaOffset+6*CapsuleScale,5*CapsuleScale,268*CapsuleScale,(expanded?220:46)*CapsuleScale);
        Sprite=new RectangleF((width-192*PetScale)/2,(expanded?232:54)*CapsuleScale,192*PetScale,208*PetScale);
        Size=new Size(width,(int)Math.Ceiling(Sprite.Bottom+8));
    }
    public PointF CapsulePoint(PointF point){return new PointF((point.X-QuotaOffset)/CapsuleScale,point.Y/CapsuleScale);}
}
internal sealed class PetPainter : IDisposable {
    readonly PrivateFontCollection fonts=new PrivateFontCollection();readonly FontFamily numbers;readonly Image refresh;readonly Image hubLogo=IconAssets.Create(28);
    public PetPainter(string root){foreach(string name in new[]{"ChakraPetch-Regular.ttf","ChakraPetch-Bold.ttf"}){string file=Path.Combine(root,@"source\assets\fonts",name);if(File.Exists(file))try{fonts.AddFontFile(file);}catch(ArgumentException){}}numbers=fonts.Families.Length>0?fonts.Families[0]:FontFamily.GenericSansSerif;string icon=Path.Combine(root,@"source\assets\arrow-clockwise.png");if(File.Exists(icon))using(var source=Image.FromFile(icon))refresh=new Bitmap(source);}
    public static Size LogicalSize(bool expanded,float petScale=1,float capsuleScale=1){return new PetLayout(expanded,petScale,capsuleScale).Size;}
    public static RectangleF QuotaRect(bool expanded,float petScale=1,float capsuleScale=1){return new PetLayout(expanded,petScale,capsuleScale).Quota;}
    public static RectangleF SpriteRect(bool expanded,float petScale=1,float capsuleScale=1){return new PetLayout(expanded,petScale,capsuleScale).Sprite;}
    static void Text(Graphics g,string text,Font font,Color color,RectangleF rectangle,StringAlignment align){using(var brush=new SolidBrush(color))using(var format=new StringFormat{Alignment=align,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(text,font,brush,rectangle,format);}
    public Bitmap Render(PetAtlas atlas,PetMotion motion,bool expanded,bool quotaVisible,PetQuotaSnapshot quota,string status,double clock,float scale,string activityLabel=null,bool activityBusy=false,double? animationClock=null,PetTokenSnapshot tokens=null,int tokenRange=0,bool resetTab=false,float petScale=1,float capsuleScale=1){
        var layout=new PetLayout(expanded,petScale,capsuleScale);Size size=layout.Size;var image=new Bitmap((int)Math.Ceiling(size.Width*scale),(int)Math.Ceiling(size.Height*scale),PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(image)){
            g.Clear(Color.Transparent);g.ScaleTransform(scale,scale);g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
            using(var number=new Font(numbers,16,FontStyle.Bold,GraphicsUnit.Pixel))using(var smallNumber=new Font(numbers,11,FontStyle.Bold,GraphicsUnit.Pixel))using(var label=new Font("Microsoft YaHei UI",10,FontStyle.Bold,GraphicsUnit.Pixel))using(var tiny=new Font("Microsoft YaHei UI",8.5F,FontStyle.Regular,GraphicsUnit.Pixel)){
                if(quotaVisible){var transform=g.Save();g.TranslateTransform(layout.QuotaOffset,0);g.ScaleTransform(layout.CapsuleScale,layout.CapsuleScale);DrawQuota(g,expanded,quota,status,number,smallNumber,label,tiny,activityLabel,activityBusy,animationClock??clock,tokens,tokenRange,resetTab);g.Restore(transform);}
                var sprite=layout.Sprite;float progress=motion.CollisionDirection>=0?(float)(motion.ReactionRemaining/.55):0;
                if(progress>0){float squash=(float)Math.Sin((1-progress)*Math.PI)*.16F;if(motion.CollisionDirection<2){sprite.X+=sprite.Width*squash/2;sprite.Width*=1-squash;sprite.Y-=sprite.Height*squash*.2F;sprite.Height*=1+squash*.2F;}else{sprite.Y+=sprite.Height*squash;sprite.Height*=1-squash;sprite.X-=sprite.Width*squash*.15F;sprite.Width*=1+squash*.3F;}}
                if(motion.Row==4&&motion.ReactionRemaining>0)sprite.Y-=(float)Math.Sin((1-motion.ReactionRemaining/motion.ReactionDuration)*Math.PI)*14*layout.PetScale;
                atlas.Draw(g,sprite,motion.Row,clock);
                if(progress>0)Collision(g,layout.Sprite,motion.CollisionDirection,1-progress);
            }
        }return image;
    }
    void DrawQuota(Graphics g,bool expanded,PetQuotaSnapshot quota,string status,Font number,Font smallNumber,Font label,Font tiny,string activityLabel,bool activityBusy,double clock,PetTokenSnapshot tokens,int tokenRange,bool resetTab){
        Color ink=Color.FromArgb(32,58,98),muted=Color.FromArgb(102,128,167),accent=Color.FromArgb(78,143,228),track=Color.FromArgb(222,233,247);
        var first=quota==null?null:quota.Short;var week=quota==null?null:quota.Week;
        if(!expanded){
            using(var path=IconAssets.Round(new RectangleF(57,8,165,37),18))using(var fill=new SolidBrush(Color.FromArgb(241,23,43,73)))using(var pen=new Pen(Color.FromArgb(153,111,151,209))){g.FillPath(fill,path);g.DrawPath(pen,path);}
            using(var fill=new SolidBrush(Color.FromArgb(247,251,255)))using(var pen=new Pen(Color.FromArgb(174,204,242))){g.FillEllipse(fill,7,5,44,44);g.DrawEllipse(pen,7,5,44,44);g.FillEllipse(fill,226,5,44,44);g.DrawEllipse(pen,226,5,44,44);}
            g.DrawImage(hubLogo,new RectangleF(16,14,26,26));
            using(var pen=new Pen(Color.FromArgb(146,183,232),1.5F)){for(int i=0;i<3;i++){double angle=i*Math.PI*2/3-Math.PI/2+(activityBusy?clock*2.8:0);float x=74+(float)Math.Cos(angle)*6,y=25+(float)Math.Sin(angle)*6;g.DrawEllipse(pen,x-1.5F,y-1.5F,3,3);}}
            string name=activityLabel??(quota==null?"等待 Codex":quota.Stale?"额度待读取":"Codex · 陪伴中");
            Text(g,name,label,Color.FromArgb(221,236,254),new RectangleF(88,10,124,33),StringAlignment.Near);
            using(var basePen=new Pen(track,4))using(var progress=new Pen(accent,4)){basePen.StartCap=basePen.EndCap=progress.StartCap=progress.EndCap=LineCap.Round;g.DrawArc(basePen,new RectangleF(231,10,34,34),135,270);if(first!=null&&first.Remaining.HasValue&&first.Remaining.Value>0)g.DrawArc(progress,new RectangleF(231,10,34,34),135,(float)(270*first.Remaining.Value/100));}
            using(var compact=new Font(numbers,13,FontStyle.Regular,GraphicsUnit.Pixel))Text(g,first==null?"—":first.Percent,compact,ink,new RectangleF(230,15,36,24),StringAlignment.Center);return;
        }
        using(var shape=IconAssets.Round(QuotaRect(true),15))using(var fill=new SolidBrush(Color.FromArgb(251,247,250,255)))using(var border=new Pen(Color.FromArgb(181,207,241),1)){g.FillPath(fill,shape);g.DrawPath(border,shape);}
        using(var pen=new Pen(accent,1.4F)){g.DrawEllipse(pen,20,17,3,3);g.DrawEllipse(pen,15,23,3,3);g.DrawEllipse(pen,25,24,3,3);g.DrawLine(pen,249,18,256,22);g.DrawLine(pen,249,26,256,22);}
        DrawRefresh(g,new RectangleF(218,13,16,16),accent);
        Text(g,"CodexHub",smallNumber,ink,new RectangleF(34,12,112,21),StringAlignment.Near);
        using(var line=new Pen(track))g.DrawLine(line,16,36,264,36);
        Text(g,activityLabel??"Codex · 陪伴中",label,ink,new RectangleF(18,39,180,17),StringAlignment.Near);
        Text(g,quota==null?"额度待读取":quota.Stale?"额度读数已过期":"真实额度 · 已连接",tiny,muted,new RectangleF(18,57,242,14),StringAlignment.Near);
        using(var shape=IconAssets.Round(new RectangleF(16,77,123,104),7))using(var fill=new SolidBrush(Color.FromArgb(235,242,252)))g.FillPath(fill,shape);
        using(var shape=IconAssets.Round(new RectangleF(145,77,119,104),7))using(var fill=new SolidBrush(Color.FromArgb(235,242,252)))g.FillPath(fill,shape);
        DrawQuotaBar(g,first,first==null?"短周期剩余":first.Label.Replace(" ","")+"剩余",new RectangleF(22,81,111,45),number,label,tiny,ink,muted,accent,track);
        DrawQuotaBar(g,week,week==null?"本周剩余":week.Label+"剩余",new RectangleF(22,132,111,45),number,label,tiny,ink,muted,accent,track);
        using(var shape=IconAssets.Round(new RectangleF(resetTab?205:149,80,53,17),4))using(var fill=new SolidBrush(Color.FromArgb(215,229,249)))g.FillPath(fill,shape);
        Text(g,"Token",smallNumber,resetTab?muted:ink,new RectangleF(149,80,53,17),StringAlignment.Center);Text(g,"重置",tiny,resetTab?ink:muted,new RectangleF(205,80,53,17),StringAlignment.Center);
        if(resetTab){
            Text(g,"短周期重置",tiny,muted,new RectangleF(151,103,107,13),StringAlignment.Near);Text(g,ResetClock(first),smallNumber,ink,new RectangleF(151,118,107,16),StringAlignment.Near);
            Text(g,"本周重置",tiny,muted,new RectangleF(151,142,107,13),StringAlignment.Near);Text(g,ResetClock(week),smallNumber,ink,new RectangleF(151,157,107,16),StringAlignment.Near);
        }else{
            string[] ranges={"今日","7天","30天"};for(int i=0;i<3;i++)Text(g,ranges[i],tiny,i==tokenRange?accent:muted,new RectangleF(149+i*37,99,36,15),StringAlignment.Center);
            string total=tokens==null||!tokens.Available?(tokens!=null&&tokens.Loading?"正在统计…":"暂无用量记录"):(tokens.Loading?"≈ ":"")+PetTokenSnapshot.Format(tokens.Total(tokenRange));Text(g,total,smallNumber,ink,new RectangleF(152,116,106,17),StringAlignment.Far);
            DrawTokenChart(g,new RectangleF(152,138,105,27),tokens,tokenRange,accent,track);
            Text(g,tokenRange==0?"00时":tokenRange==1?"6天前":"29天前",tiny,muted,new RectangleF(150,166,36,12),StringAlignment.Near);Text(g,tokenRange==0?"12":"",tiny,muted,new RectangleF(191,166,26,12),StringAlignment.Center);Text(g,tokenRange==0?"24时":"今天",tiny,muted,new RectangleF(224,166,36,12),StringAlignment.Far);
        }
        using(var line=new Pen(track))g.DrawLine(line,16,187,264,187);
        Text(g,"今日 Token",tiny,muted,new RectangleF(18,191,72,16),StringAlignment.Near);Text(g,tokens!=null&&tokens.Available?PetTokenSnapshot.Format(tokens.Today):"—",smallNumber,ink,new RectangleF(91,191,127,16),StringAlignment.Near);Text(g,"24h",tiny,muted,new RectangleF(230,191,32,16),StringAlignment.Far);
        string hint=String.IsNullOrEmpty(status)?"本机用量 · 含缓存输入 · 点击空白收起":status;Text(g,hint,tiny,muted,new RectangleF(18,208,242,12),StringAlignment.Near);
    }
    // Normalized from Phosphor's MIT arrow-clockwise vector. Keep its circular
    // contour and aligned arrowhead at small sizes rather than using a glyph.
    void DrawRefresh(Graphics g,RectangleF box,Color color){
        if(refresh!=null){g.DrawImage(refresh,box);return;}
        var saved=g.Save();g.TranslateTransform(box.X,box.Y);g.ScaleTransform(box.Width/256F,box.Height/256F);
        using(var pen=new Pen(color,18)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;g.DrawArc(pen,40,40,176,176,40,292);g.DrawLines(pen,new[]{new PointF(232,56),new PointF(232,104),new PointF(184,104)});g.DrawLine(pen,232,104,189,65);}g.Restore(saved);
    }
    static string ResetClock(PetQuotaWindow value){if(value==null||!value.ResetsAt.HasValue)return "未知";try{return DateTimeOffset.FromUnixTimeSeconds(value.ResetsAt.Value).ToLocalTime().ToString("MM-dd HH:mm");}catch(ArgumentOutOfRangeException){return "未知";}}
    static void DrawQuotaBar(Graphics g,PetQuotaWindow value,string name,RectangleF rect,Font number,Font label,Font tiny,Color ink,Color muted,Color accent,Color track){
        Text(g,name,label,muted,new RectangleF(rect.X,rect.Y,72,20),StringAlignment.Near);Text(g,value==null?"未知":value.Percent,number,ink,new RectangleF(rect.Right-44,rect.Y-1,44,22),StringAlignment.Far);
        using(var basePen=new Pen(Color.FromArgb(214,226,243),5))using(var filled=new Pen(accent,5)){basePen.StartCap=basePen.EndCap=filled.StartCap=filled.EndCap=LineCap.Round;g.DrawLine(basePen,rect.Left+2,rect.Top+25,rect.Right-2,rect.Top+25);if(value!=null&&value.Remaining.HasValue&&value.Remaining.Value>0)g.DrawLine(filled,rect.Left+2,rect.Top+25,rect.Left+2+(rect.Width-4)*(float)value.Remaining.Value/100,rect.Top+25);}
        Text(g,value==null?"重置时间未知":value.ResetText,tiny,muted,new RectangleF(rect.X,rect.Y+31,rect.Width,13),StringAlignment.Near);
    }
    static void DrawTokenChart(Graphics g,RectangleF box,PetTokenSnapshot tokens,int range,Color accent,Color track){
        using(var grid=new Pen(track,.7F)){grid.DashStyle=DashStyle.Dot;g.DrawLine(grid,box.Left,box.Top,box.Right,box.Top);g.DrawLine(grid,box.Left,box.Top+box.Height/2,box.Right,box.Top+box.Height/2);grid.DashStyle=DashStyle.Solid;g.DrawLine(grid,box.Left,box.Bottom,box.Right,box.Bottom);g.DrawLine(grid,box.Left,box.Top,box.Left,box.Bottom);}
        if(tokens==null||!tokens.Available)return;double[] series=tokens.Series(range);double max=series.Max();if(max<=0)max=1;var points=series.Select((value,i)=>new PointF(box.Left+box.Width*i/(series.Length-1),box.Bottom-(float)(value/max)*box.Height)).ToArray();var area=new List<PointF>{new PointF(box.Left,box.Bottom)};area.AddRange(points);area.Add(new PointF(box.Right,box.Bottom));using(var fill=new SolidBrush(Color.FromArgb(35,accent)))g.FillPolygon(fill,area.ToArray());using(var line=new Pen(accent,1.25F)){line.LineJoin=LineJoin.Round;g.DrawLines(line,points);}
    }
    static void Collision(Graphics g,RectangleF sprite,int direction,float progress){
        float x=direction==0?sprite.Left+6:direction==1?sprite.Right-6:sprite.Left+sprite.Width/2,y=direction==2?sprite.Top+8:direction==3?sprite.Bottom-10:sprite.Top+sprite.Height*.5F;
        int alpha=(int)(255*(1-progress));for(int i=0;i<4;i++){
            float angle=(float)(i*Math.PI/2+.4),distance=7+progress*27,px=x+(float)Math.Cos(angle)*distance,py=y+(float)Math.Sin(angle)*distance,radius=3.5F*(1-progress*.3F);
            var points=new PointF[8];for(int j=0;j<8;j++){float radius2=(j%2==0?radius:radius*.3F);points[j]=new PointF(px+(float)Math.Cos(j*Math.PI/4)*radius2,py+(float)Math.Sin(j*Math.PI/4)*radius2);}
            using(var fill=new SolidBrush(Color.FromArgb(alpha,i%2==0?Color.FromArgb(249,219,149):Color.FromArgb(175,220,255))))g.FillPolygon(fill,points);
        }
    }
    public void Dispose(){fonts.Dispose();if(refresh!=null)refresh.Dispose();hubLogo.Dispose();}
}
internal sealed class PetWindow : Form {
    [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr insertAfter,int x,int y,int width,int height,uint flags);
    [StructLayout(LayoutKind.Sequential)]struct NativePoint {public int X,Y;public NativePoint(int x,int y){X=x;Y=y;}}
    [StructLayout(LayoutKind.Sequential)]struct NativeSize {public int Width,Height;public NativeSize(int w,int h){Width=w;Height=h;}}
    [StructLayout(LayoutKind.Sequential,Pack=1)]struct Blend {public byte Op,Flags,Alpha,Format;}
    [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr value);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll",SetLastError=true)]static extern bool UpdateLayeredWindow(IntPtr window,IntPtr screen,ref NativePoint destination,ref NativeSize size,IntPtr source,ref NativePoint origin,uint key,ref Blend blend,uint flags);
    readonly PetAtlas atlas;readonly PetPainter painter;readonly PetPreferences settings;readonly PetPack pack;
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=33};readonly Stopwatch watch=Stopwatch.StartNew();
    readonly ContextMenuStrip menu=new ContextMenuStrip{ShowImageMargin=false};
    double last,checkedSurface,actionStarted;int lastRow=-1;Rectangle previousBounds;IntPtr previousWindow,lastForeground;bool disposed,lastScope;float preferredScale,petFactor=1,capsuleFactor=1;
    PetSurface surface;Bitmap presented;Point cursorStart;PointF positionStart;bool mouseHeld,moved,expanded,resetTab;int tokenRange;
    public readonly PetMotion Motion=new PetMotion(Environment.TickCount);
    public PetQuotaSnapshot Quota;public PetTokenSnapshot Tokens;public string QuotaStatus="正在读取账号额度…";
    public PetActivity Activity=new PetActivity();
    public Func<PetSurface> SurfaceProvider;public bool UseForegroundFilter=false;
    public event Action OpenSettings,RefreshQuota,PreferenceChanged;
    public bool Expanded {get{return expanded;}}
    public float RenderScale {get;private set;}
    public float PetFactor {get{return petFactor;}}
    public float CapsuleFactor {get{return capsuleFactor;}}
    public PetLayout Geometry {get{return new PetLayout(expanded,petFactor,capsuleFactor);}}
    public Rectangle AllowedBounds {get{return surface==null?Rectangle.Empty:surface.Bounds;}}
    public static PetWindow Create(PetPack pack,PetPreferences settings,string root){
        IntPtr previous=IntPtr.Zero;try{previous=SetThreadDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
        try{var window=new PetWindow(pack,settings,root);IntPtr handle=window.Handle;try{uint dpi=GetDpiForWindow(handle);if(dpi>0)window.preferredScale=window.RenderScale=dpi/96F;}catch(EntryPointNotFoundException){}window.ClientSize=window.PhysicalSize();return window;}
        finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
    }
    public PetWindow(PetPack pack,PetPreferences preferences,string root){
        this.pack=pack;settings=preferences;atlas=new PetAtlas(pack);painter=new PetPainter(root);Text="CodexHub · "+pack.Name;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;TopMost=true;AutoScaleMode=AutoScaleMode.None;Cursor=Cursors.Hand;
        lastScope=settings.CodexOnly;petFactor=settings.Scale/100F;capsuleFactor=settings.CapsuleScale/100F;using(var g=Graphics.FromHwnd(IntPtr.Zero))preferredScale=RenderScale=g.DpiX/96F;
        ClientSize=PhysicalSize();menu.BackColor=Color.FromArgb(27,32,37);menu.ForeColor=Color.FromArgb(231,240,238);
        menu.Items.Add("桌面宠物设置",null,delegate{if(OpenSettings!=null)OpenSettings();});
        menu.Items.Add("刷新额度",null,delegate{if(RefreshQuota!=null)RefreshQuota();});
        menu.Items.Add("打个招呼",null,delegate{Interact(3,true);});
        menu.Items.Add("跳一下",null,delegate{Interact(4,true);});
        var actions=new ToolStripMenuItem("动作预览");string[] names={"待机","向右行走","向左行走","打招呼","跳跃","碰撞反馈","等待","工作","检查"};for(int i=0;i<names.Length;i++){int row=i;actions.DropDownItems.Add(names[i],null,delegate{Interact(row,false);});}menu.Items.Add(actions);
        if(pack.Actions!=null){var all=new ToolStripMenuItem("白龙完整动作");for(int i=0;i<pack.Actions.Count;i++){int row=9+i;all.DropDownItems.Add(pack.Actions[i].name,null,delegate{Interact(row,false);});}menu.Items.Add(all);}
        menu.Items.Add(new ToolStripSeparator());
        var range=new ToolStripMenuItem("活动范围");ToolStripMenuItem inCodex=new ToolStripMenuItem("仅在 Codex 窗口内"),onDesktop=new ToolStripMenuItem("整个桌面（可跨屏拖动）");inCodex.Click+=delegate{SetScope(true);};onDesktop.Click+=delegate{SetScope(false);};range.DropDownItems.Add(inCodex);range.DropDownItems.Add(onDesktop);menu.Items.Add(range);
        var roam=new ToolStripMenuItem("随机行走"){Checked=settings.Roaming,CheckOnClick=true};roam.Click+=delegate{settings.Roaming=roam.Checked;if(PreferenceChanged!=null)PreferenceChanged();};menu.Items.Add(roam);
        menu.Items.Add("隐藏宠物",null,delegate{settings.Enabled=false;if(PreferenceChanged!=null)PreferenceChanged();});
        menu.Opening+=delegate{inCodex.Checked=settings.CodexOnly;onDesktop.Checked=!settings.CodexOnly;roam.Checked=settings.Roaming;};HubPopupMenu.Apply(menu);
        SurfaceProvider=CodexPetSurface.Find;timer.Tick+=delegate{Tick();};timer.Start();
        Motion.Row=3;Motion.ReactionRemaining=1.3;
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var value=base.CreateParams;value.ExStyle|=0x80000|0x80|0x08000000;return value;}}
    Size PhysicalSize(){var logical=Geometry.Size;return new Size((int)Math.Ceiling(logical.Width*RenderScale),(int)Math.Ceiling(logical.Height*RenderScale));}
    public void SetScope(bool codexOnly){settings.X=(int)Motion.Position.X;settings.Y=(int)Motion.Position.Y;settings.CodexOnly=lastScope=codexOnly;checkedSurface=0;previousBounds=Rectangle.Empty;surface=null;if(PreferenceChanged!=null)PreferenceChanged();AdvanceOnce();}
    void Tick(){
        double now=watch.Elapsed.TotalSeconds,delta=last==0?.033:now-last;last=now;
        if(!settings.Enabled){if(Visible)Hide();return;}
        if(lastScope!=settings.CodexOnly){lastScope=settings.CodexOnly;settings.X=(int)Motion.Position.X;settings.Y=(int)Motion.Position.Y;checkedSurface=0;previousBounds=Rectangle.Empty;surface=null;}
        if(now-checkedSurface>.25||checkedSurface==0){
            checkedSurface=now;
            PetSurface current=settings.CodexOnly?SurfaceProvider():new PetSurface(Screen.FromPoint(Motion.Position.X==0&&Motion.Position.Y==0?Cursor.Position:Point.Round(Motion.Position)).WorkingArea,IntPtr.Zero);
            if(current!=null&&settings.CodexOnly){var visible=Rectangle.Intersect(current.Bounds,Screen.FromRectangle(current.Bounds).WorkingArea);current=new PetSurface(visible,current.Window);}
            if(current==null){surface=null;if(Visible)Hide();return;}
            FitScale(current.Bounds);if(current.Bounds.Width<Width||current.Bounds.Height<Height){surface=null;if(Visible)Hide();return;}
            surface=current;
            if(previousBounds==Rectangle.Empty){Motion.Position=settings.X!=int.MinValue&&settings.Y!=int.MinValue?new PointF(settings.X,settings.Y):new PointF(current.Bounds.Right-Width-22,current.Bounds.Bottom-Height-12);}
            else if(settings.CodexOnly&&previousWindow==current.Window){Motion.Position=new PointF(Motion.Position.X+current.Bounds.X-previousBounds.X,Motion.Position.Y+current.Bounds.Y-previousBounds.Y);}
            previousBounds=current.Bounds;previousWindow=current.Window;
        }
        if(surface==null)return;
        FitScale(surface.Bounds);Motion.Place(Motion.Position,Size,surface.Bounds);
        if(!HubPopupMenu.AnyOpen){if(Activity.Active&&!Motion.Dragging&&Motion.ReactionRemaining<=0){Motion.Velocity=PointF.Empty;Motion.Row=7;}else Motion.Step(delta,Size,surface.Bounds,settings.Roaming);}Location=Point.Round(Motion.Position);
        if(!Visible)Show();MaintainTopmost();Present();
    }
    public void AdvanceOnce(){Tick();}
    void MaintainTopmost(){if(HubPopupMenu.AnyOpen)return;IntPtr foreground=GetForegroundWindow();if(foreground==lastForeground)return;lastForeground=foreground;SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x0013);}
    void FitScale(Rectangle bounds){
        if(preferredScale<=0||bounds.Width<1||bounds.Height<1)return;RenderScale=preferredScale;float width=(bounds.Width-1)/RenderScale,height=(bounds.Height-1)/RenderScale,header=expanded?232:54;
        capsuleFactor=Math.Max(.05F,Math.Min(settings.CapsuleScale/100F,Math.Min(width/280F,(height-24)/header)));
        petFactor=Math.Max(.05F,Math.Min(settings.Scale/100F,Math.Min(width/208F,(height-header*capsuleFactor-8)/208F)));
        Size next=PhysicalSize();if(next!=ClientSize){if(previousBounds!=Rectangle.Empty)Motion.Position=new PointF(Motion.Position.X-(next.Width-Width)/2F,Motion.Position.Y);ClientSize=next;}
    }
    void Present(){
        if(IsDisposed||!IsHandleCreated)return;double time=watch.Elapsed.TotalSeconds;if(Motion.Row!=lastRow){lastRow=Motion.Row;actionStarted=time;}var frame=painter.Render(atlas,Motion,expanded,settings.ShowQuota,Quota,QuotaStatus,time-actionStarted,RenderScale,Activity.Label,Activity.Active,time,Tokens,tokenRange,resetTab,petFactor,capsuleFactor);
        IntPtr screen=GetDC(IntPtr.Zero),dc=CreateCompatibleDC(screen),bitmap=IntPtr.Zero,previous=IntPtr.Zero;
        try{bitmap=frame.GetHbitmap(Color.FromArgb(0));previous=SelectObject(dc,bitmap);var point=new NativePoint(Left,Top);var origin=new NativePoint(0,0);var size=new NativeSize(frame.Width,frame.Height);var blend=new Blend{Alpha=255,Format=1};if(!UpdateLayeredWindow(Handle,screen,ref point,ref size,dc,ref origin,0,ref blend,2))throw new Win32Exception();}
        finally{if(previous!=IntPtr.Zero)SelectObject(dc,previous);if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);DeleteDC(dc);ReleaseDC(IntPtr.Zero,screen);}
        if(presented!=null)presented.Dispose();presented=frame;
    }
    public Bitmap Snapshot(){return painter.Render(atlas,Motion,expanded,settings.ShowQuota,Quota,QuotaStatus,watch.Elapsed.TotalSeconds-actionStarted,RenderScale,Activity.Label,Activity.Active,watch.Elapsed.TotalSeconds,Tokens,tokenRange,resetTab,petFactor,capsuleFactor);}
    public void ToggleExpanded(){
        var previous=Geometry;expanded=!expanded;if(surface!=null)FitScale(surface.Bounds);else ClientSize=PhysicalSize();var next=Geometry;Motion.Position=new PointF(Motion.Position.X,Motion.Position.Y+(previous.Sprite.Y-next.Sprite.Y)*RenderScale);if(surface!=null)Motion.Place(Motion.Position,Size,surface.Bounds);Location=Point.Round(Motion.Position);Present();
    }
    public void Interact(int row,bool sound){double duration=pack.Actions!=null?pack.Actions[row<9?pack.RowMap[row]:row-9].durationMs/1000D:1.3;Motion.Interact(row,duration);if(sound&&settings.Sound)PetSound.Play();Present();}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Right){menu.Show(this,e.Location);return;}if(e.Button!=MouseButtons.Left)return;mouseHeld=true;moved=false;cursorStart=Cursor.Position;positionStart=Motion.Position;Capture=true;}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);DragTo(Cursor.Position);}
    internal void DragTo(Point cursor){if(!mouseHeld)return;int dx=cursor.X-cursorStart.X,dy=cursor.Y-cursorStart.Y;if(Math.Abs(dx)+Math.Abs(dy)<4&&!moved)return;moved=true;
        Rectangle bounds=settings.CodexOnly?(surface==null?Rectangle.Empty:surface.Bounds):Screen.FromPoint(cursor).WorkingArea;FitScale(bounds);if(bounds.Width<Width||bounds.Height<Height)return;
        if(!settings.CodexOnly)surface=new PetSurface(bounds,IntPtr.Zero);Motion.Drag(new PointF(positionStart.X+dx,positionStart.Y+dy),Size,bounds);if(pack.Actions!=null)Motion.Row=4;Location=Point.Round(Motion.Position);Present();
    }
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button!=MouseButtons.Left||!mouseHeld)return;mouseHeld=false;Capture=false;if(moved){Motion.Drop();settings.X=Left;settings.Y=Top;if(PreferenceChanged!=null)PreferenceChanged();}
        else if(settings.ShowQuota&&Geometry.Quota.Contains(e.X/RenderScale,e.Y/RenderScale)){
            PointF point=Geometry.CapsulePoint(new PointF(e.X/RenderScale,e.Y/RenderScale));float x=point.X,y=point.Y;
            if(expanded&&x>=214&&x<=239&&y<36){if(RefreshQuota!=null)RefreshQuota();}
            else if(expanded&&x>=145&&x<=264&&y>=77&&y<=98){resetTab=x>=205;Present();}
            else if(expanded&&!resetTab&&x>=149&&x<=260&&y>=99&&y<=115){tokenRange=Math.Max(0,Math.Min(2,(int)((x-149)/37)));Present();}
            else if(expanded&&x>=145&&x<=264&&y>=77&&y<=181){}
            else if(!expanded&&x<54){if(OpenSettings!=null)OpenSettings();}else ToggleExpanded();
        }
    }
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture&&mouseHeld){mouseHeld=false;if(Motion.Dragging)Motion.Drop();}}
    protected override void OnMouseDoubleClick(MouseEventArgs e){base.OnMouseDoubleClick(e);if(e.Button==MouseButtons.Left&&(!settings.ShowQuota||!Geometry.Quota.Contains(e.X/RenderScale,e.Y/RenderScale)))Interact(3,true);}
    protected override void WndProc(ref Message message){
        if(message.Msg==0x02E0){preferredScale=RenderScale=(message.WParam.ToInt64()&0xffff)/96F;if(surface!=null)FitScale(surface.Bounds);ClientSize=PhysicalSize();if(surface!=null)Motion.Place(Motion.Position,Size,surface.Bounds);Present();return;}
        if(message.Msg==0x21){message.Result=(IntPtr)3;return;}
        if(message.Msg==0x84&&presented!=null){int x=unchecked((short)(long)message.LParam)-Left,y=unchecked((short)((long)message.LParam>>16))-Top;if(x<0||y<0||x>=presented.Width||y>=presented.Height||presented.GetPixel(x,y).A<16){message.Result=(IntPtr)(-1);return;}}
        base.WndProc(ref message);
    }
    protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;timer.Stop();timer.Dispose();menu.Dispose();atlas.Dispose();painter.Dispose();if(presented!=null){presented.Dispose();presented=null;}}base.Dispose(disposing);}
}
internal static class PetSound {
    public static byte[] Create(){
        const int rate=22050,count=6600;using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(count*2);
            for(int i=0;i<count;i++){double t=i/(double)rate,frequency=i<count/2?659.255:880,envelope=Math.Sin(Math.PI*(i%(count/2))/(count/2))*Math.Exp(-3*(i%(count/2))/(double)(count/2));writer.Write((short)(Math.Sin(2*Math.PI*frequency*t)*envelope*4800));}writer.Flush();return stream.ToArray();
        }
    }
    public static void Play(){System.Threading.ThreadPool.QueueUserWorkItem(delegate{try{using(var stream=new MemoryStream(Create()))using(var sound=new SoundPlayer(stream))sound.PlaySync();}catch(InvalidOperationException){}});}
}
