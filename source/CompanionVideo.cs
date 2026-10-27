using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

// Decode production VP9-alpha off the UI thread. A bounded queue prevents a
// ten-second clip from becoming hundreds of full-size managed bitmaps.
internal sealed class PetVideoStream : IDisposable {
    internal sealed class Frame {public byte[] Pixels;public int Index;}
    readonly BlockingCollection<Frame> frames=new BlockingCollection<Frame>(3);
    readonly ConcurrentQueue<byte[]> recycled=new ConcurrentQueue<byte[]>();
    readonly CancellationTokenSource cancel=new CancellationTokenSource();readonly object gate=new object();
    readonly int bytes;PetProcessScope scope;Frame pending;volatile bool disposed,ended;volatile string error;int decoded,lastShown=-1;
    public readonly int Width,Height;public readonly double Fps;public string Error {get{return error;}}public double ShownSeconds{get{return Math.Max(0,lastShown/Fps);}}
    public bool Ended {get{return ended&&frames.Count==0&&pending==null&&lastShown>=decoded-1;}}
    public bool Ready {get{return pending!=null||frames.Count>0||lastShown>=0;}}
    public PetVideoStream(string executable,string input,int width,int height,double fps,double offset=0){
        if(width<1||height<1||width>1280||height>1280||fps<=0||fps>60)throw new InvalidDataException("透明动画尺寸或帧率无效。");
        Width=width;Height=height;Fps=fps;bytes=checked(width*height*4);
        Task.Factory.StartNew(()=>Decode(executable,input,offset),CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
    }
    static string Quote(string value){return "\""+value.Replace("\"","\\\"")+"\"";}
    void Decode(string executable,string input,double seek){
        try{
            if(!File.Exists(executable))throw new FileNotFoundException("缺少安装包内的透明动画解码组件。");
            string args="-nostdin -hide_banner -loglevel error -threads 2 -c:v libvpx-vp9 -i "+Quote(input)+(seek>0?" -ss "+seek.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture):"")+" -an -sn -dn -vf scale="+Width+":"+Height+":flags=bilinear -f rawvideo -pix_fmt bgra pipe:1";
            var local=new PetProcessScope(new ProcessStartInfo(executable,args){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(executable),RedirectStandardOutput=true,RedirectStandardError=true});
            lock(gate){scope=local;if(disposed)local.Cancel();}
            using(local){var stderr=local.Process.StandardError.ReadToEndAsync();var inputStream=local.Process.StandardOutput.BaseStream;
                while(!cancel.IsCancellationRequested){byte[] buffer;if(!recycled.TryDequeue(out buffer))buffer=new byte[bytes];int offset=0;
                    while(offset<bytes&&!cancel.IsCancellationRequested){int read=inputStream.Read(buffer,offset,bytes-offset);if(read==0)break;offset+=read;}
                    if(offset!=bytes){Recycle(buffer);break;}frames.Add(new Frame{Pixels=buffer,Index=decoded++},cancel.Token);
                }
                if(!cancel.IsCancellationRequested){local.Process.WaitForExit();if(local.Process.ExitCode!=0)error="透明动画解码失败。";}
            }
        }catch(OperationCanceledException){}catch(ObjectDisposedException){}catch(Exception exception){if(!disposed)error=exception is FileNotFoundException?exception.Message:"透明动画读取失败。";}
        finally{ended=true;frames.CompleteAdding();lock(gate){scope=null;}if(disposed){Frame frame;byte[] buffer;while(frames.TryTake(out frame)){}while(recycled.TryDequeue(out buffer)){} }}
    }
    public bool TryFrame(double elapsed,out Frame selected,int maxAdvance=0){
        selected=null;if(disposed)return false;int target=Math.Max(0,(int)(elapsed*Fps));if(lastShown>=0&&maxAdvance>0)target=Math.Min(target,lastShown+maxAdvance);
        while(true){Frame next=pending;if(next!=null)pending=null;else if(!frames.TryTake(out next))break;
            if(next.Index>target&&lastShown>=0){pending=next;break;}if(selected!=null)Recycle(selected.Pixels);selected=next;lastShown=next.Index;
        }return selected!=null;
    }
    public void Recycle(byte[] buffer){if(!disposed&&recycled.Count<3)recycled.Enqueue(buffer);}
    public void Dispose(){if(disposed)return;disposed=true;cancel.Cancel();lock(gate){if(scope!=null)scope.Cancel();}pending=null;Frame frame;byte[] pixels;while(frames.TryTake(out frame)){}while(recycled.TryDequeue(out pixels)){} }
}
internal sealed partial class PetAtlas {
    PetVideoStream video,prepared,dragPrepared;Bitmap videoFrame,previousVideo;bool previewOnly;int preparedRow=-1,dragRow=-1,videoGeneration=-1;double videoOrigin=-1,blendOrigin=-1,suspendedAt=-1;readonly Stopwatch videoClock=Stopwatch.StartNew();
    public void SuspendVideo(){if(IsVideo&&suspendedAt<0)suspendedAt=videoClock.Elapsed.TotalSeconds;}
    public void ResumeVideo(){if(suspendedAt<0)return;double duration=videoClock.Elapsed.TotalSeconds-suspendedAt;if(videoOrigin>=0)videoOrigin+=duration;if(blendOrigin>=0)blendOrigin+=duration;suspendedAt=-1;}
    public bool DragHeld;public double VideoPlaybackTime{get;private set;}
    public void ReleaseDragFrame(){if(!IsVideo)return;if(videoFrame!=null)videoFrame.Dispose();if(previousVideo!=null)previousVideo.Dispose();videoFrame=previousVideo=null;}
    public bool DragReady{get{return !IsVideo||dragPrepared!=null&&(dragPrepared.Ready||dragPrepared.Error!=null);}}
    public bool IsVideo{get{return animations!=null&&animations.Format=="transparent-video-set";}}
    public bool VideoFinished{get{return !IsVideo||video!=null&&(video.Ended||!String.IsNullOrEmpty(video.Error));}}
    public string VideoError{get{return video==null?null:video.Error;}}
    double playbackOffset;
    string Decoder{get{return PetMediaRuntime.Find();}}
    PetVideoStream Stream(int row,bool movement=false){var a=animations.Actions[ActionIndex(row)];return new PetVideoStream(Decoder,Path.Combine(animations.Directory,a.file),a.cellWidth,a.cellHeight,a.fps,row==dragRow?a.dragStartSeconds:movement?a.moveLeadSeconds:0);}
    public void BeginVideo(int row,int generation,bool direct=false,bool movement=false){if(!IsVideo||previewOnly||generation==videoGeneration)return;videoGeneration=generation;playbackOffset=row==dragRow?animations.Actions[ActionIndex(row)].dragStartSeconds:movement?animations.Actions[ActionIndex(row)].moveLeadSeconds:0;
        if(previousVideo!=null)previousVideo.Dispose();previousVideo=direct||videoFrame==null?null:new Bitmap(videoFrame);
        if(video!=null)video.Dispose();video=row==dragRow&&dragPrepared!=null?dragPrepared:!movement&&prepared!=null&&preparedRow==row?prepared:Stream(row,movement);if(video==dragPrepared)dragPrepared=null;if(video!=prepared&&prepared!=null)prepared.Dispose();prepared=null;preparedRow=-1;currentAction=ActionIndex(row);videoOrigin=blendOrigin=-1;VideoPlaybackTime=0;if(row!=dragRow)PrepareDrag(dragRow);
    }
    public void PrepareDrag(int row){if(!IsVideo||previewOnly||row<0)return;dragRow=row;if(dragPrepared==null)dragPrepared=Stream(row);}
    public void PrepareVideo(int row){if(!IsVideo||previewOnly||preparedRow==row)return;if(prepared!=null)prepared.Dispose();preparedRow=row;prepared=Stream(row);}
    void DrawVideo(Graphics g,RectangleF destination,int row,double time){
        if(previewOnly){g.DrawImage(bitmap,destination,new RectangleF(200,50,240,285),GraphicsUnit.Pixel);return;}
        if(video==null||ActionIndex(row)!=currentAction)BeginVideo(row,videoGeneration+1);
        double now=videoClock.Elapsed.TotalSeconds;if(videoOrigin<0&&video.Ready)videoOrigin=now;
        var action=animations.Actions[ActionIndex(row)];double offset=playbackOffset,elapsed=Math.Max(0,now-videoOrigin);if(row==dragRow&&DragHeld)elapsed=Math.Min(Math.Max(0,action.dragHoldSeconds-offset),elapsed*3.5);
        PetVideoStream.Frame frame;if(videoOrigin>=0&&video.TryFrame(elapsed,out frame,row==dragRow&&DragHeld?4:2)){
            if(videoFrame==null||videoFrame.Width!=video.Width||videoFrame.Height!=video.Height){if(videoFrame!=null)videoFrame.Dispose();videoFrame=new Bitmap(video.Width,video.Height,PixelFormat.Format32bppArgb);}
            var bits=videoFrame.LockBits(new Rectangle(0,0,video.Width,video.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);try{Marshal.Copy(frame.Pixels,0,bits.Scan0,frame.Pixels.Length);}finally{videoFrame.UnlockBits(bits);video.Recycle(frame.Pixels);}if(blendOrigin<0)blendOrigin=now;
        }
        VideoPlaybackTime=videoOrigin<0?0:offset+Math.Min(elapsed,video.ShownSeconds);
        if(videoFrame==null){g.DrawImage(bitmap,destination);return;}
        float alpha=blendOrigin<0?0:Math.Min(1,(float)((now-blendOrigin)/.12));if(previousVideo!=null&&alpha>=1){previousVideo.Dispose();previousVideo=null;}if(previousVideo!=null&&alpha<1)g.DrawImage(previousVideo,destination);
        if(alpha<1&&previousVideo!=null){using(var attributes=new ImageAttributes()){var matrix=new ColorMatrix();matrix.Matrix33=alpha;attributes.SetColorMatrix(matrix);g.DrawImage(videoFrame,new[]{new PointF(destination.Left,destination.Top),new PointF(destination.Right,destination.Top),new PointF(destination.Left,destination.Bottom)},new RectangleF(0,0,videoFrame.Width,videoFrame.Height),GraphicsUnit.Pixel,attributes);}}else g.DrawImage(videoFrame,destination);
    }
    void DisposeVideo(){if(video!=null)video.Dispose();if(prepared!=null)prepared.Dispose();if(dragPrepared!=null)dragPrepared.Dispose();if(videoFrame!=null)videoFrame.Dispose();if(previousVideo!=null)previousVideo.Dispose();video=prepared=dragPrepared=null;videoFrame=previousVideo=null;}
}
