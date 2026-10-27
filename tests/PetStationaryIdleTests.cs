using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

internal static class PetStationaryIdleTests {
    static int checks;
    static void Need(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Tick(PetAnimationDirector director,PetMotion motion,double now,string stage,bool roaming,bool video){director.Tick(now,motion,stage,roaming,video,director.Time+.11>=director.Duration(director.Row));}
    static object Stationary(PetPack pack){
        bool video=pack.Format=="transparent-video-set";
        var director=new PetAnimationDirector(pack,new PetBehaviorProfile{SleepSeconds=30},17);
        var motion=new PetMotion(3){Position=new PointF(100,100)};director.Restore(0);
        int generation=director.Generation,rests=0,changesAfterRest=0;var laterActions=new HashSet<int>();
        for(int step=1;step<=1800;step++){
            double now=step*.1;motion.Update(.1,new Size(100,100),new Rectangle(0,0,1000,800),false,false);
            Tick(director,motion,now,"idle",false,video);
            if(director.Generation!=generation){if(director.Sleeping)rests++;if(now>70){changesAfterRest++;laterActions.Add(director.Row);}}
            generation=director.Generation;
        }
        Need(rests==1,"Inactivity must play one complete rest action: "+pack.Id+" rests="+rests);
        Need(changesAfterRest>=3&&laterActions.Count>=3,"Stationary pet freezes after rest instead of resuming idle actions: "+pack.Id);
        Need(!director.Sleeping,"Finished rest permanently holds the stationary pet: "+pack.Id);
        Need(motion.Position==new PointF(100,100)&&motion.Velocity==PointF.Empty&&!motion.Walking,"Idle animation moves a pet with roaming disabled: "+pack.Id);
        // A real user interaction starts a new inactivity period; it can rest once again.
        director.Click(181);for(int step=1811;step<=2700;step++){double now=step*.1;Tick(director,motion,now,"idle",false,video);if(director.Generation!=generation&&director.Sleeping)rests++;generation=director.Generation;}
        Need(rests==2,"Interaction did not rearm one rest action: "+pack.Id+" rests="+rests);
        int before=director.Generation;Tick(director,motion,271,"working",false,video);for(int step=2711;step<=3000;step++)Tick(director,motion,step*.1,"working",false,video);
        Need(director.Generation>before&&!director.Sleeping&&director.Stage=="working","Fixed pet stopped responding to work: "+pack.Id);
        return new{id=pack.Id,video,rests,changesAfterRest,laterActions=laterActions.Count,positionUnchanged=true};
    }
    static void StopWalking(PetPack pack){
        var director=new PetAnimationDirector(pack,new PetBehaviorProfile(),1);var motion=new PetMotion(2){Position=new PointF(200,200)};
        director.Restore(0);motion.ResumeAutonomy(true);director.Tick(.1,motion,"idle",true,false,false);
        Need(director.Moving,"Fixture did not start walking: "+pack.Id);
        motion.Update(.1,new Size(100,100),new Rectangle(0,0,1000,800),false,false);director.Tick(.2,motion,"idle",false,false,false);
        Need(!director.Moving&&director.Spec.idle.Any(i=>director.Row==(pack.Actions==null?i:9+i)),"Disabling roaming leaves the walking pose playing in place: "+pack.Id);
    }
    [STAThread]static int Main(string[] args){try{
        var packs=PetPack.Discover(args[0],args.Length>2?args[2]:Path.Combine(Path.GetDirectoryName(args[1]),"empty-state")).Where(p=>p.Available).ToList();
        packs.Add(new PetPack{Id="sprite-fallback"});var results=new List<object>();
        foreach(var pack in packs){results.Add(Stationary(pack));StopWalking(pack);}
        File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{pass=true,checks,pets=results,noWindowsShown=true,noLiveSettingsChanged=true}));return 0;
    }catch(Exception error){File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{pass=false,checks,error=error.ToString()}));return 1;}}
}
