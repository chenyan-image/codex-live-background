using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class PetPreferences {
    public bool Enabled=true,Roaming=true,Sound=true,CodexOnly=true,ShowQuota=true;
    public string SelectedId="deepseek";
    public int Scale=100,CapsuleScale=100,X=int.MinValue,Y=int.MinValue;
    public Dictionary<string,PetBehaviorProfile> Behaviors=new Dictionary<string,PetBehaviorProfile>();
    public PetBehaviorProfile ForPet(string id){if(Behaviors==null)Behaviors=new Dictionary<string,PetBehaviorProfile>();PetBehaviorProfile value;if(!Behaviors.TryGetValue(id,out value)||value==null)Behaviors[id]=value=new PetBehaviorProfile();value.Normalize();return value;}
    public static PetPreferences Load(string state){
        string file=Path.Combine(state,"pet-settings.json");if(!File.Exists(file))return new PetPreferences();
        try{if(new FileInfo(file).Length>65536)throw new InvalidDataException();var value=new JavaScriptSerializer().Deserialize<PetPreferences>(File.ReadAllText(file));if(value==null)throw new InvalidDataException();value.Scale=Math.Max(70,Math.Min(150,value.Scale));value.CapsuleScale=Math.Max(70,Math.Min(150,value.CapsuleScale));return value;}
        catch{File.Copy(file,file+".damaged-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),false);return new PetPreferences();}
    }
    public void Save(string state){Directory.CreateDirectory(state);string file=Path.Combine(state,"pet-settings.json"),temp=file+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));if(File.Exists(file))File.Replace(temp,file,file+".bak");else File.Move(temp,file);}
}
internal sealed class PetAction {
    public string id{get;set;}public string name{get;set;}public string file{get;set;}public int durationMs{get;set;}public int frames{get;set;}
    public int columns{get;set;}public int row{get;set;}public int cellWidth{get;set;}public int cellHeight{get;set;}public string[] files{get;set;}public int[] frameDurations{get;set;}public int[] frameIndices{get;set;}public bool flip{get;set;}public double fps{get;set;}
}
internal sealed class PetPack {
    public string Id,Name,Directory,AtlasFile,Author,Source;
    public bool Builtin,Optional,ProceduralMotion;public string Format;public List<PetAction> Actions;public int[] RowMap;public PetBehaviorSpec Behavior;
    public bool Available {get{return !Optional;}}
    public override string ToString(){return Name;}
    static string Text(Dictionary<string,object> data,string key,string fallback){object value;return data.TryGetValue(key,out value)&&value is string?(string)value:fallback;}
    public static PetPack Read(string directory,bool builtin){
        string manifest=Path.Combine(directory,"pet.json");if(!File.Exists(manifest)||new FileInfo(manifest).Length>65536)throw new InvalidDataException("缺少有效的 pet.json。");
        var json=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(manifest));
        string sprite=Text(json,"spritesheetPath","spritesheet.png");
        if(Path.GetFileName(sprite)!=sprite||sprite.IndexOfAny(new[]{'/', '\\', ':'})>=0)throw new InvalidDataException("图集必须位于宠物目录内。");
        string format=Text(json,"format","");bool optional=format=="optional-bailong"||format=="optional-remielle";
        string file=Path.Combine(directory,sprite);if(!optional&&(!File.Exists(file)||Path.GetExtension(file).ToLowerInvariant()!=".png"))throw new InvalidDataException("宠物尚未转换为可读的 PNG 图集。");
        string name=WallpaperLibrary.ValidateName(Text(json,"displayName",Path.GetFileName(directory)));
        var pack=new PetPack{Id=Path.GetFileName(directory),Name=name,Directory=directory,AtlasFile=file,Builtin=builtin,Author="本地导入",Source="",Optional=optional,Format=format,ProceduralMotion=json.ContainsKey("proceduralMotion")&&Object.Equals(json["proceduralMotion"],true)};
        if(format=="animated-gif-set"||format=="frame-set"||format=="transparent-video-set"){
            object value;if(!json.TryGetValue("actions",out value))throw new InvalidDataException("缺少动作列表。");pack.Actions=new JavaScriptSerializer().Deserialize<List<PetAction>>(new JavaScriptSerializer().Serialize(value));
            if(pack.Actions==null||pack.Actions.Count<1||pack.Actions.Count>128)throw new InvalidDataException("动作数量无效。");
            foreach(var action in pack.Actions){
                string[] files=action.files??new[]{action.file};if(files.Length<1||files.Length>512||action.frames<1||action.frames>512||action.durationMs<1||action.durationMs>60000)throw new InvalidDataException("动作帧或时长无效。");
                foreach(string frameFile in files)if(String.IsNullOrEmpty(frameFile)||frameFile!=Path.GetFileName(frameFile)||frameFile.IndexOfAny(new[]{'/','\\',':'})>=0||Path.GetExtension(frameFile).ToLowerInvariant()!=(format=="frame-set"?".png":format=="transparent-video-set"?".webm":".gif")||!File.Exists(Path.Combine(directory,frameFile))||new FileInfo(Path.Combine(directory,frameFile)).Length>32*1024*1024)throw new InvalidDataException("动作文件无效。");
                if(format=="transparent-video-set"&&(action.cellWidth<1||action.cellWidth>1280||action.cellHeight<1||action.cellHeight>1280||action.fps<1||action.fps>60))throw new InvalidDataException("透明视频参数无效。");
                if(action.frameDurations!=null&&(action.frameDurations.Length!=action.frames||action.frameDurations.Any(ms=>ms<10||ms>30000)))throw new InvalidDataException("动作帧时间无效。");
                if(action.frameIndices!=null&&(action.frameIndices.Length!=action.frames||action.frameIndices.Any(i=>i<0||i>4095)))throw new InvalidDataException("动作帧序号无效。");
                if(format=="frame-set"&&(action.columns<1||action.columns>32||action.row<0||action.row>127||action.cellWidth<1||action.cellWidth>1024||action.cellHeight<1||action.cellHeight>1024||(action.files!=null&&action.files.Length!=action.frames)))throw new InvalidDataException("动作图集布局无效。");
            }
            pack.RowMap=new JavaScriptSerializer().Deserialize<int[]>(new JavaScriptSerializer().Serialize(json["rowMap"]));if(pack.RowMap.Length!=9||pack.RowMap.Any(i=>i<0||i>=pack.Actions.Count))throw new InvalidDataException("动作映射无效。");
        }
        if(json.ContainsKey("behavior")){pack.Behavior=new JavaScriptSerializer().Deserialize<PetBehaviorSpec>(new JavaScriptSerializer().Serialize(json["behavior"]));pack.Behavior.Validate(pack.Actions==null?9:pack.Actions.Count);}
        string source=Path.Combine(directory,"source.json");if(File.Exists(source)&&new FileInfo(source).Length<65536){var origin=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(source));pack.Author=Text(origin,"author",pack.Author);pack.Source=Text(origin,"source","");}
        return pack;
    }
    public static List<PetPack> Discover(string root,string state){
        var result=new List<PetPack>();foreach(var pair in new[]{Tuple.Create(Path.Combine(root,@"source\assets\pets"),true),Tuple.Create(Path.Combine(state,"pets"),false)}){
            if(!System.IO.Directory.Exists(pair.Item1))continue;
            foreach(string path in System.IO.Directory.GetDirectories(pair.Item1).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))try{result.Add(Read(path,pair.Item2));}catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}
        }return result.GroupBy(pack=>pack.Id,StringComparer.OrdinalIgnoreCase).Select(group=>group.OrderByDescending(pack=>pack.Available).First()).OrderBy(pack=>pack.Optional?4:pack.Id=="deepseek"?0:pack.Id=="deepseek-fat-fish"?1:pack.Id.StartsWith("dyber-")?2:3).ThenBy(pack=>pack.Name,StringComparer.CurrentCulture).ToList();
    }
}
internal sealed partial class PetAtlas : IDisposable {
    public const int Columns=8,CellWidth=192,CellHeight=208;
    Bitmap bitmap,gif,currentPng;readonly PetPack animations;int currentAction=-1,lastGifFrame=-1;int[] gifDelays;
    readonly Dictionary<string,Bitmap> pngs=new Dictionary<string,Bitmap>(StringComparer.OrdinalIgnoreCase);readonly LinkedList<string> pngOrder=new LinkedList<string>();
    public Bitmap Bitmap {get{return videoFrame??currentPng??gif??bitmap;}}public readonly int Rows;
    public readonly int[] FrameCounts;
    public PetAtlas(PetPack pack):this(pack.AtlasFile,pack){}
    public PetAtlas(PetPack pack,bool preview):this(pack.AtlasFile,pack,preview){}
    public PetAtlas(string path):this(path,null){}
    PetAtlas(string path,PetPack pack,bool preview=false){
        previewOnly=preview;
        if(pack!=null&&pack.Format=="transparent-video-set"){animations=pack;Rows=9+pack.Actions.Count;FrameCounts=new int[Rows];for(int i=0;i<Rows;i++)FrameCounts[i]=pack.Actions[i<9?pack.RowMap[i]:i-9].frames;using(var source=Image.FromFile(path))bitmap=new Bitmap(source);return;}
        if(pack!=null&&pack.Actions!=null){animations=pack;Rows=9+pack.Actions.Count;FrameCounts=new int[Rows];for(int i=0;i<Rows;i++)FrameCounts[i]=Math.Max(1,pack.Actions[i<9?pack.RowMap[i]:i-9].frames);Frame(0,0);return;}
        if(new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("宠物图集不能超过 32 MiB。");
        using(var source=Image.FromFile(path)){
            if(source.Width!=1536||(source.Height!=1872&&source.Height!=2288))throw new InvalidDataException("图集需要 1536×1872（v1）或 1536×2288（v2），每格 192×208。");
            bitmap=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb);using(var g=Graphics.FromImage(bitmap))g.DrawImageUnscaled(source,0,0);
        }
        Rows=Bitmap.Height/CellHeight;FrameCounts=new int[Rows];
        var data=Bitmap.LockBits(new Rectangle(0,0,Bitmap.Width,Bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try{
            var bytes=new byte[data.Stride*Bitmap.Height];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);
            for(int row=0;row<Rows;row++)for(int col=0;col<Columns;col++){
                bool occupied=false;
                for(int y=0;y<CellHeight&&!occupied;y++)for(int x=0;x<CellWidth;x++)if(bytes[(row*CellHeight+y)*data.Stride+(col*CellWidth+x)*4+3]>8){occupied=true;break;}
                if(!occupied)break;FrameCounts[row]++;
            }
        }finally{Bitmap.UnlockBits(data);}
        if(FrameCounts.Take(9).Any(x=>x==0)){Bitmap.Dispose();throw new InvalidDataException("图集的九种基础动作必须都有连续可见帧。");}
    }
    public int ActionIndex(int row){return row<9?animations.RowMap[Math.Max(0,row)]:Math.Min(animations.Actions.Count-1,row-9);}
    Bitmap Png(string file){
        Bitmap value;if(pngs.TryGetValue(file,out value)){pngOrder.Remove(file);pngOrder.AddLast(file);return value;}
        using(var source=Image.FromFile(Path.Combine(animations.Directory,file))){if(source.Width>8192||source.Height>16384||(long)source.Width*source.Height>24000000)throw new InvalidDataException("动作图集尺寸超限。");value=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb);using(var g=Graphics.FromImage(value))g.DrawImageUnscaled(source,0,0);}
        while(pngOrder.Count>=3){string oldest=pngOrder.First.Value;pngOrder.RemoveFirst();pngs[oldest].Dispose();pngs.Remove(oldest);}pngs.Add(file,value);pngOrder.AddLast(file);return value;
    }
    public Rectangle Frame(int row,int frame){
        row=Math.Max(0,Math.Min(Rows-1,row));frame=Math.Max(0,frame);
        if(IsVideo)return new Rectangle(0,0,Bitmap.Width,Bitmap.Height);
        if(animations!=null){int action=ActionIndex(row);var spec=animations.Actions[action];
            if(animations.Format=="frame-set"){
                int index=frame%spec.frames;string file=spec.files==null?spec.file:spec.files[index];currentPng=Png(file);int cell=spec.frameIndices!=null?spec.frameIndices[index]:spec.files==null?index:0;var box=new Rectangle(cell%spec.columns*spec.cellWidth,(spec.row+cell/spec.columns)*spec.cellHeight,spec.cellWidth,spec.cellHeight);
                if(box.Right>currentPng.Width||box.Bottom>currentPng.Height)throw new InvalidDataException("动作帧超出图集。");return box;
            }
            if(action!=currentAction){if(gif!=null)gif.Dispose();gif=new Bitmap(Path.Combine(animations.Directory,spec.file));if(gif.Width>512||gif.Height>512){gif.Dispose();gif=null;throw new InvalidDataException("动作尺寸超限。");}currentAction=action;lastGifFrame=-1;int count=gif.GetFrameCount(FrameDimension.Time);gifDelays=Enumerable.Repeat(Math.Max(10,spec.durationMs/count),count).ToArray();try{byte[] delays=gif.GetPropertyItem(0x5100).Value;for(int i=0;i<count&&i*4+4<=delays.Length;i++)gifDelays[i]=Math.Max(10,BitConverter.ToInt32(delays,i*4)*10);}catch(ArgumentException){}}
            int selected=frame%gifDelays.Length;if(selected!=lastGifFrame){gif.SelectActiveFrame(FrameDimension.Time,selected);lastGifFrame=selected;}return new Rectangle(0,0,gif.Width,gif.Height);
        }
        return new Rectangle((frame%Math.Max(1,FrameCounts[row]))*CellWidth,row*CellHeight,CellWidth,CellHeight);
    }
    public static int SampleFrame(double seconds,int frames,int durationMs,int[] delays){
        if(delays==null)return (int)(Math.Max(0,seconds)*1000/Math.Max(1,durationMs/(double)Math.Max(1,frames)))%Math.Max(1,frames);
        double time=Math.Max(0,seconds)*1000%delays.Sum();for(int i=0;i<delays.Length;i++){if(time<delays[i])return i;time-=delays[i];}return delays.Length-1;
    }
    public void Draw(Graphics graphics,RectangleF destination,int row,double time){
        if(IsVideo){DrawVideo(graphics,destination,row,time);return;}
        int frame=(int)(Math.Max(0,time)*10);if(animations!=null){int index=ActionIndex(row);var action=animations.Actions[index];if(animations.Format!="frame-set"&&index!=currentAction)Frame(row,0);frame=SampleFrame(time,action.frames,action.durationMs,animations.Format=="frame-set"?action.frameDurations:gifDelays);}
        var source=Frame(row,frame);if(animations!=null&&animations.ProceduralMotion){float sway=(float)Math.Sin(time*2.2)*1.2F;destination.X+=sway;destination.Y+=(float)Math.Sin(time*1.6)*.8F;}
        var saved=graphics.Save();if(animations!=null&&animations.Actions[ActionIndex(row)].flip){graphics.TranslateTransform(destination.Left+destination.Right,0);graphics.ScaleTransform(-1,1);}graphics.DrawImage(Bitmap,destination,source,GraphicsUnit.Pixel);graphics.Restore(saved);
    }
    public void Dispose(){DisposeVideo();if(bitmap!=null)bitmap.Dispose();if(gif!=null)gif.Dispose();foreach(var value in pngs.Values)value.Dispose();pngs.Clear();pngOrder.Clear();currentPng=null;}
}
internal sealed class PetDragTracker {
    readonly Queue<Tuple<Point,double>> samples=new Queue<Tuple<Point,double>>();Point lastPoint;double lastMove;
    public void Reset(Point point,double time){samples.Clear();lastPoint=point;lastMove=time;samples.Enqueue(Tuple.Create(point,time));}
    public void Add(Point point,double time){if(point!=lastPoint){lastMove=time;lastPoint=point;}samples.Enqueue(Tuple.Create(point,time));while(samples.Count>2&&(time-samples.Peek().Item2>.14||samples.Count>32))samples.Dequeue();}
    public PointF Release(Point point,double time){Add(point,time);if(time-lastMove>.12||samples.Count<2)return PointF.Empty;var first=samples.Peek();double elapsed=time-first.Item2;if(elapsed<.008)return PointF.Empty;return new PointF((float)((point.X-first.Item1.X)/elapsed),(float)((point.Y-first.Item1.Y)/elapsed));}
}
internal sealed class PetMotion {
    readonly Random random;
    public PointF Position;public PointF Velocity;
    public bool Dragging,Sliding,ClickResponse;public int Row,CollisionDirection=-1;public double ReactionRemaining,ReactionDuration=1.3;
    public float PressScale=1;public bool Pressed;
    double decision=7;bool walking;public int CollisionCount;
    public PetMotion(int seed){random=new Random(seed);}
    public static PointF Clamp(PointF p,Size size,Rectangle bounds){return new PointF(Math.Max(bounds.Left,Math.Min(bounds.Right-size.Width,p.X)),Math.Max(bounds.Top,Math.Min(bounds.Bottom-size.Height,p.Y)));}
    public void Place(PointF p,Size size,Rectangle bounds){Position=Clamp(p,size,bounds);}
    public void Interact(int row,double duration=1.3){Sliding=false;ClickResponse=false;Row=Math.Max(0,Math.Min(255,row));ReactionDuration=ReactionRemaining=Math.Max(.3,Math.Min(60,duration));CollisionDirection=-1;decision=1.5;Velocity=PointF.Empty;}
    public void ReactToClick(){int pose=Row;Interact(pose,.65);ClickResponse=true;}
    public void StepPress(double delta){float target=Pressed?.94F:1F;PressScale+=(target-PressScale)*(float)(1-Math.Exp(-Math.Max(0,Math.Min(.1,delta))*(Pressed?28:18)));if(Math.Abs(target-PressScale)<.0001F)PressScale=target;}
    public void Drag(PointF p,Size size,Rectangle bounds){Dragging=true;Sliding=false;ClickResponse=false;Velocity=PointF.Empty;Position=Clamp(p,size,bounds);Row=0;CollisionDirection=-1;ReactionRemaining=0;}
    public void Drop(){Dragging=false;Interact(3);}
    public void ResumeAutonomy(){Dragging=Sliding=ClickResponse=false;walking=false;Velocity=PointF.Empty;ReactionRemaining=0;CollisionDirection=-1;Row=0;decision=6+random.NextDouble()*6;}
    public void Wander(){if(Dragging||Sliding||ReactionRemaining>0||decision>0)return;walking=false;decision=0;}
    public void Update(double delta,Size size,Rectangle bounds,bool roaming,bool working){Step(delta,size,bounds,roaming);if(working&&!Dragging&&!Sliding&&Velocity==PointF.Empty&&ReactionRemaining<=0)Row=7;}
    public void Drop(PointF velocity){double speed=Math.Sqrt(velocity.X*velocity.X+velocity.Y*velocity.Y);if(speed<75){Drop();return;}Dragging=false;Sliding=true;ClickResponse=Pressed=false;PressScale=1;CollisionDirection=-1;ReactionRemaining=0;double factor=Math.Min(1,1800/speed);Velocity=new PointF((float)(velocity.X*factor),(float)(velocity.Y*factor));Row=Velocity.X>=0?1:2;decision=1.5;}
    void Slide(double delta,Size size,Rectangle bounds){
        const double drag=3.8;double decay=Math.Exp(-drag*delta),travel=(1-decay)/drag;var next=new PointF(Position.X+(float)(Velocity.X*travel),Position.Y+(float)(Velocity.Y*travel));var velocity=new PointF((float)(Velocity.X*decay),(float)(Velocity.Y*decay));int edge=-1;
        if(next.X<bounds.Left||next.X+size.Width>bounds.Right){edge=next.X<bounds.Left?0:1;velocity.X=-velocity.X*.48F;velocity.Y*=.88F;}
        if(next.Y<bounds.Top||next.Y+size.Height>bounds.Bottom){if(edge<0)edge=next.Y<bounds.Top?2:3;velocity.Y=-velocity.Y*.48F;velocity.X*=.88F;}
        Position=Clamp(next,size,bounds);Velocity=velocity;
        if(edge>=0){Row=5;CollisionDirection=edge;ReactionDuration=ReactionRemaining=.85;CollisionCount++;}
        else if(ReactionRemaining>0){ReactionRemaining=Math.Max(0,ReactionRemaining-delta);if(ReactionRemaining==0)CollisionDirection=-1;}
        else Row=Velocity.X>=0?1:2;
        if(Velocity.X*Velocity.X+Velocity.Y*Velocity.Y<25*25){Sliding=false;Velocity=PointF.Empty;decision=1.1;if(ReactionRemaining<=0)Row=0;}
    }
    public void Step(double delta,Size size,Rectangle bounds,bool roaming){
        delta=Math.Max(0,Math.Min(.1,delta));Position=Clamp(Position,size,bounds);if(Dragging)return;
        if(Sliding){Slide(delta,size,bounds);return;}
        if(ReactionRemaining>0){ReactionRemaining=Math.Max(0,ReactionRemaining-delta);if(ReactionRemaining>0)return;CollisionDirection=-1;ClickResponse=false;}
        decision-=delta;
        if(!roaming){walking=false;Velocity=PointF.Empty;Row=0;return;}
        if(decision<=0){
            walking=!walking;
            if(!walking){Velocity=PointF.Empty;Row=0;decision=12+random.NextDouble()*12;}
            else{double angle=random.NextDouble()*Math.PI*2,speed=24+random.NextDouble()*12;Velocity=new PointF((float)(Math.Cos(angle)*speed),(float)(Math.Sin(angle)*speed*.6));decision=1.5+random.NextDouble()*1.8;}
        }
        if(Velocity==PointF.Empty)return;
        var next=new PointF(Position.X+Velocity.X*(float)delta,Position.Y+Velocity.Y*(float)delta);
        bool hitX=next.X<bounds.Left||next.X+size.Width>bounds.Right,hitY=next.Y<bounds.Top||next.Y+size.Height>bounds.Bottom;
        int edge=hitX?(next.X<bounds.Left?0:1):hitY?(next.Y<bounds.Top?2:3):-1;
        Position=Clamp(next,size,bounds);
        if(edge>=0){Velocity=new PointF(hitX?-Velocity.X:Velocity.X,hitY?-Velocity.Y:Velocity.Y);Row=5;CollisionDirection=edge;ReactionDuration=ReactionRemaining=.85;CollisionCount++;decision=Math.Max(.8,decision);}
        else Row=Velocity.X>=0?1:2;
    }
}
internal static class PetImporter {
    public static string Extract(string input,string state){
        if(!File.Exists(input))throw new FileNotFoundException("找不到宠物包。",input);
        if(new FileInfo(input).Length>32*1024*1024)throw new InvalidDataException("宠物包不能超过 32 MiB。");
        string parent=Path.Combine(state,"pet-imports"),stage=Path.Combine(parent,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try{
            if(Path.GetExtension(input).Equals(".zip",StringComparison.OrdinalIgnoreCase)){
                using(var zip=ZipFile.OpenRead(input)){
                    if(zip.Entries.Count>40||zip.Entries.Sum(e=>e.Length)>64*1024*1024)throw new InvalidDataException("宠物包内容过多。");
                    foreach(var entry in zip.Entries)if(entry.FullName.Replace('\\','/').Split('/').Any(p=>p=="..")||entry.FullName.StartsWith("/")||entry.FullName.Contains(":"))throw new InvalidDataException("宠物包包含无效路径。");
                    var manifests=zip.Entries.Where(e=>e.Name=="pet.json").ToArray();if(manifests.Length!=1)throw new InvalidDataException("宠物包必须只有一个 pet.json。");
                    var item=manifests[0];if(item.Length>65536)throw new InvalidDataException("pet.json 过大。");
                    string text;using(var reader=new StreamReader(item.Open()))text=reader.ReadToEnd();
                    var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(text);object sprite;string name=data.TryGetValue("spritesheetPath",out sprite)?Convert.ToString(sprite):"spritesheet.webp";
                    if(name!=Path.GetFileName(name)||name.IndexOfAny(new[]{'/','\\',':'})>=0)throw new InvalidDataException("图集路径无效。");
                    string prefix=item.FullName.Substring(0,item.FullName.Length-item.Name.Length);var atlases=zip.Entries.Where(e=>e.FullName==prefix+name).ToArray();if(atlases.Length!=1||atlases[0].Length>32*1024*1024)throw new InvalidDataException("未找到对应图集。");
                    if(!new[]{".png",".webp"}.Contains(Path.GetExtension(name).ToLowerInvariant()))throw new InvalidDataException("仅支持 PNG / WebP 图集。");
                    File.WriteAllText(Path.Combine(stage,"pet.json"),text,new UTF8Encoding(false));atlases[0].ExtractToFile(Path.Combine(stage,name));
                    foreach(string notice in new[]{"LICENSE.md","LICENSE","source.json","ASSET-USAGE.md","NOTICE.md"}){var entry=zip.Entries.FirstOrDefault(e=>e.FullName==prefix+notice);if(entry!=null&&entry.Length<=65536)entry.ExtractToFile(Path.Combine(stage,notice));}
                }
            }else{
                if(Path.GetFileName(input)!="pet.json")throw new InvalidDataException("请选择宠物 ZIP 或 pet.json。");
                var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(input));string name=Convert.ToString(data["spritesheetPath"]);
                if(name!=Path.GetFileName(name)||name.IndexOfAny(new[]{'/','\\',':'})>=0||!new[]{".png",".webp"}.Contains(Path.GetExtension(name).ToLowerInvariant()))throw new InvalidDataException("图集路径无效。");
                string atlas=Path.Combine(Path.GetDirectoryName(input),name);if(new FileInfo(atlas).Length>32*1024*1024)throw new InvalidDataException("图集过大。");File.Copy(input,Path.Combine(stage,"pet.json"));File.Copy(atlas,Path.Combine(stage,name));foreach(string notice in new[]{"LICENSE.md","LICENSE","source.json","ASSET-USAGE.md","NOTICE.md"}){string file=Path.Combine(Path.GetDirectoryName(input),notice);if(File.Exists(file)&&new FileInfo(file).Length<=65536)File.Copy(file,Path.Combine(stage,notice));}
            }
            return stage;
        }catch{DeleteStage(stage,state);throw;}
    }
    public static PetPack Finish(string stage,string state,string stableId=null){
        string manifest=Path.Combine(stage,"pet.json");var json=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(manifest));string input=Path.Combine(stage,Convert.ToString(json["spritesheetPath"]));
        string png=Path.Combine(stage,"spritesheet.png");if(Path.GetExtension(input).Equals(".png",StringComparison.OrdinalIgnoreCase)&&!String.Equals(input,png,StringComparison.OrdinalIgnoreCase))File.Copy(input,png);
        using(var atlas=new PetAtlas(png)){}
        json["spritesheetPath"]="spritesheet.png";json["spriteVersionNumber"]=ImageHeight(png)==2288?2:1;
        File.WriteAllText(manifest,new JavaScriptSerializer().Serialize(json),new UTF8Encoding(false));
        if(stableId!=null&&stableId!="xiaolemi")throw new InvalidDataException("内置形象标识无效。");string id=stableId??"imported-"+Guid.NewGuid().ToString("N"),destination=Path.Combine(state,"pets",id);Directory.CreateDirectory(Path.GetDirectoryName(destination));Directory.Move(stage,destination);return PetPack.Read(destination,false);
    }
    static int ImageHeight(string path){using(var image=Image.FromFile(path))return image.Height;}
    public static void DeleteStage(string stage,string state){string prefix=Path.GetFullPath(Path.Combine(state,"pet-imports"))+Path.DirectorySeparatorChar;string full=Path.GetFullPath(stage);if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!Directory.Exists(full))return;if(Directory.GetFileSystemEntries(full).Any(p=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0))return;Directory.Delete(full,true);}
}
