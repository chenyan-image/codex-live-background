using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Input routing only: no real pointer messages, visible windows, quota CLI,
// material downloads, service actions, or decoder processes are used here.
internal static class PetClickInteractionTests {
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks;
    static void Need(bool condition,string text){checks++;if(!condition)throw new Exception(text);}
    static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,Hidden).GetValue(target);}
    static void Set(object target,string name,object value){target.GetType().GetField(name,Hidden).SetValue(target,value);}
    static object Invoke(object target,string name,params object[] args){try{return target.GetType().GetMethod(name,Hidden).Invoke(target,args);}catch(TargetInvocationException error){throw error.InnerException??error;}}
    static void AdvancePanel(PetWindow pet,double elapsed){Invoke(pet,"AdvancePanel",Field<double>(pet,"panelStarted")+elapsed);}
    static void Release(PetWindow pet,MouseButtons button,bool moved,bool hud,int x=10){
        Set(pet,"mouseHeld",true);Set(pet,"bodyHeld",!hud);Set(pet,"moved",moved);
        Invoke(pet,"PointerUp",new MouseEventArgs(button,1,x,20,0),hud);
        Set(pet,"mouseHeld",false);Set(pet,"bodyHeld",false);
    }
    static void Ready(PetWindow pet,Rectangle area){
        Field<System.Windows.Forms.Timer>(pet,"timer").Stop();
        pet.SurfaceProvider=()=>new PetSurface(area,new IntPtr(11));
        Set(pet,"surface",new PetSurface(area,new IntPtr(11)));Set(pet,"previousBounds",area);
        pet.Motion.Position=new PointF(area.Left+area.Width/2-pet.Width/2,area.Top+area.Height/2-pet.Height/2);
        pet.Location=Point.Round(pet.Motion.Position);
        IntPtr handle=pet.Handle;Invoke(pet,"Present");
        Need(!pet.Visible&&!Field<PetOverlay>(pet,"hud").Visible&&!Field<PetOverlay>(pet,"popup").Visible,"Hidden input fixture displayed a window");
    }
    static PetPack SyntheticDragon(PetPack source){return new PetPack{Id="bailong",Name="白龙娘",Directory=source.Directory,AtlasFile=source.AtlasFile,Author="test fixture",Format=source.Format,Actions=source.Actions,RowMap=source.RowMap,Behavior=source.Behavior,ProceduralMotion=source.ProceduralMotion};}
    static object CheckPet(string root,PetPack pack,Rectangle area,bool synthetic){
        int start=checks,clickRefreshes=0;
        foreach(int opacity in new[]{0,100}){
            var settings=new PetPreferences{Roaming=false,Sound=false,CodexOnly=true,PositionVersion=1,CapsuleOpacity=opacity};
            using(var pet=new PetWindow(pack,settings,root,new PetAtlas(pack,true))){
                Ready(pet,area);pet.RefreshQuota+=delegate{clickRefreshes++;};
                var menu=Field<ContextMenuStrip>(pet,"menu");
                Need(!menu.Items.Cast<ToolStripItem>().Any(item=>item.Text.Contains("聊聊")||item.Text.Contains("离线互动")||item.Text.Contains("离线聊天")),"Chat entry remains for "+pack.Id);
                foreach(bool tasks in new[]{true,false}){
                    Rectangle before=pet.Bounds;pet.OpenPanel(tasks);AdvancePanel(pet,.33);
                    Need(pet.Expanded&&pet.TaskPanel==tasks&&pet.PanelReveal==1,"Could not open card for "+pack.Id);
                    int expected=clickRefreshes+1;Release(pet,MouseButtons.Left,false,false);
                    Need(clickRefreshes==expected,"Body click did not issue exactly one refresh for "+pack.Id);
                    Need(!pet.Expanded&&pet.PanelReveal==1,"Body click did not start animated card dismissal for "+pack.Id);
                    AdvancePanel(pet,.12);Need(pet.PanelReveal>0&&pet.PanelReveal<1,"Dismissal jumps instead of animating for "+pack.Id);
                    Need(pet.Bounds==before,"Card dismissal moved or resized pet for "+pack.Id);
                    AdvancePanel(pet,.33);Need(pet.PanelReveal==0,"Card did not finish dismissing for "+pack.Id);
                    Need(pet.Bounds==before,"Completed dismissal changed pet bounds for "+pack.Id);
                }
                int count=clickRefreshes;Release(pet,MouseButtons.Right,false,false);Need(clickRefreshes==count,"Right-button release refreshed quota for "+pack.Id);
                pet.OpenPanel(true);AdvancePanel(pet,.33);count=clickRefreshes;
                Release(pet,MouseButtons.Left,true,false);Need(clickRefreshes==count&&pet.Expanded,"Drag release was treated as a body click for "+pack.Id);
                int x=(int)(100*pet.RenderScale*pet.CapsuleFactor);Release(pet,MouseButtons.Left,false,true,x);Need(clickRefreshes==count,"HUD click refreshed quota for "+pack.Id);
                Need(!pet.Visible&&!Field<PetOverlay>(pet,"hud").Visible&&!Field<PetOverlay>(pet,"popup").Visible,"Input fixture displayed a window for "+pack.Id);
            }
        }
        return new{id=pack.Id,checks=checks-start,bodyRefreshes=clickRefreshes,opacityValues=new[]{0,100},syntheticAssets=synthetic};
    }
    [STAThread]static int Main(string[] args){
        string report=args.Length>2?args[2]:Path.Combine(Path.GetTempPath(),"PetClickInteractionTests.json");
        try{
            if(args.Length<3)throw new ArgumentException("Expected PackageRoot PetState Report");
            Application.EnableVisualStyles();string root=Path.GetFullPath(args[0]),state=Path.GetFullPath(args[1]);
            var available=PetPack.Discover(root,state).Where(pack=>pack.Available&&new[]{"deepseek","deepseek-fat-fish","bailong"}.Contains(pack.Id)).ToList();
            Need(available.Any(pack=>pack.Id=="deepseek"),"Whale girl asset is unavailable");Need(available.Any(pack=>pack.Id=="deepseek-fat-fish"),"Whale maid asset is unavailable");
            bool synthetic=!available.Any(pack=>pack.Id=="bailong");if(synthetic)available.Add(SyntheticDragon(available.First(pack=>pack.Id=="deepseek")));
            Need(typeof(PetWindow).Assembly.GetType("PetOfflineChat")==null,"Removed chat dialog is still compiled");
            Need(typeof(PetWindow).GetMethod("OfflineChat",Hidden)==null,"Removed chat entry is still compiled");
            var monitor=Screen.AllScreens.OrderBy(screen=>screen.WorkingArea.Left).First().WorkingArea;
            var area=new Rectangle(monitor.Left+30,monitor.Top+30,Math.Max(600,monitor.Width-60),Math.Max(500,monitor.Height-60));
            var results=available.Select(pack=>CheckPet(root,pack,area,synthetic&&pack.Id=="bailong")).ToArray();
            File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{pass=true,checks,pets=results,physicalMouseNotSimulated=true,windowsShown=false,actualQuotaRead=false,materialDownload=false,animationDecodingTested=false,serviceActions=false,oldResizeAndRestoreTestsRepeated=false}));return 0;
        }catch(Exception error){File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{pass=false,checks,error=error.ToString()}));return 1;}
    }
}
