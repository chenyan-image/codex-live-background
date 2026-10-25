using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Security.Principal;

[assembly:AssemblyTitle("卸载 Codex 动态背景")]
[assembly:AssemblyProduct("Codex 动态背景")]
[assembly:AssemblyVersion("1.6.2.0")]
[assembly:AssemblyFileVersion("1.6.2.0")]
internal static class Uninstall {
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern int MsiQueryProductState(string product);
    static string Product(){using(var hash=MD5.Create())return new Guid(hash.ComputeHash(Encoding.UTF8.GetBytes("codex-live-background|product-"+Assembly.GetExecutingAssembly().GetName().Version.ToString(3)))).ToString("B").ToUpperInvariant();}
    static bool NeedsAuthorization(string root,string local,string system){return !String.Equals(Path.GetPathRoot(root),Path.GetPathRoot(system),StringComparison.OrdinalIgnoreCase)||!Path.GetFullPath(root).StartsWith(Path.GetFullPath(local).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);}
    [STAThread]static int Main(string[] args){
        Application.EnableVisualStyles();
        try{
            bool authorized=args.Length==2&&args[0]=="--authorized-uninstall";
            string sid=WindowsIdentity.GetCurrent().User.Value;
            if(authorized&&args[1]!=sid)throw new Exception("授权后的 Windows 账号与原账号不同，已停止，未更改其他账号的背景设置。");
            string product=Product();
            if(args.Length==1&&args[0]=="--inspect"){Console.WriteLine(product);return 0;}
            if(MsiQueryProductState(product)!=5){MessageBox.Show("未找到此版本的 MSI 安装记录。请从 Windows 设置卸载已安装的版本。","Codex 动态背景");return 1;}
            if(!authorized&&MessageBox.Show("卸载 Codex 动态背景？\r\n\r\n将停止后台组件，移除自启动、启动挂接、程序及生成的缓存和记录。\r\n原始壁纸文件与 Codex 登录资料保留。","卸载 Codex 动态背景",MessageBoxButtons.OKCancel,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.OK)return 0;
            bool admin=new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            if(authorized&&!admin)throw new Exception("尚未取得所需的 Windows 授权，未开始卸载。");
            if(!admin&&NeedsAuthorization(AppDomain.CurrentDomain.BaseDirectory,Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Environment.SystemDirectory)){
                using(var child=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--authorized-uninstall \""+sid+"\""){UseShellExecute=true,Verb="runas"})){}
                return 0;
            }
            // Start Windows Installer and exit immediately so it can remove this EXE.
            var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"msiexec.exe"),"/x "+product+" /passive /norestart"){UseShellExecute=false,CreateNoWindow=true};
            using(var child=Process.Start(start)){}
            return 0;
        }catch(System.ComponentModel.Win32Exception error){if(error.NativeErrorCode==1223)return 0;MessageBox.Show(error.Message,"卸载未完成");return 1;}
        catch(Exception error){MessageBox.Show(error.Message,"卸载未完成");return 1;}
    }
}
