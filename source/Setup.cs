using System;
using System.IO;
using System.IO.Compression;
using System.Drawing;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Reflection;
using System.Threading;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class Setup : Form {
    readonly Label status=new Label();readonly TextBox log=new TextBox();readonly ProgressBar progress=new ProgressBar();readonly ActionButton install=new ActionButton();bool busy,completed;string testReport;int resultCode;
    Setup(){
        DoubleBuffered=true;Text="安装 Codex 动态背景 · "+SetupPayload.Version;BackColor=Color.FromArgb(14,21,25);ForeColor=Color.FromArgb(231,240,238);Font=new Font("Microsoft YaHei UI",10);ClientSize=new Size(660,478);FormBorderStyle=FormBorderStyle.None;Padding=new Padding(1);MinimumSize=MaximumSize=Size;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
        using(var bitmap=IconAssets.Create(64)){IntPtr handle=bitmap.GetHicon();try{Icon=(Icon)Icon.FromHandle(handle).Clone();}finally{DestroyIcon(handle);}}
        var shell=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};shell.RowStyles.Add(new RowStyle(SizeType.Absolute,38));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(shell);
        var bar=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};var close=new Button{Text="×",Dock=DockStyle.Right,Width=46,FlatStyle=FlatStyle.Flat,ForeColor=ForeColor,Font=new Font("Segoe UI",13)};close.FlatAppearance.BorderSize=0;close.Click+=delegate{Close();};bar.Controls.Add(close);
        var title=new Label{Text="Codex 动态背景 · 安装程序",Dock=DockStyle.Fill,Padding=new Padding(14,0,0,0),TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.FromArgb(151,176,175)};title.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}};bar.Controls.Add(title);close.BringToFront();shell.Controls.Add(bar,0,0);
        var layout=new BufferedTable{Dock=DockStyle.Fill,Padding=new Padding(28,18,28,28),ColumnCount=1,RowCount=6,Margin=Padding.Empty};shell.Controls.Add(layout,0,1);
        foreach(int height in new[]{54,58,32})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        layout.Controls.Add(new Label{Text="安装 Codex 动态背景",Font=new Font(Font.FontFamily,21,FontStyle.Bold),Dock=DockStyle.Fill},0,0);
        layout.Controls.Add(new Label{Text="自动检测旧版本并迁移更新。\r\n保留壁纸和登录资料，完成后自动打开管理器。",ForeColor=Color.FromArgb(151,176,175),Dock=DockStyle.Fill},0,1);
        status.Text="准备安装 · v"+SetupPayload.Version;status.Dock=DockStyle.Fill;status.ForeColor=Color.FromArgb(135,235,194);layout.Controls.Add(status,0,2);
        log.Multiline=true;log.ReadOnly=true;log.BorderStyle=BorderStyle.None;log.BackColor=Color.FromArgb(23,31,35);log.ForeColor=Color.FromArgb(176,204,197);log.Dock=DockStyle.Fill;log.ScrollBars=ScrollBars.Vertical;layout.Controls.Add(log,0,3);
        progress.Dock=DockStyle.Fill;progress.Height=5;progress.Margin=new Padding(0,12,0,10);layout.Controls.Add(progress,0,4);
        install.Text="安装 / 更新";install.Primary=true;install.Dock=DockStyle.Right;install.Click+=delegate{if(completed){Close();return;}if(!busy)BeginInstall();};layout.Controls.Add(install,0,5);
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy){e.Cancel=true;MessageBox.Show(this,"安装正在进行，请等待完成。",Text);}};
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr w,IntPtr l);
    protected override CreateParams CreateParams {get{var value=base.CreateParams;value.ExStyle|=0x02000000;return value;}}
    void Update(string message,int percent){if(IsDisposed)return;BeginInvoke((MethodInvoker)delegate{status.Text=message;progress.Value=Math.Max(0,Math.Min(100,percent));});}
    void Append(string text){if(IsDisposed)return;BeginInvoke((MethodInvoker)delegate{log.AppendText(text+Environment.NewLine);});}
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
    void RunStep(string stage,string action){
        string ps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
        string args="-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \""+Path.Combine(stage,"control.ps1")+"\" -Action "+action;
        var info=new ProcessStartInfo(ps,args){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=stage,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        using(var process=Process.Start(info)){
            var errors=new StringBuilder();process.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null)Append(e.Data);};process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null)lock(errors)errors.AppendLine(e.Data);};process.BeginOutputReadLine();process.BeginErrorReadLine();if(!process.WaitForExit(300000)){process.Kill();throw new Exception("安装步骤超时，请重试。");}process.WaitForExit();if(process.ExitCode!=0)lock(errors)throw new Exception(errors.Length>0?errors.ToString().Trim():"安装操作未完成，请查看日志。");
        }
    }
    void BeginInstall(){
        busy=true;install.Enabled=false;ThreadPool.QueueUserWorkItem(delegate{
            string stage=Path.Combine(Path.GetTempPath(),"CodexBackgroundSetup-"+Guid.NewGuid().ToString("N"));bool preparing=false,success=false;string failure=null;
            try{
                Directory.CreateDirectory(stage);ExtractAndVerify(stage,Update);Update("正在检查 Codex 和运行环境…",30);RunStep(stage,"Check");
                preparing=true;Update("正在检测并迁移旧版本…",45);RunStep(stage,"PrepareUpgrade");Update("正在安装并启用背景服务…",65);RunStep(stage,"Install");
                string app=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"Programs\CodexLiveBackground\CodexLiveBackground.exe");
                if(testReport==null)Process.Start(new ProcessStartInfo(app){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(app)});success=true;Update("安装完成，背景服务已启用。",100);
            }catch(Exception error){
                if(preparing)try{Append("正在恢复旧安装…");RunStep(stage,"RollbackUpgrade");}catch(Exception rollback){Append("恢复信息："+rollback.Message);}
                failure=error.Message;Append(error.Message);Update("安装未完成，可重试；个人资料保留。",0);
            }finally{
                // The exact temporary directory was created above; never use user-supplied cleanup paths.
                string prefix=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar+"CodexBackgroundSetup-";
                if(stage.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(stage))try{Directory.Delete(stage,true);}catch{}
            }
            BeginInvoke((MethodInvoker)delegate{busy=false;completed=success;install.Text=success?"完成":"重试安装";install.Enabled=true;if(testReport!=null){resultCode=success?0:1;File.WriteAllText(testReport,new JavaScriptSerializer().Serialize(new{pass=success,error=failure,version=SetupPayload.Version,log=log.Text}));Close();}});
        });
    }
    [STAThread]static int Main(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        bool test=args.Length==2&&args[0].StartsWith("--");bool owns=false;using(var mutex=new Mutex(false,"Local\\CodexLiveBackground.Setup."+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value)){
        try{try{owns=mutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}if(!owns){if(!test)MessageBox.Show("安装程序已在运行，请返回原窗口。","Codex 动态背景");return 1;}
        if(args.Length==2&&args[0]=="--verify-only"){
            string stage=Path.Combine(Path.GetTempPath(),"CodexBackgroundSetup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            try{ExtractAndVerify(stage,delegate{});File.WriteAllText(args[1],"{\"pass\":true,\"payloadVerified\":true}");return 0;}finally{Directory.Delete(stage,true);}
        }
        using(var form=new Setup()){
            if(args.Length==2&&args[0]=="--smoke-test"){form.Show();Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(args[1]);}return 0;}
            if(args.Length==2&&args[0]=="--install-test"){form.testReport=args[1];form.Shown+=delegate{form.BeginInstall();};}
            Application.Run(form);return form.resultCode;
        }}catch(Exception error){if(test)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{pass=false,error=error.ToString()}));else MessageBox.Show(error.Message,"Codex 动态背景");return 1;}
        finally{if(owns)mutex.ReleaseMutex();}
    }}
}
