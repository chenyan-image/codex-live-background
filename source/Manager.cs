using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("Codex 动态背景")]
[assembly: AssemblyProduct("Codex 动态背景")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

internal sealed class Manager : Form {
    readonly TextBox media = new TextBox();
    readonly TextBox log = new TextBox();
    readonly NumericUpDown opacity = new NumericUpDown();
    readonly FlowLayoutPanel commands = new FlowLayoutPanel();
    bool busy;
    readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    Manager(bool checkStatus) {
        Text = "Codex 动态背景 · 1.1.0";
        Font = new Font("Microsoft YaHei UI", 10F);
        BackColor = Color.FromArgb(245,247,250);
        ClientSize = new Size(780, 490);
        MinimumSize = new Size(796, 529);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(24), ColumnCount=1, RowCount=7 };
        foreach (int height in new [] {42, 64, 46, 42, 52}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text="Codex 动态背景", Font=new Font(Font.FontFamily,18,FontStyle.Bold), Dock=DockStyle.Fill },0,0);
        layout.Controls.Add(new Label { Text="装载服务：启用登录自启动和背景自动恢复。\r\n换壁纸：选好 MP4 / GIF / 图片后立即应用，关闭此工具后服务继续运行。", Dock=DockStyle.Fill },0,1);
        media.Dock=DockStyle.Fill; media.ReadOnly=true; media.Margin=new Padding(0,7,0,0);
        layout.Controls.Add(media,0,2);
        var settings = new FlowLayoutPanel { Dock=DockStyle.Fill };
        settings.Controls.Add(new Label { Text="界面不透明度", AutoSize=true, Margin=new Padding(0,7,10,0) });
        opacity.Minimum=10; opacity.Maximum=100; opacity.Value=72; opacity.Width=72;
        settings.Controls.Add(opacity);
        settings.Controls.Add(new Label { Text="%　支持 MP4 / GIF / 图片，最大 128 MiB", AutoSize=true, Margin=new Padding(8,7,0,0) });
        layout.Controls.Add(settings,0,3);
        commands.Dock=DockStyle.Fill;
        commands.Controls.Add(MakeButton("装载服务", delegate { Run("Install"); }));
        var apply=MakeButton("换壁纸", delegate { ChooseAndApply(); });
        apply.BackColor=Color.FromArgb(35,90,210); apply.ForeColor=Color.White; commands.Controls.Add(apply);
        layout.Controls.Add(commands,0,4);
        log.Multiline=true; log.ReadOnly=true; log.ScrollBars=ScrollBars.Vertical; log.Dock=DockStyle.Fill;
        log.BackColor=Color.White; log.Text="准备就绪。首次使用请点击装载服务，然后点击换壁纸。";
        layout.Controls.Add(log,0,5);
        var footer = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, RowCount=1, Margin=Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,32));
        footer.Controls.Add(new Label { Text="Windows 10/11 x64 · 官方 Codex 稳定版 · 本地处理 · 非官方定制工具", ForeColor=Color.DimGray, Dock=DockStyle.Fill, TextAlign=ContentAlignment.MiddleLeft, Margin=Padding.Empty },0,0);
        footer.Controls.Add(new Label { Text="yz", Font=new Font(Font.FontFamily,8F), ForeColor=Color.Gray, Dock=DockStyle.Fill, TextAlign=ContentAlignment.MiddleRight, Margin=Padding.Empty },1,0);
        layout.Controls.Add(footer,0,6);
        if (checkStatus) Shown += delegate { Run("Status"); };
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel=true; MessageBox.Show(this,"当前操作尚未结束，请等待完成。",Text); } };
    }
    Button MakeButton(string title, EventHandler action) {
        var b = new Button { Text=title, Size=new Size(162,38), FlatStyle=FlatStyle.Flat, BackColor=Color.White, Margin=new Padding(0,0,10,8) };
        b.FlatAppearance.BorderColor=Color.FromArgb(205,212,224); b.Click += action; return b;
    }
    void ChooseAndApply() {
        if (busy) return;
        using (var dialog=new OpenFileDialog { Title="选择壁纸并应用", Filter="支持的素材|*.mp4;*.gif;*.png;*.jpg;*.jpeg;*.webp|MP4 视频|*.mp4|GIF 动图|*.gif|图片|*.png;*.jpg;*.jpeg;*.webp", CheckFileExists=true }) {
            if (dialog.ShowDialog(this)!=DialogResult.OK) return;
            media.Text=dialog.FileName;
        }
        Run("Apply");
    }
    static string Quote(string value) {
        var b=new StringBuilder("\""); int slashes=0;
        foreach(char c in value) { if(c=='\\') { slashes++; continue; } if(c=='"') b.Append('\\',slashes*2+1).Append(c); else b.Append('\\',slashes).Append(c); slashes=0; }
        return b.Append('\\',slashes*2).Append('"').ToString();
    }
    void Run(string action) {
        if (busy) return;
        busy=true; commands.Enabled=false; opacity.Enabled=false;
        string chosen=media.Text;
        string alpha=(opacity.Value/100M).ToString(System.Globalization.CultureInfo.InvariantCulture);
        log.Text=action=="Install"?"正在装载服务并设置自启动，请稍候…":action=="Apply"?"正在更换壁纸，请稍候…":"正在检查服务与自启动状态…";
        ThreadPool.QueueUserWorkItem(delegate {
            string result; bool success=false;
            try {
                var ps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
                var args="-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "+Quote(Path.Combine(root,"control.ps1"))+" -Action "+action;
                if (action=="Apply") args+=" -Media "+Quote(chosen)+" -Opacity "+alpha;
                var info=new ProcessStartInfo(ps,args) { UseShellExecute=false, CreateNoWindow=true, WorkingDirectory=root, RedirectStandardOutput=true, RedirectStandardError=true, StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8 };
                using(var p=Process.Start(info)) {
                    Task<string> stdout=p.StandardOutput.ReadToEndAsync(), stderr=p.StandardError.ReadToEndAsync();
                    p.WaitForExit(); Task.WaitAll(stdout,stderr);
                    success=p.ExitCode==0;
                    result=(stdout.Result+Environment.NewLine+stderr.Result).Trim();
                    if(result.Length==0) result=success?"操作完成。":"操作失败，请查看使用说明中的排错方法。";
                }
            } catch(Exception error) { result=error.Message; }
            if(IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate { log.Text=(success?"✓ ":"操作未完成：")+result; busy=false; commands.Enabled=true; opacity.Enabled=true; });
        });
    }
    [STAThread] static int Main(string[] args) {
        SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        bool smokeTest=args.Length==2 && args[0]=="--smoke-test";
        using(var form=new Manager(!smokeTest)) {
            if(smokeTest) {
                form.Show(); Application.DoEvents();
                using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height)); bitmap.Save(args[1]); }
                return form.commands.Controls.Count==2?0:1;
            }
            Application.Run(form);
        }
        return 0;
    }
}
