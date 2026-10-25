using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
[assembly:AssemblyTitle("Codex 动态背景 · 授权安装")]
[assembly:AssemblyProduct("Codex 动态背景")]
[assembly:AssemblyVersion("1.6.2.0")]
[assembly:AssemblyFileVersion("1.6.2.0")]
internal static class AdminInstall {
    static string Quote(string value){return "\""+value.Replace("\"","")+"\"";}
    static bool IsAdmin(){return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);}
    static void Verify(string path){using(var file=File.OpenRead(path))using(var sha=SHA256.Create()){string hash=BitConverter.ToString(sha.ComputeHash(file)).Replace("-","");if(!String.Equals(hash,AdminInstallPayload.Sha256,StringComparison.OrdinalIgnoreCase))throw new Exception("安装包版本或校验值不匹配。请下载与此入口同版的 "+AdminInstallPayload.FileName+"。");}}
    [STAThread]static int Main(string[] args){
        Application.EnableVisualStyles();
        try{
            bool elevated=args.Length==3&&args[0]=="--elevated";
            string sid=WindowsIdentity.GetCurrent().User.Value;
            if(elevated&&args[2]!=sid)throw new Exception("授权后的 Windows 账号与原账号不同，已停止，避免安装到另一个账号。请使用原账号的管理员授权。");
            string path=elevated?Path.GetFullPath(args[1]):Path.Combine(AppDomain.CurrentDomain.BaseDirectory,AdminInstallPayload.FileName);
            if(!File.Exists(path)&&!elevated){using(var picker=new OpenFileDialog{Title="选择同版本的 MSI 安装包",Filter="Codex 安装包|"+AdminInstallPayload.FileName,FileName=AdminInstallPayload.FileName}){if(picker.ShowDialog()!=DialogResult.OK)return 0;path=picker.FileName;}}
            Verify(path);
            if(!IsAdmin()){
                if(elevated)throw new Exception("未取得管理员授权，尚未开始安装。");
                using(var child=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--elevated "+Quote(path)+" "+Quote(sid)){UseShellExecute=true,Verb="runas"})){}
                return 0;
            }
            string log=Path.Combine(Path.GetTempPath(),"CodexBackground-authorized-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".log");
            string installer=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"msiexec.exe");
            // Keep registrations in this user's context; start the manager later
            // from its ordinary desktop shortcut so it does not inherit elevation.
            using(var process=Process.Start(new ProcessStartInfo(installer,"/i "+Quote(path)+" LAUNCHMANAGER=0 /norestart /L*V "+Quote(log)){UseShellExecute=false,CreateNoWindow=true})){}
            return 0;
        }catch(System.ComponentModel.Win32Exception error){if(error.NativeErrorCode==1223)return 0;MessageBox.Show(error.Message,"授权安装未开始");return 1;}
        catch(Exception error){MessageBox.Show(error.Message,"授权安装未开始");return 1;}
    }
}
