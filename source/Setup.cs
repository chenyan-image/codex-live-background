using System;
using System.IO;
using System.IO.Compression;
using System.Drawing;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Reflection;
using System.Threading;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
internal sealed class Setup : Form {
    readonly Label status=new Label();readonly TextBox log=new TextBox(),target=new TextBox();readonly ProgressBar progress=new ProgressBar();readonly Button install=new Button(),remove=new Button(),browse=new Button();
    bool busy,completed;readonly bool uninstall,relocated;string retryAction;
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    Setup(bool mode,string root,bool temporary){
        uninstall=mode;relocated=temporary;Text="CodexHub · 1.7.0 预览版 5";BackColor=Color.FromArgb(14,21,25);ForeColor=Color.FromArgb(231,240,238);Font=new Font("Microsoft YaHei UI",10);ClientSize=new Size(710,594);FormBorderStyle=FormBorderStyle.None;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;Padding=new Padding(1);
        try{Icon=new Icon(Assembly.GetExecutingAssembly().GetManifestResourceStream("Codex.Setup.ico"));}catch{}
        var frame=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};frame.RowStyles.Add(new RowStyle(SizeType.Absolute,32));frame.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(frame);
        var bar=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=Color.FromArgb(18,27,31)};var caption=new Label{Text=Text,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(10,0,0,0),Font=new Font(Font.FontFamily,9),ForeColor=Color.FromArgb(174,197,194)};bar.Controls.Add(caption);
        var close=new WindowCaptionButton(WindowCaptionKind.Close){Dock=DockStyle.Right};close.Click+=delegate{Close();};
        var minimize=new WindowCaptionButton(WindowCaptionKind.Minimize){Dock=DockStyle.Right};minimize.Click+=delegate{WindowState=FormWindowState.Minimized;};bar.Controls.Add(minimize);bar.Controls.Add(close);frame.Controls.Add(bar,0,0);
        MouseEventHandler drag=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);}};bar.MouseDown+=drag;caption.MouseDown+=drag;Paint+=delegate(object sender,PaintEventArgs e){WindowFrame.PaintBorder(this,e.Graphics,Color.FromArgb(49,69,73));};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=8,Margin=Padding.Empty};foreach(int h in new[]{48,43,42,42})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));foreach(int h in new[]{24,48,35})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));frame.Controls.Add(layout,0,1);
        layout.Controls.Add(new Label{Text=mode?"卸载 CodexHub":"安装 CodexHub",Font=new Font(Font.FontFamily,21,FontStyle.Bold),Dock=DockStyle.Fill},0,0);
        layout.Controls.Add(new Label{Text="动态背景 + 桌面宠物 · 单 EXE 安装 · 自定义目录\r\n原始壁纸与 Codex 登录资料保留。",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(151,176,175)},0,1);
        var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty};row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));target.Text=root;target.Dock=DockStyle.Fill;target.ReadOnly=mode;row.Controls.Add(target,0,0);browse.Text="选择目录";browse.Dock=DockStyle.Fill;browse.Enabled=!mode;browse.Click+=delegate{using(var picker=new FolderBrowserDialog{Description="请选择当前账号可写的专用软件目录",SelectedPath=target.Text})if(picker.ShowDialog(this)==DialogResult.OK)target.Text=picker.SelectedPath;};row.Controls.Add(browse,1,0);layout.Controls.Add(row,0,2);
        status.Text=mode?"准备卸载：先停止本项目进程，再清理程序。":"准备安装：将检测并迁移旧版本。";status.Dock=DockStyle.Fill;status.ForeColor=Color.FromArgb(135,235,194);layout.Controls.Add(status,0,3);
        log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Vertical;log.Dock=DockStyle.Fill;log.BackColor=Color.FromArgb(23,31,35);log.ForeColor=Color.FromArgb(185,207,204);layout.Controls.Add(log,0,4);progress.Dock=DockStyle.Fill;layout.Controls.Add(progress,0,5);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};install.Text=mode?"开始卸载":"安装 / 修复";install.Width=150;install.Height=36;install.Click+=delegate{if(completed){Close();return;}Begin(retryAction??(uninstall?"Uninstall":"Install"));};buttons.Controls.Add(install);remove.Text="只卸载旧版本";remove.Width=150;remove.Height=36;remove.Visible=!mode;remove.Click+=delegate{Begin("Uninstall");};buttons.Controls.Add(remove);layout.Controls.Add(buttons,0,6);
        foreach(var button in new[]{install,remove,browse}){button.FlatStyle=FlatStyle.Flat;button.BackColor=Color.FromArgb(34,52,56);button.ForeColor=Color.FromArgb(224,244,237);button.FlatAppearance.BorderColor=Color.FromArgb(69,107,105);}
        target.BackColor=Color.FromArgb(23,31,35);target.ForeColor=Color.FromArgb(231,240,238);
        layout.Controls.Add(new Label{Text="窗口会保持到结束；失败时显示日志位置。",ForeColor=Color.FromArgb(151,176,175),Dock=DockStyle.Fill},0,7);
        FormClosing+=delegate(object s,FormClosingEventArgs e){if(busy){e.Cancel=true;MessageBox.Show(this,"正在处理文件或停止后台组件，请等待当前步骤结束。",Text);}};FormClosed+=delegate{if(relocated)ScheduleTemporaryCleanup();};
    }
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);WindowFrame.Update(this);}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);WindowFrame.Update(this);}
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    static string Quote(string value){var b=new StringBuilder("\"");int slashes=0;foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='"')b.Append('\\',slashes*2+1).Append(c);else b.Append('\\',slashes).Append(c);slashes=0;}return b.Append('\\',slashes*2).Append('"').ToString();}
    static bool Admin(){return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);}
    static string NewTemp(string prefix){string path=Path.Combine(Path.GetTempPath(),prefix+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
    static void RemoveTemp(string path,string prefix){string boundary=Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\')+"\\"+prefix;string full=Path.GetFullPath(path).TrimEnd('\\');if(!full.StartsWith(boundary,StringComparison.OrdinalIgnoreCase)||full.Substring(boundary.Length).Length!=32)throw new Exception("临时目录边界不匹配");if(Directory.Exists(full)){foreach(var f in Directory.EnumerateFileSystemEntries(full,"*",SearchOption.AllDirectories))if((File.GetAttributes(f)&FileAttributes.ReparsePoint)!=0)throw new Exception("临时目录含链接，已保留");Directory.Delete(full,true);}}
        static string Hash(Stream stream){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
    static string Inside(string directory,string relative){
        if(String.IsNullOrEmpty(relative)||Path.IsPathRooted(relative)||relative.IndexOf(':')>=0)throw new Exception("安装包路径无效。");
        string full=Path.GetFullPath(Path.Combine(directory,relative.Replace('/',Path.DirectorySeparatorChar)));
        if(!full.StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("安装包路径越界。");return full;
    }
    static void ExtractAndVerify(string stage,Action<string,int> report){
        using(var payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("Codex.Payload.zip")){
            if(payload==null||!Hash(payload).Equals(SetupPayload.Sha256,StringComparison.OrdinalIgnoreCase))throw new Exception("安装包校验失败，请从项目发行页重新下载。");
            payload.Position=0;using(var zip=new ZipArchive(payload,ZipArchiveMode.Read)){
                if(zip.Entries.Count>1000)throw new Exception("安装包文件数量异常。");long total=0;int count=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var entry in zip.Entries){
                    string file=Inside(stage,entry.FullName);if(!seen.Add(file))throw new Exception("安装包包含重复路径。");
                    if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(file);continue;}
                    total+=entry.Length;if(entry.Length>200L*1024*1024||total>500L*1024*1024)throw new Exception("安装包大小异常。");
                    Directory.CreateDirectory(Path.GetDirectoryName(file));using(var source=entry.Open())using(var target=File.Create(file))source.CopyTo(target);report("正在准备安装文件…",++count*25/zip.Entries.Count);
                }
            }
        }
        var manifest=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(stage,"manifest.json"),Encoding.UTF8));
        if(Convert.ToString(manifest["version"])!=SetupPayload.Version)throw new Exception("安装包版本不一致。");
        foreach(var item in (IEnumerable)manifest["files"]){var entry=(Dictionary<string,object>)item;string file=Inside(stage,Convert.ToString(entry["path"]));if(new FileInfo(file).Length!=Convert.ToInt64(entry["bytes"]))throw new Exception("安装文件长度校验失败。");using(var stream=File.OpenRead(file))if(!Hash(stream).Equals(Convert.ToString(entry["sha256"]),StringComparison.OrdinalIgnoreCase))throw new Exception("安装文件校验失败。");}
    }

    static string RunStep(string stage,string action,string root,string operation,int managerPid,Action<string> report){
        string ps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");string args="-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy RemoteSigned -File "+Quote(Path.Combine(stage,"native-actions.ps1"))+" -Action "+action+" -InstallRoot "+Quote(root)+" -SetupExecutable "+Quote(Application.ExecutablePath)+" -OperationRoot "+Quote(operation)+" -ManagerPid "+managerPid;
        using(var p=Process.Start(new ProcessStartInfo(ps,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8})){
            var output=new StringBuilder();var errors=new StringBuilder();p.OutputDataReceived+=delegate(object s,DataReceivedEventArgs e){if(e.Data!=null){lock(output)output.AppendLine(e.Data);report(e.Data);}};p.ErrorDataReceived+=delegate(object s,DataReceivedEventArgs e){if(e.Data!=null){lock(errors)errors.AppendLine(e.Data);report(e.Data);}};p.BeginOutputReadLine();p.BeginErrorReadLine();if(!p.WaitForExit(900000)){try{p.Kill();}catch{}throw new Exception("操作超时，日志与旧文件备份已保留。");}p.WaitForExit();if(p.ExitCode!=0)throw new Exception(errors.Length>0?errors.ToString():output.ToString());return output.ToString();
        }
    }
    void Report(string line){if(IsDisposed)return;BeginInvoke((MethodInvoker)delegate{log.AppendText(line+Environment.NewLine);if(line.Length<180)status.Text=line;});}
    static string ReadShared(string path){if(!File.Exists(path))return "";using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(stream,Encoding.UTF8))return reader.ReadToEnd();}
    void Begin(string action){
        if(busy)return;if(action=="Uninstall"&&MessageBox.Show(this,"停止并移除本项目后台组件、程序和缓存？\r\n原始壁纸与 Codex 登录资料保留，正常 Codex 聊天不会关闭。",Text,MessageBoxButtons.OKCancel,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.OK)return;
        string root;try{root=Path.GetFullPath(target.Text);if(Application.ExecutablePath.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new Exception("请从下载文件夹运行安装包，或使用安装目录内的独立卸载入口。");}catch(Exception e){MessageBox.Show(this,e.Message,Text);return;}
        retryAction=action;busy=true;install.Enabled=remove.Enabled=browse.Enabled=target.Enabled=false;progress.Style=ProgressBarStyle.Marquee;status.Text="正在校验并准备安装文件…";
        ThreadPool.QueueUserWorkItem(delegate{
            string stage=NewTemp("CodexBackgroundSetup-"),operation=NewTemp("CodexBackgroundOperation-");bool success=false;
            try{
                ExtractAndVerify(stage,delegate{});string output=RunStep(stage,"Plan",root,operation,Process.GetCurrentProcess().Id,delegate{});string planJson=null;foreach(string line in output.Split('\n'))if(line.StartsWith("DREAM_PLAN:"))planJson=line.Substring(11).Trim();if(planJson==null)throw new Exception("旧版本检查没有返回有效结果");var plan=Json.Deserialize<Dictionary<string,object>>(planJson);bool authorization=Convert.ToBoolean(plan["requiresAuthorization"]);
                string requestPath=Path.Combine(operation,"request.json");File.WriteAllText(requestPath,Json.Serialize(new{action=action,root=root,managerPid=Process.GetCurrentProcess().Id,requiresAuthorization=authorization}),new UTF8Encoding(false));
                var start=new ProcessStartInfo(Application.ExecutablePath,"--worker "+Quote(requestPath)+" "+Quote(WindowsIdentity.GetCurrent().User.Value)){UseShellExecute=authorization&&!Admin(),WindowStyle=ProcessWindowStyle.Hidden};if(start.UseShellExecute){start.Verb="runas";Report("旧 MSI 迁移需要 Windows 授权，请使用原账号确认。");}else start.CreateNoWindow=true;
                using(var worker=Process.Start(start)){int shown=0;while(!worker.WaitForExit(200)){string lines=ReadShared(Path.Combine(operation,"worker.log"));if(lines.Length>shown){foreach(string line in lines.Substring(shown).Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries))Report(line);shown=lines.Length;}}if(worker.ExitCode!=0&&!File.Exists(Path.Combine(operation,"result.json")))throw new Exception("未取得授权或工作进程未完成。日志："+operation);}
                var result=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(operation,"result.json"),Encoding.UTF8));success=Convert.ToBoolean(result["pass"]);if(!success)throw new Exception(Convert.ToString(result["error"]));Report(action=="Install"?"安装完成，请从桌面打开管理器。":"卸载完成，相关后台组件已退出。");try{RemoveTemp(operation,"CodexBackgroundOperation-");}catch{Report("操作已完成，暂时被占用的临时记录保留在："+operation);}
            }catch(System.ComponentModel.Win32Exception e){Report(e.NativeErrorCode==1223?"已取消 Windows 授权，未继续操作。":e.Message);}catch(Exception e){Report("操作未完成："+e.Message);Report("诊断和恢复文件保留在："+operation);}finally{try{RemoveTemp(stage,"CodexBackgroundSetup-");}catch{}}
            if(IsDisposed)return;BeginInvoke((MethodInvoker)delegate{busy=false;completed=success;progress.Style=ProgressBarStyle.Blocks;progress.Value=success?100:0;status.Text=success?(action=="Install"?"安装完成":"卸载完成"):"操作未完成，可查看日志后重试";install.Text=success?"完成":"重试";install.Enabled=true;remove.Enabled=!success;browse.Enabled=target.Enabled=!success&&!uninstall;});
        });
    }
    static int Worker(string requestPath,string sid){
        if(sid!=WindowsIdentity.GetCurrent().User.Value){MessageBox.Show("Windows 授权切换到了另一个账号，已停止，避免配置装错账号。","CodexHub");return 1;}
        string operation=Path.GetDirectoryName(Path.GetFullPath(requestPath));if(!operation.StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\')+"\\CodexBackgroundOperation-",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(requestPath)!="request.json")return 1;
        string stage=NewTemp("CodexBackgroundSetup-");bool success=false;string failure=null;
        try{var request=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(requestPath,Encoding.UTF8));if(Convert.ToBoolean(request["requiresAuthorization"])&&!Admin())throw new Exception("尚未获得 Windows 授权");ExtractAndVerify(stage,delegate{});string action=Convert.ToString(request["action"]);if(action!="Install"&&action!="Uninstall")throw new Exception("操作参数无效");object logLock=new object();RunStep(stage,action,Convert.ToString(request["root"]),operation,Convert.ToInt32(request["managerPid"]),delegate(string line){lock(logLock)File.AppendAllText(Path.Combine(operation,"worker.log"),line+Environment.NewLine,new UTF8Encoding(false));});success=true;}catch(Exception e){failure=e.Message;}finally{try{RemoveTemp(stage,"CodexBackgroundSetup-");}catch{}}
        File.WriteAllText(Path.Combine(operation,"result.json"),Json.Serialize(new{pass=success,error=failure}),new UTF8Encoding(false));return success?0:1;
    }
    void ScheduleTemporaryCleanup(){
        string folder=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');if(!folder.StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\')+"\\CodexBackgroundUninstall-",StringComparison.OrdinalIgnoreCase))return;
        string script=Path.Combine(folder,"cleanup.ps1");string body="param([int]$ProcessId,[long]$Ticks,[string]$Folder)\r\n$ErrorActionPreference='Stop'\r\n$prefix=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\\')+'\\CodexBackgroundUninstall-'\r\n$full=[IO.Path]::GetFullPath($Folder).TrimEnd('\\')\r\nif(-not $full.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or $full.Substring($prefix.Length) -notmatch '^[a-f0-9]{32}$'){exit 1}\r\nfor($i=0;$i -lt 1200;$i++){$p=Get-Process -Id $ProcessId -ErrorAction SilentlyContinue;if(-not $p -or $p.StartTime.ToUniversalTime().Ticks -ne $Ticks){break};Start-Sleep -Milliseconds 100}\r\nif($p -and -not $p.HasExited){exit 1}\r\nforeach($item in Get-ChildItem -LiteralPath $full -Recurse -Force){if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){exit 1}}\r\nRemove-Item -LiteralPath $full -Recurse -Force\r\n";File.WriteAllText(script,body,new UTF8Encoding(true));string ps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");using(var p=Process.Start(new ProcessStartInfo(ps,"-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy RemoteSigned -File "+Quote(script)+" -ProcessId "+Process.GetCurrentProcess().Id+" -Ticks "+Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks+" -Folder "+Quote(folder)){UseShellExecute=false,CreateNoWindow=true})){}
    }
    [STAThread]static int Main(string[] args){
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==3&&args[0]=="--worker")return Worker(args[1],args[2]);
        if(args.Length==2&&args[0]=="--verify-only"){string stage=NewTemp("CodexBackgroundSetup-");try{ExtractAndVerify(stage,delegate{});File.WriteAllText(args[1],Json.Serialize(new{pass=true,version=SetupPayload.Version,baseVersion="1.5.4",msiWrapped=false}));return 0;}finally{RemoveTemp(stage,"CodexBackgroundSetup-");}}
        bool uninstall=args.Length>=2&&args[0]=="--uninstall";string root=uninstall?Path.GetFullPath(args[1]):Convert.ToString(Registry.GetValue(@"HKEY_CURRENT_USER\Software\CodexLiveBackground","InstallRoot",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"Programs\CodexLiveBackground")));
        if(!uninstall&&Path.GetFileName(Application.ExecutablePath).StartsWith("卸载")){uninstall=true;root=AppDomain.CurrentDomain.BaseDirectory;}
        bool temporary=args.Length>=3&&args[2]=="--relocated";
        if(uninstall&&!temporary&&Application.ExecutablePath.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)){string folder=NewTemp("CodexBackgroundUninstall-");string copy=Path.Combine(folder,"Uninstall.exe");File.Copy(Application.ExecutablePath,copy);using(var p=Process.Start(new ProcessStartInfo(copy,"--uninstall "+Quote(root)+" --relocated"){UseShellExecute=false})){}return 0;}
        bool owns=false;using(var mutex=new Mutex(false,"Local\\CodexLiveBackground.Setup."+WindowsIdentity.GetCurrent().User.Value)){try{try{owns=mutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}if(!owns){MessageBox.Show("另一个安装或卸载窗口正在运行。","CodexHub");return 1;}using(var form=new Setup(uninstall,root,temporary)){if(args.Length==2&&args[0]=="--smoke-test"){form.Show();Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(args[1]);}return 0;}Application.Run(form);return 0;}}finally{if(owns)mutex.ReleaseMutex();}}
    }
}
