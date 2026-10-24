using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Original procedural illustrations. No third-party wallpaper media is used.
internal static class BuiltinWallpapers {
    static Color C(int r,int g,int b){return Color.FromArgb(r,g,b);}
    static void Sky(Graphics g,Color top,Color bottom){using(var b=new LinearGradientBrush(new Rectangle(0,0,2560,1440),top,bottom,90F))g.FillRectangle(b,0,0,2560,1440);}
    static void Glow(Graphics g,float x,float y,float radius,Color color){using(var p=new GraphicsPath()){p.AddEllipse(x-radius,y-radius,radius*2,radius*2);using(var b=new PathGradientBrush(p)){b.CenterColor=color;b.SurroundColors=new[]{Color.FromArgb(0,color)};g.FillPath(b,p);}}}
    static void Stars(Graphics g,int seed,int count){var random=new Random(seed);for(int i=0;i<count;i++){float x=random.Next(2560),y=random.Next(1000),s=(float)(random.NextDouble()*2.5+.7);using(var b=new SolidBrush(Color.FromArgb(random.Next(35,160),204,222,235)))g.FillEllipse(b,x,y,s,s);}}
    static void Ridge(Graphics g,int seed,float baseline,float amplitude,Color color){var random=new Random(seed);var points=new PointF[24];points[0]=new PointF(-120,1600);for(int i=1;i<23;i++)points[i]=new PointF((i-1)*130-80,baseline+(float)random.NextDouble()*amplitude);points[23]=new PointF(2700,1600);using(var b=new SolidBrush(color))g.FillPolygon(b,points);}
    public static Bitmap Render(int scene){
        var bitmap=new Bitmap(2560,1440,PixelFormat.Format24bppRgb);
        using(var g=Graphics.FromImage(bitmap)){
            g.SmoothingMode=SmoothingMode.AntiAlias;g.CompositingQuality=CompositingQuality.HighQuality;
            if(scene==0){
                Sky(g,C(8,14,30),C(39,66,83));Glow(g,1750,540,850,Color.FromArgb(65,70,133,146));Stars(g,4,190);
                using(var b=new SolidBrush(C(213,226,215)))g.FillEllipse(b,1810,250,105,105);
                Ridge(g,7,600,230,C(34,56,72));Ridge(g,15,780,220,C(24,43,56));Ridge(g,3,960,210,C(15,29,40));Ridge(g,21,1190,200,C(8,19,27));
            }else if(scene==1){
                Sky(g,C(7,19,31),C(38,78,89));Glow(g,1600,650,690,Color.FromArgb(56,114,176,170));Stars(g,11,90);
                using(var b=new SolidBrush(C(212,231,213)))g.FillEllipse(b,1630,340,125,125);
                using(var b=new SolidBrush(C(12,35,48)))g.FillRectangle(b,0,850,2560,590);
                var random=new Random(12);for(int i=0;i<160;i++){float y=870+i*3.5F,w=30+i*2.7F;using(var p=new Pen(Color.FromArgb((int)(95*(1-i/180F)),157,209,192),1.5F))g.DrawLine(p,1692-w/2+(float)random.NextDouble()*45,y,1692+w/2,y);}
                using(var p=new Pen(Color.FromArgb(34,127,171,165),2)){for(int i=0;i<18;i++)g.DrawBezier(p,-200,925+i*36,500,820+i*41,1600,1020+i*24,2800,910+i*36);}
                Ridge(g,16,1260,120,C(5,20,27));
            }else if(scene==2){
                Sky(g,C(7,20,22),C(54,82,75));Glow(g,1500,700,1000,Color.FromArgb(70,137,168,132));
                var random=new Random(8);for(int layer=0;layer<4;layer++){Color color=new[]{C(41,68,62),C(29,56,51),C(18,42,39),C(7,27,29)}[layer];for(int i=0;i<16;i++){float x=i*180+random.Next(-60,90),y=230+random.Next(350)+layer*120,h=900;using(var b=new SolidBrush(color)){g.FillRectangle(b,x-6,y,12,h);for(int branch=0;branch<9;branch++){float v=y+branch*75,w=40+branch*15;g.FillPolygon(b,new[]{new PointF(x,v),new PointF(x-w,v+170),new PointF(x+w,v+170)});}}}}
                using(var b=new LinearGradientBrush(new Rectangle(0,780,2560,660),Color.FromArgb(0,56,88,79),Color.FromArgb(150,7,23,26),90F))g.FillRectangle(b,0,780,2560,660);
            }else if(scene==3){
                Sky(g,C(5,13,29),C(10,37,66));Glow(g,1900,350,1100,Color.FromArgb(65,25,107,142));
                for(int i=0;i<14;i++){float y=510+i*65;using(var p=new GraphicsPath()){p.AddBezier(-150,y,700,y-550,1450,y+560,2700,y-220);p.AddLine(2700,y-220,2700,1600);p.AddLine(2700,1600,-150,1600);p.CloseFigure();using(var b=new SolidBrush(Color.FromArgb(80,8+i*2,35+i*2,57+i*3)))g.FillPath(b,p);}using(var pen=new Pen(Color.FromArgb(18+i*2,78,156,187),1.5F))g.DrawBezier(pen,-150,y,700,y-550,1450,y+560,2700,y-220);}
            }else if(scene==4){
                Sky(g,C(26,19,25),C(111,76,59));Glow(g,1700,630,850,Color.FromArgb(74,190,135,81));
                using(var b=new SolidBrush(C(219,165,107)))g.FillEllipse(b,1770,430,120,120);
                for(int i=0;i<5;i++){float y=790+i*160;using(var p=new GraphicsPath()){p.AddBezier(-150,y+300,600,y-500,1450,y+230,2700,y-180);p.AddLine(2700,y-180,2700,1600);p.AddLine(2700,1600,-150,1600);p.CloseFigure();using(var b=new LinearGradientBrush(new Rectangle(0,(int)y-200,2560,1000),new[]{C(110,77,64),C(85,58,53),C(64,44,44),C(41,31,36),C(21,22,29)}[i],C(20,21,28),80F))g.FillPath(b,p);}}
            }else{
                Sky(g,C(7,11,22),C(15,27,40));Glow(g,1170,470,1150,Color.FromArgb(40,78,112,137));Stars(g,37,700);
                using(var pen=new Pen(Color.FromArgb(26,108,151,160),1)){for(int i=0;i<8;i++)g.DrawEllipse(pen,330+i*75,-620+i*75,1900-i*150,1900-i*150);g.DrawLine(pen,1280,0,1280,1440);g.DrawLine(pen,0,690,2560,690);}
                var points=new[]{new PointF(640,570),new PointF(900,420),new PointF(1130,660),new PointF(1520,470),new PointF(1800,730)};
                using(var pen=new Pen(Color.FromArgb(75,146,205,191),1.5F))g.DrawLines(pen,points);foreach(var p in points){Glow(g,p.X,p.Y,33,Color.FromArgb(80,147,227,203));using(var b=new SolidBrush(C(201,235,223)))g.FillEllipse(b,p.X-3,p.Y-3,6,6);}
            }
        }
        return bitmap;
    }
    public static void Generate(string directory){
        Directory.CreateDirectory(directory);string[] names={"midnight-ridge","quiet-moon","forest-mist","blue-tide","amber-dunes","star-atlas"};
        for(int i=0;i<names.Length;i++)using(var image=Render(i)){var encoder=Array.Find(ImageCodecInfo.GetImageEncoders(),x=>x.MimeType=="image/jpeg");using(var parameters=new EncoderParameters(1)){parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,94L);image.Save(Path.Combine(directory,names[i]+".jpg"),encoder,parameters);}}
        File.WriteAllText(Path.Combine(directory,"README.txt"),"Six original procedural illustrations generated by BuiltinWallpapers.cs. Distributed under the project MIT license. No user or Wallpaper Engine media is bundled.",System.Text.Encoding.UTF8);
    }
    [STAThread]public static void Main(string[] args){Generate(args[0]);}
}
