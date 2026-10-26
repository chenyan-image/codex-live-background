using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

internal sealed class PetBehaviorProfile {
    public bool WhisperEnabled=true,WorkEnabled=true,QuotaEnabled=true;
    public int WhisperSeconds=300,SleepSeconds=300,IdleWeight=10,TurnWeight=5,MoveWeight=5;
    public Dictionary<string,int> CategoryWeights=new Dictionary<string,int>();
    public DateTime NextWhisperUtc;
    public void Normalize(){WhisperSeconds=Math.Max(30,Math.Min(86400,WhisperSeconds));IdleWeight=Limit(IdleWeight);TurnWeight=Limit(TurnWeight);MoveWeight=Limit(MoveWeight);if(CategoryWeights==null)CategoryWeights=new Dictionary<string,int>();foreach(string key in CategoryWeights.Keys.ToArray())CategoryWeights[key]=Limit(CategoryWeights[key]);}
    static int Limit(int value){return Math.Max(0,Math.Min(100,value));}
}
internal sealed class PetActionCategory {public string name{get;set;}public int weight{get;set;}public int[] actions{get;set;}public bool noMirror{get;set;}}
internal sealed class PetBehaviorSpec {
    public int[] idle{get;set;}public int[] turn{get;set;}public int[] move{get;set;}public int[] click{get;set;}public int[] drag{get;set;}public int[] whisper{get;set;}public int[] sleep{get;set;}
    public int[][] quota{get;set;}public int[][] work{get;set;}public PetActionCategory[] categories{get;set;}
    public void Validate(int count){foreach(var pool in new[]{idle,turn,move,click,drag,whisper,sleep}.Concat(quota??new int[0][]).Concat(work??new int[0][]).Concat((categories??new PetActionCategory[0]).Select(v=>v.actions))){if(pool==null)continue;if(pool.Length>128||pool.Any(i=>i<0||i>=count))throw new InvalidDataException("宠物事件动作映射无效。");}if(quota!=null&&quota.Length!=6||work!=null&&work.Length!=6)throw new InvalidDataException("宠物事件需要六个档位。");}
}
// One controller for all image/video formats. Pools contain native action rows;
// names and mappings belong to the pack, never to a hard-coded pet type.
internal sealed class PetAnimationDirector {
    readonly PetPack pack;readonly PetBehaviorProfile profile;readonly Random random;readonly PetBehaviorSpec spec;readonly Dictionary<string,int> previous=new Dictionary<string,int>();
    readonly Queue<int> workChain=new Queue<int>();string kind="",stage="",queuedKind;int queuedRow=-1,lastPhysics=-1,plannedRow=-1;double started,workSince,lastWorkAt,lastQuota=-1800;public int Row{get;private set;}public double Time{get;private set;}public int Generation{get;private set;}
    public bool Sleeping{get{return kind=="sleep";}}
    public string Stage{get{return stage;}}
    public bool CanWalk(double now,PetAtlas atlas){bool phaseOnly=queuedKind=="work"&&(stage=="thinking"||stage=="working"||stage=="result");return !Sleeping&&(kind=="physics"||kind=="move"||workChain.Count==0&&(queuedRow<0||phaseOnly)&&(atlas.IsVideo?atlas.VideoFinished:now-started>=Duration(Row)));}
    public PetAnimationDirector(PetPack pack,PetBehaviorProfile profile,int seed){this.pack=pack;this.profile=profile;random=new Random(seed);spec=pack.Behavior??Fallback(pack);Row=Pick("idle",spec.idle,0);}
    public PetBehaviorSpec Spec{get{return spec;}}
    static int Native(PetPack p,int row){return p.Actions==null?row:9+p.RowMap[row];}
    static PetBehaviorSpec Fallback(PetPack p){
        Func<int,int[]> row=i=>new[]{p.Actions==null?i:p.RowMap[i]};var s=new PetBehaviorSpec{idle=row(0),turn=row(8),move=row(1).Concat(row(2)).Distinct().ToArray(),click=row(3),drag=row(0),whisper=row(3),quota=new[]{row(3),row(0),row(0),row(6),row(5),row(6)},work=new[]{row(7),row(7),row(8),row(6),row(3),row(5)},categories=new PetActionCategory[0]};
        s.sleep=row(6);if(p.Actions!=null){s.sleep=Find(p,new[]{"睡","困倦"},s.sleep);s.drag=Find(p,new[]{"拖拽"},s.drag);s.work=new[]{Find(p,new[]{"收到任务","思考"},s.work[0]),Find(p,new[]{"处理任务","敲键盘","工作"},s.work[1]),Find(p,new[]{"整理归档","整理"},s.work[2]),Find(p,new[]{"翻阅工作","等待"},s.work[3]),Find(p,new[]{"庆祝","完成"},s.work[4]),Find(p,new[]{"生气","道歉"},s.work[5])};s.categories=new[]{new PetActionCategory{name="日常动作",weight=20,actions=Enumerable.Range(0,p.Actions.Count).Where(i=>!s.idle.Contains(i)&&!s.move.Contains(i)&&!s.work.SelectMany(v=>v).Contains(i)&&!s.sleep.Contains(i)).ToArray()}};}else s.categories=new[]{new PetActionCategory{name="日常动作",weight=30,actions=new[]{3,4,6,8}}};
        return s;
    }
    static int[] Find(PetPack pack,string[] terms,int[] fallback){var indices=pack.Actions.Select((a,i)=>new{a,i}).Where(v=>terms.Any(term=>v.a.name.Contains(term))).Select(v=>v.i).ToArray();return indices.Length>0?indices:fallback;}
    int ToRow(int index){return pack.Actions==null?index:9+index;}
    int Pick(string key,int[] pool,int fallback){if(pool==null||pool.Length==0)return fallback;int old;previous.TryGetValue(key,out old);var choices=pool.Where(v=>pool.Length==1||v!=old).ToArray();int value=choices[random.Next(choices.Length)];previous[key]=value;return ToRow(value);}
    int[] StagePool(string value){int i=value=="thinking"?0:value=="working"?1:value=="result"?2:value=="waiting"?3:value=="success"?4:value=="error"?5:-1;return i<0?null:spec.work[i];}
    void Begin(int row,string next,double now){if(kind!="work"&&next=="work")workSince=now;Row=row;kind=next;started=now;Time=0;plannedRow=-1;Generation++;}
    public void Restore(double now){queuedRow=-1;workChain.Clear();stage="";lastWorkAt=now;Begin(Pick("idle",spec.idle,0),"idle",now);}
    public void Manual(int row,double now){queuedRow=-1;Begin(row,"manual",now);}
    public void QueueWhisper(){queuedRow=Pick("whisper",spec.whisper,Row);queuedKind="whisper";}
    public void Click(double now){Begin(Pick("click",spec.click,Row),"click",now);}
    public static int QuotaBand(double remaining){double used=100-Math.Max(0,Math.Min(100,remaining));return used>=100?5:Math.Min(4,(int)(used/20));}
    int quotaBand=-1;
    public void Quota(double remaining,double now,bool preview=false){int band=QuotaBand(remaining);if(!preview&&(!profile.QuotaEnabled||band==quotaBand&&now-lastQuota<1800))return;quotaBand=band;lastQuota=now;queuedRow=Pick("quota"+band,spec.quota[band],Row);queuedKind="quota";}
    public void PreviewQuota(int band){queuedRow=Pick("quota"+band,spec.quota[Math.Max(0,Math.Min(5,band))],Row);queuedKind="quota";}
    public double Duration(int row){return pack.Actions==null?.8:pack.Actions[row<9?pack.RowMap[row]:row-9].durationMs/1000D;}
    public void Tick(double now,PetMotion motion,string currentStage,PetAtlas atlas,bool roaming){
        Tick(now,motion,currentStage,roaming,atlas.IsVideo,atlas.VideoFinished,atlas.PrepareVideo);
    }
    public void Tick(double now,PetMotion motion,string currentStage,bool roaming,bool video,bool videoFinished,Action<int> prefetch=null){
        Time=Math.Max(0,now-started);
        if(currentStage=="thinking"||currentStage=="working"||currentStage=="result"||currentStage=="waiting")lastWorkAt=now;
        if(currentStage!=stage){string before=stage;stage=currentStage;var pool=StagePool(stage);if(profile.WorkEnabled&&pool!=null){int selected=Pick("work-"+stage,pool,Row);bool terminal=stage=="success"||stage=="error"||stage=="waiting",starting=(before==""||before=="idle"||before=="success"||before=="error")&&!terminal;
            if(starting||terminal){if(workChain.Count<4)workChain.Enqueue(selected);if(terminal&&queuedKind=="work")queuedRow=-1;}
            else{queuedRow=selected;queuedKind="work";}
        }}
        int physics=motion.Dragging?10:motion.CollisionDirection>=0?11:motion.Velocity!=PointF.Empty?12:-1;
        if(physics>=0){if(physics!=lastPhysics){int row=physics==10?Pick("drag",spec.drag,Native(pack,0)):physics==11?Native(pack,5):Pick("move",spec.move,Native(pack,1));Begin(row,"physics",now);}lastPhysics=physics;if(physics==11)return;}
        else if(lastPhysics>=0)lastPhysics=-1;
        bool done=video?videoFinished:Time>=Duration(Row);
        if(video&&!done&&Time>Duration(Row)-1&&plannedRow<0){plannedRow=workChain.Count>0?workChain.Peek():queuedRow>=0?queuedRow:profile.WorkEnabled&&StagePool(stage)!=null?Pick("work-"+stage,StagePool(stage),Row):Pick("idle",spec.idle,Row);if(prefetch!=null)prefetch(plannedRow);}
        if(!done)return;
        if(physics>=0){Begin(physics==10?Pick("drag",spec.drag,Row):Pick("move",spec.move,Row),"physics",now);return;}
        if(workChain.Count>0){Begin(workChain.Dequeue(),"work",now);return;}
        if(queuedRow>=0&&!(queuedKind=="work"&&kind=="work"&&now-workSince<2.4&&stage!="error"&&stage!="success"&&stage!="waiting")){int row=queuedRow;queuedRow=-1;Begin(row,queuedKind,now);return;}
        if(profile.WorkEnabled&&(stage=="thinking"||stage=="working"||stage=="waiting"||stage=="result")){Begin(Pick("work-"+stage,StagePool(stage),Row),"work",now);return;}
        if(now-lastWorkAt>=Math.Max(30,profile.SleepSeconds)){Begin(Pick("sleep",spec.sleep??spec.idle,Row),"sleep",now);return;}
        if(kind=="idle"&&Time<8)return;
        if(kind!="idle"&&kind!="sleep"){Begin(Pick("idle",spec.idle,Row),"idle",now);return;}
        var pools=new List<Tuple<string,int[],int>>{Tuple.Create("idle",spec.idle,profile.IdleWeight),Tuple.Create("turn",spec.turn,profile.TurnWeight)};
        if(roaming)pools.Add(Tuple.Create("move",spec.move,profile.MoveWeight));
        foreach(var c in spec.categories??new PetActionCategory[0]){int weight;int chosen=profile.CategoryWeights.TryGetValue(c.name,out weight)?weight:c.weight;pools.Add(Tuple.Create(c.name,c.actions,chosen));}
        int total=pools.Where(p=>p.Item2!=null&&p.Item2.Length>0).Sum(p=>Math.Max(0,p.Item3)),roll=total==0?0:random.Next(total);foreach(var pool in pools){if(pool.Item2==null||pool.Item2.Length==0)continue;roll-=Math.Max(0,pool.Item3);if(roll<0){Begin(Pick(pool.Item1,pool.Item2,Row),pool.Item1,now);if(pool.Item1=="move")motion.Wander();return;}}Begin(Pick("idle",spec.idle,Row),"idle",now);
    }
}
internal static class PetPhrasebook {
    // Original offline lines. No model client, prompt, network or chat history.
    static readonly string[] daily={"眼睛看累了，就看看远处吧。","我在这里，慢慢来就好。","今天也要给自己一点空闲。","这块小地方，刚好够我散步。","喝口水，再继续也来得及。","风景没有跑，我也没有跑。","忙完这段，记得伸个懒腰。","有些灵感，要等一小会儿。","让我帮你看着进度。","不用着急，走一小步也是前进。","刚刚发现一个适合发呆的角落。","认真工作，也别忘了吃饭。","路过一下，不打扰你啦。","今天的桌面也很适合散步。","任务交给 Codex，休息留给你。","我把安静的一小会儿留给你。","事情多的时候，一件件来。","该让肩膀和手腕歇一歇了。","你忙你的，我在旁边陪着。","今天的小目标，也值得期待。"};
    static readonly Dictionary<string,string[]> work=new Dictionary<string,string[]>{{"thinking",new[]{"正在理清思路，稍等一下。","先想清楚，再往下走。","线索正在一点点连起来。"}},{"working",new[]{"已经动手啦，我帮你看着。","正在处理这一项操作。","一步一步，把事情做完。"}},{"result",new[]{"结果回来了，正在核对。","让我看看这一步有没有做好。","把刚才的结果整理一下。"}},{"waiting",new[]{"这一步需要你确认。","我在这里等你的决定。","准备好之后再继续吧。"}},{"success",new[]{"这一项做完啦！","可以轻轻松一口气了。","又完成了一件事情。"}},{"error",new[]{"遇到一点问题，先看看原因。","这一步没有完成，别急。","需要调整一下，再试试看。"}}};
    public static string Whisper(string id,int index){string prefix=id=="deepseek"?"鲸鲸说：":id=="deepseek-fat-fish"?"鲸鱼女仆：":"";return prefix+daily[Math.Abs(index%daily.Length)];}
    public static string Work(string stage,int index){string[] choices;return work.TryGetValue(stage,out choices)?choices[Math.Abs(index%choices.Length)]:null;}
    public static string Reply(string input,string id,int index){string text=(input??"").Trim();if(text.Contains("累")||text.Contains("困"))return "那就先休息一会儿，我帮你守着桌面。";if(text.Contains("你好")||text.Contains("在吗"))return "在呢，今天也一起慢慢来吧。";if(text.Contains("谢谢"))return "能陪着你就很开心啦。";if(text.Contains("工作")||text.Contains("任务"))return "进度可以点上方胶囊查看，我只在本地陪你聊天。";return Whisper(id,index);}
}
