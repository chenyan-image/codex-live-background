using System;using System.Collections.Generic;using System.Diagnostics;using System.Drawing;using System.Drawing.Imaging;using System.IO;using System.Linq;using System.Reflection;using System.Runtime.InteropServices;using System.Security.Cryptography;using System.Web.Script.Serialization;using System.Windows.Forms;
internal static class PetRenderMemoryTests {
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;static int checks;
    static void Need(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    static object Field(object target,string name){return target.GetType().GetField(name,Hidden).GetValue(target);}
    static void Call(object target,string name,params object[] values){target.GetType().GetMethod(name,Hidden).Invoke(target,values);}
    static Bitmap Render(PetPainter painter,PetAtlas atlas,double time,float scale,Bitmap buffer=null){var method=typeof(PetPainter).GetMethod("Render");var parameters=method.GetParameters();var values=parameters.Select(p=>p.IsOptional?(object)Type.Missing:null).ToArray();values[0]=atlas;values[1]=new PetMotion(1);values[2]=false;values[3]=false;values[4]=null;values[5]="";values[6]=time;values[7]=scale;for(int i=0;i<parameters.Length;i++){if(parameters[i].Name=="bodyOnly")values[i]=true;if(parameters[i].Name=="petScale")values[i]=1.4F;if(parameters[i].Name=="target")values[i]=buffer;}return (Bitmap)method.Invoke(painter,values);}
    static string Pixels(Bitmap image){var data=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);try{var bytes=new byte[Math.Abs(data.Stride)*data.Height];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);using(var hash=SHA256.Create())return Convert.ToBase64String(hash.ComputeHash(bytes));}finally{image.UnlockBits(data);}}
    static int VisibleDifference(Bitmap first,Bitmap second){int max=0;foreach(Color background in new[]{Color.Black,Color.White,Color.FromArgb(15,22,28)})using(var a=new Bitmap(first.Width,first.Height))using(var b=new Bitmap(second.Width,second.Height)){using(var g=Graphics.FromImage(a)){g.Clear(background);g.DrawImageUnscaled(first,0,0);}using(var g=Graphics.FromImage(b)){g.Clear(background);g.DrawImageUnscaled(second,0,0);}for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++){var p=a.GetPixel(x,y);var q=b.GetPixel(x,y);max=Math.Max(max,Math.Max(Math.Abs(p.R-q.R),Math.Max(Math.Abs(p.G-q.G),Math.Abs(p.B-q.B))));}}return max;}
    static bool Disposed(Bitmap image){try{image.GetPixel(0,0);return false;}catch(ArgumentException){return true;}catch(ObjectDisposedException){return true;}}
    static object ReferencePixels(string root,PetPack pack){using(var painter=new PetPainter(root))using(var atlas=new PetAtlas(pack))using(var body=Render(painter,atlas,.4F,1))using(var capsule=painter.RenderCapsule(1,null,"proof","idle",true,.4))using(var quota=painter.RenderPanel(1,false,null,"proof","idle",false,.4,null,0,false,false,new PetActivity(),0))using(var task=painter.RenderPanel(1,true,null,"proof","idle",false,.4,null,0,false,false,new PetActivity(),0)){return new{body=Pixels(body),capsule=Pixels(capsule),quota=Pixels(quota),task=Pixels(task)};}}
    static void FontDisposal(PetPainter painter,bool strict){var names=new[]{"numberFont","smallNumberFont","labelFont","tinyFont"};var fonts=names.Select(name=>typeof(PetPainter).GetField(name,Hidden)).Where(field=>field!=null).Select(field=>(Font)field.GetValue(painter)).ToArray();painter.Dispose();if(strict)Need(fonts.Length==4,"Expected four reusable drawing fonts");foreach(var font in fonts){bool disposed=false;try{font.GetHeight();}catch(ArgumentException){disposed=true;}catch(ObjectDisposedException){disposed=true;}Need(disposed,"Painter disposal retained a cached font");}}
    static object Painter(string root,PetPack pack,bool strict){
        int allocations=0,reused=0;long canvasBytes=0;Bitmap buffer=null;
        using(var painter=new PetPainter(root))using(var atlas=new PetAtlas(pack)){
            for(int i=0;i<180;i++){var frame=Render(painter,atlas,i*.016,1,buffer);if(!Object.ReferenceEquals(buffer,frame)){allocations++;canvasBytes+=(long)frame.Width*frame.Height*4;if(buffer!=null)buffer.Dispose();buffer=frame;}else reused++;}
            using(var independent=Render(painter,atlas,179*.016,1)){Need(!Object.ReferenceEquals(independent,buffer),"Public Render returned a window-owned buffer");Need(Pixels(independent)==Pixels(buffer),"Reused body buffer changed rendered pixels");}
            using(var larger=Render(painter,atlas,.4,1.5F,buffer)){Need(!Object.ReferenceEquals(larger,buffer)&&larger.Width>buffer.Width,"Resize reused a buffer of the wrong dimensions");Need(!Disposed(buffer),"Render disposed its caller's old buffer");}
            using(var panel=painter.RenderPanel(1,false,null,"","idle",false,0,null,0,false,false,new PetActivity(),0))using(var other=painter.RenderPanel(1,false,null,"","idle",false,0,null,0,false,false,new PetActivity(),0))using(var morph=painter.MorphPanel(panel,1,new RectangleF(228,7,40,40),1)){
                Need(!Object.ReferenceEquals(panel,other)&&!Object.ReferenceEquals(panel,morph),"Public panel/morph ownership changed");Need(VisibleDifference(panel,morph)<=1,"Full expansion direct drawing differs visibly from its copy");
            }
            if(strict)Need(allocations==1&&reused==179,"Body rendering still allocates a new full-size canvas per frame");
        }
        using(var fonts=new PetPainter(root))FontDisposal(fonts,strict);if(buffer!=null)buffer.Dispose();return new{frames=180,canvasAllocations=allocations,reusedFrames=reused,canvasAllocatedBytes=canvasBytes};
    }
    static object Window(string root,PetPack pack,bool strict){
        var allocations=new List<int>();for(int cycle=0;cycle<2;cycle++){
            var preferences=new PetPreferences{Sound=false,ShowQuota=false,Roaming=false,CodexOnly=true};Bitmap last=null;int count=0;
            using(var window=PetWindow.Create(pack,preferences,root)){
                ((Timer)Field(window,"timer")).Stop();Need(!window.Visible,"Memory proof showed a live pet window");
                for(int i=0;i<40;i++){Call(window,"Present");var frame=(Bitmap)Field(window,"presented");if(!Object.ReferenceEquals(last,frame))count++;last=frame;}
                using(var snapshot=window.Snapshot())using(var other=window.Snapshot()){
                    Need(!Object.ReferenceEquals(snapshot,last)&&!Object.ReferenceEquals(snapshot,other),"Snapshot shares the reused body buffer");
                    preferences.Scale=135;Call(window,"FitScale",new Rectangle(0,0,2000,1600));Call(window,"Present");var resized=(Bitmap)Field(window,"presented");Need(!Object.ReferenceEquals(last,resized)&&Disposed(last),"Resize leaked the previous window canvas");last=resized;
                    window.Dispose();Need(Field(window,"presented")==null&&Disposed(last),"Window disposal retained its pixel buffer");Need(!Disposed(snapshot),"Window disposal invalidated a caller-owned Snapshot");
                }
                if(strict)Need(count==1,"Window did not reuse its body canvas");
            }
            allocations.Add(count);
        }return new{windowRecreations=2,bodyAllocationsPerFortyFrames=allocations,snapshotOwnershipPreserved=true,resizeAndDisposeReleaseOldBuffer=true,noWindowsShown=true};
    }
    [STAThread]static int Main(string[] args){try{Application.EnableVisualStyles();string root=args[0],report=args[1];bool strict=args.Length>2;var pack=PetPack.Discover(root,Path.Combine(Path.GetDirectoryName(report),"empty-state")).First(p=>p.Id=="deepseek");var pixelHashes=ReferencePixels(root,pack);var painter=Painter(root,pack,strict);var window=Window(root,pack,strict);var process=Process.GetCurrentProcess();process.Refresh();File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{pass=true,checks,pixelHashes,painter,window,privateBytes=process.PrivateMemorySize64,workingSetBytes=process.WorkingSet64,forcedGc=false,workingSetTrimmed=false}));return 0;}catch(Exception error){File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{pass=false,checks,error=error.ToString()}));return 1;}}
}
