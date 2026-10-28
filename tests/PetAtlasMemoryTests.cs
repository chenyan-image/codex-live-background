using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;

// Atlas ownership only: no real windows, services, preferences or forced GC.
internal static class PetAtlasMemoryTests {
    const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
    static int checks;
    static void Need(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static object Field(object target,string name){return target.GetType().GetField(name,Hidden).GetValue(target);}
    static long Bytes(object cache){var images=cache as IDictionary;return images==null?0:images.Values.Cast<Bitmap>().Sum(image=>(long)image.Width*image.Height*4);}
    static void Ready(PetAtlas atlas){var clock=Stopwatch.StartNew();while(!atlas.DragReady&&clock.ElapsedMilliseconds<5000)Thread.Sleep(5);Need(atlas.DragReady&&atlas.DragPreparationError==null,"Drag preparation failed or timed out");}
    static bool Disposed(Bitmap image){try{image.GetPixel(0,0);return false;}catch(ArgumentException){return true;}catch(ObjectDisposedException){return true;}}
    static object Bundled(string root,bool strict){
        var pack=PetPack.Read(Path.Combine(root,@"source\assets\pets\naifrog"),true);
        long before,warm,after;var references=new List<Bitmap>();
        using(var atlas=new PetAtlas(pack)){
            before=Bytes(Field(atlas,"pngs"));atlas.PrepareDrag(new PetAnimationDirector(pack,new PetBehaviorProfile(),1).DragRow);Ready(atlas);warm=Bytes(Field(atlas,"warmedDragPngs"));
            for(int round=0;round<3;round++)for(int row=9;row<atlas.Rows;row++){var frame=atlas.Frame(row,0);Need(frame.Width==192&&frame.Height==208,"Atlas playback dimensions changed");}
            after=Bytes(Field(atlas,"pngs"))+Bytes(Field(atlas,"warmedDragPngs"));references.AddRange(((IDictionary)Field(atlas,"pngs")).Values.Cast<Bitmap>());
            if(strict){Need(warm==0,"Cached single-sheet atlas was decoded again for dragging");Need(after==before,"Drag preparation retained a second copy of the same atlas");}
            Need(references.All(image=>!Disposed(image)),"Drag preparation disposed an active image");
        }
        Need(references.All(Disposed),"Atlas disposal retained a cached image");return new{beforeBytes=before,warmBytes=warm,afterAllActionsBytes=after,savedBytes=after-before};
    }
    static object SeparateFrames(string output){
        string directory=Path.Combine(output,"separate-frames");Directory.CreateDirectory(directory);
        foreach(string name in new[]{"idle.png","drag.png"})using(var image=new Bitmap(192,208,PixelFormat.Format32bppArgb)){using(var g=Graphics.FromImage(image)){g.Clear(Color.Transparent);g.FillEllipse(Brushes.Gold,20,20,150,170);}image.Save(Path.Combine(directory,name));}
        var pack=new PetPack{Id="fixture",Directory=directory,AtlasFile=Path.Combine(directory,"idle.png"),Format="frame-set",RowMap=new int[9],Actions=new List<PetAction>{new PetAction{file="idle.png",durationMs=600,frames=1,columns=1,cellWidth=192,cellHeight=208},new PetAction{file="drag.png",durationMs=600,frames=1,columns=1,cellWidth=192,cellHeight=208}}};
        using(var atlas=new PetAtlas(pack)){
            atlas.PrepareDrag(10);Ready(atlas);var warm=(IDictionary)Field(atlas,"warmedDragPngs");Need(warm.Count==1,"Uncached drag image was not prepared");var prepared=(Bitmap)warm["drag.png"];atlas.Frame(10,0);
            Need(warm.Count==0&&Object.ReferenceEquals(prepared,atlas.Bitmap),"Prepared image ownership was not transferred to the active cache");Need(!Disposed(prepared),"Transfer disposed the prepared image");
        }
        using(var atlas=new PetAtlas(pack)){
            atlas.PrepareDrag(10);Ready(atlas);var warm=(IDictionary)Field(atlas,"warmedDragPngs");var delayed=(Bitmap)warm["drag.png"];
            // Reproduce the valid result of UI decoding the image before the
            // worker publishes its already-decoded copy, without timing sleeps.
            warm.Remove("drag.png");atlas.Frame(10,0);var active=atlas.Bitmap;warm["drag.png"]=delayed;Need(!Object.ReferenceEquals(active,delayed),"Race fixture needs two independent bitmaps");atlas.Frame(10,0);
            Need(warm.Count==0&&Disposed(delayed),"Late warm duplicate was retained");Need(!Disposed(active)&&Object.ReferenceEquals(active,atlas.Bitmap),"Duplicate cleanup invalidated the active frame");
        }
        return new{uncachedDragStillPrepared=true,ownershipTransferred=true,lateDuplicateReleased=true};
    }
    [STAThread]static int Main(string[] args){try{bool strict=args.Length<3||args[2]!="--baseline";var bundled=Bundled(args[0],strict);object separate=strict?SeparateFrames(Path.GetDirectoryName(args[1])):null;File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{pass=true,checks,bundled,separate,realWindowsTouched=false}));return 0;}catch(Exception error){File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{pass=false,checks,error=error.ToString()}));return 1;}}
}
