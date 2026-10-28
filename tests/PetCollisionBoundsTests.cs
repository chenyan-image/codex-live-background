using System;using System.Collections.Generic;using System.Drawing;using System.IO;using System.Linq;using System.Reflection;using System.Web.Script.Serialization;using System.Windows.Forms;
internal static class PetCollisionBoundsTests {
 const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;static int checks;
 static void Need(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static T Field<T>(object owner,string name){return (T)owner.GetType().GetField(name,Hidden).GetValue(owner);}
 static object Call(object owner,string name,params object[] values){return owner.GetType().GetMethod(name,Hidden).Invoke(owner,values);}
 static float[] Gaps(Rectangle area,Rectangle limits,Size window,RectangleF body){return new[]{limits.Left+body.Left-area.Left,area.Right-(limits.Right-window.Width+body.Right),limits.Top+body.Top-area.Top,area.Bottom-(limits.Bottom-window.Height+body.Bottom)};}
 static bool Inside(PointF position,RectangleF body,Rectangle area){return position.X+body.Left>=area.Left-.01&&position.X+body.Right<=area.Right+.01&&position.Y+body.Top>=area.Top-.01&&position.Y+body.Bottom<=area.Bottom+.01;}
 static int Edges(PetPack pack,Rectangle area,Rectangle limits,Size size,RectangleF body,float scale,bool strict){
  int early=0;for(int edge=0;edge<4;edge++){
   var position=new PointF(area.Left+area.Width/2F-(body.Left+body.Right)/2,area.Top+area.Height/2F-(body.Top+body.Bottom)/2);var velocity=PointF.Empty;
   if(edge==0){position.X=area.Left-body.Left+5*scale;velocity.X=-200*scale;}if(edge==1){position.X=area.Right-body.Right-5*scale;velocity.X=200*scale;}if(edge==2){position.Y=area.Top-body.Top+5*scale;velocity.Y=-200*scale;}if(edge==3){position.Y=area.Bottom-body.Bottom-5*scale;velocity.Y=200*scale;}
   var motion=new PetMotion(2){Position=position};motion.Drop(velocity);motion.Step(.01,size,limits,false);if(motion.CollisionCount>0)early++;
   if(strict)Need(motion.CollisionCount==0,"Collided while still about 3 logical pixels from the edge: "+pack.Id);
   motion.Step(.1,size,limits,false);Need(motion.CollisionCount==1&&motion.CollisionDirection==edge,"Edge collision was lost: "+pack.Id+" edge="+edge);
   Need(edge==0?motion.Velocity.X>0:edge==1?motion.Velocity.X<0:edge==2?motion.Velocity.Y>0:motion.Velocity.Y<0,"Collision failed to rebound inward");
   Need(Inside(motion.Position,body,area),"Collision crossed the current display/Codex boundary");
   var director=new PetAnimationDirector(pack,new PetBehaviorProfile(),2);director.Tick(.02,motion,"idle",false,false,false);Need(director.CollisionActive&&director.Spec.collision.Any(i=>director.Row==(pack.Actions==null?i:9+i)),"Collision reaction mapping changed: "+pack.Id);
   motion.Drag(new PointF(edge==0?-100000:edge==1?100000:position.X,edge==2?-100000:edge==3?100000:position.Y),size,limits);Need(motion.Dragging&&Inside(motion.Position,body,area),"Manual dragging escaped bounds");
  }return early;
 }
 [STAThread]static int Main(string[] args){try{
  bool strict=args.Length>3;string decoder=Path.Combine(args[0],@"engine\runtime\ffmpeg\ffmpeg.exe");if(!File.Exists(decoder))decoder=Directory.GetFiles(Path.Combine(args[1],"runtime"),"ffmpeg.exe",SearchOption.AllDirectories).First();typeof(PetMediaRuntime).GetField("cached",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,decoder);
  var results=new List<object>();foreach(var pack in PetPack.Discover(args[0],args[1]).Where(p=>p.Available))using(var window=new PetWindow(pack,new PetPreferences{Sound=false,Roaming=false,ShowQuota=false},args[0])){
   Field<Timer>(window,"timer").Stop();var atlas=Field<PetAtlas>(window,"atlas");var director=Field<PetAnimationDirector>(window,"director");var spec=director.Spec;float minGap=Single.MaxValue,maxGap=0,maxUnionExtra=0;int cases=0,early=0;
   foreach(float scale in new[]{1F,1.5F,2F}){
    typeof(PetWindow).GetProperty("RenderScale").GetSetMethod(true).Invoke(window,new object[]{scale});window.ClientSize=(Size)Call(window,"PhysicalSize");
    foreach(int native in spec.idle.Concat(spec.move??new int[0]).Concat(spec.drag??new int[0]).Concat(spec.collision??new int[0]).Distinct()){
     int row=pack.Actions==null?native:9+native;director.Manual(row,0);var current=atlas.ContentBounds(row);var currentBody=(RectangleF)Call(window,"BodyRectangle",current,false);var contact=RectangleF.Union(atlas.IdleContentBounds,current);var collisionBody=(RectangleF)Call(window,"BodyRectangle",contact,false);
     foreach(var area in new[]{new Rectangle(-1920,0,1920,1040),new Rectangle(0,0,2560,1392),new Rectangle(120,80,1100,800)}){
      var limits=(Rectangle)Call(window,"MotionBounds",area);var gaps=Gaps(area,limits,window.Size,collisionBody);minGap=Math.Min(minGap,gaps.Min()/scale);maxGap=Math.Max(maxGap,gaps.Max()/scale);var visible=Gaps(area,limits,window.Size,currentBody);maxUnionExtra=Math.Max(maxUnionExtra,visible.Zip(gaps,(a,b)=>(a-b)/scale).Max());
      if(strict)Need(gaps.All(g=>g>=-.01&&g<=2*scale+1.01),"Collision padding remains too large: "+pack.Id);
      early+=Edges(pack,area,limits,window.Size,collisionBody,scale,strict);cases++;
     }
    }
   }
   Need(!window.Visible,"Bounds proof displayed a pet window");results.Add(new{id=pack.Id,cases,logicalGapMin=minGap,logicalGapMax=maxGap,maxUnionExtra,earlyCollisionsAtFivePixelApproach=early});
  }
  File.WriteAllText(args[2],new JavaScriptSerializer().Serialize(new{pass=true,checks,pets=results,dpiScales=new[]{1,1.5,2},fourEdgeReactionsPreserved=true,crossDisplayBouncePreserved=true,manualDragClamped=true,noWindowsShown=true,liveSettingsChanged=false}));return 0;
 }catch(Exception error){File.WriteAllText(args[2],new JavaScriptSerializer().Serialize(new{pass=false,checks,error=error.ToString()}));return 1;}}
}