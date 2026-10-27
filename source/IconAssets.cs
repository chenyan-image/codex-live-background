using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

// Adapted Mountains geometry from Phosphor Icons, MIT. See assets/Phosphor-LICENSE.
internal static class IconAssets {
    public static GraphicsPath Round(RectangleF rect,float radius) {
        var p=new GraphicsPath(); float d=radius*2;
        p.AddArc(rect.X,rect.Y,d,d,180,90);p.AddArc(rect.Right-d,rect.Y,d,d,270,90);
        p.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90);p.AddArc(rect.X,rect.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    public static Bitmap Create(int size) {
        Stream source=Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexHub.Brand");
        if(source==null){string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,@"source\assets\brand\codexhub.png");if(File.Exists(path))source=File.OpenRead(path);}
        if(source==null)return LegacyCreate(size);
        using(source)using(var logo=Image.FromStream(source)){
            var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb);using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.CompositingQuality=CompositingQuality.HighQuality;g.DrawImage(logo,new Rectangle(0,0,size,size));}return bitmap;
        }
    }
    static Bitmap LegacyCreate(int size) {
        var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(bitmap)) {
            g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(size/64F,size/64F);
            using(var shape=Round(new RectangleF(1,1,62,62),16))
            using(var fill=new LinearGradientBrush(new Point(0,0),new Point(64,64),Color.FromArgb(38,103,103),Color.FromArgb(10,36,45)))g.FillPath(fill,shape);
            var saved=g.Save();g.TranslateTransform(8,7);g.ScaleTransform(48F/256F,48F/256F);
            using(var moon=new SolidBrush(Color.FromArgb(244,220,165)))g.FillEllipse(moon,142,28,44,44);
            using(var rear=new SolidBrush(Color.FromArgb(90,175,160)))g.FillPolygon(rear,new[]{new PointF(129,208),new PointF(186,112),new PointF(243,208)});
            using(var front=new SolidBrush(Color.FromArgb(203,248,224)))g.FillPolygon(front,new[]{new PointF(12,208),new PointF(88,80),new PointF(164,208)});
            using(var face=new SolidBrush(Color.FromArgb(125,215,184)))g.FillPolygon(face,new[]{new PointF(88,80),new PointF(88,208),new PointF(164,208)});
            using(var snow=new SolidBrush(Color.FromArgb(236,255,245)))g.FillPolygon(snow,new[]{new PointF(88,80),new PointF(65,120),new PointF(82,113),new PointF(94,126),new PointF(111,120)});
            g.Restore(saved);
        }
        return bitmap;
    }
    [STAThread] public static void Main(string[] args) {
        using(var stream=File.Create(args[0]))using(var writer=new BinaryWriter(stream)) {
            int[] sizes={16,24,32,48,64,128,256};var images=new byte[sizes.Length][];
            for(int i=0;i<sizes.Length;i++)using(var b=Create(sizes[i]))using(var buffer=new MemoryStream()){b.Save(buffer,ImageFormat.Png);images[i]=buffer.ToArray();}
            writer.Write((short)0);writer.Write((short)1);writer.Write((short)sizes.Length);int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((short)0);writer.Write((short)1);writer.Write((short)32);writer.Write(images[i].Length);writer.Write(offset);offset+=images[i].Length;}
            foreach(var bytes in images)writer.Write(bytes);
        }
        if(args.Length>1)using(var b=Create(256))b.Save(args[1],ImageFormat.Png);
    }
}
