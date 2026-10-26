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
// Manager stays system-DPI aware. Only pet windows and monitor geometry use
// physical pixels, including callbacks arriving from its system-DPI timer.
internal sealed class PetDpi : IDisposable {
    [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);readonly IntPtr previous;
    PetDpi(){try{previous=SetThreadDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}}
    public static PetDpi Enter(){return new PetDpi();}public void Dispose(){if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
}
internal sealed class PetDisplay {
    public string DeviceName;public Rectangle Bounds,WorkingArea;
}
internal static class PetDisplays {
    [StructLayout(LayoutKind.Sequential)]struct Rect {public int Left,Top,Right,Bottom;public Rectangle Box{get{return Rectangle.FromLTRB(Left,Top,Right,Bottom);}}}
    [StructLayout(LayoutKind.Sequential)]struct PointI {public int X,Y;public PointI(Point point){X=point.X;Y=point.Y;}}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Info {public int Size;public Rect Monitor,Work;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;}
    delegate bool MonitorCallback(IntPtr monitor,IntPtr dc,IntPtr bounds,IntPtr data);
    [DllImport("user32.dll")]static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorCallback callback,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetMonitorInfo(IntPtr monitor,ref Info info);
    [DllImport("user32.dll")]static extern IntPtr MonitorFromPoint(PointI point,uint flags);
    [DllImport("user32.dll")]static extern IntPtr MonitorFromRect(ref Rect bounds,uint flags);
    static PetDisplay Read(IntPtr monitor){var info=new Info{Size=Marshal.SizeOf(typeof(Info))};if(!GetMonitorInfo(monitor,ref info))throw new Win32Exception();return new PetDisplay{DeviceName=info.Device,Bounds=info.Monitor.Box,WorkingArea=info.Work.Box};}
    public static PetDisplay[] All{get{using(PetDpi.Enter()){var result=new List<PetDisplay>();EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,(monitor,dc,bounds,data)=>{result.Add(Read(monitor));return true;},IntPtr.Zero);return result.ToArray();}}}
    public static PetDisplay At(Point point){using(PetDpi.Enter())return Read(MonitorFromPoint(new PointI(point),2));}
    public static PetDisplay At(Rectangle bounds){using(PetDpi.Enter()){var native=new Rect{Left=bounds.Left,Top=bounds.Top,Right=bounds.Right,Bottom=bounds.Bottom};return Read(MonitorFromRect(ref native,2));}}
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
    [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attr,out int value,int size);
    internal static bool MatchesRegisteredImage(string executable,string installed,string fullName){return CodexPresence.MatchesRegisteredImage(executable,installed,fullName);}
    static bool OfficialProcess(Process process){return CodexPresence.OfficialProcess(process);}
    public static PetSurface Find(){using(PetDpi.Enter())return FindPhysical();}
    static PetSurface FindPhysical(){
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
    public static uint Owner(IntPtr window){uint id;GetWindowThreadProcessId(window,out id);return id;}
    public static PetSurface RefreshKnown(IntPtr window,uint owner){using(PetDpi.Enter()){if(window==IntPtr.Zero||owner==0||Owner(window)!=owner||!IsWindowVisible(window)||IsIconic(window))return null;int cloaked;if(DwmGetWindowAttribute(window,14,out cloaked,4)==0&&cloaked!=0)return null;Rect client;var origin=new Point();if(!GetClientRect(window,out client)||!ClientToScreen(window,ref origin)||client.Right<240||client.Bottom<300)return null;return new PetSurface(new Rectangle(origin.X,origin.Y,client.Right,client.Bottom),window);}}
}
internal sealed class PetLayout {
    public readonly Size Size;public readonly RectangleF Quota,Sprite;public readonly float PetScale,CapsuleScale,QuotaOffset;
    public PetLayout(bool expanded,float petScale=1,float capsuleScale=1,float reveal=-1,bool wide=false,bool bodyOnly=false){
        PetScale=Math.Max(.05F,petScale);CapsuleScale=Math.Max(.05F,capsuleScale);
        if(bodyOnly){Size=new Size((int)Math.Ceiling((wide?512:192)*PetScale),(int)Math.Ceiling((wide?288:208)*PetScale+8));Sprite=new RectangleF(0,0,(wide?512:192)*PetScale,(wide?288:208)*PetScale);Quota=RectangleF.Empty;QuotaOffset=0;return;}
        if(reveal<0)reveal=expanded?1:0;reveal=Math.Max(0,Math.Min(1,reveal));
        int width=(int)Math.Ceiling(Math.Max(280*CapsuleScale,(wide?512:208)*PetScale));QuotaOffset=(width-280*CapsuleScale)/2;
        Quota=new RectangleF(QuotaOffset+6*CapsuleScale,5*CapsuleScale,268*CapsuleScale,(46+174*reveal)*CapsuleScale);
        Sprite=new RectangleF((width-(wide?512:192)*PetScale)/2,(54+178*reveal)*CapsuleScale,(wide?512:192)*PetScale,(wide?288:208)*PetScale);
        Size=new Size(width,(int)Math.Ceiling(Sprite.Bottom+8));
    }
    public PointF CapsulePoint(PointF point){return new PointF((point.X-QuotaOffset)/CapsuleScale,point.Y/CapsuleScale);}
}
internal sealed class PetPainter : IDisposable {
    readonly PrivateFontCollection fonts=new PrivateFontCollection();readonly FontFamily numbers;readonly Image hubLogo=IconAssets.Create(28);readonly Dictionary<string,GraphicsPath> dialLabels=new Dictionary<string,GraphicsPath>();
    readonly Font compactLabel=new Font("Microsoft YaHei UI",11.2F,FontStyle.Bold,GraphicsUnit.Pixel);Color hubTint=Color.FromArgb(78,143,228),capsuleTint=Color.FromArgb(78,143,228),ringTint=Color.FromArgb(78,143,228);int capsuleAlpha=240;string lastHub,lastCapsule,lastRing;
    public void ApplyAppearance(PetPreferences settings){if(settings.HubColor!=lastHub){lastHub=settings.HubColor;hubTint=ColorTranslator.FromHtml(PetPreferences.NormalizeColor(lastHub));}if(settings.CapsuleColor!=lastCapsule){lastCapsule=settings.CapsuleColor;capsuleTint=ColorTranslator.FromHtml(PetPreferences.NormalizeColor(lastCapsule));}if(settings.RingColor!=lastRing){lastRing=settings.RingColor;ringTint=ColorTranslator.FromHtml(PetPreferences.NormalizeColor(lastRing));}capsuleAlpha=(int)Math.Round(Math.Max(0,Math.Min(100,settings.CapsuleOpacity))*2.55);}
    public PetPainter(string root){foreach(string name in new[]{"ChakraPetch-Regular.ttf","ChakraPetch-Bold.ttf"}){string file=Path.Combine(root,@"source\assets\fonts",name);if(File.Exists(file))try{fonts.AddFontFile(file);}catch(ArgumentException){}}numbers=fonts.Families.Length>0?fonts.Families[0]:FontFamily.GenericSansSerif;}
    public static Size LogicalSize(bool expanded,float petScale=1,float capsuleScale=1){return new PetLayout(expanded,petScale,capsuleScale).Size;}
    public static RectangleF QuotaRect(bool expanded,float petScale=1,float capsuleScale=1){return new PetLayout(expanded,petScale,capsuleScale).Quota;}
    public static RectangleF SpriteRect(bool expanded,float petScale=1,float capsuleScale=1){return new PetLayout(expanded,petScale,capsuleScale).Sprite;}
    static void Text(Graphics g,string text,Font font,Color color,RectangleF rectangle,StringAlignment align){using(var brush=new SolidBrush(color))using(var format=new StringFormat{Alignment=align,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(text,font,brush,rectangle,format);}
    public Bitmap Render(PetAtlas atlas,PetMotion motion,bool expanded,bool quotaVisible,PetQuotaSnapshot quota,string status,double clock,float scale,string activityLabel=null,bool activityBusy=false,double? animationClock=null,PetTokenSnapshot tokens=null,int tokenRange=0,bool resetTab=false,float petScale=1,float capsuleScale=1,bool refreshing=false,float panelReveal=-1,bool taskPanel=false,PetActivity activity=null,int taskIndex=0,bool bodyOnly=false){
        float reveal=panelReveal<0?(expanded?1:0):Math.Max(0,Math.Min(1,panelReveal));var layout=new PetLayout(expanded,petScale,capsuleScale,reveal,atlas.IsVideo,bodyOnly);Size size=layout.Size;var image=new Bitmap((int)Math.Ceiling(size.Width*scale),(int)Math.Ceiling(size.Height*scale),PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(image)){
            g.Clear(Color.Transparent);g.ScaleTransform(scale,scale);g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
            using(var number=new Font(numbers,16,FontStyle.Bold,GraphicsUnit.Pixel))using(var smallNumber=new Font(numbers,11,FontStyle.Bold,GraphicsUnit.Pixel))using(var label=new Font("Microsoft YaHei UI",10,FontStyle.Bold,GraphicsUnit.Pixel))using(var tiny=new Font("Microsoft YaHei UI",8.5F,FontStyle.Regular,GraphicsUnit.Pixel)){
                if(quotaVisible){var transform=g.Save();g.TranslateTransform(layout.QuotaOffset,0);g.ScaleTransform(layout.CapsuleScale,layout.CapsuleScale);
                    if(reveal==0)DrawQuota(g,false,quota,status,number,smallNumber,label,tiny,activityLabel,activityBusy,animationClock??clock,tokens,tokenRange,resetTab,refreshing);
                    else if(reveal==1){if(taskPanel)DrawTasks(g,activity,taskIndex,smallNumber,label,tiny,animationClock??clock);else DrawQuota(g,true,quota,status,number,smallNumber,label,tiny,activityLabel,activityBusy,animationClock??clock,tokens,tokenRange,resetTab,refreshing);}
                    else{float raster=scale*layout.CapsuleScale;using(var layer=new Bitmap((int)Math.Ceiling(280*raster),(int)Math.Ceiling(228*raster),PixelFormat.Format32bppPArgb))using(var lg=Graphics.FromImage(layer)){lg.ScaleTransform(raster,raster);lg.SmoothingMode=SmoothingMode.AntiAlias;lg.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;if(taskPanel)DrawTasks(lg,activity,taskIndex,smallNumber,label,tiny,animationClock??clock);else DrawQuota(lg,true,quota,status,number,smallNumber,label,tiny,activityLabel,activityBusy,animationClock??clock,tokens,tokenRange,resetTab,refreshing);var clip=g.Save();g.SetClip(new RectangleF(0,0,280,51+174*reveal));using(var attributes=new ImageAttributes()){var matrix=new ColorMatrix();matrix.Matrix33=reveal;attributes.SetColorMatrix(matrix);g.DrawImage(layer,new Rectangle(0,0,280,228),0,0,layer.Width,layer.Height,GraphicsUnit.Pixel,attributes);}g.Restore(clip);}}
                    g.Restore(transform);}
                var sprite=layout.Sprite;float progress=motion.CollisionDirection>=0?(float)(motion.ReactionRemaining/Math.Max(.001,motion.ReactionDuration)):0;
                if(progress>0){float squash=(float)Math.Sin((1-progress)*Math.PI)*.16F;if(motion.CollisionDirection<2){sprite.X+=sprite.Width*squash/2;sprite.Width*=1-squash;sprite.Y-=sprite.Height*squash*.2F;sprite.Height*=1+squash*.2F;}else{sprite.Y+=sprite.Height*squash;sprite.Height*=1-squash;sprite.X-=sprite.Width*squash*.15F;sprite.Width*=1+squash*.3F;}}
                if(motion.Row==4&&motion.ReactionRemaining>0)sprite.Y-=(float)Math.Sin((1-motion.ReactionRemaining/motion.ReactionDuration)*Math.PI)*14*layout.PetScale;
                if(motion.ClickResponse&&motion.ReactionRemaining>0){double t=1-motion.ReactionRemaining/motion.ReactionDuration;sprite.X+=(float)(Math.Sin(t*Math.PI*4)*Math.Sin(t*Math.PI)*Math.Exp(-t*2))*2*layout.PetScale;}
                float press=Math.Max(.9F,Math.Min(1,motion.PressScale));sprite.X+=sprite.Width*(1-press)/2;sprite.Y+=sprite.Height*(1-press);sprite.Width*=press;sprite.Height*=press;
                var bodyState=g.Save();if(progress>0){float t=1-progress,pulse=(float)(Math.Sin(Math.PI*Math.Min(1,t*1.5))*Math.Exp(-t*1.6));float angle=motion.CollisionDirection==0?10*pulse:motion.CollisionDirection==1?-10*pulse:(float)Math.Sin(t*Math.PI*3)*3*(1-t);float dx=motion.CollisionDirection==0?12*pulse:motion.CollisionDirection==1?-12*pulse:0,dy=motion.CollisionDirection==2?10*pulse:motion.CollisionDirection==3?-10*pulse:0;float ax=sprite.Left+sprite.Width/2,ay=sprite.Bottom;g.TranslateTransform(ax+dx,ay+dy);g.RotateTransform(angle);g.TranslateTransform(-ax,-ay);}atlas.Draw(g,sprite,motion.Row,clock);g.Restore(bodyState);
                if(progress>0){var contact=layout.Sprite;if(atlas.IsVideo)contact=new RectangleF(contact.X+contact.Width*.3125F,contact.Y+contact.Height*.139F,contact.Width*.375F,contact.Height*.79F);Collision(g,contact,motion.CollisionDirection,1-progress);}
                if(motion.ClickResponse&&motion.ReactionRemaining>0)Collision(g,layout.Sprite,2,(float)(1-motion.ReactionRemaining/motion.ReactionDuration));
            }
        }return image;
    }
    public Bitmap RenderPanel(float scale,bool tasks,PetQuotaSnapshot quota,string status,string activityLabel,bool busy,double clock,PetTokenSnapshot tokens,int range,bool reset,bool refreshing,PetActivity activity,int index){
        var image=new Bitmap(Math.Max(1,(int)Math.Ceiling(280*scale)),Math.Max(1,(int)Math.Ceiling(228*scale)),PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(image)){g.ScaleTransform(scale,scale);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;using(var number=new Font(numbers,16,FontStyle.Bold,GraphicsUnit.Pixel))using(var small=new Font(numbers,11,FontStyle.Bold,GraphicsUnit.Pixel))using(var label=new Font("Microsoft YaHei UI",10,FontStyle.Bold,GraphicsUnit.Pixel))using(var tiny=new Font("Microsoft YaHei UI",8.5F,FontStyle.Regular,GraphicsUnit.Pixel)){if(tasks)DrawTasks(g,activity,index,small,label,tiny,clock);else DrawQuota(g,true,quota,status,number,small,label,tiny,activityLabel,busy,clock,tokens,range,reset,refreshing);}}return image;
    }
    public Bitmap RenderCapsule(float scale,PetQuotaSnapshot quota,string status,string activityLabel,bool busy,double clock){var image=new Bitmap((int)Math.Ceiling(280*scale),(int)Math.Ceiling(54*scale),PixelFormat.Format32bppPArgb);using(var g=Graphics.FromImage(image)){g.ScaleTransform(scale,scale);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;using(var number=new Font(numbers,16,FontStyle.Bold,GraphicsUnit.Pixel))using(var small=new Font(numbers,11,FontStyle.Bold,GraphicsUnit.Pixel))using(var label=new Font("Microsoft YaHei UI",10,FontStyle.Bold,GraphicsUnit.Pixel))using(var tiny=new Font("Microsoft YaHei UI",8.5F,FontStyle.Regular,GraphicsUnit.Pixel))DrawQuota(g,false,quota,status,number,small,label,tiny,activityLabel,busy,clock,null,0,false,false);}return image;}
    public static RectangleF PanelShape(RectangleF origin,float progress){float t=Math.Max(0,Math.Min(1,progress));return new RectangleF(origin.X+(6-origin.X)*t,origin.Y+(5-origin.Y)*t,origin.Width+(268-origin.Width)*t,origin.Height+(220-origin.Height)*t);}
    public Bitmap MorphPanel(Bitmap complete,float scale,RectangleF origin,float progress){if(progress>=1)return new Bitmap(complete);var image=new Bitmap(complete.Width,complete.Height,PixelFormat.Format32bppPArgb);var bounds=PanelShape(origin,progress);float content=Math.Max(0,Math.Min(1,(progress-.18F)/.72F));using(var g=Graphics.FromImage(image)){g.ScaleTransform(scale,scale);g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;using(var shape=IconAssets.Round(bounds,20-5*progress))using(var fill=new SolidBrush(Color.FromArgb(255,(int)((origin.Width<60?247:23)*(1-progress)+247*progress),(int)((origin.Width<60?251:43)*(1-progress)+250*progress),(int)((origin.Width<60?255:73)*(1-progress)+255*progress))))using(var border=new Pen(Color.FromArgb(160,167,198,238))){g.FillPath(fill,shape);g.SetClip(shape);float sx=bounds.Width/268F,sy=bounds.Height/220F;using(var attributes=new ImageAttributes()){var matrix=new ColorMatrix();matrix.Matrix33=content;attributes.SetColorMatrix(matrix);g.DrawImage(complete,new[]{new PointF(bounds.Left-6*sx,bounds.Top-5*sy),new PointF(bounds.Left+274*sx,bounds.Top-5*sy),new PointF(bounds.Left-6*sx,bounds.Top+223*sy)},new RectangleF(0,0,complete.Width,complete.Height),GraphicsUnit.Pixel,attributes);}g.ResetClip();g.DrawPath(border,shape);}}return image;}
    void DrawTasks(Graphics g,PetActivity activity,int index,Font number,Font label,Font tiny,double clock){
        Color ink=Color.FromArgb(32,58,98),muted=Color.FromArgb(102,128,167),accent=Color.FromArgb(78,143,228),track=Color.FromArgb(222,233,247);
        using(var shape=IconAssets.Round(QuotaRect(true),15))using(var fill=new SolidBrush(Color.FromArgb(251,247,250,255)))using(var border=new Pen(Color.FromArgb(181,207,241))){g.FillPath(fill,shape);g.DrawPath(border,shape);}
        Text(g,"CodexHub · 当前任务",number,ink,new RectangleF(20,12,218,21),StringAlignment.Near);using(var pen=new Pen(accent,1.4F)){g.DrawLine(pen,249,18,256,22);g.DrawLine(pen,249,26,256,22);}using(var line=new Pen(track))g.DrawLine(line,16,36,264,36);
        PetTaskInfo[] tasks=activity==null?new PetTaskInfo[0]:activity.Tasks;var task=tasks.Length==0?null:tasks[index%tasks.Length];
        Text(g,task==null?"等待 Codex 任务":task.Phase,label,accent,new RectangleF(19,43,213,17),StringAlignment.Near);
        if(task!=null&&task.Active){var saved=g.Save();g.TranslateTransform(251,51);g.RotateTransform((float)(clock*220%360));using(var pen=new Pen(accent,1.4F))g.DrawArc(pen,-4,-4,8,8,30,270);g.Restore(saved);}
        using(var brush=new SolidBrush(ink))using(var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter})g.DrawString(task==null?"开始一次对话后，这里会显示任务和执行操作。":task.Title,label,brush,new RectangleF(19,65,240,34),format);
        string workspace="";if(task!=null&&!String.IsNullOrEmpty(task.Workspace))try{workspace=Path.GetFileName(task.Workspace.TrimEnd(Path.DirectorySeparatorChar));}catch(ArgumentException){}
        Text(g,workspace,tiny,muted,new RectangleF(19,101,240,13),StringAlignment.Near);using(var line=new Pen(track))g.DrawLine(line,17,120,263,120);Text(g,"执行操作",label,muted,new RectangleF(19,124,240,16),StringAlignment.Near);
        string[] operations=task==null?new string[0]:task.Operations;for(int i=0;i<Math.Min(4,operations.Length);i++)Text(g,operations[operations.Length-Math.Min(4,operations.Length)+i],tiny,ink,new RectangleF(19,145+i*14,240,14),StringAlignment.Near);
        if(operations.Length==0)Text(g,"等待新的执行操作…",tiny,muted,new RectangleF(19,146,240,18),StringAlignment.Near);
        Text(g,tasks.Length>1?(index%tasks.Length+1)+" / "+tasks.Length+" 项任务 · 点击标题切换":"本机任务状态 · 不显示聊天正文",tiny,muted,new RectangleF(19,207,240,13),StringAlignment.Near);
    }
    void DrawQuota(Graphics g,bool expanded,PetQuotaSnapshot quota,string status,Font number,Font smallNumber,Font label,Font tiny,string activityLabel,bool activityBusy,double clock,PetTokenSnapshot tokens,int tokenRange,bool resetTab,bool refreshing){
        Color ink=Color.FromArgb(32,58,98),muted=Color.FromArgb(102,128,167),accent=Color.FromArgb(78,143,228),track=Color.FromArgb(222,233,247);
        var first=quota==null?null:quota.Short;var week=quota==null?null:quota.Week;
        if(!expanded){
            using(var path=IconAssets.Round(new RectangleF(57,8,165,37),18))using(var fill=new SolidBrush(Color.FromArgb(capsuleAlpha,(int)(capsuleTint.R*.30),(int)(capsuleTint.G*.30),(int)(capsuleTint.B*.32))))using(var pen=new Pen(Color.FromArgb((int)(capsuleAlpha*.64),capsuleTint))){g.FillPath(fill,path);g.DrawPath(pen,path);}
            using(var fill=new SolidBrush(Color.FromArgb((hubTint.R+765)/4,(hubTint.G+765)/4,(hubTint.B+765)/4)))g.FillEllipse(fill,9,7,40,40);
            using(var pen=new Pen(hubTint,2))g.DrawEllipse(pen,10,8,38,38);
            g.DrawImage(hubLogo,new RectangleF(16,14,26,26));
            using(var pen=new Pen(Color.FromArgb((capsuleTint.R+255)/2,(capsuleTint.G+255)/2,(capsuleTint.B+255)/2),1.5F)){for(int i=0;i<3;i++){double angle=i*Math.PI*2/3-Math.PI/2+(activityBusy?clock*2.8:0);float x=74+(float)Math.Cos(angle)*6,y=25+(float)Math.Sin(angle)*6;g.DrawEllipse(pen,x-1.5F,y-1.5F,3,3);}}
            string name=activityLabel??(quota==null?"等待 Codex":quota.Stale?"额度待读取":"Codex · 陪伴中");
            Text(g,name,compactLabel,Color.FromArgb(233,242,255),new RectangleF(88,10,124,33),StringAlignment.Near);
            DrawQuotaDial(g,new PointF(228,7),first,ringTint,ink);return;
        }
        using(var shape=IconAssets.Round(QuotaRect(true),15))using(var fill=new SolidBrush(Color.FromArgb(251,247,250,255)))using(var border=new Pen(Color.FromArgb(181,207,241),1)){g.FillPath(fill,shape);g.DrawPath(border,shape);}
        using(var pen=new Pen(accent,1.4F)){g.DrawEllipse(pen,20,17,3,3);g.DrawEllipse(pen,15,23,3,3);g.DrawEllipse(pen,25,24,3,3);g.DrawLine(pen,249,18,256,22);g.DrawLine(pen,249,26,256,22);}
        var refreshState=g.Save();if(refreshing){g.TranslateTransform(226,21);g.RotateTransform((float)(clock*300%360));g.TranslateTransform(-226,-21);}DrawRefresh(g,new RectangleF(218,13,16,16),accent);g.Restore(refreshState);
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
    public static string CompactQuotaText(PetQuotaWindow value){if(value==null||!value.Remaining.HasValue)return "—";double percent=value.Remaining.Value;if(percent>0&&percent<1)return "<1%";return (percent>=100?100:Math.Min(99,Math.Round(Math.Max(0,percent),MidpointRounding.AwayFromZero))).ToString("0")+"%";}
    GraphicsPath DialLabel(string text){
        GraphicsPath cached;if(dialLabels.TryGetValue(text,out cached))return cached;var glyphs=new GraphicsPath();string digits=text.EndsWith("%")?text.Substring(0,text.Length-1):text;
        using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())glyphs.AddString(digits,numbers,(int)FontStyle.Bold,15.4F,PointF.Empty,format);
        using(var squeeze=new Matrix()){squeeze.Scale(.92F,1);glyphs.Transform(squeeze);}
        if(text.EndsWith("%"))using(var suffix=new GraphicsPath()){using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())suffix.AddString("%",numbers,(int)FontStyle.Bold,9.8F,PointF.Empty,format);var numberBox=glyphs.GetBounds();var smallBox=suffix.GetBounds();using(var place=new Matrix()){place.Translate(numberBox.Right+.65F-smallBox.Left,numberBox.Bottom-smallBox.Bottom);suffix.Transform(place);}glyphs.AddPath(suffix,false);}
        var bounds=glyphs.GetBounds();float fit=Math.Min(1,28.2F/(float)Math.Sqrt(bounds.Width*bounds.Width+bounds.Height*bounds.Height));using(var center=new Matrix()){center.Translate(20,20);center.Scale(fit,fit);center.Translate(-(bounds.Left+bounds.Width/2),-(bounds.Top+bounds.Height/2));glyphs.Transform(center);}dialLabels.Add(text,glyphs);return glyphs;
    }
    public RectangleF QuotaLabelBounds(string text){return DialLabel(text).GetBounds();}
    void DrawQuotaDial(Graphics g,PointF origin,PetQuotaWindow value,Color accent,Color ink){
        var saved=g.Save();g.TranslateTransform(origin.X,origin.Y);g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var fill=new SolidBrush(Color.FromArgb(247,251,255)))g.FillEllipse(fill,.5F,.5F,39,39);
        using(var track=new Pen(Color.FromArgb(219,225,237),4.6F))using(var arc=new Pen(accent,4.6F)){track.StartCap=track.EndCap=arc.StartCap=arc.EndCap=LineCap.Round;var box=new RectangleF(3.4F,3.4F,33.2F,33.2F);g.DrawArc(track,box,135,270);if(value!=null&&value.Remaining.HasValue&&value.Remaining.Value>0)g.DrawArc(arc,box,135,(float)(270*Math.Min(100,value.Remaining.Value)/100));}
        using(var fill=new SolidBrush(ink))g.FillPath(fill,DialLabel(CompactQuotaText(value)));g.Restore(saved);
    }
    // Native vector construction follows Phosphor's circular-arrow proportions.
    // The arc ends at the arrow shaft; one path keeps the join continuous.
    void DrawRefresh(Graphics g,RectangleF box,Color color){
        var saved=g.Save();g.TranslateTransform(box.X,box.Y);g.ScaleTransform(box.Width/256F,box.Height/256F);
        using(var pen=new Pen(color,16)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;using(var curve=new GraphicsPath()){curve.AddArc(40,40,176,176,45,270);curve.AddLine(190.2254F,65.7746F,232,104);g.DrawPath(pen,curve);}g.DrawLines(pen,new[]{new PointF(232,56),new PointF(232,104),new PointF(184,104)});}g.Restore(saved);
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
        float factor=Math.Max(.8F,Math.Min(1.8F,sprite.Width/192)),x=direction==0?sprite.Left+sprite.Width*.16F:direction==1?sprite.Right-sprite.Width*.16F:sprite.Left+sprite.Width/2,y=direction==2?sprite.Top+sprite.Height*.12F:direction==3?sprite.Bottom-10*factor:sprite.Top+sprite.Height*.48F;
        float opacity=(float)(Math.Min(1,progress/.07)*Math.Pow(Math.Max(0,1-progress),.65)),normal=direction==0?0:direction==1?(float)Math.PI:direction==2?(float)Math.PI/2:-(float)Math.PI/2;
        for(int i=0;i<5;i++){float angle=normal+(i-2)*.5F,distance=(8+progress*(i==2?34:26))*factor,px=x+(float)Math.Cos(angle)*distance,py=y+(float)Math.Sin(angle)*distance,radius=(i==2?11F:6.5F)*(1-progress*.25F)*factor;var points=new PointF[8];for(int j=0;j<8;j++){float r=j%2==0?radius:radius*.24F;points[j]=new PointF(px+(float)Math.Cos(j*Math.PI/4)*r,py+(float)Math.Sin(j*Math.PI/4)*r);}using(var glow=new Pen(Color.FromArgb((int)(80*opacity),80,175,255),4.5F*factor)){glow.LineJoin=LineJoin.Round;g.DrawPolygon(glow,points);}using(var outline=new Pen(Color.FromArgb((int)(170*opacity),36,71,115),.8F*factor))using(var fill=new SolidBrush(Color.FromArgb((int)(255*opacity),i%2==0?Color.FromArgb(255,234,148):Color.FromArgb(213,240,255)))){g.FillPolygon(fill,points);g.DrawPolygon(outline,points);}using(var core=new SolidBrush(Color.FromArgb((int)(235*opacity),Color.White)))g.FillEllipse(core,px-1.3F*factor,py-1.3F*factor,2.6F*factor,2.6F*factor);}
        using(var line=new Pen(Color.FromArgb((int)(180*opacity),241,249,255),1.8F*factor)){line.StartCap=line.EndCap=LineCap.Round;for(int i=-1;i<=1;i++){float angle=normal+i*.65F,travel=(14+30*progress)*factor;g.DrawLine(line,x+(float)Math.Cos(angle)*travel,y+(float)Math.Sin(angle)*travel,x+(float)Math.Cos(angle)*(travel+9*factor),y+(float)Math.Sin(angle)*(travel+9*factor));}}
    }
    public void Dispose(){foreach(var path in dialLabels.Values)path.Dispose();dialLabels.Clear();compactLabel.Dispose();fonts.Dispose();hubLogo.Dispose();}
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
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=16};readonly Stopwatch watch=Stopwatch.StartNew();
    readonly ContextMenuStrip menu=new HubContextMenu{ShowImageMargin=false};
    readonly ToolStripMenuItem autonomyItem=new ToolStripMenuItem();
    double last,checkedSurface,actionStarted,lastPresentedAt=-1;Rectangle previousBounds;IntPtr previousWindow,lastForeground,trustedWindow;uint trustedOwner;bool disposed,lastScope;float preferredScale,petFactor=1,capsuleFactor=1;
    PetSurface surface;string desktopDevice;Bitmap presented;Point cursorStart;PointF positionStart;bool mouseHeld,bodyHeld,rightHeld,moved,expanded,taskPanel,resetTab,hudBelow,panelBelow;int tokenRange,taskIndex;float panelReveal,panelFrom;double panelStarted=-1;
    public readonly PetMotion Motion=new PetMotion(Environment.TickCount);
    readonly PetDragTracker dragTracker=new PetDragTracker();bool wasSliding;
    readonly PetAnimationDirector director;readonly PetBehaviorProfile behavior;readonly PetOverlay popup=new PetOverlay(),hud=new PetOverlay();bool draggingFromHud;
    int animationGeneration=-1,previousButtons,phraseIndex;double lastClickSelection=-1000,lastMoveClock;
    public PetQuotaSnapshot Quota;public PetTokenSnapshot Tokens;public bool QuotaRefreshing;public string QuotaStatus="正在读取账号额度…";
    public PetActivity Activity=new PetActivity();
    public Func<PetSurface> SurfaceProvider;public bool UseForegroundFilter=false,DismissOnOutsideClick=false,TrackLiveSurface;
    public event Action OpenSettings,RefreshQuota,PreferenceChanged;
    public event Action<bool> ScopeRequested;
    public bool Expanded {get{return expanded;}}
    public bool TaskPanel {get{return taskPanel;}}
    public float PanelReveal {get{return panelReveal;}}
    public float RenderScale {get;private set;}
    public float PetFactor {get{return petFactor;}}
    public float CapsuleFactor {get{return capsuleFactor;}}
    public PetLayout Geometry {get{return new PetLayout(false,petFactor,capsuleFactor,0,atlas.IsVideo,true);}}
    public RectangleF VisibleBodyScreenBounds{get{return BodyRectangle(atlas.ContentBounds(director.Row),true);}}
    RectangleF BodyRectangle(RectangleF normalized,bool screen){var sprite=Geometry.Sprite;return new RectangleF((screen?Left:0)+(sprite.X+normalized.X*sprite.Width)*RenderScale,(screen?Top:0)+(sprite.Y+normalized.Y*sprite.Height)*RenderScale,normalized.Width*sprite.Width*RenderScale,normalized.Height*sprite.Height*RenderScale);}
    public Rectangle AllowedBounds {get{return surface==null?Rectangle.Empty:surface.Bounds;}}
    public static PetWindow Create(PetPack pack,PetPreferences settings,string root,PetAtlas prepared=null){
        IntPtr previous=IntPtr.Zero;try{previous=SetThreadDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
        try{var window=new PetWindow(pack,settings,root,prepared);IntPtr handle=window.Handle;try{uint dpi=GetDpiForWindow(handle);if(dpi>0)window.preferredScale=window.RenderScale=dpi/96F;}catch(EntryPointNotFoundException){}window.ClientSize=window.PhysicalSize();return window;}
        finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
    }
    public PetWindow(PetPack pack,PetPreferences preferences,string root,PetAtlas prepared=null){
        this.pack=pack;settings=preferences;behavior=settings.ForPet(pack.Id);director=new PetAnimationDirector(pack,behavior,Environment.TickCount);atlas=prepared??new PetAtlas(pack);painter=new PetPainter(root);Text="CodexHub · "+pack.Name;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;TopMost=true;AutoScaleMode=AutoScaleMode.None;Cursor=Cursors.Hand;
        lastScope=settings.CodexOnly;petFactor=settings.Scale*PetScaleUnit;capsuleFactor=settings.CapsuleScale/100F;using(var g=Graphics.FromHwnd(IntPtr.Zero))preferredScale=RenderScale=g.DpiX/96F;
        if(settings.PositionVersion<1){if(settings.X!=int.MinValue&&settings.Y!=int.MinValue){var old=new PetLayout(false,petFactor,capsuleFactor,0,atlas.IsVideo);settings.X+=(int)(old.Sprite.X*RenderScale);settings.Y+=(int)(old.Sprite.Y*RenderScale);}settings.PositionVersion=1;}
        ClientSize=PhysicalSize();menu.SuspendLayout();menu.BackColor=Color.FromArgb(27,32,37);menu.ForeColor=Color.FromArgb(231,240,238);
        menu.Items.Add("查看状态",null,delegate{if(!expanded)ToggleExpanded();});
        menu.Items.Add("和"+pack.Name+"聊聊（离线）…",null,delegate{OfflineChat();});
        autonomyItem.Click+=delegate{if(NeedsAutonomyResume)ResumeAutonomy();else{settings.Roaming=false;if(!Motion.Sliding&&!Motion.Dragging)Motion.Velocity=PointF.Empty;SyncAutonomyMenu();if(PreferenceChanged!=null)PreferenceChanged();}};menu.Items.Add(autonomyItem);SyncAutonomyMenu();
        var range=new ToolStripMenuItem("桌面操作");range.DropDown.SuspendLayout();ToolStripMenuItem inCodex=new ToolStripMenuItem("仅在 Codex 窗口内"),onDesktop=new ToolStripMenuItem("整个桌面（可跨屏拖动）");inCodex.Click+=delegate{if(ScopeRequested!=null)ScopeRequested(true);else SetScope(true);};onDesktop.Click+=delegate{if(ScopeRequested!=null)ScopeRequested(false);else SetScope(false);};range.DropDownItems.Add(inCodex);range.DropDownItems.Add(onDesktop);
        range.DropDownItems.Add(new ToolStripSeparator());var roam=new ToolStripMenuItem();roam.Click+=delegate{settings.Roaming=!settings.Roaming;SyncAutonomyMenu();if(PreferenceChanged!=null)PreferenceChanged();};range.DropDownItems.Add(roam);range.DropDownItems.Add("刷新额度",null,delegate{if(RefreshQuota!=null)RefreshQuota();});range.DropDown.ResumeLayout(false);menu.Items.Add(range);
        var actions=new ToolStripMenuItem("手动动作");actions.DropDown.SuspendLayout();
        if(pack.Actions==null){string[] names={"待机","向右行走","向左行走","打招呼","跳跃","碰撞反馈","等待","工作","检查"};for(int i=0;i<names.Length;i++){int row=i;actions.DropDownItems.Add(names[i],null,delegate{Interact(row,false);});}}
        else{var categorized=new HashSet<int>();foreach(var category in director.Spec.categories??new PetActionCategory[0]){var group=new ToolStripMenuItem(category.name);group.DropDown.SuspendLayout();foreach(int index in category.actions){int row=9+index;categorized.Add(index);group.DropDownItems.Add(pack.Actions[index].name,null,delegate{Interact(row,false);});}group.DropDown.ResumeLayout(false);if(group.DropDownItems.Count>0)actions.DropDownItems.Add(group);}var all=new ToolStripMenuItem(pack.Id=="bailong"?"白龙完整动作":"全部原始动作");all.DropDown.SuspendLayout();for(int i=0;i<pack.Actions.Count;i++){int row=9+i;all.DropDownItems.Add(pack.Actions[i].name,null,delegate{Interact(row,false);});}all.DropDown.ResumeLayout(false);actions.DropDownItems.Add(all);}
        var events=new ToolStripMenuItem("额度回应（动作预览）");events.DropDown.SuspendLayout();string[] bands={"充足 · 已用 0–19%","宽裕 · 已用 20–39%","过半 · 已用 40–59%","留意 · 已用 60–79%","告急 · 已用 80–99%","耗尽 · 已用 100%"};for(int i=0;i<6;i++){int band=i;events.DropDownItems.Add(bands[i],null,delegate{director.PreviewQuota(band);});}events.DropDown.ResumeLayout(false);actions.DropDownItems.Add(events);actions.DropDown.ResumeLayout(false);menu.Items.Add(actions);
        menu.Items.Add(new ToolStripSeparator());menu.Items.Add("桌宠设置…",null,delegate{if(OpenSettings!=null)OpenSettings();});
        menu.Items.Add("隐藏全部组件",null,delegate{settings.Enabled=false;if(PreferenceChanged!=null)PreferenceChanged();});menu.Items.Add("退出桌宠",null,delegate{settings.Enabled=false;if(PreferenceChanged!=null)PreferenceChanged();Hide();});
        menu.Opening+=delegate{expanded=false;panelReveal=0;panelStarted=-1;popup.Hide();inCodex.Checked=onDesktop.Checked=roam.Checked=false;inCodex.Text="仅在 Codex 窗口内"+(settings.CodexOnly?" · 当前":"");onDesktop.Text="整个桌面（可跨屏拖动）"+(!settings.CodexOnly?" · 当前":"");roam.Text=settings.Roaming?"暂停随机行走":"开启随机行走";SyncAutonomyMenu();};HubPopupMenu.Apply(menu);menu.ResumeLayout(false);
        popup.Clicked+=PanelClick;hud.MouseDown+=HudMouseDown;hud.MouseMove+=delegate{DragTo(PetOverlay.Pointer);};hud.MouseUp+=HudMouseUp;hud.MouseCaptureChanged+=delegate{if(!hud.Capture&&mouseHeld&&draggingFromHud){mouseHeld=false;atlas.DragHeld=false;if(Motion.Dragging){Motion.Drop();director.EndDrag(watch.Elapsed.TotalSeconds);atlas.ReleaseDragFrame();Present();}}};SurfaceProvider=CodexPetSurface.Find;timer.Tick+=delegate{Tick();};timer.Start();
        atlas.PrepareDrag(director.DragRow);
        Motion.Row=3;Motion.ReactionRemaining=1.3;
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var value=base.CreateParams;value.ExStyle|=0x80000|0x80|0x08000000;return value;}}
    Size PhysicalSize(){var logical=Geometry.Size;return new Size((int)Math.Ceiling(logical.Width*RenderScale),(int)Math.Ceiling(logical.Height*RenderScale));}
    public void SetScope(bool codexOnly){settings.X=(int)Motion.Position.X;settings.Y=(int)Motion.Position.Y;settings.CodexOnly=lastScope=codexOnly;if(!codexOnly)desktopDevice=null;checkedSurface=0;previousBounds=Rectangle.Empty;surface=null;if(PreferenceChanged!=null)PreferenceChanged();AdvanceOnce();}
    void Tick(){using(PetDpi.Enter())TickPhysical();}
    void TickPhysical(){
        double now=watch.Elapsed.TotalSeconds,delta=last==0?.016:now-last;last=now;
        AdvancePanel(now);
        if(rightHeld&&(Control.MouseButtons&MouseButtons.Right)==0)rightHeld=false;Motion.Pressed=!Motion.Dragging&&((mouseHeld&&bodyHeld&&!moved)||rightHeld);Motion.StepPress(delta);
        if(!settings.Enabled){atlas.SuspendVideo();if(Visible)Hide();popup.Hide();hud.Hide();return;}
        if(lastScope!=settings.CodexOnly){lastScope=settings.CodexOnly;if(!lastScope)desktopDevice=null;settings.X=(int)Motion.Position.X;settings.Y=(int)Motion.Position.Y;checkedSurface=0;previousBounds=Rectangle.Empty;surface=null;}
        if(now-checkedSurface>.25||checkedSurface==0||settings.CodexOnly&&TrackLiveSurface&&trustedWindow!=IntPtr.Zero){
            checkedSurface=now;
            PetSurface current=settings.CodexOnly?SurfaceProvider():new PetSurface(GetDesktopArea(),IntPtr.Zero);
            if(settings.CodexOnly&&TrackLiveSurface){if(current!=null&&current.Window!=IntPtr.Zero&&current.Window!=trustedWindow){trustedWindow=current.Window;trustedOwner=CodexPetSurface.Owner(trustedWindow);}if(trustedWindow!=IntPtr.Zero)current=CodexPetSurface.RefreshKnown(trustedWindow,trustedOwner);}
            if(current!=null&&settings.CodexOnly){var visible=Rectangle.Intersect(current.Bounds,PetDisplays.At(current.Bounds).WorkingArea);current=visible.Width<32||visible.Height<32?null:new PetSurface(visible,current.Window);}
            if(current==null){surface=null;Motion.Sliding=false;Motion.Velocity=PointF.Empty;atlas.SuspendVideo();if(Visible)Hide();popup.Hide();hud.Hide();return;}
            FitScale(current.Bounds);
            surface=current;
            if(previousBounds==Rectangle.Empty){Motion.Position=settings.X!=int.MinValue&&settings.Y!=int.MinValue?new PointF(settings.X,settings.Y):new PointF(current.Bounds.Right-Width-22,current.Bounds.Bottom-Height-12);}
            else if(settings.CodexOnly&&previousWindow==current.Window){Motion.Position=new PointF(Motion.Position.X+current.Bounds.X-previousBounds.X,Motion.Position.Y+current.Bounds.Y-previousBounds.Y);}
            previousBounds=current.Bounds;previousWindow=current.Window;
        }
        if(surface==null)return;
        atlas.ResumeVideo();
        FitScale(surface.Bounds);Rectangle motionBounds=MotionBounds(surface.Bounds);Motion.Place(Motion.Position,Size,motionBounds);
        Motion.TravelScale=Math.Max(.6F,BodyRectangle(atlas.IdleContentBounds,false).Width/180F);Motion.AnimationControlled=atlas.IsVideo;Motion.AnimationDelta=0;Motion.AnimationEnded=false;
        if(atlas.IsVideo&&director.Moving&&animationGeneration==director.Generation){var clip=pack.Actions[atlas.ActionIndex(director.Row)];Motion.AnimationDuration=Math.Max(.1,clip.durationMs/1000D-clip.moveLeadSeconds-clip.moveTailSeconds);double active=Math.Max(0,Math.Min(Motion.AnimationDuration,atlas.VideoPlaybackTime-clip.moveLeadSeconds));Motion.AnimationDelta=Math.Max(0,active-lastMoveClock);lastMoveClock=active;Motion.AnimationEnded=atlas.VideoFinished;}
        if(!HubPopupMenu.AnyOpen&&!(mouseHeld&&!moved))Motion.Update(delta,Size,motionBounds,settings.Roaming&&director.Stage==Activity.Stage&&director.CanWalk(now,atlas),Activity.Active);Location=Point.Round(Motion.Position);
        if(!HubPopupMenu.AnyOpen)director.Tick(now,Motion,Activity.Stage,atlas,settings.Roaming);
        Motion.Place(Motion.Position,Size,MotionBounds(surface.Bounds));Location=Point.Round(Motion.Position);
        atlas.DragHeld=Motion.Dragging;
        atlas.MirrorMotion=atlas.IsVideo&&director.Moving&&pack.Actions[atlas.ActionIndex(director.Row)].moveFacesLeft&&Motion.Velocity.X>0;
        if(wasSliding&&!Motion.Sliding){settings.X=Left;settings.Y=Top;if(PreferenceChanged!=null)PreferenceChanged();}wasSliding=Motion.Sliding;
        if(!Visible)Show();MaintainTopmost();Present();PresentOverlays(now);
    }
    public void AdvanceOnce(){Tick();}
    Rectangle GetDesktopArea(){var screen=PetDisplays.All.FirstOrDefault(s=>s.DeviceName==desktopDevice);if(screen==null){PointF position=settings.X!=int.MinValue&&previousBounds==Rectangle.Empty?new PointF(settings.X,settings.Y):Motion.Position;var body=Geometry.Sprite;screen=PetDisplays.At(Point.Round(new PointF(position.X+(body.X+body.Width/2)*RenderScale,position.Y+(body.Y+body.Height*.6F)*RenderScale)));desktopDevice=screen.DeviceName;}return screen.WorkingArea;}
    void MaintainTopmost(){if(HubPopupMenu.AnyOpen)return;IntPtr foreground=GetForegroundWindow();if(foreground==lastForeground)return;lastForeground=foreground;SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x0013);}
    void FitScale(Rectangle bounds){
        if(preferredScale<=0||bounds.Width<1||bounds.Height<1)return;RenderScale=preferredScale;float width=(bounds.Width-1)/RenderScale,height=(bounds.Height-1)/RenderScale,header=54;
        capsuleFactor=Math.Max(.05F,Math.Min(settings.CapsuleScale/100F,Math.Min(width/280F,(height-24)/header)));
        petFactor=Math.Max(.05F,Math.Min(settings.Scale*PetScaleUnit,Math.Min(width/((atlas.IsVideo?512F:192F)*atlas.IdleContentBounds.Width),(height-8)/((atlas.IsVideo?288F:208F)*atlas.IdleContentBounds.Height))));
        Size next=PhysicalSize();if(next!=ClientSize){if(previousBounds!=Rectangle.Empty)Motion.Position=new PointF(Motion.Position.X-(next.Width-Width)/2F,Motion.Position.Y);ClientSize=next;}
    }
    float PetScaleUnit {get{return pack.Id=="deepseek-fat-fish"?.0112F:.014F;}}
    void Present(){using(PetDpi.Enter())PresentPhysical();}
    void PresentPhysical(){
        painter.ApplyAppearance(settings);
        if(surface!=null){var safe=PetMotion.Clamp(Motion.Position,Size,MotionBounds(surface.Bounds));if(safe!=Motion.Position){Motion.Position=safe;Location=Point.Round(safe);}}
        if(IsDisposed||!IsHandleCreated)return;double time=watch.Elapsed.TotalSeconds;int physical=Motion.Row;Motion.Row=director.Row;if(animationGeneration!=director.Generation){animationGeneration=director.Generation;actionStarted=time;lastMoveClock=0;atlas.BeginVideo(director.Row,animationGeneration,director.DirectSwitch,director.Moving);}atlas.HoldLast=director.Sleeping;var frame=painter.Render(atlas,Motion,false,false,Quota,QuotaStatus,time-actionStarted,RenderScale,Activity.Label,Activity.Active,time,Tokens,tokenRange,resetTab,petFactor,capsuleFactor,QuotaRefreshing,0,false,Activity,taskIndex,true);Motion.Row=physical;
        IntPtr screen=GetDC(IntPtr.Zero),dc=CreateCompatibleDC(screen),bitmap=IntPtr.Zero,previous=IntPtr.Zero;
        try{bitmap=frame.GetHbitmap(Color.FromArgb(0));previous=SelectObject(dc,bitmap);var point=new NativePoint(Left,Top);var origin=new NativePoint(0,0);var size=new NativeSize(frame.Width,frame.Height);var blend=new Blend{Alpha=255,Format=1};if(!UpdateLayeredWindow(Handle,screen,ref point,ref size,dc,ref origin,0,ref blend,2))throw new Win32Exception();}
        finally{if(previous!=IntPtr.Zero)SelectObject(dc,previous);if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);DeleteDC(dc);ReleaseDC(IntPtr.Zero,screen);}
        if(presented!=null)presented.Dispose();presented=frame;lastPresentedAt=time;
    }
    public Bitmap Snapshot(){int physical=Motion.Row;Motion.Row=director.Row;try{return painter.Render(atlas,Motion,false,false,Quota,QuotaStatus,watch.Elapsed.TotalSeconds-actionStarted,RenderScale,Activity.Label,Activity.Active,watch.Elapsed.TotalSeconds,Tokens,tokenRange,resetTab,petFactor,capsuleFactor,QuotaRefreshing,0,false,Activity,taskIndex,true);}finally{Motion.Row=physical;}}
    public void ToggleExpanded(){OpenPanel(false);}
    public void OpenPanel(bool tasks){menu.Close();bool closing=expanded&&taskPanel==tasks;expanded=!closing;taskPanel=tasks;if(expanded){var body=BodyRectangle(atlas.IdleContentBounds,true);var area=OverlayArea();panelBelow=body.Top-10*RenderScale-225*RenderScale*capsuleFactor<area.Top;}panelFrom=panelReveal;panelStarted=watch.Elapsed.TotalSeconds;previousButtons=PetOverlay.Buttons;PresentOverlays(watch.Elapsed.TotalSeconds);}
    void AdvancePanel(double now){if(panelStarted<0)return;double t=Math.Max(0,Math.Min(1,(now-panelStarted)/.32)),ease=t<.5?4*t*t*t:1-Math.Pow(-2*t+2,3)/2;panelReveal=(float)(panelFrom+((expanded?1:0)-panelFrom)*ease);if(t>=1)panelStarted=-1;}
    public void Interact(int row,bool sound){double duration=director.Duration(row);Motion.Interact(row,duration);director.Manual(row,watch.Elapsed.TotalSeconds);actionStarted=watch.Elapsed.TotalSeconds;if(sound&&settings.Sound)PetSound.Play();Present();}
    void ReactToClick(bool sound){Motion.ReactToClick();double now=watch.Elapsed.TotalSeconds;if(sound&&now-lastClickSelection>=Math.Min(.8,Math.Max(.25,SystemInformation.DoubleClickTime/1000D+.05))){lastClickSelection=now;director.Click(now);if(settings.Sound)PetSound.Play();}Present();}
    bool NeedsAutonomyResume{get{return !settings.Roaming||director.ManualActive||director.Sleeping;}}
    void SyncAutonomyMenu(){autonomyItem.Checked=false;autonomyItem.Text=NeedsAutonomyResume?director.Sleeping?"唤醒并恢复自主走动":"恢复自主走动":"暂停自主走动";}
    public void ResumeAutonomy(){settings.Roaming=true;Motion.TravelScale=Math.Max(.6F,BodyRectangle(atlas.IdleContentBounds,false).Width/180F);Motion.ResumeAutonomy(true);double now=watch.Elapsed.TotalSeconds;director.Restore(now);director.Tick(now,Motion,Activity.Stage,atlas,true);SyncAutonomyMenu();Present();if(PreferenceChanged!=null)PreferenceChanged();}
    Rectangle MotionBounds(Rectangle area){var silhouette=RectangleF.Union(atlas.IdleContentBounds,atlas.ContentBounds(director.Row));var body=BodyRectangle(silhouette,false);body.Inflate(8*RenderScale,8*RenderScale);int left=(int)Math.Floor(body.Left),top=(int)Math.Floor(body.Top),right=Width-(int)Math.Ceiling(body.Right),bottom=Height-(int)Math.Ceiling(body.Bottom);return new Rectangle(area.Left-left,area.Top-top,area.Width+left+right,area.Height+top+bottom);}
    void PanelClick(Point point){float scale=RenderScale*capsuleFactor,x=point.X/scale,y=point.Y/scale;
        if(y<36&&x>=240){OpenPanel(taskPanel);return;}
        if(taskPanel){if(y>=40&&y<=115)taskIndex++;return;}
        if(x>=214&&x<=239&&y<36){if(RefreshQuota!=null)RefreshQuota();return;}
        if(x>=145&&x<=264&&y>=77&&y<=98){resetTab=x>=205;return;}
        if(!resetTab&&x>=149&&x<=260&&y>=99&&y<=115){tokenRange=Math.Max(0,Math.Min(2,(int)((x-149)/37)));return;}
    }
    void PresentOverlays(double now){using(PetDpi.Enter())PresentOverlaysPhysical(now);}
    Rectangle OverlayArea(){var area=PetDisplays.At(Rectangle.Round(VisibleBodyScreenBounds)).WorkingArea;return settings.CodexOnly&&surface!=null?Rectangle.Intersect(area,surface.Bounds):area;}
    void PresentOverlaysPhysical(double now){if(surface==null||!Visible){popup.Hide();hud.Hide();return;}float scale=RenderScale*capsuleFactor;var area=OverlayArea();var body=BodyRectangle(atlas.IdleContentBounds,true);float above=body.Top-10*RenderScale-47*scale;
        if(panelReveal==0){if(!hudBelow&&above<area.Top)hudBelow=true;else if(hudBelow&&above>=area.Top+12*RenderScale)hudBelow=false;}
        if(settings.ShowQuota){using(var frame=painter.RenderCapsule(scale,Quota,QuotaStatus,Activity.Label,Activity.Active,now)){int x=(int)(body.Left+body.Width/2-frame.Width/2),y=(int)(hudBelow?body.Bottom+10*RenderScale-7*scale:above);x=Math.Max(area.Left,Math.Min(area.Right-frame.Width,x));y=Math.Max(area.Top,Math.Min(area.Bottom-frame.Height,y));if(panelReveal<.99F)hud.Present(frame,new Point(x,y),1);else hud.Hide();}}else hud.Hide();
        if(panelReveal>0&&settings.ShowQuota){using(var frame=painter.RenderPanel(scale,taskPanel,Quota,QuotaStatus,Activity.Label,Activity.Active,now,Tokens,tokenRange,resetTab,QuotaRefreshing,Activity,taskIndex)){
            int x=(int)(body.Left+body.Width/2-frame.Width/2),y=(int)(panelBelow?body.Bottom+10*RenderScale-5*scale:body.Top-10*RenderScale-225*scale);x=Math.Max(area.Left,Math.Min(area.Right-frame.Width,x));y=Math.Max(area.Top,Math.Min(area.Bottom-frame.Height,y));var origin=new RectangleF((hud.Left-x)/scale+(taskPanel?57:228),(hud.Top-y)/scale+(taskPanel?8:7),taskPanel?165:40,taskPanel?37:40);using(var morph=painter.MorphPanel(frame,scale,origin,panelReveal))popup.Present(morph,new Point(x,y),1);}
            int buttons=PetOverlay.Buttons;if(DismissOnOutsideClick&&(buttons&~previousButtons)!=0&&!popup.Bounds.Contains(PetOverlay.Pointer)&&!hud.Bounds.Contains(PetOverlay.Pointer)&&!HubPopupMenu.AnyOpen){if(expanded)OpenPanel(taskPanel);}previousButtons=buttons;
        }else popup.Hide();
    }
    public void NotifyQuota(PetQuotaSnapshot value){if(value==null||value.Stale){director.QuotaUnknown();return;}var primary=value.Short!=null&&value.Short.Remaining.HasValue?value.Short:value.Week;if(primary!=null&&primary.Remaining.HasValue)director.Quota(primary.Remaining.Value,watch.Elapsed.TotalSeconds);else director.QuotaUnknown();}
    public void RandomAction(){director.Click(watch.Elapsed.TotalSeconds);Present();}
    public void Greet(){if(pack.Actions!=null){int index=pack.Actions.FindIndex(a=>a.name.Contains("挥手")||a.name.Contains("招手")||a.name.Contains("打招呼"));if(index>=0){Interact(9+index,true);return;}}Interact(3,true);}
    void OfflineChat(){using(var dialog=new PetOfflineChat(pack.Name,text=>PetPhrasebook.Reply(text,pack.Id,phraseIndex++)))dialog.ShowDialog();}
    void PointerDown(MouseEventArgs e,bool fromHud){if(e.Button==MouseButtons.Right){rightHeld=true;Motion.Pressed=true;Motion.StepPress(.016);ReactToClick(false);menu.Show(Cursor.Position);return;}if(e.Button!=MouseButtons.Left)return;draggingFromHud=fromHud;mouseHeld=true;moved=false;Motion.Sliding=false;Motion.Velocity=PointF.Empty;cursorStart=PetOverlay.Pointer;positionStart=Motion.Position;dragTracker.Reset(cursorStart,watch.Elapsed.TotalSeconds);bodyHeld=Motion.Pressed=!fromHud;if(fromHud)hud.Capture=true;else Capture=true;}
    void HudMouseDown(object sender,MouseEventArgs e){PointerDown(e,true);}
    void HudMouseUp(object sender,MouseEventArgs e){PointerUp(e,true);}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);PointerDown(e,false);}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);DragTo(PetOverlay.Pointer);}
    internal void DragTo(Point cursor){using(PetDpi.Enter())DragPhysical(cursor);}
    void DragPhysical(Point cursor){if(!mouseHeld)return;int dx=cursor.X-cursorStart.X,dy=cursor.Y-cursorStart.Y;if(Math.Abs(dx)+Math.Abs(dy)<4&&!moved)return;bool first=!moved;moved=true;
        var monitor=PetDisplays.At(cursor);Rectangle bounds=settings.CodexOnly?(surface==null?Rectangle.Empty:surface.Bounds):monitor.WorkingArea;if(bounds.IsEmpty)return;FitScale(bounds);
        if(!settings.CodexOnly){desktopDevice=monitor.DeviceName;surface=new PetSurface(bounds,IntPtr.Zero);}double now=watch.Elapsed.TotalSeconds;dragTracker.Add(cursor,now);Motion.Pressed=false;Motion.PressScale=1;Motion.Drag(new PointF(positionStart.X+dx,positionStart.Y+dy),Size,MotionBounds(bounds));director.StartDrag(now);atlas.DragHeld=true;Point previous=Location;Location=Point.Round(Motion.Position);int shiftX=Left-previous.X,shiftY=Top-previous.Y;if(hud.Visible)hud.Location=new Point(hud.Left+shiftX,hud.Top+shiftY);if(popup.Visible)popup.Location=new Point(popup.Left+shiftX,popup.Top+shiftY);if(first||now-lastPresentedAt>=1D/90){Present();PresentOverlays(now);}
    }
    void PointerUp(MouseEventArgs e,bool fromHud){if(e.Button!=MouseButtons.Left||!mouseHeld)return;mouseHeld=false;Capture=false;hud.Capture=false;atlas.DragHeld=false;if(moved){Motion.Drop(dragTracker.Release(PetOverlay.Pointer,watch.Elapsed.TotalSeconds));director.EndDrag(watch.Elapsed.TotalSeconds,Motion.Sliding);atlas.ReleaseDragFrame();Present();PresentOverlays(watch.Elapsed.TotalSeconds);settings.X=Left;settings.Y=Top;if(PreferenceChanged!=null)PreferenceChanged();}
        else if(fromHud){float x=e.X/(RenderScale*capsuleFactor);if(x<54){if(OpenSettings!=null)OpenSettings();}else if(x<224)OpenPanel(true);else ToggleExpanded();}
        else ReactToClick(true);
    }
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);PointerUp(e,false);}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture&&mouseHeld&&!draggingFromHud){mouseHeld=false;atlas.DragHeld=false;if(Motion.Dragging){Motion.Drop();director.EndDrag(watch.Elapsed.TotalSeconds);atlas.ReleaseDragFrame();Present();}}}
    protected override void OnMouseDoubleClick(MouseEventArgs e){base.OnMouseDoubleClick(e);if(e.Button==MouseButtons.Left)ReactToClick(true);}
    protected override void WndProc(ref Message message){
        if(message.Msg==0x02E0){preferredScale=RenderScale=(message.WParam.ToInt64()&0xffff)/96F;if(surface!=null)FitScale(surface.Bounds);ClientSize=PhysicalSize();if(surface!=null)Motion.Place(Motion.Position,Size,MotionBounds(surface.Bounds));Present();return;}
        if(message.Msg==0x21){message.Result=(IntPtr)3;return;}
        if(message.Msg==0x84&&presented!=null){int x=unchecked((short)(long)message.LParam)-Left,y=unchecked((short)((long)message.LParam>>16))-Top;if(x<0||y<0||x>=presented.Width||y>=presented.Height||presented.GetPixel(x,y).A<16){message.Result=(IntPtr)(-1);return;}}
        base.WndProc(ref message);
    }
    protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;timer.Stop();timer.Dispose();popup.Dispose();hud.Dispose();menu.Dispose();atlas.Dispose();painter.Dispose();if(presented!=null){presented.Dispose();presented=null;}}base.Dispose(disposing);}
}
internal static class PetSound {
    static int playing;
    public static byte[] Create(){
        using(var source=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexHub.ClickSound")){if(source!=null)using(var output=new MemoryStream()){source.CopyTo(output);return output.ToArray();}}
        const int rate=22050,count=6600;using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(count*2);
            for(int i=0;i<count;i++){double t=i/(double)rate,frequency=i<count/2?659.255:880,envelope=Math.Sin(Math.PI*(i%(count/2))/(count/2))*Math.Exp(-3*(i%(count/2))/(double)(count/2));writer.Write((short)(Math.Sin(2*Math.PI*frequency*t)*envelope*4800));}writer.Flush();return stream.ToArray();
        }
    }
    public static void Play(){if(System.Threading.Interlocked.CompareExchange(ref playing,1,0)!=0)return;System.Threading.ThreadPool.QueueUserWorkItem(delegate{try{using(var stream=new MemoryStream(Create()))using(var sound=new SoundPlayer(stream))sound.PlaySync();}catch(InvalidOperationException){}finally{System.Threading.Interlocked.Exchange(ref playing,0);}});}
}
