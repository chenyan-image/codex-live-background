using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class Manager : Form {
    readonly TextBox media = new TextBox();
    readonly TextBox log = new TextBox();
    readonly NumericUpDown opacity = new NumericUpDown();
    readonly FlowLayoutPanel commands = new FlowLayoutPanel();
    bool busy;
    readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    Manager() {
        Text = "Codex 动态背景 · 1.0.2";
        Font = new Font("Microsoft YaHei UI", 10F);
        BackColor = Color.FromArgb(245,247,250);
        ClientSize = new Size(780, 555);
        MinimumSize = new Size(796, 594);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(24), ColumnCount=1, RowCount=7 };
        foreach (int height in new [] {42, 58, 46, 42, 100}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text="给 Codex 换一个动态背景", Font=new Font(Font.FontFamily,18,FontStyle.Bold), Dock=DockStyle.Fill },0,0);
        layout.Controls.Add(new Label { Text="首次使用：安装 / 启用 → 手动退出原 Codex → 打开 Codex → 选择并应用。\r\n以后更换素材直接应用，关闭此工具后背景和自动恢复继续运行。", Dock=DockStyle.Fill },0,1);
        var picker = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, RowCount=1 };
        picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        picker.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,115));
        media.Dock=DockStyle.Fill; media.ReadOnly=true; media.Margin=new Padding(0,7,10,0);
        picker.Controls.Add(media,0,0);
        var browse=MakeButton("选择素材…", delegate { Choose(); }); browse.Dock=DockStyle.Fill;
        picker.Controls.Add(browse,1,0); layout.Controls.Add(picker,0,2);
        var settings = new FlowLayoutPanel { Dock=DockStyle.Fill };
        settings.Controls.Add(new Label { Text="界面不透明度", AutoSize=true, Margin=new Padding(0,7,10,0) });
        opacity.Minimum=10; opacity.Maximum=100; opacity.Value=72; opacity.Width=72;
        settings.Controls.Add(opacity);
        settings.Controls.Add(new Label { Text="%　支持 MP4 / GIF / 图片，最大 128 MiB", AutoSize=true, Margin=new Padding(8,7,0,0) });
        layout.Controls.Add(settings,0,3);
        commands.Dock=DockStyle.Fill;
        commands.Controls.Add(MakeButton("安装 / 启用", delegate { Run("Install"); }));
        commands.Controls.Add(MakeButton("打开 Codex", delegate { Run("Launch"); }));
        var apply=MakeButton("应用背景", delegate { if (media.Text.Length==0) { Choose(); return; } Run("Apply"); });
        apply.BackColor=Color.FromArgb(35,90,210); apply.ForeColor=Color.White; commands.Controls.Add(apply);
        commands.Controls.Add(MakeButton("查看状态", delegate { Run("Status"); }));
        commands.Controls.Add(MakeButton("暂停背景", delegate { Run("Pause"); }));
        commands.Controls.Add(MakeButton("恢复背景", delegate { Run("Resume"); }));
        commands.Controls.Add(MakeButton("停用 / 卸载", delegate {
            if (MessageBox.Show(this,"停用背景并移除自动启动设置？素材和登录资料会保留。",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes) Run("Remove");
        }));
        commands.Controls.Add(MakeButton("使用说明", delegate { Process.Start(new ProcessStartInfo(Path.Combine(root,"使用说明.txt")) { UseShellExecute=true }); }));
        layout.Controls.Add(commands,0,4);
        log.Multiline=true; log.ReadOnly=true; log.ScrollBars=ScrollBars.Vertical; log.Dock=DockStyle.Fill;
        log.BackColor=Color.White; log.Text="准备就绪。请先安装 / 启用。\r\n首次使用独立背景配置时可能需要重新登录 Codex。";
        layout.Controls.Add(log,0,5);
        layout.Controls.Add(new Label { Text="Windows 10/11 x64 · 官方 Codex 稳定版 · 本地处理 · 非官方定制工具", ForeColor=Color.DimGray, Dock=DockStyle.Fill, TextAlign=ContentAlignment.MiddleLeft },0,6);
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel=true; MessageBox.Show(this,"当前操作尚未结束，请等待完成。",Text); } };
    }
    Button MakeButton(string title, EventHandler action) {
        var b = new Button { Text=title, Size=new Size(162,38), FlatStyle=FlatStyle.Flat, BackColor=Color.White, Margin=new Padding(0,0,10,8) };
        b.FlatAppearance.BorderColor=Color.FromArgb(205,212,224); b.Click += action; return b;
    }
    void Choose() {
        using (var dialog=new OpenFileDialog { Title="选择动态背景", Filter="支持的素材|*.mp4;*.gif;*.png;*.jpg;*.jpeg;*.webp|MP4 视频|*.mp4|GIF 动图|*.gif", CheckFileExists=true }) {
            if (dialog.ShowDialog(this)==DialogResult.OK) media.Text=dialog.FileName;
        }
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
        log.Text="正在执行，请稍候…";
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
        using(var form=new Manager()) {
            if(args.Length==2 && args[0]=="--smoke-test") {
                form.Show(); Application.DoEvents();
                using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height)); bitmap.Save(args[1]); }
                return form.commands.Controls.Count==8?0:1;
            }
            Application.Run(form);
        }
        return 0;
    }
}
