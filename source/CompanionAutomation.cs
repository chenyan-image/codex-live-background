using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

internal sealed class SparkPetGuide : ISparkGuide {
    readonly Func<PetWindow> current;PetWindow leased;
    public SparkPetGuide(Func<PetWindow> pet){current=pet;}
    public Task WalkTo(Point point,CancellationToken stop){
        stop.ThrowIfCancellationRequested();var window=current();if(window==null||window.IsDisposed)throw new InvalidOperationException("请先在桌面宠物中开启一个宠物，再运行续火花。");
        if(leased!=null&&leased!=window)throw new InvalidOperationException("宠物已切换，续火花已停止。");leased=window;return window.WalkForSpark(point,stop);
    }
    public void End(){if(leased!=null&&!leased.IsDisposed)leased.EndSparkGuide();leased=null;}
}
internal sealed partial class PetWindow {
    internal bool SparkGuiding {get{return sparkGuiding;}}
    bool sparkGuiding,sparkMoving,sparkPanelExpanded;int sparkRow;PointF sparkSavedPosition,sparkDestination,sparkOrigin;Rectangle sparkArea;double sparkActionAt,sparkMoveAt,sparkDuration;
    TaskCompletionSource<bool> sparkMove;CancellationToken sparkStop;
    internal Task WalkForSpark(Point target,CancellationToken stop){
        if(!settings.Enabled)throw new InvalidOperationException("桌面宠物已关闭，续火花未执行。");
        if(!sparkGuiding){sparkGuiding=true;sparkSavedPosition=Motion.Position;sparkPanelExpanded=expanded;expanded=false;panelReveal=0;popup.Hide();hud.Hide();menu.Close();Motion.Interact(0);}
        sparkArea=PetDisplays.At(target).WorkingArea;FitScale(sparkArea);var body=BodyRectangle(atlas.IdleContentBounds,false);
        float x=target.X-body.Width-18-body.X,y=target.Y-body.Height*.6F-body.Y;
        // Keep the pet beside the sidebar so it cannot cover OCR/input targets.
        if(DouyinDesktop.GuideRight>sparkArea.Left+body.Width+16)x=Math.Min(x,DouyinDesktop.GuideRight-body.Right-14);
        x=Math.Max(sparkArea.Left-body.X,Math.Min(sparkArea.Right-body.Right,x));y=Math.Max(sparkArea.Top-body.Y,Math.Min(sparkArea.Bottom-body.Bottom,y));
        if(new RectangleF(x+body.X-8,y+body.Y-8,body.Width+16,body.Height+16).Contains(target)){x=target.X+18-body.X;x=Math.Max(sparkArea.Left-body.X,Math.Min(sparkArea.Right-body.Right,x));}
        sparkOrigin=Motion.Position;sparkDestination=new PointF(x,y);sparkMoving=true;sparkStop=stop;sparkRow=x>=Motion.Position.X?1:2;sparkMoveAt=sparkActionAt=watch.Elapsed.TotalSeconds;
        double dx=x-sparkOrigin.X,dy=y-sparkOrigin.Y;sparkDuration=Math.Max(.18,Math.Min(.55,Math.Sqrt(dx*dx+dy*dy)/1800));director.Manual(sparkRow,sparkActionAt);atlas.ResumeVideo();animationGeneration=director.Generation;actionStarted=sparkActionAt;atlas.BeginVideo(sparkRow,animationGeneration,true,true);
        sparkMove=new TaskCompletionSource<bool>();return sparkMove.Task;
    }
    internal void EndSparkGuide(){
        if(!sparkGuiding)return;sparkGuiding=sparkMoving=false;var pending=sparkMove;sparkMove=null;if(pending!=null)pending.TrySetCanceled();
        Motion.Interact(0);Motion.Position=sparkSavedPosition;Location=Point.Round(sparkSavedPosition);director.Restore(watch.Elapsed.TotalSeconds);expanded=sparkPanelExpanded;panelReveal=expanded?1:0;checkedSurface=0;animationGeneration=-1;
        AdvanceOnce();
    }
    void TickSparkGuide(double now,double delta){
        if(sparkStop.IsCancellationRequested||!settings.Enabled){EndSparkGuide();return;}
        atlas.ResumeVideo();
        if(sparkMoving){
            if(atlas.IsVideo&&atlas.VideoFinished&&now-sparkActionAt>.3){sparkActionAt=now;director.Manual(sparkRow,now);}
            double progress=Math.Max(0,Math.Min(1,(now-sparkMoveAt)/sparkDuration)),ease=progress*progress*(3-2*progress);
            Motion.Position=new PointF(sparkOrigin.X+(float)((sparkDestination.X-sparkOrigin.X)*ease),sparkOrigin.Y+(float)((sparkDestination.Y-sparkOrigin.Y)*ease));
            if(progress>=1){Motion.Position=sparkDestination;sparkMoving=false;director.Manual(0,now);}
        }
        Location=Point.Round(Motion.Position);if(!Visible)Show();MaintainTopmost();atlas.MirrorMotion=atlas.IsVideo&&sparkMoving&&pack.Actions[atlas.ActionIndex(director.Row)].moveFacesLeft&&sparkRow==1;
        Present();popup.Hide();hud.Hide();
        if(!sparkMoving&&sparkMove!=null){var complete=sparkMove;sparkMove=null;complete.TrySetResult(true);}
    }
}
