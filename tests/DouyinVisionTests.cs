using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

internal static class DouyinVisionTests {
    static int checks;static void Check(bool condition,string label){checks++;if(!condition)throw new Exception(label);}
    static int Main(string[] args){try{
        string fixtures=args[0],output=args[1];Directory.CreateDirectory(output);var clock=Stopwatch.StartNew();
        using(var template=new Bitmap(args[2]))using(var chat=new Bitmap(Path.Combine(fixtures,"codex-clipboard-3b554c34-3280-4644-9b3d-ac7cefb9a0d6.png"))){
            double score;var roi=new Rectangle(0,chat.Height-125,chat.Width,125);var match=SparkVisionImage.Emoji(chat,template,roi,1,out score);
            Console.WriteLine("Emoji correlation: "+score.ToString("F3")+" "+match);Check(!match.IsEmpty&&match.Right>chat.Width-110,"composer emoji located in supplied screenshot");
            using(var scaled=new Bitmap(chat,chat.Width*2,chat.Height*2)){match=SparkVisionImage.Emoji(scaled,template,new Rectangle(0,scaled.Height-250,scaled.Width,250),2,out score);Check(!match.IsEmpty&&match.Right>scaled.Width-220,"200 percent visual toolbar");}
            using(var blank=new Bitmap(chat.Width,125)){match=SparkVisionImage.Emoji(blank,template,new Rectangle(Point.Empty,blank.Size),1,out score);Check(match.IsEmpty,"blank surface is not an emoji target");}
            using(var full=new Bitmap(1200,chat.Height+55)){
                using(var graphics=Graphics.FromImage(full)){graphics.Clear(Color.FromArgb(24,24,32));graphics.DrawImageUnscaled(chat,1200-chat.Width,55);}
                var origin=new Point(1200-520,0);using(var sidebar=full.Clone(new Rectangle(origin,new Size(520,full.Height)),full.PixelFormat)){
                    var view=DouyinVision.Describe(new IntPtr(12),new Rectangle(Point.Empty,full.Size),1,"联系人",sidebar,origin,p=>p.Y>55&&p.Y<95?"联系人":null,n=>null,CancellationToken.None);
                    Check(view.IsChat("联系人")&&view.Emoji!=null&&view.Back!=null,"chat header and composer detected together");Check(view.Spark==null,"old fire messages cannot become the picker tile");
                    int headerReads=0;view=DouyinVision.Describe(new IntPtr(12),new Rectangle(Point.Empty,full.Size),1,"完整备注·测试",sidebar,origin,p=>null,n=>null,CancellationToken.None,()=>{headerReads++;return "完整备注·测试";});
                    Check(headerReads==1&&view.IsChat("完整备注·测试")&&view.Emoji!=null,"direct native header callback supplies complete recipient identity to the visual chat");
                    view=DouyinVision.Describe(new IntPtr(12),new Rectangle(Point.Empty,full.Size),1,"完整备注·测试",sidebar,origin,p=>"完整备注·测试",n=>null,CancellationToken.None,()=>"其他联系人");
                    Check(!view.IsChat("完整备注·测试"),"native header mismatch cannot be overridden by a nearby matching text point");
                }
            }
        }
        using(var screenshot=new Bitmap(Path.Combine(fixtures,"codex-clipboard-50c45871-5298-46f1-9b02-ee177bb1eb1c.png"))){
            var origin=new Point(screenshot.Width-520,0);using(var sidebar=screenshot.Clone(new Rectangle(origin,new Size(520,screenshot.Height)),screenshot.PixelFormat)){
                var view=DouyinVision.Describe(new IntPtr(12),new Rectangle(Point.Empty,screenshot.Size),1,"冰月一十",sidebar,origin,p=>p.Y>508&&p.Y<533?"冰月一十":null,n=>"冰月一十",CancellationToken.None);
                Console.WriteLine("List: "+view.HasList+"; contacts: "+view.Contacts.Count+"; field: "+(view.SearchBox==null?Rectangle.Empty:view.SearchBox.Bounds));
                Check(view.HasList&&view.SearchComplete&&view.Contacts.Count==1,"visible list and exact native name at OCR anchor");
                view=DouyinVision.Describe(new IntPtr(12),new Rectangle(Point.Empty,screenshot.Size),1,"冰月十一",sidebar,origin,p=>"冰月一十",n=>"冰月十一",CancellationToken.None);
                Check(view.SearchComplete&&view.Contacts.All(n=>n.Name!="冰月十一"),"OCR-confusable names are never fuzzy-matched");
                view=DouyinVision.Describe(new IntPtr(12),new Rectangle(Point.Empty,screenshot.Size),1,"",sidebar,origin,p=>{throw new Exception("unfiltered contacts must not be read");},n=>"",CancellationToken.None);
                Check(view.HasList&&view.Contacts.Count==0,"unfiltered message contents excluded from recognition");Check(view.Nodes.Contains(view.SearchBox),"initial search target survives click revalidation");
                view=DouyinVision.Describe(new IntPtr(12),new Rectangle(Point.Empty,screenshot.Size),1,"another name",sidebar,origin,p=>null,n=>"old query",CancellationToken.None);
                Check(view.HasList&&view.Nodes.Contains(view.SearchBox),"new search can replace a previous query");
            }
        }
        using(var picker=new Bitmap(Path.Combine(fixtures,"codex-clipboard-2909f011-9ec9-423b-a77e-07764ec4cc6a.png"))){
            var lines=SparkVisionText.Lines(SparkWindowsOcr.Read(picker,new Rectangle(Point.Empty,picker.Size),Point.Empty,CancellationToken.None));
            Check(lines.Any(line=>SparkVisionText.Compact(line.Text)=="续火花"),"Chinese fire picker label recognized");Check(SparkVisionImage.Fire(picker,new Rectangle(16,26,56,55)),"fire tile has matching orange graphic");
        }
        var cancel=new CancellationTokenSource();cancel.Cancel();bool cancelled=false;using(var image=new Bitmap(20,20)){try{SparkWindowsOcr.Read(image,new Rectangle(0,0,20,20),Point.Empty,cancel.Token);}catch(OperationCanceledException){cancelled=true;}}
        Check(cancelled,"OCR honours task cancellation");
        var result=new{pass=true,checks=checks,totalMilliseconds=clock.ElapsedMilliseconds,realMessagesSent=0};File.WriteAllText(Path.Combine(output,"result.json"),new JavaScriptSerializer().Serialize(result));Console.WriteLine(new JavaScriptSerializer().Serialize(result));return 0;
    }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
