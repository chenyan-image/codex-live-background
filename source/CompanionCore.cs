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
    public bool FollowCodex;
    public string HubColor="#4E8FE4",CapsuleColor="#4E8FE4",RingColor="#4E8FE4";
    public int CapsuleOpacity=94;
    public string SelectedId="deepseek";
    public int Scale=100,CapsuleScale=100,X=int.MinValue,Y=int.MinValue,PositionVersion;
    public Dictionary<string,PetBehaviorProfile> Behaviors=new Dictionary<string,PetBehaviorProfile>();
    public PetBehaviorProfile ForPet(string id){if(Behaviors==null)Behaviors=new Dictionary<string,PetBehaviorProfile>();PetBehaviorProfile value;if(!Behaviors.TryGetValue(id,out value)||value==null)Behaviors[id]=value=new PetBehaviorProfile();value.Normalize();return value;}
    public static PetPreferences Load(string state){
        string file=Path.Combine(state,"pet-settings.json");if(!File.Exists(file))return new PetPreferences();
        try{if(new FileInfo(file).Length>65536)throw new InvalidDataException();string text=File.ReadAllText(file);var json=new JavaScriptSerializer();var value=json.Deserialize<PetPreferences>(text);if(value==null)throw new InvalidDataException();if(!json.Deserialize<Dictionary<string,object>>(text).ContainsKey("HubColor"))value.HubColor=value.RingColor;value.Scale=Math.Max(70,Math.Min(150,value.Scale));value.CapsuleScale=Math.Max(70,Math.Min(150,value.CapsuleScale));value.CapsuleOpacity=Math.Max(0,Math.Min(100,value.CapsuleOpacity));value.HubColor=NormalizeColor(value.HubColor);value.CapsuleColor=NormalizeColor(value.CapsuleColor);value.RingColor=NormalizeColor(value.RingColor);return value;}
        catch{File.Copy(file,file+".damaged-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),false);return new PetPreferences();}
    }
    public void Save(string state){Directory.CreateDirectory(state);string file=Path.Combine(state,"pet-settings.json"),temp=file+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));if(File.Exists(file))File.Replace(temp,file,file+".bak");else File.Move(temp,file);}
    public static string NormalizeColor(string value){return value!=null&&System.Text.RegularExpressions.Regex.IsMatch(value,"^#[0-9a-fA-F]{6}$")?value.ToUpperInvariant():"#4E8FE4";}
}
internal sealed class PetAction {
    public string id{get;set;}public string name{get;set;}public string file{get;set;}public int durationMs{get;set;}public int frames{get;set;}
    public int columns{get;set;}public int row{get;set;}public int cellWidth{get;set;}public int cellHeight{get;set;}public string[] files{get;set;}public int[] frameDurations{get;set;}public int[] frameIndices{get;set;}public bool flip{get;set;}public double fps{get;set;}
    public double moveLeadSeconds{get;set;}public double moveTailSeconds{get;set;}public double dragStartSeconds{get;set;}public double dragHoldSeconds{get;set;}public bool moveFacesLeft{get;set;}
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
        string format=Text(json,"format","");bool optional=format=="optional-bailong"||format=="optional-remielle"||format=="optional-cloud";
        string file=Path.Combine(directory,sprite);if(!optional&&(!File.Exists(file)||Path.GetExtension(file).ToLowerInvariant()!=".png"))throw new InvalidDataException("宠物尚未转换为可读的 PNG 图集。");
        string name=WallpaperLibrary.ValidateName(Text(json,"displayName",Path.GetFileName(directory)));
        var pack=new PetPack{Id=Path.GetFileName(directory),Name=name,Directory=directory,AtlasFile=file,Builtin=builtin,Author="本地导入",Source="",Optional=optional,Format=format,ProceduralMotion=json.ContainsKey("proceduralMotion")&&Object.Equals(json["proceduralMotion"],true)};
        if(pack.Id=="deepseek"&&(pack.Name=="鲸鱼娘 · DeepSeek"||pack.Name=="鲸鱼娘·DeepSeek"))pack.Name="鲸鱼娘";
        if(format=="animated-gif-set"||format=="frame-set"||format=="transparent-video-set"){
            object value;if(!json.TryGetValue("actions",out value))throw new InvalidDataException("缺少动作列表。");pack.Actions=new JavaScriptSerializer().Deserialize<List<PetAction>>(new JavaScriptSerializer().Serialize(value));
            if(pack.Actions==null||pack.Actions.Count<1||pack.Actions.Count>128)throw new InvalidDataException("动作数量无效。");
            foreach(var action in pack.Actions){
                string[] files=action.files??new[]{action.file};if(files.Length<1||files.Length>512||action.frames<1||action.frames>512||action.durationMs<1||action.durationMs>60000)throw new InvalidDataException("动作帧或时长无效。");
                foreach(string frameFile in files)if(String.IsNullOrEmpty(frameFile)||frameFile!=Path.GetFileName(frameFile)||frameFile.IndexOfAny(new[]{'/','\\',':'})>=0||Path.GetExtension(frameFile).ToLowerInvariant()!=(format=="frame-set"?".png":format=="transparent-video-set"?".webm":".gif")||!File.Exists(Path.Combine(directory,frameFile))||new FileInfo(Path.Combine(directory,frameFile)).Length>32*1024*1024)throw new InvalidDataException("动作文件无效。");
                if(format=="transparent-video-set"&&(action.cellWidth<1||action.cellWidth>1280||action.cellHeight<1||action.cellHeight>1280||action.fps<1||action.fps>60))throw new InvalidDataException("透明视频参数无效。");
                if(new[]{action.moveLeadSeconds,action.moveTailSeconds,action.dragStartSeconds,action.dragHoldSeconds}.Any(n=>Double.IsNaN(n)||Double.IsInfinity(n)||n<0||n>=action.durationMs/1000D)||action.moveLeadSeconds+action.moveTailSeconds>=action.durationMs/1000D)throw new InvalidDataException("动作交互时间无效。");
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
            foreach(string path in System.IO.Directory.GetDirectories(pair.Item1).Where(p=>System.IO.Path.GetFileName(p)!="xiaolemi").OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))try{result.Add(Read(path,pair.Item2));}catch(InvalidDataException){}catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}
        }return result.Where(pack=>pack.Id!="xiaolemi").GroupBy(pack=>pack.Id,StringComparer.OrdinalIgnoreCase).Select(group=>group.OrderByDescending(pack=>pack.Available).First()).OrderBy(pack=>pack.Id=="deepseek"?0:pack.Id=="deepseek-fat-fish"?1:pack.Id=="bailong"?2:pack.Optional?4:3).ThenBy(pack=>pack.Name,StringComparer.CurrentCulture).ToList();
    }
}
internal sealed partial class PetAtlas : IDisposable {
    public const int Columns=8,CellWidth=192,CellHeight=208;
    Bitmap bitmap,gif,currentPng;readonly PetPack animations;int currentAction=-1,lastGifFrame=-1;int[] gifDelays;
    readonly object dragImagesGate=new object();Bitmap warmedDragGif,idleGif;int[] warmedDragDelays,idleGifDelays;int warmedDragAction=-1;RectangleF warmedDragBounds;bool dragImagesPreparing,dragImagesReady,imagesDisposed;
    Dictionary<string,Bitmap> warmedDragPngs;public string DragPreparationError{get;private set;}
    bool ImageDragReady{get{lock(dragImagesGate)return animations==null||dragImagesReady;}}
    static int[] GifTiming(Bitmap image,PetAction action){int count=image.GetFrameCount(FrameDimension.Time);var delays=Enumerable.Repeat(Math.Max(10,action.durationMs/count),count).ToArray();try{byte[] values=image.GetPropertyItem(0x5100).Value;for(int i=0;i<count&&i*4+4<=values.Length;i++)delays[i]=Math.Max(10,BitConverter.ToInt32(values,i*4)*10);}catch(ArgumentException){}return delays;}
    void PrepareImageDrag(int row){
        if(animations==null)return;int action=ActionIndex(row);lock(dragImagesGate){if(imagesDisposed||dragImagesPreparing||dragImagesReady)return;dragImagesPreparing=true;}
        // The atlas cache belongs to this caller's rendering thread. Snapshot
        // filenames and bounds here; the worker owns only newly decoded images.
        string[] missingPngs=null;RectangleF? cachedDragBounds=null;
        if(animations.Format=="frame-set"){
            var spec=animations.Actions[action];var files=(spec.files??new[]{spec.file}).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
            missingPngs=files.Where(file=>!pngs.ContainsKey(file)).ToArray();Bitmap first;
            if(pngs.TryGetValue(files[0],out first)){int cell=spec.frameIndices==null?0:spec.frameIndices[0];cachedDragBounds=MeasureBody(first,new Rectangle(cell%spec.columns*spec.cellWidth,(spec.row+cell/spec.columns)*spec.cellHeight,spec.cellWidth,spec.cellHeight));}
            if(missingPngs.Length==0){lock(dragImagesGate){if(imagesDisposed)return;warmedDragAction=action;warmedDragBounds=cachedDragBounds??IdleContentBounds;dragImagesReady=true;dragImagesPreparing=false;}return;}
        }
        System.Threading.ThreadPool.QueueUserWorkItem(delegate{Bitmap warm=null;Dictionary<string,Bitmap> frames=null;int[] delays=null;RectangleF bounds=IdleContentBounds;string failure=null;
            try{var spec=animations.Actions[action];if(animations.Format=="animated-gif-set"){warm=new Bitmap(Path.Combine(animations.Directory,spec.file));if(warm.Width>512||warm.Height>512)throw new InvalidDataException("Drag GIF dimensions exceed limits.");delays=GifTiming(warm,spec);warm.SelectActiveFrame(FrameDimension.Time,0);bounds=MeasureBody(warm,new Rectangle(0,0,warm.Width,warm.Height));}
                else if(animations.Format=="frame-set"){frames=new Dictionary<string,Bitmap>(StringComparer.OrdinalIgnoreCase);long bytes=0;foreach(string file in missingPngs){using(var source=Image.FromFile(Path.Combine(animations.Directory,file))){if(source.Width>8192||source.Height>16384||(long)source.Width*source.Height>24000000)throw new InvalidDataException("Drag frame dimensions exceed limits.");long size=(long)source.Width*source.Height*4;if(bytes+size>4*1024*1024&&frames.Count>0)break;var copy=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb);using(var g=Graphics.FromImage(copy))g.DrawImageUnscaled(source,0,0);frames.Add(file,copy);bytes+=size;}}
                    if(cachedDragBounds.HasValue)bounds=cachedDragBounds.Value;else{Bitmap first=frames.Values.First();int cell=spec.frameIndices==null?0:spec.frameIndices[0];bounds=MeasureBody(first,new Rectangle(cell%spec.columns*spec.cellWidth,(spec.row+cell/spec.columns)*spec.cellHeight,spec.cellWidth,spec.cellHeight));}}
            }catch(Exception error){failure=error.Message;}
            lock(dragImagesGate){if(imagesDisposed){if(warm!=null)warm.Dispose();if(frames!=null)foreach(var image in frames.Values)image.Dispose();return;}warmedDragGif=warm;warmedDragDelays=delays;warmedDragPngs=frames;warmedDragAction=action;warmedDragBounds=bounds;DragPreparationError=failure;dragImagesReady=true;dragImagesPreparing=false;}
        });
    }
    void DisposeDragImages(){lock(dragImagesGate){imagesDisposed=true;if(warmedDragGif!=null&&warmedDragGif!=gif)warmedDragGif.Dispose();if(idleGif!=null&&idleGif!=gif&&idleGif!=warmedDragGif)idleGif.Dispose();warmedDragGif=idleGif=null;if(warmedDragPngs!=null){foreach(var image in warmedDragPngs.Values)image.Dispose();warmedDragPngs=null;}}}
    readonly Dictionary<string,Bitmap> pngs=new Dictionary<string,Bitmap>(StringComparer.OrdinalIgnoreCase);readonly LinkedList<string> pngOrder=new LinkedList<string>();
    readonly Dictionary<int,RectangleF> contentBounds=new Dictionary<int,RectangleF>();
    public RectangleF IdleContentBounds{get;private set;}
    public Bitmap Bitmap {get{return videoFrame??currentPng??gif??bitmap;}}public readonly int Rows;
    public readonly int[] FrameCounts;
    public PetAtlas(PetPack pack):this(pack.AtlasFile,pack){}
    public PetAtlas(PetPack pack,bool preview):this(pack.AtlasFile,pack,preview){}
    public PetAtlas(string path):this(path,null){}
    PetAtlas(string path,PetPack pack,bool preview=false){
        previewOnly=preview;
        if(pack!=null&&(pack.Format=="transparent-video-set"||preview&&pack.Actions!=null)){animations=pack;Rows=9+pack.Actions.Count;FrameCounts=new int[Rows];for(int i=0;i<Rows;i++)FrameCounts[i]=pack.Actions[i<9?pack.RowMap[i]:i-9].frames;using(var source=Image.FromFile(path))bitmap=new Bitmap(source);IdleContentBounds=MeasureBody(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));return;}
        if(pack!=null&&pack.Actions!=null){animations=pack;Rows=9+pack.Actions.Count;FrameCounts=new int[Rows];for(int i=0;i<Rows;i++)FrameCounts[i]=Math.Max(1,pack.Actions[i<9?pack.RowMap[i]:i-9].frames);if(pack.Format=="animated-gif-set"){using(var poster=new Bitmap(path))IdleContentBounds=MeasureBody(poster,new Rectangle(0,0,poster.Width,poster.Height));}Frame(0,0);if(pack.Format!="animated-gif-set")IdleContentBounds=ContentBounds(0);return;}
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
        IdleContentBounds=ContentBounds(0);
    }
    public RectangleF ContentBounds(int row){if(IsVideo||previewOnly&&animations!=null)return IdleContentBounds;int key=animations==null?row:ActionIndex(row);RectangleF value;if(contentBounds.TryGetValue(key,out value))return value;lock(dragImagesGate){if(animations!=null&&key==warmedDragAction&&dragImagesReady){contentBounds[key]=warmedDragBounds;return warmedDragBounds;}if(animations!=null&&row==dragRow&&dragImagesPreparing)return IdleContentBounds;}Rectangle frame=Frame(row,0);value=MeasureBody(Bitmap,frame);contentBounds[key]=value;return value;}
    // Largest opaque component excludes detached sparkles and caption/VFX pixels.
    internal static RectangleF MeasureBody(Bitmap source,Rectangle frame){using(var crop=new Bitmap(frame.Width,frame.Height,PixelFormat.Format32bppArgb)){using(var g=Graphics.FromImage(crop))g.DrawImage(source,new Rectangle(0,0,crop.Width,crop.Height),frame,GraphicsUnit.Pixel);var data=crop.LockBits(new Rectangle(0,0,crop.Width,crop.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);try{int width=crop.Width,height=crop.Height;var bytes=new byte[data.Stride*height];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);var seen=new bool[width*height];var queue=new int[seen.Length];int best=0,left=0,top=0,right=width,bottom=height;for(int at=0;at<seen.Length;at++){if(seen[at]||bytes[(at/width)*data.Stride+(at%width)*4+3]<96)continue;int count=1,read=0,minX=at%width,maxX=minX,minY=at/width,maxY=minY;seen[at]=true;queue[0]=at;while(read<count){int point=queue[read++],x=point%width,y=point/width;minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int xx=x+dx,yy=y+dy;if(xx<0||xx>=width||yy<0||yy>=height)continue;int next=yy*width+xx;if(seen[next]||bytes[yy*data.Stride+xx*4+3]<96)continue;seen[next]=true;queue[count++]=next;}}if(count>best){best=count;left=minX;right=maxX+1;top=minY;bottom=maxY+1;}}return new RectangleF(left/(float)width,top/(float)height,(right-left)/(float)width,(bottom-top)/(float)height);}finally{crop.UnlockBits(data);}}}
    public int ActionIndex(int row){return row<9?animations.RowMap[Math.Max(0,row)]:Math.Min(animations.Actions.Count-1,row-9);}
    Bitmap Png(string file){
        Bitmap value;if(pngs.TryGetValue(file,out value)){lock(dragImagesGate){Bitmap duplicate;if(warmedDragPngs!=null&&warmedDragPngs.TryGetValue(file,out duplicate)){warmedDragPngs.Remove(file);if(!Object.ReferenceEquals(value,duplicate))duplicate.Dispose();}}pngOrder.Remove(file);pngOrder.AddLast(file);return value;}
        lock(dragImagesGate){if(warmedDragPngs!=null&&warmedDragPngs.TryGetValue(file,out value))warmedDragPngs.Remove(file);}
        if(value==null)using(var source=Image.FromFile(Path.Combine(animations.Directory,file))){if(source.Width>8192||source.Height>16384||(long)source.Width*source.Height>24000000)throw new InvalidDataException("动作图集尺寸超限。");value=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb);using(var g=Graphics.FromImage(value))g.DrawImageUnscaled(source,0,0);}
        // Bound decoded pixels, not compressed file sizes. Keep one oversized
        // atlas if required, but never preload all expression images.
        long bytes=(long)value.Width*value.Height*4;
        while(pngOrder.Count>0&&(pngOrder.Count>=(animations.ProceduralMotion?8:3)||pngs.Values.Sum(b=>(long)b.Width*b.Height*4)+bytes>4*1024*1024)){string oldest=pngOrder.First.Value;pngOrder.RemoveFirst();pngs[oldest].Dispose();pngs.Remove(oldest);}pngs.Add(file,value);pngOrder.AddLast(file);return value;
    }
    public Rectangle Frame(int row,int frame){
        row=Math.Max(0,Math.Min(Rows-1,row));frame=Math.Max(0,frame);
        if(IsVideo)return new Rectangle(0,0,Bitmap.Width,Bitmap.Height);
        if(animations!=null){int action=ActionIndex(row);var spec=animations.Actions[action];
            if(animations.Format=="frame-set"){
                int index=frame%spec.frames;string file=spec.files==null?spec.file:spec.files[index];currentPng=Png(file);int cell=spec.frameIndices!=null?spec.frameIndices[index]:spec.files==null?index:0;var box=new Rectangle(cell%spec.columns*spec.cellWidth,(spec.row+cell/spec.columns)*spec.cellHeight,spec.cellWidth,spec.cellHeight);
                if(box.Right>currentPng.Width||box.Bottom>currentPng.Height)throw new InvalidDataException("动作帧超出图集。");return box;
            }
            if(action!=currentAction){Bitmap next=null;int[] timing=null;lock(dragImagesGate){if(action==warmedDragAction&&warmedDragGif!=null){next=warmedDragGif;timing=warmedDragDelays;}else if(row==dragRow&&dragImagesPreparing&&gif!=null)return new Rectangle(0,0,gif.Width,gif.Height);else if(action==ActionIndex(0)&&idleGif!=null){next=idleGif;timing=idleGifDelays;}}
                if(next==null){next=new Bitmap(Path.Combine(animations.Directory,spec.file));if(next.Width>512||next.Height>512){next.Dispose();throw new InvalidDataException("动作尺寸超限。");}timing=GifTiming(next,spec);}
                if(gif!=null&&gif!=warmedDragGif&&gif!=idleGif)gif.Dispose();gif=next;gifDelays=timing;currentAction=action;lastGifFrame=-1;if(action==ActionIndex(0)&&idleGif==null){idleGif=gif;idleGifDelays=gifDelays;}}
            int selected=frame%gifDelays.Length;if(selected!=lastGifFrame){gif.SelectActiveFrame(FrameDimension.Time,selected);lastGifFrame=selected;}return new Rectangle(0,0,gif.Width,gif.Height);
        }
        return new Rectangle((frame%Math.Max(1,FrameCounts[row]))*CellWidth,row*CellHeight,CellWidth,CellHeight);
    }
    public static int SampleFrame(double seconds,int frames,int durationMs,int[] delays){
        if(delays==null)return (int)(Math.Max(0,seconds)*1000/Math.Max(1,durationMs/(double)Math.Max(1,frames)))%Math.Max(1,frames);
        double time=Math.Max(0,seconds)*1000%delays.Sum();for(int i=0;i<delays.Length;i++){if(time<delays[i])return i;time-=delays[i];}return delays.Length-1;
    }
    public bool HoldLast,MirrorMotion;
    public void Draw(Graphics graphics,RectangleF destination,int row,double time){
        if(previewOnly&&animations==null){row=0;time=0;}
        if(HoldLast||DragHeld&&row==dragRow&&!IsVideo)time=Math.Min(time,animations==null?.699:Math.Max(0,animations.Actions[ActionIndex(row)].durationMs/1000D-.001));
        if(previewOnly&&animations!=null){if(IsVideo)graphics.DrawImage(bitmap,destination,new RectangleF(IdleContentBounds.X*bitmap.Width,IdleContentBounds.Y*bitmap.Height,IdleContentBounds.Width*bitmap.Width,IdleContentBounds.Height*bitmap.Height),GraphicsUnit.Pixel);else graphics.DrawImage(bitmap,destination);return;}
        if(IsVideo){var state=graphics.Save();if(MirrorMotion){graphics.TranslateTransform(destination.Left+destination.Right,0);graphics.ScaleTransform(-1,1);}DrawVideo(graphics,destination,row,time);graphics.Restore(state);return;}
        int frame=(int)(Math.Max(0,time)*10);if(animations!=null){int index=ActionIndex(row);var action=animations.Actions[index];if(animations.Format!="frame-set"&&index!=currentAction)Frame(row,0);frame=SampleFrame(time,action.frames,action.durationMs,animations.Format=="frame-set"?action.frameDurations:gifDelays);}
        var source=Frame(row,frame);if(animations!=null&&animations.ProceduralMotion){float sway=(float)Math.Sin(time*2.2)*1.2F;destination.X+=sway;destination.Y+=(float)Math.Sin(time*1.6)*.8F;}
        var saved=graphics.Save();if(animations!=null&&animations.Actions[ActionIndex(row)].flip){graphics.TranslateTransform(destination.Left+destination.Right,0);graphics.ScaleTransform(-1,1);}graphics.DrawImage(Bitmap,destination,source,GraphicsUnit.Pixel);graphics.Restore(saved);
    }
    public void Dispose(){DisposeDragImages();DisposeVideo();if(bitmap!=null)bitmap.Dispose();if(gif!=null)gif.Dispose();foreach(var value in pngs.Values)value.Dispose();pngs.Clear();pngOrder.Clear();currentPng=null;}
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
    double decision=7,walkDistance;bool walking,recoiling;public int CollisionCount;
    public bool Walking{get{return walking&&!Sliding&&!Dragging;}}
    public float TravelScale=1;public bool AnimationControlled;public double AnimationDelta,AnimationDuration=6;public bool AnimationEnded;
    public PetMotion(int seed){random=new Random(seed);}
    public static PointF Clamp(PointF p,Size size,Rectangle bounds){return new PointF(Math.Max(bounds.Left,Math.Min(bounds.Right-size.Width,p.X)),Math.Max(bounds.Top,Math.Min(bounds.Bottom-size.Height,p.Y)));}
    public void Place(PointF p,Size size,Rectangle bounds){Position=Clamp(p,size,bounds);}
    public void Interact(int row,double duration=1.3){Sliding=recoiling=walking=false;ClickResponse=false;Row=Math.Max(0,Math.Min(255,row));ReactionDuration=ReactionRemaining=Math.Max(.3,Math.Min(60,duration));CollisionDirection=-1;decision=12+random.NextDouble()*12;Velocity=PointF.Empty;}
    public void ReactToClick(){int pose=Row;Interact(pose,.65);ClickResponse=true;}
    public void StepPress(double delta){float target=Pressed?.94F:1F;PressScale+=(target-PressScale)*(float)(1-Math.Exp(-Math.Max(0,Math.Min(.1,delta))*(Pressed?28:18)));if(Math.Abs(target-PressScale)<.0001F)PressScale=target;}
    public void Drag(PointF p,Size size,Rectangle bounds){Dragging=true;Sliding=recoiling=walking=false;ClickResponse=false;Velocity=PointF.Empty;Position=Clamp(p,size,bounds);Row=0;CollisionDirection=-1;ReactionRemaining=0;}
    public void Drop(){Dragging=false;Interact(3);}
    void StartWalk(){double angle=random.NextDouble()*Math.PI*2;decision=2.8+random.NextDouble()*1.4;walkDistance=(200+random.NextDouble()*160)*Math.Max(.5,TravelScale);double speed=walkDistance/decision;walking=true;Velocity=new PointF((float)(Math.Cos(angle)*speed),(float)(Math.Sin(angle)*speed*.55));Row=Velocity.X>=0?1:2;}
    void EndWalk(){walking=false;Velocity=PointF.Empty;Row=0;decision=12+random.NextDouble()*12;}
    public void ResumeAutonomy(bool immediate=false){Dragging=Sliding=ClickResponse=recoiling=false;walking=false;Velocity=PointF.Empty;ReactionRemaining=0;CollisionDirection=-1;Row=0;decision=6+random.NextDouble()*6;if(immediate)StartWalk();}
    public void Wander(){if(Dragging||Sliding||ReactionRemaining>0||decision>0)return;walking=false;decision=0;}
    public void Update(double delta,Size size,Rectangle bounds,bool roaming,bool working){Step(delta,size,bounds,roaming);if(working&&!Dragging&&!Sliding&&Velocity==PointF.Empty&&ReactionRemaining<=0)Row=7;}
    public void Drop(PointF velocity){double speed=Math.Sqrt(velocity.X*velocity.X+velocity.Y*velocity.Y);if(speed<75){Drop();return;}Dragging=false;Sliding=true;ClickResponse=Pressed=false;CollisionDirection=-1;ReactionRemaining=0;double factor=Math.Min(1,1800/speed);Velocity=new PointF((float)(velocity.X*factor),(float)(velocity.Y*factor));Row=Velocity.X>=0?1:2;decision=1.5;}
    void Slide(double delta,Size size,Rectangle bounds){
        const double drag=3.8;double decay=Math.Exp(-drag*delta),travel=(1-decay)/drag;var next=new PointF(Position.X+(float)(Velocity.X*travel),Position.Y+(float)(Velocity.Y*travel));var velocity=new PointF((float)(Velocity.X*decay),(float)(Velocity.Y*decay));int edge=-1;
        if(next.X<bounds.Left||next.X+size.Width>bounds.Right){edge=next.X<bounds.Left?0:1;velocity.X=-velocity.X*.48F;velocity.Y*=.88F;}
        if(next.Y<bounds.Top||next.Y+size.Height>bounds.Bottom){if(edge<0)edge=next.Y<bounds.Top?2:3;velocity.Y=-velocity.Y*.48F;velocity.X*=.88F;}
        Position=Clamp(next,size,bounds);Velocity=velocity;
        if(edge>=0){Row=5;CollisionDirection=edge;ReactionDuration=ReactionRemaining=.85;CollisionCount++;}
        else if(ReactionRemaining>0){ReactionRemaining=Math.Max(0,ReactionRemaining-delta);if(ReactionRemaining==0)CollisionDirection=-1;}
        else Row=Velocity.X>=0?1:2;
        if(Velocity.X*Velocity.X+Velocity.Y*Velocity.Y<25*25){Sliding=false;Velocity=PointF.Empty;decision=12+random.NextDouble()*12;if(ReactionRemaining<=0)Row=0;}
    }
    public void Step(double delta,Size size,Rectangle bounds,bool roaming){
        delta=Math.Max(0,Math.Min(.1,delta));Position=Clamp(Position,size,bounds);if(Dragging)return;
        if(Sliding){Slide(delta,size,bounds);return;}
        if(ReactionRemaining>0){if(recoiling){double decay=Math.Exp(-8*delta),travel=(1-decay)/8;Position=Clamp(new PointF(Position.X+Velocity.X*(float)travel,Position.Y+Velocity.Y*(float)travel),size,bounds);Velocity=new PointF(Velocity.X*(float)decay,Velocity.Y*(float)decay);}ReactionRemaining=Math.Max(0,ReactionRemaining-delta);if(ReactionRemaining>0)return;if(recoiling){recoiling=false;Velocity=PointF.Empty;}CollisionDirection=-1;ClickResponse=false;}
        if(!AnimationControlled||!walking)decision-=delta;
        if(!roaming){walking=false;Velocity=PointF.Empty;Row=0;return;}
        if(AnimationControlled&&walking&&AnimationEnded){EndWalk();return;}
        if(decision<=0){
            if(walking)EndWalk();else StartWalk();
        }
        if(Velocity==PointF.Empty)return;
        if(AnimationControlled){double length=Math.Sqrt(Velocity.X*Velocity.X+Velocity.Y*Velocity.Y);if(length>0){float factor=(float)(walkDistance/Math.Max(.1,AnimationDuration)/length);Velocity=new PointF(Velocity.X*factor,Velocity.Y*factor);}delta=Math.Max(0,Math.Min(.1,AnimationDelta));}
        var next=new PointF(Position.X+Velocity.X*(float)delta,Position.Y+Velocity.Y*(float)delta);
        bool hitX=next.X<bounds.Left||next.X+size.Width>bounds.Right,hitY=next.Y<bounds.Top||next.Y+size.Height>bounds.Bottom;
        int edge=hitX?(next.X<bounds.Left?0:1):hitY?(next.Y<bounds.Top?2:3):-1;
        Position=Clamp(next,size,bounds);
        if(edge>=0){EndWalk();recoiling=true;float kick=120*Math.Max(.5F,TravelScale);Velocity=new PointF(hitX?(edge==0?kick:-kick):0,hitY?(next.Y<bounds.Top?kick:-kick):0);Row=5;CollisionDirection=edge;ReactionDuration=ReactionRemaining=.85;CollisionCount++;}
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
