using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

internal sealed class SparkVisionWord {
    public string Text;public double X,Y,Width,Height;public int Line;
    public Rectangle Bounds {get{return Rectangle.FromLTRB((int)X,(int)Y,(int)Math.Ceiling(X+Width),(int)Math.Ceiling(Y+Height));}}
}
internal sealed class SparkVisionLine {
    public string Text;public Rectangle Bounds;public SparkVisionWord[] Words;
}
internal static class SparkVisionText {
    public static string Compact(string value){return Regex.Replace(SparkSettings.NameKey(value),@"\s+","");}
    public static SparkVisionLine[] Lines(IEnumerable<SparkVisionWord> words){return words.GroupBy(w=>w.Line).Select(group=>{
        var items=group.OrderBy(w=>w.X).ToArray();var bounds=items.Select(w=>w.Bounds).Aggregate(Rectangle.Union);
        return new SparkVisionLine{Words=items,Text=String.Concat(items.Select(w=>w.Text)),Bounds=bounds};
    }).OrderBy(line=>line.Bounds.Top).ToArray();}
}
internal static class SparkWindowsOcr {
    static MethodInfo read;
    static MethodInfo Reader(){
        if(read!=null)return read;
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexHub.WindowsOcr")){
            if(stream==null)throw new NotSupportedException("本程序未包含视觉识别组件。");
            using(var memory=new MemoryStream()){stream.CopyTo(memory);read=Assembly.Load(memory.ToArray()).GetType("CodexHubWindowsOcr",true).GetMethod("Read");}
        }return read;
    }
    public static List<SparkVisionWord> Read(Bitmap image,Rectangle region,Point origin,CancellationToken stop){
        stop.ThrowIfCancellationRequested();region=Rectangle.Intersect(new Rectangle(Point.Empty,image.Size),region);if(region.IsEmpty)return new List<SparkVisionWord>();
        // Upscaling small text also makes thin CJK strokes easier to recognize.
        float factor=Math.Min(1.5F,2400F/Math.Max(region.Width,region.Height));
        using(var crop=new Bitmap(Math.Max(1,(int)(region.Width*factor)),Math.Max(1,(int)(region.Height*factor)),PixelFormat.Format32bppArgb)){
            using(var graphics=Graphics.FromImage(crop)){graphics.DrawImage(image,new Rectangle(Point.Empty,crop.Size),region,GraphicsUnit.Pixel);}
            var data=crop.LockBits(new Rectangle(Point.Empty,crop.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);byte[] pixels=new byte[crop.Width*crop.Height*4];
            try{Marshal.Copy(data.Scan0,pixels,0,pixels.Length);}finally{crop.UnlockBits(data);}
            string json;try{json=(string)Reader().Invoke(null,new object[]{pixels,crop.Width,crop.Height,stop});}catch(TargetInvocationException error){throw error.InnerException??error;}
            var words=new JavaScriptSerializer().Deserialize<List<SparkVisionWord>>(json);
            foreach(var word in words){word.X=word.X/factor+origin.X+region.Left;word.Y=word.Y/factor+origin.Y+region.Top;word.Width/=factor;word.Height/=factor;}return words;
        }
    }
}
internal static class SparkVisionImage {
    internal static byte[] Gray(Bitmap image){
        using(var copy=new Bitmap(image.Width,image.Height,PixelFormat.Format32bppArgb)){
            using(var graphics=Graphics.FromImage(copy))graphics.DrawImageUnscaled(image,0,0);
            var data=copy.LockBits(new Rectangle(Point.Empty,copy.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);byte[] bytes=new byte[copy.Width*copy.Height*4];
            try{Marshal.Copy(data.Scan0,bytes,0,bytes.Length);}finally{copy.UnlockBits(data);}
            byte[] gray=new byte[copy.Width*copy.Height];for(int i=0;i<gray.Length;i++)gray[i]=(byte)((bytes[i*4]*29+bytes[i*4+1]*150+bytes[i*4+2]*77)>>8);return gray;
        }
    }
    // Normalized correlation tolerates theme brightness and DPI differences.
    // Only the input-toolbar ROI is searched, never arbitrary desktop pixels.
    public static Rectangle Emoji(Bitmap image,Bitmap template,Rectangle region,float dpi,out double score){
        region=Rectangle.Intersect(new Rectangle(Point.Empty,image.Size),region);float reduction=Math.Max(1,dpi);
        using(var small=new Bitmap(Math.Max(1,(int)(region.Width/reduction)),Math.Max(1,(int)(region.Height/reduction)))){
            using(var graphics=Graphics.FromImage(small))graphics.DrawImage(image,new Rectangle(Point.Empty,small.Size),region,GraphicsUnit.Pixel);
            var match=MatchEmoji(small,template,new Rectangle(Point.Empty,small.Size),out score);
            return match.IsEmpty?Rectangle.Empty:Rectangle.FromLTRB(region.Left+(int)(match.Left*reduction),region.Top+(int)(match.Top*reduction),region.Left+(int)Math.Ceiling(match.Right*reduction),region.Top+(int)Math.Ceiling(match.Bottom*reduction));
        }
    }
    static Rectangle MatchEmoji(Bitmap image,Bitmap template,Rectangle region,out double score){
        var gray=Gray(image);Rectangle best=Rectangle.Empty;double bestScore=0;
        foreach(float relative in new[]{.72F,.85F,1F,1.15F,1.3F,1.5F}){
            int width=Math.Max(14,(int)(template.Width*relative)),height=Math.Max(14,(int)(template.Height*relative));if(width>region.Width||height>region.Height)continue;
            using(var scaled=new Bitmap(template,width,height)){
                var target=Gray(scaled);var points=new List<Point>();var values=new List<double>();
                for(int y=1;y<height;y+=2)for(int x=1;x<width;x+=2){points.Add(new Point(x,y));values.Add(target[y*width+x]);}
                double mean=values.Average(),variance=0;var weights=values.Select(v=>v-mean).ToArray();foreach(var weight in weights)variance+=weight*weight;if(variance<100)continue;
                Func<int,int,double> correlate=(x,y)=>{double sum=0,squares=0,product=0;for(int p=0;p<points.Count;p++){double value=gray[(y+points[p].Y)*image.Width+x+points[p].X];sum+=value;squares+=value*value;product+=value*weights[p];}double spread=squares-sum*sum/points.Count;return spread<100?0:product/Math.Sqrt(spread*variance);};
                int bx=0,by=0;double local=0;
                for(int y=region.Top;y<=region.Bottom-height;y+=3)for(int x=region.Left;x<=region.Right-width;x+=3){double value=correlate(x,y);if(value>local){local=value;bx=x;by=y;}}
                int sx=bx,sy=by;
                for(int y=Math.Max(region.Top,sy-3);y<=Math.Min(region.Bottom-height,sy+3);y++)for(int x=Math.Max(region.Left,sx-3);x<=Math.Min(region.Right-width,sx+3);x++){double value=correlate(x,y);if(value>local){local=value;bx=x;by=y;}}
                if(local>bestScore){bestScore=local;best=new Rectangle(bx,by,width,height);}
            }
        }score=bestScore;return bestScore>=.82?best:Rectangle.Empty;
    }
    public static bool Fire(Bitmap image,Rectangle rectangle){
        rectangle=Rectangle.Intersect(new Rectangle(Point.Empty,image.Size),rectangle);int orange=0;
        for(int y=rectangle.Top;y<rectangle.Bottom;y+=2)for(int x=rectangle.Left;x<rectangle.Right;x+=2){var color=image.GetPixel(x,y);if(color.R>165&&color.G>65&&color.G<235&&color.B<color.R*.62)orange++;}
        return orange>=rectangle.Width*rectangle.Height*.025;
    }
}
internal static class DouyinVision {
    static SparkNode Node(string key,string name,string kind,Rectangle bounds,string value=""){return new SparkNode{Key=key,Name=name,Kind=kind,Bounds=bounds,Value=value};}
    static Rectangle Local(Rectangle bounds,Point origin){bounds.Offset(-origin.X,-origin.Y);return bounds;}
    static SparkView Complete(SparkView view){view.Nodes=view.Contacts.Concat(new[]{view.MessageButton,view.SearchBox,view.Composer,view.Back,view.Emoji,view.Spark}).Where(n=>n!=null).ToList();return view;}
    static Rectangle SearchField(Bitmap shot,Point origin,Rectangle header,Rectangle window,float dpi){
        int left=header.Left,right=window.Right-(int)(65*dpi),start=header.Bottom+(int)(10*dpi),end=Math.Min(window.Top+(int)(180*dpi),header.Bottom+(int)(75*dpi));
        int run=0,top=0;for(int y=start;y<end;y++){
            var colors=new List<int>();for(int x=left+(int)(18*dpi);x<right-(int)(10*dpi);x+=Math.Max(1,(right-left)/6)){if(x-origin.X<0||x-origin.X>=shot.Width||y-origin.Y<0||y-origin.Y>=shot.Height)continue;var c=shot.GetPixel(x-origin.X,y-origin.Y);colors.Add((c.R+c.G+c.B)/3);}
            bool fill=colors.Count>=3&&colors.Max()-colors.Min()<8&&colors.Average()>53&&colors.Average()<180;
            if(fill){if(run==0)top=y;run++;}else{if(run>=18*dpi)return new Rectangle(left,top,right-left,run);run=0;}
        }
        if(run>=18*dpi)return new Rectangle(left,top,right-left,run);
        return new Rectangle(left,header.Bottom+(int)(25*dpi),right-left,(int)(32*dpi));
    }
    public static SparkView Read(IntPtr window,Rectangle bounds,float dpi,string query,Func<Point,string> nameAt,Func<SparkNode,string> searchAt,Func<bool> foreground,CancellationToken stop,Func<string> headerTitle=null){
        if(!foreground())throw new SparkFocusLost();dpi=Math.Max(.75F,dpi);
        var region=new Rectangle(Math.Max(bounds.Left,bounds.Right-(int)(520*dpi)),bounds.Top,Math.Min(bounds.Width,(int)(520*dpi)),bounds.Height);
        using(var shot=new Bitmap(region.Width,region.Height,PixelFormat.Format32bppArgb)){
            using(var graphics=Graphics.FromImage(shot))graphics.CopyFromScreen(region.Location,Point.Empty,region.Size,CopyPixelOperation.SourceCopy);
            if(!foreground())throw new SparkFocusLost();
            return Describe(window,bounds,dpi,query,shot,region.Location,nameAt,searchAt,stop,headerTitle);
        }
    }
    internal static SparkView Describe(IntPtr window,Rectangle bounds,float dpi,string query,Bitmap shot,Point origin,Func<Point,string> nameAt,Func<SparkNode,string> searchAt,CancellationToken stop,Func<string> headerTitle=null){
        var view=new SparkView{Window=window,Bounds=bounds,Foreground=true};
        var top=SparkVisionText.Lines(SparkWindowsOcr.Read(shot,new Rectangle(0,0,shot.Width,Math.Min(shot.Height,(int)(185*dpi))),origin,stop));
        var message=top.FirstOrDefault(line=>SparkVisionText.Compact(line.Text)=="消息"&&line.Bounds.Top<bounds.Top+58*dpi);
        if(message!=null)view.MessageButton=Node("v:messages","消息","Text",message.Bounds);
        var heading=top.FirstOrDefault(line=>Regex.IsMatch(SparkVisionText.Compact(line.Text),@"^消息[（(]\d+[)）]$")&&line.Bounds.Top>bounds.Top+38*dpi);
        Rectangle emoji=Rectangle.Empty;
        if(heading==null)using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexHub.DouyinEmoji")){
            if(stream!=null)using(var template=new Bitmap(stream)){double score;emoji=SparkVisionImage.Emoji(shot,template,new Rectangle(0,Math.Max(0,shot.Height-(int)(125*dpi)),shot.Width,Math.Min(shot.Height,(int)(125*dpi))),dpi,out score);}
        }
        if(heading!=null&&emoji.IsEmpty){
            var field=SearchField(shot,origin,heading.Bounds,bounds,dpi);view.SearchBox=Node("v:search","搜索","Edit",field);string actual=searchAt(view.SearchBox);
            if(actual==null){view.SearchBox=null;view.Ready=false;return Complete(view);}
            view.SearchBox.Value=actual;view.ListBounds=Rectangle.FromLTRB(Math.Max(bounds.Left,heading.Bounds.Left-(int)(18*dpi)),field.Bottom+(int)(5*dpi),bounds.Right-(int)(8*dpi),bounds.Bottom-(int)(8*dpi));view.Ready=true;
            if(String.IsNullOrEmpty(query)||SparkSettings.NameKey(actual)!=query)return Complete(view);
            var lines=SparkVisionText.Lines(SparkWindowsOcr.Read(shot,Local(view.ListBounds,origin),origin,stop));
            bool explicitEmpty=lines.Any(line=>Regex.IsMatch(SparkVisionText.Compact(line.Text),@"^(?:暂无.*(?:结果|联系人|用户)|没有.*(?:结果|联系人|用户)|未找到.*|无搜索结果)$"));
            foreach(var line in lines.Where(line=>line.Bounds.Left>view.ListBounds.Left+55*dpi&&line.Bounds.Left<view.ListBounds.Left+130*dpi&&line.Bounds.Height>=10*dpi).Take(35)){
                string name=nameAt(new Point(line.Words[0].Bounds.Left+Math.Max(2,line.Words[0].Bounds.Width/2),line.Words[0].Bounds.Top+line.Words[0].Bounds.Height/2));
                // Native name is a point lookup, not a scan. It disambiguates
                // OCR-confusable characters and preserves spaces and emoji.
                if(String.IsNullOrWhiteSpace(name)||name.Length>512)continue;
                // Filtered results have a separate “发消息” button. Clicking
                // the highlighted nickname alone does not open its chat.
                var open=lines.FirstOrDefault(candidate=>SparkVisionText.Compact(candidate.Text)=="发消息"&&candidate.Bounds.Left>line.Bounds.Left&&Math.Abs(candidate.Bounds.Top-line.Bounds.Top)<24*dpi);
                var target=open==null?line.Words[0].Bounds:open.Bounds;
                if(open==null)foreach(int offset in new[]{65,85,110}){
                    var point=new Point(bounds.Right-(int)(offset*dpi),line.Bounds.Top+line.Bounds.Height/2);
                    if(SparkVisionText.Compact(nameAt(point))=="发消息"){target=new Rectangle(point.X-(int)(10*dpi),point.Y-(int)(8*dpi),(int)(20*dpi),(int)(16*dpi));break;}
                }
                var contact=Node("v:person:"+view.Contacts.Count,SparkSettings.NameKey(name),"Text",target);
                if(!view.Contacts.Any(n=>Math.Abs(n.Center.Y-contact.Center.Y)<12*dpi))view.Contacts.Add(contact);
            }
            view.SearchComplete=explicitEmpty||view.Contacts.Count>0;view.Ready=view.SearchComplete;return Complete(view);
        }
        if(!emoji.IsEmpty){
            emoji.Offset(origin);view.Emoji=Node("v:emoji","表情","Point",emoji);
            int left=Math.Max(bounds.Left,bounds.Right-(int)(348*dpi));int headerY=bounds.Top+(int)(75*dpi);
            if(headerTitle!=null)view.HeaderName=headerTitle();
            if(view.HeaderName==null)
            foreach(var word in top.SelectMany(line=>line.Words).Where(word=>word.Bounds.Top>bounds.Top+55*dpi&&word.Bounds.Top<bounds.Top+105*dpi&&word.Bounds.Left>left+75*dpi&&word.Bounds.Left<bounds.Right-90*dpi).OrderBy(word=>word.X)){
                string name=nameAt(new Point(word.Bounds.Left+word.Bounds.Width/2,word.Bounds.Top+word.Bounds.Height/2));
                if(!String.IsNullOrWhiteSpace(name)&&(String.IsNullOrEmpty(query)||String.Equals(SparkSettings.NameKey(name),query,StringComparison.OrdinalIgnoreCase))){view.HeaderName=name;headerY=word.Bounds.Top+word.Bounds.Height/2;break;}
            }
            if(!String.IsNullOrEmpty(query)&&view.HeaderName==null){
                // The visible title can be ellipsized to one glyph. Point
                // lookup still exposes its complete accessible name.
                foreach(int y in new[]{80,74,86}){
                    foreach(int x in new[]{105,125,145,165}){
                        var point=new Point(left+(int)(x*dpi),bounds.Top+(int)(y*dpi));string name=nameAt(point);
                        if(String.Equals(SparkSettings.NameKey(name),query,StringComparison.OrdinalIgnoreCase)){view.HeaderName=name;headerY=point.Y;break;}
                    }if(view.HeaderName!=null)break;
                }
            }
            view.ChatBounds=Rectangle.FromLTRB(left,Math.Max(bounds.Top,headerY-(int)(24*dpi)),bounds.Right-(int)(6*dpi),bounds.Bottom-(int)(6*dpi));
            view.Composer=Node("v:composer","发送消息","Group",Rectangle.FromLTRB(left+(int)(12*dpi),emoji.Top-(int)(8*dpi),bounds.Right-(int)(12*dpi),emoji.Bottom+(int)(8*dpi)));
            view.Back=Node("v:back","返回消息列表","Point",new Rectangle(left+(int)(10*dpi),headerY-(int)(10*dpi),(int)(24*dpi),(int)(20*dpi)));
            var bottom=SparkVisionText.Lines(SparkWindowsOcr.Read(shot,new Rectangle(0,Math.Max(0,shot.Height-(int)(350*dpi)),shot.Width,Math.Min(shot.Height,(int)(350*dpi))),origin,stop));
            var label=bottom.FirstOrDefault(line=>SparkVisionText.Compact(line.Text)=="续火花"&&line.Bounds.Bottom<emoji.Top-20*dpi);
            if(label!=null){
                var neighbors=bottom.Count(line=>new[]{"比心","笑哭","在干嘛","麻了","躺平","摇骰子","猜拳"}.Contains(SparkVisionText.Compact(line.Text)));
                var tile=new Rectangle(label.Bounds.Left-(int)(9*dpi),label.Bounds.Top-(int)(62*dpi),Math.Max((int)(58*dpi),label.Bounds.Width+(int)(18*dpi)),label.Bounds.Height+(int)(70*dpi));
                if(neighbors>=2&&view.ChatBounds.Contains(tile)&&SparkVisionImage.Fire(shot,Local(new Rectangle(tile.Left,tile.Top,tile.Width,(int)(58*dpi)),origin)))view.Spark=Node("v:spark","续火花","Group",tile);
            }
            view.Ready=!String.IsNullOrWhiteSpace(view.HeaderName)&&(String.IsNullOrEmpty(query)||String.Equals(SparkSettings.NameKey(view.HeaderName),query,StringComparison.OrdinalIgnoreCase))&&(label==null||view.Spark!=null);view.Nodes=new[]{view.Emoji,view.Composer,view.Back,view.Spark}.Where(n=>n!=null).ToList();
        }else view.Ready=view.MessageButton!=null;
        return Complete(view);
    }
}
