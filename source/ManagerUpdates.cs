using System;using System.Diagnostics;using System.IO;using System.Linq;using System.Windows.Forms;
internal sealed partial class Manager {
    bool updatingSoftware;readonly Button updateSoftware=new ActionButton();
    void BuildUpdateButton(){updateSoftware.Text="更新软件";updateSoftware.AccessibleName="更新软件，保留素材与设置";updateSoftware.Click+=delegate{BeginSoftwareUpdate();};commands.Controls.Add(updateSoftware);}
    void BeginSoftwareUpdate(){
        if(updatingSoftware||busy)return;if(petImporting||ImportingWallpapers||downloadingBuiltin){log.Text="素材仍在准备，请在完成后更新程序。";return;}updatingSoftware=true;SetBusyControls(true);updateSoftware.Enabled=false;log.Text="正在检查 GitHub 新版本，仅更新程序，保留素材和个人设置…";
        ImportWork.Start(delegate{try{var current=GetType().Assembly.GetName().Version;var release=SoftwareUpdate.FindRelease(current);if(release==null){Ui(delegate{log.Text="已是可用的最新版本（"+current+"）。";updatingSoftware=false;SetBusyControls(false);updateSoftware.Enabled=true;});return;}string archive=SoftwareUpdate.Download(release,stateDirectory,message=>Ui(delegate{log.Text=message;}));var verified=SoftwareUpdate.VerifyArchive(archive,release.sha256,Path.Combine(stateDirectory,"updates",release.version,"verified-"+Guid.NewGuid().ToString("N")));if(verified.version!=release.version)throw new InvalidDataException("更新包版本与 GitHub 清单不一致。");
            var request=new SoftwareUpdateRequest{Root=root,Archive=archive,Sha256=release.sha256,StateRoot=stateDirectory,ManagerPid=Process.GetCurrentProcess().Id,ManagerStarted=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,ResumeArguments=Environment.GetCommandLineArgs().Skip(1).ToArray()};string host=SoftwareUpdate.StageHost(root,request);Ui(delegate{log.Text="校验完成，正在更新程序；完成后自动重新打开。";using(var started=Process.Start(new ProcessStartInfo(host,"--apply "+SoftwareUpdate.Quote(Path.Combine(Path.GetDirectoryName(host),"request.json"))){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(host)})){}exitRequested=true;Close();});
        }catch(Exception error){Ui(delegate{log.Text="更新未完成："+error.Message+"\r\n现有程序、素材和设置均未改动。";updatingSoftware=false;SetBusyControls(false);updateSoftware.Enabled=true;});}});
    }
}
