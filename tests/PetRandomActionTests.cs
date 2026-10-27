using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

// Exercises the production director only: no windows, processes, or live quota.
internal static class PetRandomActionTests {
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static int checks;
    static void Need(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,Private).GetValue(target);}
    static void Begin(PetAnimationDirector director,int native,string kind,double now){typeof(PetAnimationDirector).GetMethod("Begin",Private).Invoke(director,new object[]{native+9,kind,now});}
    static PetBehaviorProfile Profile(){return new PetBehaviorProfile{SleepSeconds=86400};}
    static PetPack Fixture(bool longClips){
        var all=Enumerable.Range(0,9).Select(i=>new PetAction{id="test-"+i,name="action-"+i,durationMs=longClips?10000:800}).ToList();
        return new PetPack{Id=longClips?"long-clip":"short-clip",Actions=all,RowMap=Enumerable.Range(0,9).ToArray(),Format=longClips?"transparent-video-set":"frame-set",Behavior=new PetBehaviorSpec{
            idle=new[]{0},turn=new[]{8},move=new[]{1,2},drag=new[]{5},sleep=new[]{6},collision=new[]{7},click=new[]{3,4},
            quota=Enumerable.Range(0,6).Select(i=>new[]{3,4}).ToArray(),work=Enumerable.Range(0,6).Select(i=>new[]{3}).ToArray(),categories=new PetActionCategory[0]
        }};
    }
    static void RepeatAfterIdle(){
        foreach(string origin in new[]{"click","random","quota"}){
            var director=new PetAnimationDirector(Fixture(false),Profile(),3);
            Begin(director,3,origin,0);Begin(director,0,"idle",1);
            // The previous action is the only least-played candidate. Previously
            // the balancing filter chose it again before applying the veto.
            var played=Field<Dictionary<int,int>>(director,"played");played[3]=0;played[4]=100;played[8]=100;
            director.Tick(8,new PetMotion(1),"idle",false,false,false);
            Need(Field<string>(director,"kind")=="random"&&director.Row!=12,"Random repeats across idle after "+origin);
            int lastRandom=Field<int>(director,"lastRandomPlayed");Begin(director,0,"idle",9);director.Click(10);
            Need(director.Row!=lastRandom+9,"Click repeats preceding automatic action after idle");
        }
        var single=Fixture(false);single.Behavior.categories=new[]{new PetActionCategory{name="disabled",actions=new[]{4,8},weight=0}};
        var disabled=Profile();disabled.CategoryWeights["disabled"]=0;
        var once=new PetAnimationDirector(single,disabled,2);Begin(once,3,"random",0);Begin(once,0,"idle",1);
        once.Tick(9,new PetMotion(1),"idle",false,false,false);
        Need(Field<string>(once,"kind")=="idle","A sole eligible auto action is repeated indefinitely");
        // Mandatory task actions remain semantic, even if the same row happened
        // to be picked as a random response just before work starts.
        var work=new PetAnimationDirector(Fixture(false),Profile(),1);Begin(work,3,"click",0);
        work.Tick(.2,new PetMotion(1),"working",false,false,false);work.Tick(1,new PetMotion(1),"working",false,false,false);
        Need(Field<string>(work,"kind")=="work"&&work.Row==12,"No-repeat changed the mandatory working action");
    }
    static int[] Daily(PetAnimationDirector director){var spec=director.Spec;return director.ClickPool.Except((spec.idle??new int[0]).Concat(spec.move??new int[0]).Concat(spec.drag??new int[0]).Concat(spec.sleep??new int[0]).Concat(spec.collision??new int[0])).ToArray();}
    static object TestPack(PetPack pack){
        var director=new PetAnimationDirector(pack,Profile(),11);int eligible=director.ClickPool.Length;
        var cycle=new HashSet<int>();int last=-1;
        for(int i=0;i<eligible*8;i++){director.Click(i);Need(director.Row!=last,"Consecutive click repeats: "+pack.Id);last=director.Row;if(i<eligible)Need(cycle.Add(last),"Catalog repeats before every eligible click action plays: "+pack.Id);}
        Need(cycle.Count==eligible,"Click coverage incomplete: "+pack.Id);
        director=new PetAnimationDirector(pack,Profile(),5);int[] daily=Daily(director);var seen=new HashSet<int>();last=-1;int randomStarts=0;
        var motion=new PetMotion(1);double now=0;int generation=director.Generation;
        for(int i=0;i<daily.Length*8+100;i++){
            now+=Math.Max(9,director.Duration(director.Row)+.01);director.Tick(now,motion,"idle",false,false,false);
            if(director.Generation!=generation&&Field<string>(director,"kind")=="random"){
                Need(director.Row!=last,"Automatic action repeats across idle: "+pack.Id);last=director.Row;
                int native=pack.Actions==null?director.Row:director.Row-9;Need(daily.Contains(native),"Automatic catalog bypassed its eligible pool: "+pack.Id);seen.Add(native);randomStarts++;
                if(randomStarts%7==0){director.Click(now+.001);Need(director.Row!=last,"Click immediately repeats preceding auto action: "+pack.Id);last=director.Row;}
            }
            generation=director.Generation;
        }
        Need(seen.Count==daily.Length,"Automatic action coverage incomplete: "+pack.Id+" "+seen.Count+"/"+daily.Length);
        var rest=new PetAnimationDirector(pack,new PetBehaviorProfile{SleepSeconds=30},3);rest.Tick(31,motion,"idle",false,false,false);Need(rest.Sleeping,"Pet never rests: "+pack.Id);generation=rest.Generation;
        rest.Tick(31+rest.Duration(rest.Row)*.5,motion,"idle",false,false,false);Need(rest.Sleeping&&rest.Generation==generation,"Rest clip was interrupted: "+pack.Id);
        for(int i=(int)Math.Ceiling(31+rest.Duration(rest.Row));i<180;i++)rest.Tick(i,motion,"idle",false,false,false);
        Need(!rest.Sleeping&&rest.Generation>generation,"Completed rest did not resume idle actions: "+pack.Id);rest.Click(181);Need(!rest.Sleeping,"Click cannot wake pet: "+pack.Id);
        return new{id=pack.Id,eligibleClickActions=eligible,automaticActions=seen.Count,randomStarts,nonRepeating=true};
    }
    static int Starts(PetPack pack,bool roaming){
        var director=new PetAnimationDirector(pack,Profile(),1);var motion=new PetMotion(2);int generation=director.Generation,count=0;
        for(int step=0;step<2400;step++){
            director.Tick(step*.1,motion,"idle",roaming,false,false);
            if(director.Generation!=generation&&Field<string>(director,"kind")=="random")count++;
            generation=director.Generation;
        }return count;
    }
    static object Frequency(){
        var shortPack=Fixture(false);var fixedPet=new PetAnimationDirector(shortPack,Profile(),1);var roamingPet=new PetAnimationDirector(shortPack,Profile(),1);
        fixedPet.Restore(0);roamingPet.Restore(0);fixedPet.Tick(6.01,new PetMotion(1),"idle",false,false,false);roamingPet.Tick(6.01,new PetMotion(1),"idle",true,false,false);
        Need(Field<string>(fixedPet,"kind")=="random"&&Field<string>(roamingPet,"kind")=="idle","Fixed idle delay was not reduced without changing roaming cadence");
        var frequencies=new List<object>();foreach(bool longClips in new[]{false,true}){
            var pack=Fixture(longClips);int roaming=Starts(pack,true),stationary=Starts(pack,false);
            Need(stationary>roaming&&stationary<roaming*1.5,"Fixed-mode frequency increase is absent or excessive: "+pack.Id+" "+stationary+"/"+roaming);
            frequencies.Add(new{pack=pack.Id,roaming,stationary});
        }
        var videoPack=Fixture(true);var video=new PetAnimationDirector(videoPack,Profile(),1);video.Restore(0);int initial=video.Generation;
        foreach(double now in new[]{6.1,8.1,9.99}){video.Tick(now,new PetMotion(1),"idle",false,true,false);Need(video.Generation==initial,"Fixed mode truncates an undecoded/uncompleted video clip");}
        video.Tick(10.01,new PetMotion(1),"idle",false,true,true);Need(Field<string>(video,"kind")=="random","Completed full video cannot advance to random");
        return frequencies;
    }
    static void QueueCleared(PetAnimationDirector director,string reason){
        Need(Field<int>(director,"queuedRow")==-1&&Field<int[]>(director,"queuedQuotaPool")==null&&Field<string>(director,"queuedQuotaKey")==null,"Stale quota queue survives "+reason);
    }
    static object QueuedQuota(PetPack whale,PetPack maid){
        var results=new List<object>();foreach(string scenario in new[]{"quota","preview","low-quota"}){
            var pack=scenario=="low-quota"?maid:whale;int collisions=0;var seeds=new List<int>();
            for(int seed=0;seed<500;seed++){
                var director=new PetAnimationDirector(pack,Profile(),seed);
                if(scenario=="preview")director.PreviewQuota(3);else director.Quota(scenario=="low-quota"?9.9:30,0);
                int queued=Field<int>(director,"queuedRow");director.Click(.01);int clicked=director.Row;
                if(clicked!=queued)continue;collisions++;seeds.Add(seed);
                int[] pool=scenario=="low-quota"?pack.Behavior.lowQuotaPool:pack.Behavior.quota[3];
                director.Tick(director.Duration(clicked)+.1,new PetMotion(1),"idle",false,false,false);
                Need(Field<string>(director,"kind")=="quota"&&director.Row!=clicked,"Queued quota repeats the immediately preceding click: "+scenario+" seed="+seed);
                Need(pool.Contains(director.Row-9),"Re-picked quota action escaped its semantic band: "+scenario);
                QueueCleared(director,"quota playback");
            }
            Need(collisions>0,"Queued quota/click regression was not exercised: "+scenario);results.Add(new{scenario,collisions,seeds=seeds.ToArray()});
        }
        foreach(string operation in new[]{"restore","manual","start-drag","end-drag","work"}){
            var director=new PetAnimationDirector(whale,Profile(),2);director.PreviewQuota(3);
            Need(Field<int[]>(director,"queuedQuotaPool")!=null,"Quota candidate pool was not stored");
            if(operation=="restore")director.Restore(.1);
            if(operation=="manual")director.Manual(9,.1);
            if(operation=="start-drag")director.StartDrag(.1);
            if(operation=="end-drag")director.EndDrag(.1);
            if(operation=="work"){
                director.Tick(.01,new PetMotion(1),"thinking",false,false,false);director.Tick(.02,new PetMotion(1),"working",false,false,false);
                Need(Field<string>(director,"queuedKind")=="work"&&Field<int[]>(director,"queuedQuotaPool")==null&&Field<string>(director,"queuedQuotaKey")==null,"Work queue inherited the quota band");continue;
            }
            QueueCleared(director,operation);
        }
        foreach(bool unknown in new[]{false,true}){
            var director=new PetAnimationDirector(maid,Profile(),1);director.Quota(9.9,0);Need(Field<string>(director,"queuedQuotaKey")=="low-quota","Low quota queue was not tagged");
            if(unknown)director.QuotaUnknown();else director.Quota(40,1);QueueCleared(director,unknown?"unknown quota":"recovered quota");
        }
        // Preloading must use the corrected candidate as well; another click
        // after preloading is revalidated once more when the event really plays.
        var video=new PetAnimationDirector(whale,Profile(),153);video.Quota(30,0);video.Click(.01);int click=video.Row,prefetched=-1;
        video.Tick(video.Duration(click)-.1,new PetMotion(1),"idle",false,true,false,row=>prefetched=row);
        Need(prefetched!=click&&prefetched==Field<int>(video,"queuedRow"),"Video prefetch kept the stale repeated quota candidate");
        Begin(video,prefetched-9,"click",.02);video.Tick(video.Duration(prefetched)+.1,new PetMotion(1),"idle",false,true,true);
        Need(video.Row!=prefetched&&Field<string>(video,"kind")=="quota","Click after prefetch bypasses the final duplicate check");
        var single=Fixture(false);single.Behavior.quota[3]=new[]{3};var mandatory=new PetAnimationDirector(single,Profile(),1);mandatory.PreviewQuota(3);Begin(mandatory,3,"click",.01);mandatory.Tick(1,new PetMotion(1),"idle",false,false,false);
        Need(mandatory.Row==12&&Field<string>(mandatory,"kind")=="quota","Single-candidate quota event lost its mandatory semantic action");
        return results;
    }
    [STAThread]static int Main(string[] args){try{
        string root=args[0],report=args[1],petState=args.Length>2?args[2]:Path.Combine(Path.GetDirectoryName(report),"empty-state");
        RepeatAfterIdle();var packs=PetPack.Discover(root,petState).Where(p=>p.Available).ToArray();var queuedQuota=QueuedQuota(packs.First(p=>p.Id=="deepseek"),packs.First(p=>p.Id=="deepseek-fat-fish"));
        var results=new List<object>();foreach(var pack in packs)results.Add(TestPack(pack));
        results.Add(TestPack(new PetPack{Id="legacy-sprite"}));Need(results.Count>=3,"Expected both bundled pets plus sprite fallback");var frequency=Frequency();
        File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{pass=true,checks,pets=results,frequency,queuedQuota,workAndSleepPreserved=true,fullVideoClipsPreserved=true,realWindowsTouched=false}));return 0;
    }catch(Exception error){File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{pass=false,checks,error=error.ToString()}));return 1;}}
}
