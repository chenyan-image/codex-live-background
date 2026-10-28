using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

internal interface ISparkAppLauncher {Task EnsureReady(Action<string> progress,CancellationToken stop);}
internal static class SparkStartFlow {
    public static async Task Run(ISparkAppLauncher launcher,SparkRunner runner,SparkSettings settings,bool inspect,CancellationToken stop){settings.Validate();stop.ThrowIfCancellationRequested();await launcher.EnsureReady(s=>runner.Report(s),stop);stop.ThrowIfCancellationRequested();await runner.Run(settings,inspect,stop);}
}
internal static class DouyinInstall {
    internal static string Executable(string value){
        if(String.IsNullOrWhiteSpace(value))return null;value=Environment.ExpandEnvironmentVariables(value.Trim());
        if(value.StartsWith("\"",StringComparison.Ordinal)){int end=value.IndexOf('"',1);if(end<0)return null;value=value.Substring(1,end-1);}
        else{int end=value.IndexOf(".exe",StringComparison.OrdinalIgnoreCase);if(end<0)return null;value=value.Substring(0,end+4).Trim();}
        try{return Path.IsPathRooted(value)&&String.Equals(Path.GetFileName(value),"douyin.exe",StringComparison.OrdinalIgnoreCase)?Path.GetFullPath(value):null;}catch(ArgumentException){return null;}catch(NotSupportedException){return null;}
    }
    static void Add(List<string> paths,string value){var path=Executable(value);if(path!=null&&!paths.Contains(path,StringComparer.OrdinalIgnoreCase))paths.Add(path);}
    public static string Find(string preferred){
        var paths=new List<string>();Add(paths,preferred);
        foreach(var process in Process.GetProcessesByName("douyin"))using(process)try{Add(paths,process.MainModule.FileName);}catch(Win32Exception){}catch(InvalidOperationException){}
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})try{
            using(var registry=RegistryKey.OpenBaseKey(hive,view)){
                using(var app=registry.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\douyin.exe")){if(app!=null)Add(paths,Convert.ToString(app.GetValue(null)));}
                using(var uninstall=registry.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall")){if(uninstall!=null)foreach(string name in uninstall.GetSubKeyNames().Take(3000))using(var item=uninstall.OpenSubKey(name)){
                    if(item==null)continue;string title=Convert.ToString(item.GetValue("DisplayName"));if(title.IndexOf("抖音",StringComparison.OrdinalIgnoreCase)<0&&title.IndexOf("douyin",StringComparison.OrdinalIgnoreCase)<0)continue;
                    Add(paths,Convert.ToString(item.GetValue("DisplayIcon")));string location=Convert.ToString(item.GetValue("InstallLocation"));if(!String.IsNullOrEmpty(location))try{Add(paths,Path.Combine(location,"douyin.exe"));}catch(ArgumentException){}
                }}
            }
        }catch(SecurityException){}catch(UnauthorizedAccessException){}catch(IOException){}
        foreach(var root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)})if(!String.IsNullOrEmpty(root))Add(paths,Path.Combine(root,@"ByteDance\douyin\douyin.exe"));
        foreach(var drive in DriveInfo.GetDrives())if(drive.DriveType==DriveType.Fixed)foreach(string folder in new[]{@"Program Files (x86)\ByteDance\douyin",@"Program Files\ByteDance\douyin",@"ByteDance\douyin",@"douyin",@"抖音"})Add(paths,Path.Combine(drive.Name,folder,"douyin.exe"));
        return paths.FirstOrDefault(File.Exists);
    }
}
internal sealed class DouyinAppLauncher : ISparkAppLauncher {
    readonly string preferred;readonly Action<string> found;
    public DouyinAppLauncher(string path,Action<string> selected=null){preferred=path;found=selected;}
    public async Task EnsureReady(Action<string> progress,CancellationToken stop){
        stop.ThrowIfCancellationRequested();if(DouyinDesktop.HasForegroundWindow())return;
        progress("正在查找已安装的抖音…");var window=DouyinDesktop.FindOpenWindow();
        if(window==IntPtr.Zero){string program=await Task.Run(()=>DouyinInstall.Find(preferred));stop.ThrowIfCancellationRequested();if(program==null)throw new FileNotFoundException("未找到抖音桌面客户端。请先安装抖音，或点击“定位抖音”选择 douyin.exe。");
            progress("正在打开抖音…");using(var child=Process.Start(new ProcessStartInfo(program){WorkingDirectory=Path.GetDirectoryName(program),UseShellExecute=false})){}if(found!=null)found(program);
        }else progress("正在恢复抖音窗口…");
        var elapsed=Stopwatch.StartNew();int stable=0;
        while(elapsed.ElapsedMilliseconds<45000){
            stop.ThrowIfCancellationRequested();if(window==IntPtr.Zero)window=DouyinDesktop.FindOpenWindow();
            if(window!=IntPtr.Zero){if(!DouyinDesktop.HasForegroundWindow()){DouyinDesktop.Activate(window);stable=0;}else if(++stable>=3){progress("抖音已打开，正在连接消息控件…");return;}}
            await Task.Delay(250,stop);
        }
        throw new InvalidOperationException("抖音启动后未出现可操作的主窗口，请检查首次启动或登录提示，再点击开始。");
    }
}
