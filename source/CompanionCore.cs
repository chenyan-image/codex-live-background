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
    public static PetPreferences Load(string state){
        string file=Path.Combine(state,"pet-settings.json");if(!File.Exists(file))return new PetPreferences();
        try{if(new FileInfo(file).Length>65536)throw new InvalidDataException();var value=new JavaScriptSerializer().Deserialize<PetPreferences>(File.ReadAllText(file));if(value==null)throw new InvalidDataException();value.Scale=Math.Max(70,Math.Min(150,value.Scale));value.CapsuleScale=Math.Max(70,Math.Min(150,value.CapsuleScale));return value;}
        catch{File.Copy(file,file+".damaged-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),false);return new PetPreferences();}
    }
    public void Save(string state){Directory.CreateDirectory(state);string file=Path.Combine(state,"pet-settings.json"),temp=file+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));if(File.Exists(file))File.Replace(temp,file,file+".bak");else File.Move(temp,file);}
}
internal sealed class PetAction {public string id{get;set;}public string name{get;set;}public string file{get;set;}public int durationMs{get;set;}public int frames{get;set;}}
internal sealed class PetPack {
    public string Id,Name,Directory,AtlasFile,Author,Source;
    public bool Builtin,Optional;public List<PetAction> Actions;public int[] RowMap;
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
        var pack=new PetPack{Id=Path.GetFileName(directory),Name=name,Directory=directory,AtlasFile=file,Builtin=builtin,Author="本地导入",Source="",Optional=optional};
        if(format=="animated-gif-set"){
            object value;if(!json.TryGetValue("actions",out value))throw new InvalidDataException("缺少动作列表。");pack.Actions=new JavaScriptSerializer().Deserialize<List<PetAction>>(new JavaScriptSerializer().Serialize(value));
            if(pack.Actions==null||pack.Actions.Count<1||pack.Actions.Count>40)throw new InvalidDataException("动作数量无效。");
            foreach(var action in pack.Actions)if(action.file!=Path.GetFileName(action.file)||Path.GetExtension(action.file).ToLowerInvariant()!=".gif"||!File.Exists(Path.Combine(directory,action.file))||new FileInfo(Path.Combine(directory,action.file)).Length>32*1024*1024)throw new InvalidDataException("动作文件无效。");
            pack.RowMap=new JavaScriptSerializer().Deserialize<int[]>(new JavaScriptSerializer().Serialize(json["rowMap"]));if(pack.RowMap.Length!=9||pack.RowMap.Any(i=>i<0||i>=pack.Actions.Count))throw new InvalidDataException("动作映射无效。");
        }
        string source=Path.Combine(directory,"source.json");if(File.Exists(source)&&new FileInfo(source).Length<65536){var origin=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(source));pack.Author=Text(origin,"author",pack.Author);pack.Source=Text(origin,"source","");}
        return pack;
    }
    public static List<PetPack> Discover(string root,string state){
        var result=new List<PetPack>();foreach(var pair in new[]{Tuple.Create(Path.Combine(root,@"source\assets\pets"),true),Tuple.Create(Path.Combine(state,"pets"),false)}){
            if(!System.IO.Directory.Exists(pair.Item1))continue;
            foreach(string path in System.IO.Directory.GetDirectories(pair.Item1).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))try{result.Add(Read(path,pair.Item2));}catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}
        }return result.GroupBy(pack=>pack.Id,StringComparer.OrdinalIgnoreCase).Select(group=>group.OrderByDescending(pack=>pack.Available).First()).OrderBy(pack=>pack.Optional?2:pack.Id=="deepseek"?0:1).ThenBy(pack=>pack.Name,StringComparer.CurrentCulture).ToList();
    }
}
internal sealed class PetAtlas : IDisposable {
    public const int Columns=8,CellWidth=192,CellHeight=208;
    Bitmap bitmap,gif;readonly PetPack animations;int currentAction=-1;public Bitmap Bitmap {get{return gif??bitmap;}}public readonly int Rows;
    public readonly int[] FrameCounts;
    public PetAtlas(PetPack pack):this(pack.AtlasFile,pack){}
    public PetAtlas(string path):this(path,null){}
    PetAtlas(string path,PetPack pack){
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
    public Rectangle Frame(int row,int frame){row=Math.Max(0,Math.Min(Rows-1,row));if(animations!=null){int action=row<9?animations.RowMap[row]:row-9;if(action!=currentAction){if(gif!=null)gif.Dispose();gif=new Bitmap(Path.Combine(animations.Directory,animations.Actions[action].file));if(gif.Width>512||gif.Height>512){gif.Dispose();gif=null;throw new InvalidDataException("白龙动作尺寸超限。");}currentAction=action;}int count=gif.GetFrameCount(FrameDimension.Time);gif.SelectActiveFrame(FrameDimension.Time,Math.Max(0,frame)%count);return new Rectangle(0,0,gif.Width,gif.Height);}return new Rectangle((frame%Math.Max(1,FrameCounts[row]))*CellWidth,row*CellHeight,CellWidth,CellHeight);}
    public void Draw(Graphics graphics,RectangleF destination,int row,double time){var source=Frame(row,(int)(time*(animations==null?10:12)));graphics.DrawImage(Bitmap,destination,source,GraphicsUnit.Pixel);}
    public void Dispose(){if(bitmap!=null)bitmap.Dispose();if(gif!=null)gif.Dispose();}
}
internal sealed class PetMotion {
    readonly Random random;
    public PointF Position;public PointF Velocity;
    public bool Dragging;public int Row,CollisionDirection=-1;public double ReactionRemaining,ReactionDuration=1.3;
    double decision=1;public int CollisionCount;
    public PetMotion(int seed){random=new Random(seed);}
    public static PointF Clamp(PointF p,Size size,Rectangle bounds){return new PointF(Math.Max(bounds.Left,Math.Min(bounds.Right-size.Width,p.X)),Math.Max(bounds.Top,Math.Min(bounds.Bottom-size.Height,p.Y)));}
    public void Place(PointF p,Size size,Rectangle bounds){Position=Clamp(p,size,bounds);}
    public void Interact(int row,double duration=1.3){Row=Math.Max(0,Math.Min(127,row));ReactionDuration=ReactionRemaining=Math.Max(.3,Math.Min(12,duration));CollisionDirection=-1;decision=1.5;Velocity=PointF.Empty;}
    public void Drag(PointF p,Size size,Rectangle bounds){Dragging=true;Velocity=PointF.Empty;Position=Clamp(p,size,bounds);Row=0;ReactionRemaining=0;}
    public void Drop(){Dragging=false;Interact(3);}
    public void Step(double delta,Size size,Rectangle bounds,bool roaming){
        delta=Math.Max(0,Math.Min(.1,delta));Position=Clamp(Position,size,bounds);if(Dragging)return;
        if(ReactionRemaining>0){ReactionRemaining=Math.Max(0,ReactionRemaining-delta);if(ReactionRemaining>0)return;CollisionDirection=-1;}
        decision-=delta;
        if(!roaming){Velocity=PointF.Empty;Row=0;return;}
        if(decision<=0){
            if(random.Next(4)==0){Velocity=PointF.Empty;Row=random.Next(3)==0?6:0;decision=1.5+random.NextDouble()*2;}
            else{double angle=random.NextDouble()*Math.PI*2,speed=32+random.NextDouble()*25;Velocity=new PointF((float)(Math.Cos(angle)*speed),(float)(Math.Sin(angle)*speed*.6));decision=3+random.NextDouble()*5;}
        }
        if(Velocity==PointF.Empty)return;
        var next=new PointF(Position.X+Velocity.X*(float)delta,Position.Y+Velocity.Y*(float)delta);
        int edge=next.X<bounds.Left?0:next.X+size.Width>bounds.Right?1:next.Y<bounds.Top?2:next.Y+size.Height>bounds.Bottom?3:-1;
        Position=Clamp(next,size,bounds);
        if(edge>=0){Velocity=edge<2?new PointF(-Velocity.X,Velocity.Y):new PointF(Velocity.X,-Velocity.Y);Row=5;CollisionDirection=edge;ReactionRemaining=.55;CollisionCount++;decision=Math.Max(1.2,decision);}
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
