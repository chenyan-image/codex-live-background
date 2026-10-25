using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;

[assembly:AssemblyTitle("卸载 Codex 动态背景")]
[assembly:AssemblyProduct("Codex 动态背景")]
[assembly:AssemblyVersion("1.6.1.0")]
[assembly:AssemblyFileVersion("1.6.1.0")]
internal static class Uninstall {
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern int MsiQueryProductState(string product);
    static string Product(){using(var hash=MD5.Create())return new Guid(hash.ComputeHash(Encoding.UTF8.GetBytes("codex-live-background|product-"+Assembly.GetExecutingAssembly().GetName().Version.ToString(3)))).ToString("B").ToUpperInvariant();}
    [STAThread]static int Main(string[] args){
        Application.EnableVisualStyles();
        try{
            string product=Product();
            if(args.Length==1&&args[0]=="--inspect"){Console.WriteLine(product);return 0;}
            if(MsiQueryProductState(product)!=5){MessageBox.Show("未找到此版本的 MSI 安装记录。请从 Windows 设置卸载已安装的版本。","Codex 动态背景");return 1;}
            if(MessageBox.Show("卸载 Codex 动态背景？\r\n\r\n将停止后台组件，移除自启动、启动挂接、程序及生成的缓存和记录。\r\n原始壁纸文件与 Codex 登录资料保留。","卸载 Codex 动态背景",MessageBoxButtons.OKCancel,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.OK)return 0;
            // Start Windows Installer and exit immediately so it can remove this EXE.
            var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"msiexec.exe"),"/x "+product+" /passive /norestart"){UseShellExecute=false,CreateNoWindow=true};
            using(var child=Process.Start(start)){}
            return 0;
        }catch(Exception error){MessageBox.Show(error.Message,"卸载未完成");return 1;}
    }
}
