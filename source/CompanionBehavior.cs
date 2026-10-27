using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

internal sealed class PetBehaviorProfile {
    public bool WhisperEnabled=false,WorkEnabled=true,QuotaEnabled=true,WorkSpeechEnabled=false;
    public int WhisperSeconds=600,SleepSeconds=300,IdleWeight=10,TurnWeight=5,MoveWeight=5,Version;
    public Dictionary<string,int> CategoryWeights=new Dictionary<string,int>();
    public DateTime NextWhisperUtc=DateTime.MinValue;
    public void Normalize(){WhisperEnabled=WorkSpeechEnabled=false;Version=3;IdleWeight=Limit(IdleWeight);TurnWeight=Limit(TurnWeight);MoveWeight=Limit(MoveWeight);if(CategoryWeights==null)CategoryWeights=new Dictionary<string,int>();foreach(string key in CategoryWeights.Keys.ToArray())CategoryWeights[key]=Limit(CategoryWeights[key]);}
    static int Limit(int value){return Math.Max(0,Math.Min(100,value));}
}
internal sealed class PetActionCategory {public string name{get;set;}public int weight{get;set;}public int[] actions{get;set;}public bool noMirror{get;set;}}
internal sealed class PetBehaviorSpec {
    public int[] idle{get;set;}public int[] turn{get;set;}public int[] move{get;set;}public int[] click{get;set;}public int[] drag{get;set;}public int[] whisper{get;set;}public int[] sleep{get;set;}public int[] collision{get;set;}public int[] lowQuotaPool{get;set;}public bool lowQuotaOnly{get;set;}
    public int[][] quota{get;set;}public int[][] work{get;set;}public PetActionCategory[] categories{get;set;}
    public void Validate(int count){foreach(var pool in new[]{idle,turn,move,click,drag,whisper,sleep,collision,lowQuotaPool}.Concat(quota??new int[0][]).Concat(work??new int[0][]).Concat((categories??new PetActionCategory[0]).Select(v=>v.actions))){if(pool==null)continue;if(pool.Length>128||pool.Any(i=>i<0||i>=count))throw new InvalidDataException("宠物事件动作映射无效。");}if(quota!=null&&quota.Length!=6||work!=null&&work.Length!=6)throw new InvalidDataException("宠物事件需要六个档位。");}
}
// One controller for all image/video formats. Pools contain native action rows;
// names and mappings belong to the pack, never to a hard-coded pet type.
internal sealed class PetAnimationDirector {
    readonly PetPack pack;readonly PetBehaviorProfile profile;readonly Random random;readonly PetBehaviorSpec spec;readonly Dictionary<string,int> previous=new Dictionary<string,int>();readonly Dictionary<int,int> played=new Dictionary<int,int>();int lastPlayed=-1,lastRandomPlayed=-1,automaticActions;
    readonly Queue<int> workChain=new Queue<int>();string kind="",stage="",queuedKind,queuedQuotaKey;int[] queuedQuotaPool;bool dragHeld,directSwitch,restedSinceActivity;int queuedRow=-1,lastPhysics=-1,plannedRow=-1,lastCollisionCount;double started,workSince,lastWorkAt,lastQuota=-1800;public int Row{get;private set;}public double Time{get;private set;}public int Generation{get;private set;}
    public bool Sleeping{get{return kind=="sleep";}}
    public bool ManualActive{get{return kind=="manual"||kind=="click";}}
    public bool CollisionActive{get{return kind=="collision"&&Time<Duration(Row);}}
    public bool Moving{get{return kind=="physics"&&lastPhysics==12;}}
    public bool DirectSwitch{get{return directSwitch;}}
    public int DragRow{get{return ToRow(spec.drag!=null&&spec.drag.Length>0?spec.drag[0]:pack.Actions==null?0:pack.RowMap[0]);}}
    public string Stage{get{return stage;}}
    public bool CanWalk(double now,PetAtlas atlas){bool phaseOnly=queuedKind=="work"&&(stage=="thinking"||stage=="working"||stage=="result");return !Sleeping&&!CollisionActive&&(kind=="physics"||kind=="move"||workChain.Count==0&&(queuedRow<0||phaseOnly)&&(atlas.IsVideo?atlas.VideoFinished:now-started>=Duration(Row)));}
    public PetAnimationDirector(PetPack pack,PetBehaviorProfile profile,int seed){this.pack=pack;this.profile=profile;random=new Random(seed);spec=pack.Behavior??Fallback(pack);Row=Pick("idle",spec.idle,0);}
    public PetBehaviorSpec Spec{get{return spec;}}
    static int Native(PetPack p,int row){return p.Actions==null?row:9+p.RowMap[row];}
    static PetBehaviorSpec Fallback(PetPack p){
        Func<int,int[]> row=i=>new[]{p.Actions==null?i:p.RowMap[i]};var s=new PetBehaviorSpec{idle=row(0),turn=row(8),move=row(1).Concat(row(2)).Distinct().ToArray(),click=row(3),drag=row(0),collision=row(5),whisper=row(3),quota=new[]{row(3),row(0),row(0),row(6),row(5),row(6)},work=new[]{row(7),row(7),row(8),row(6),row(3),row(5)},categories=new PetActionCategory[0]};
        s.sleep=row(6);if(p.Actions!=null){s.sleep=Find(p,new[]{"睡","困倦"},s.sleep);s.drag=Find(p,new[]{"拖拽"},s.drag);s.work=new[]{Find(p,new[]{"收到任务","思考"},s.work[0]),Find(p,new[]{"处理任务","敲键盘","工作"},s.work[1]),Find(p,new[]{"整理归档","整理","放大镜","查看细节"},s.work[2]),Find(p,new[]{"翻阅工作","等待"},s.work[3]),Find(p,new[]{"庆祝","完成"},s.work[4]),Find(p,new[]{"生气","道歉"},s.work[5])};s.categories=new[]{new PetActionCategory{name="日常动作",weight=20,actions=Enumerable.Range(0,p.Actions.Count).Where(i=>!s.idle.Contains(i)&&!s.move.Contains(i)&&!s.work.SelectMany(v=>v).Contains(i)&&!s.sleep.Contains(i)).ToArray()}};}else s.categories=new[]{new PetActionCategory{name="日常动作",weight=30,actions=new[]{3,4,6,8}}};
        return s;
    }
    static int[] Find(PetPack pack,string[] terms,int[] fallback){var indices=pack.Actions.Select((a,i)=>new{a,i}).Where(v=>terms.Any(term=>v.a.name.Contains(term))).Select(v=>v.i).ToArray();return indices.Length>0?indices:fallback;}
    int ToRow(int index){return pack.Actions==null?index:9+index;}
    int Priority(int index){int result=0;foreach(var category in spec.categories??new PetActionCategory[0])if(category.actions.Contains(index)){int weight;result+=profile.CategoryWeights.TryGetValue(category.name,out weight)?weight:category.weight;}return Math.Max(1,result);}
    static bool RandomSelection(string key){return key=="all-clicks"||key=="auto-catalog"||key=="low-quota"||key.StartsWith("quota",StringComparison.Ordinal);}
    int Pick(string key,int[] pool,int fallback){
        if(pool==null||pool.Length==0)return fallback;
        int old;if(!previous.TryGetValue(key,out old))old=-1;
        var choices=pool.Distinct().ToArray();
        // Keep the last expressive action across the neutral idle transition.
        // Exclude it before balancing counts; a lone least-played candidate
        // must not defeat the no-repeat rule shared by clicks and auto play.
        if(RandomSelection(key)&&choices.Length>1)choices=choices.Where(v=>v!=lastRandomPlayed).ToArray();
        if(choices.Length>1)choices=choices.Where(v=>v!=lastPlayed).ToArray();
        int least=choices.Min(v=>played.ContainsKey(v)?played[v]:0);
        choices=choices.Where(v=>(played.ContainsKey(v)?played[v]:0)==least).ToArray();
        if(choices.Length>1&&choices.Contains(old))choices=choices.Where(v=>v!=old).ToArray();
        int value=choices[random.Next(choices.Length)];
        if(key=="auto-catalog"){int roll=random.Next(choices.Sum(i=>Priority(i)));foreach(int choice in choices){roll-=Priority(choice);if(roll<0){value=choice;break;}}}
        previous[key]=value;return ToRow(value);
    }
    int[] StagePool(string value){int i=value=="thinking"?0:value=="working"?1:value=="result"?2:value=="waiting"?3:value=="success"?4:value=="error"?5:-1;return i<0?null:spec.work[i];}
    static bool ContinuousWork(string value){return value=="thinking"||value=="working"||value=="result";}
    int[] WorkRotationPool(string value){if(!ContinuousWork(value))return StagePool(value);return (spec.work[1]??new int[0]).Concat(spec.work[2]??new int[0]).Concat(spec.work[3]??new int[0]).Distinct().ToArray();}
    int NextWorkRow(){var pool=WorkRotationPool(stage);if(pool==null||pool.Length==0)pool=StagePool(stage);return Pick("work-cycle-"+(ContinuousWork(stage)?"active":stage),pool,Row);}
    int MoveRow(PetMotion motion){int direction=motion.Velocity.X>=0?1:2;if(pack.Actions==null)return direction;int mapped=pack.RowMap[direction];return spec.move!=null&&spec.move.Length<=2&&spec.move.Contains(mapped)?ToRow(mapped):Pick("move",spec.move,Native(pack,direction));}
    void Begin(int row,string next,double now){directSwitch=kind=="drag"||kind=="collision"||next=="drag"||next=="collision";if(kind!="work"&&next=="work")workSince=now;Row=row;lastPlayed=pack.Actions==null?row:row<9?pack.RowMap[row]:row-9;int count;played.TryGetValue(lastPlayed,out count);played[lastPlayed]=count+1;if(next=="click"||next=="random"||next=="quota")lastRandomPlayed=lastPlayed;if(next=="random")automaticActions++;kind=next;started=now;Time=0;plannedRow=-1;Generation++;}
    void ClearQueued(){queuedRow=-1;queuedKind=null;queuedQuotaPool=null;queuedQuotaKey=null;}
    void ClearQueuedQuota(){if(queuedKind=="quota")ClearQueued();}
    void QueueQuota(string key,int[] pool){queuedRow=Pick(key,pool,Row);queuedKind="quota";queuedQuotaPool=pool;queuedQuotaKey=key;}
    int QueuedPlaybackRow(){
        int native=pack.Actions==null?queuedRow:queuedRow<9?pack.RowMap[queuedRow]:queuedRow-9;
        // A click can choose a quota candidate while the event waits for the
        // current clip. Revalidate at playback, within the same quota band.
        if(queuedKind=="quota"&&queuedQuotaPool!=null&&native==lastRandomPlayed&&queuedQuotaPool.Any(i=>i!=native))queuedRow=Pick(queuedQuotaKey,queuedQuotaPool,queuedRow);
        return queuedRow;
    }
    void MarkActivity(double now){lastWorkAt=now;restedSinceActivity=false;}
    public void Restore(double now){ClearQueued();workChain.Clear();stage="";lastPhysics=-1;dragHeld=false;MarkActivity(now);Begin(Pick("idle",spec.idle,0),"idle",now);}
    public void Manual(int row,double now){ClearQueued();MarkActivity(now);dragHeld=false;Begin(row,"manual",now);}
    public void StartDrag(double now){ClearQueuedQuota();dragHeld=true;MarkActivity(now);if(kind=="drag")return;lastPhysics=10;Begin(DragRow,"drag",now);}
    public void EndDrag(double now,bool sliding=false){ClearQueuedQuota();dragHeld=false;MarkActivity(now);lastPhysics=sliding?13:-1;Begin(Pick("idle",spec.idle,0),"idle",now);}
    bool lowQuota;public bool LowQuotaEligible{get{return lowQuota;}}
    public int[] ClickPool{get{return Enumerable.Range(0,pack.Actions==null?9:pack.Actions.Count).Where(i=>!spec.lowQuotaOnly||lowQuota||spec.lowQuotaPool==null||!spec.lowQuotaPool.Contains(i)).ToArray();}}
    public void Click(double now){dragHeld=false;lastPhysics=-1;MarkActivity(now);Begin(Pick("all-clicks",ClickPool,Row),"click",now);}
    public static int QuotaBand(double remaining){double used=100-Math.Max(0,Math.Min(100,remaining));return used>=100?5:Math.Min(4,(int)(used/20));}
    int quotaBand=-1;
    public void QuotaUnknown(){lowQuota=false;if(queuedQuotaKey=="low-quota")ClearQueued();}
    public void Quota(double remaining,double now,bool preview=false){bool before=lowQuota;lowQuota=remaining<10;if(!lowQuota&&queuedQuotaKey=="low-quota")ClearQueued();int band=QuotaBand(remaining);if(spec.lowQuotaOnly&&!preview){if(!lowQuota||!profile.QuotaEnabled||before&&now-lastQuota<120)return;lastQuota=now;QueueQuota("low-quota",spec.lowQuotaPool);return;}if(!preview&&(!profile.QuotaEnabled||band==quotaBand&&now-lastQuota<1800))return;quotaBand=band;lastQuota=now;QueueQuota("quota"+band,spec.quota[band]);}
    public void PreviewQuota(int band){band=Math.Max(0,Math.Min(5,band));QueueQuota("quota"+band,spec.quota[band]);}
    public double Duration(int row){return pack.Actions==null?.8:pack.Actions[row<9?pack.RowMap[row]:row-9].durationMs/1000D;}
    public void Tick(double now,PetMotion motion,string currentStage,PetAtlas atlas,bool roaming){
        Tick(now,motion,currentStage,roaming,atlas.IsVideo,atlas.VideoFinished,atlas.PrepareVideo);
    }
    public void Tick(double now,PetMotion motion,string currentStage,bool roaming,bool video,bool videoFinished,Action<int> prefetch=null){
        Time=Math.Max(0,now-started);
        if(currentStage=="thinking"||currentStage=="working"||currentStage=="result"||currentStage=="waiting")MarkActivity(now);
        if(currentStage!=stage){string before=stage;stage=currentStage;var pool=StagePool(stage);if(profile.WorkEnabled&&pool!=null){int selected=Pick("work-"+stage,pool,Row);bool terminal=stage=="success"||stage=="error"||stage=="waiting",starting=(before==""||before=="idle"||before=="success"||before=="error")&&!terminal;
            if(starting||terminal){if(workChain.Count<4)workChain.Enqueue(selected);if(terminal&&queuedKind=="work")ClearQueued();}
            else{ClearQueued();queuedRow=selected;queuedKind="work";}
        }}
        int physics=motion.Dragging||dragHeld?10:motion.CollisionDirection>=0?11:motion.Sliding?13:motion.Velocity!=PointF.Empty?12:-1;
        if(physics==10){StartDrag(now);return;}
        if(CollisionActive&&physics!=10&&(physics!=11||motion.CollisionCount==lastCollisionCount))return;
        if(physics==13){if(lastPhysics!=13)Begin(Pick("idle",spec.idle,0),"idle",now);lastPhysics=13;return;}
        if(physics>=0){if(physics!=lastPhysics||physics==11&&motion.CollisionCount!=lastCollisionCount){int row=physics==10?Pick("drag",spec.drag,Native(pack,0)):physics==11?Pick("collision",spec.collision,Native(pack,5)):MoveRow(motion);if(physics==11)lastCollisionCount=motion.CollisionCount;Begin(row,physics==11&&spec.collision!=null&&spec.collision.Length>0?"collision":"physics",now);}lastPhysics=physics;if(physics==11)return;}
        else if(lastPhysics>=0){bool stoppedWalking=lastPhysics==12&&kind=="physics";lastPhysics=-1;if(stoppedWalking)Begin(Pick("idle",spec.idle,Row),"idle",now);}
        bool done=video?videoFinished:Time>=Duration(Row);
        if(video&&!done&&Time>Duration(Row)-1&&plannedRow<0){plannedRow=workChain.Count>0?workChain.Peek():queuedRow>=0?QueuedPlaybackRow():profile.WorkEnabled&&StagePool(stage)!=null?NextWorkRow():Pick("idle",spec.idle,Row);if(prefetch!=null)prefetch(plannedRow);}
        if(!done)return;
        if(physics>=0){Begin(physics==10?Pick("drag",spec.drag,Row):MoveRow(motion),"physics",now);return;}
        if(workChain.Count>0){Begin(workChain.Dequeue(),"work",now);return;}
        if(queuedRow>=0&&!(queuedKind=="work"&&kind=="work"&&now-workSince<2.4&&stage!="error"&&stage!="success"&&stage!="waiting")){int row=QueuedPlaybackRow();string next=queuedKind;if(next=="work"&&ContinuousWork(stage)&&row==Row)row=NextWorkRow();ClearQueued();Begin(row,next,now);return;}
        if(profile.WorkEnabled&&(stage=="thinking"||stage=="working"||stage=="waiting"||stage=="result")){Begin(plannedRow>=0&&WorkRotationPool(stage)!=null&&WorkRotationPool(stage).Any(i=>ToRow(i)==plannedRow)&&plannedRow!=Row?plannedRow:NextWorkRow(),"work",now);return;}
        // Roaming only controls displacement. A complete rest clip returns to
        // idle; rearm rest after real input/work instead of freezing or napping repeatedly.
        if(kind=="sleep"){Begin(Pick("idle",spec.idle,Row),"idle",now);return;}
        if(!restedSinceActivity&&now-lastWorkAt>=Math.Max(30,profile.SleepSeconds)){restedSinceActivity=true;Begin(Pick("sleep",spec.sleep??spec.idle,Row),"sleep",now);return;}
        if(kind=="idle"&&Time<(roaming?8:6))return;
        // Long clips keep their complete animation. Once every four fixed-mode
        // actions, omit the extra idle clip instead of cutting a video short.
        bool continueCatalog=!roaming&&kind=="random"&&automaticActions%4==0&&Duration(Row)>=8;
        if(kind!="idle"&&kind!="sleep"&&!continueCatalog){Begin(Pick("idle",spec.idle,Row),"idle",now);return;}
        // Shared play counts cover clicks and idle actions. Exhaust less-played
        // actions before reusing favorites; reactive/work states remain semantic.
        var reserved=(spec.idle??new int[0]).Concat(spec.move??new int[0]).Concat(spec.drag??new int[0]).Concat(spec.sleep??new int[0]).Concat(spec.collision??new int[0]);
        var disabled=(spec.categories??new PetActionCategory[0]).Where(c=>profile.CategoryWeights.ContainsKey(c.name)&&profile.CategoryWeights[c.name]==0).SelectMany(c=>c.actions);
        int[] daily=ClickPool.Except(reserved).Except(disabled).ToArray();
        if(daily.Length==1&&daily[0]==lastRandomPlayed)daily=new int[0];
        Begin(Pick("auto-catalog",daily,Pick("idle",spec.idle,Row)),daily.Length>0?"random":"idle",now);
    }
}
