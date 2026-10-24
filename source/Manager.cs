using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Windows.Forms;

[assembly: AssemblyTitle("Codex 动态背景")]
[assembly: AssemblyProduct("Codex 动态背景")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

internal sealed class Surface : Panel {
    public Surface(){DoubleBuffered=true; BackColor=Color.FromArgb(23,31,35); Padding=new Padding(20);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var p=IconAssets.Round(new RectangleF(.5F,.5F,Width-1,Height-1),14))using(var pen=new Pen(Color.FromArgb(47,59,62)))e.Graphics.DrawPath(pen,p);}
}
internal sealed class ActionButton : Button {
    bool hover; public bool Primary; public bool Destructive;
    public ActionButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;Size=new Size(170,44);Margin=new Padding(0,0,12,0);}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.Clear(Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Color fill=Primary?Color.FromArgb(135,235,194):hover?Color.FromArgb(45,59,62):Color.FromArgb(30,40,44);
        Color text=Primary?Color.FromArgb(12,43,35):Destructive?Color.FromArgb(242,163,158):Color.FromArgb(222,232,232);
        if(!Enabled){fill=Color.FromArgb(35,44,47);text=Color.FromArgb(105,123,125);}
        using(var p=IconAssets.Round(new RectangleF(1,1,Width-2,Height-2),10))using(var brush=new SolidBrush(fill))using(var pen=new Pen(Primary?fill:Color.FromArgb(62,78,80))){e.Graphics.FillPath(brush,p);e.Graphics.DrawPath(pen,p);}
        TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        if(Focused&&ShowFocusCues)using(var ring=IconAssets.Round(new RectangleF(5,5,Width-10,Height-10),6))using(var pen=new Pen(Primary?Color.FromArgb(31,111,86):Color.FromArgb(135,235,194)))e.Graphics.DrawPath(pen,ring);
    }
}
internal sealed class OpacitySlider : TrackBar {
    public OpacitySlider(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnValueChanged(EventArgs e){base.OnValueChanged(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;float x=12+(Width-24)*(Value-Minimum)/(float)(Maximum-Minimum),y=Height/2F;
        using(var track=new Pen(Color.FromArgb(55,77,79),4)){track.StartCap=track.EndCap=LineCap.Round;g.DrawLine(track,12,y,Width-12,y);}
        using(var filled=new Pen(Color.FromArgb(116,217,179),4)){filled.StartCap=filled.EndCap=LineCap.Round;g.DrawLine(filled,12,y,x,y);}
        using(var knob=new SolidBrush(Color.FromArgb(208,255,234)))g.FillEllipse(knob,x-6,y-6,12,12);
        if(Focused)using(var ring=new Pen(Color.FromArgb(135,235,194)))g.DrawEllipse(ring,x-10,y-10,20,20);
    }
}
internal sealed class WallpaperPreview : Control {
    Image picture;
    public WallpaperPreview(){DoubleBuffered=true;}
    public void SetMedia(string file){
        if(picture!=null){picture.Dispose();picture=null;}
        try{if(File.Exists(file)&&!new[]{".mp4",".webm"}.ContainsExtension(file))using(var source=Image.FromFile(file))picture=new Bitmap(source);}catch{}
        Invalidate();
    }
    protected override void Dispose(bool disposing){if(disposing&&picture!=null)picture.Dispose();base.Dispose(disposing);}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var clip=IconAssets.Round(new RectangleF(0,0,Width-1,Height-1),12)){
            g.SetClip(clip);
            if(picture!=null){float scale=Math.Max(Width/(float)picture.Width,Height/(float)picture.Height);float w=picture.Width*scale,h=picture.Height*scale;g.DrawImage(picture,(Width-w)/2,(Height-h)/2,w,h);}
            else{
                using(var b=new LinearGradientBrush(ClientRectangle,Color.FromArgb(45,89,91),Color.FromArgb(15,36,44),55F))g.FillRectangle(b,ClientRectangle);
                using(var b=new SolidBrush(Color.FromArgb(124,210,179)))g.FillEllipse(b,Width*.65F,Height*.17F,30,30);
                using(var p=new GraphicsPath())using(var b=new SolidBrush(Color.FromArgb(33,88,84))){p.AddBezier(0,Height*.8F,Width*.26F,Height*.15F,Width*.5F,Height*.9F,Width,Height*.3F);p.AddLine(Width,Height*.3F,Width,Height);p.AddLine(Width,Height,0,Height);p.CloseFigure();g.FillPath(b,p);}
                using(var p=new GraphicsPath())using(var b=new SolidBrush(Color.FromArgb(17,56,58))){p.AddBezier(0,Height*.65F,Width*.4F,Height*1.2F,Width*.5F,Height*.3F,Width,Height*.75F);p.AddLine(Width,Height*.75F,Width,Height);p.AddLine(Width,Height,0,Height);p.CloseFigure();g.FillPath(b,p);}
                using(var f=new Font("Segoe UI",8F,FontStyle.Bold))using(var b=new SolidBrush(Color.FromArgb(163,215,198)))g.DrawString("LOCAL WALLPAPER",f,b,14,Height-27);
            }
            g.ResetClip();
        }
    }
}
internal static class Extensions {public static bool ContainsExtension(this string[] extensions,string file){return Array.IndexOf(extensions,Path.GetExtension(file).ToLowerInvariant())>=0;}}

internal sealed class Manager : Form {
    readonly TextBox media=new TextBox();
    readonly TextBox log=new TextBox();
    readonly TrackBar opacity=new OpacitySlider();
    readonly Label percentage=new Label(),status=new Label(),wallpaperName=new Label();
    readonly FlowLayoutPanel commands=new FlowLayoutPanel();
    readonly ProgressBar progress=new ProgressBar();
    readonly WallpaperPreview preview=new WallpaperPreview();
    bool busy;
    readonly string root=AppDomain.CurrentDomain.BaseDirectory;
    static readonly Color Muted=Color.FromArgb(146,166,169),Ink=Color.FromArgb(231,240,238),Mint=Color.FromArgb(135,235,194);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    Label Caption(string text,float size,Color color){return new Label{Text=text,Font=new Font("Microsoft YaHei UI",size),ForeColor=color,AutoSize=false,Dock=DockStyle.Fill,Margin=Padding.Empty,TextAlign=ContentAlignment.MiddleLeft};}
    Manager(bool checkStatus){
        Text="Codex 动态背景 · 1.2.0";Font=new Font("Microsoft YaHei UI",10F);ForeColor=Ink;BackColor=Color.FromArgb(14,21,25);
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;FormBorderStyle=FormBorderStyle.None;Padding=new Padding(1);ClientSize=new Size(860,694);MinimumSize=new Size(820,675);StartPosition=FormStartPosition.CenterScreen;
        try{Icon=new Icon(Path.Combine(root,"CodexBackground.ico"),32,32);}catch{try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}}
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(28,20,28,12),ColumnCount=1,RowCount=8};
        foreach(int h in new[]{72,35,185,78,62,25})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,33));Controls.Add(layout);
        var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,62));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));
        header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        header.Controls.Add(new PictureBox{Image=IconAssets.Create(48),SizeMode=PictureBoxSizeMode.CenterImage,Dock=DockStyle.Fill,Margin=new Padding(0,0,14,0)},0,0);
        var brand=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Margin=Padding.Empty};brand.RowStyles.Add(new RowStyle(SizeType.Absolute,43));brand.Controls.Add(Caption("Codex 动态背景",21,Ink),0,0);brand.Controls.Add(Caption("让每一次打开，都有喜欢的风景。",9,Muted),0,1);header.Controls.Add(brand,1,0);
        brand.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        status.Text="●  准备就绪";status.Font=new Font(Font.FontFamily,9);status.ForeColor=Mint;status.TextAlign=ContentAlignment.MiddleRight;status.Dock=DockStyle.Fill;header.Controls.Add(status,2,0);layout.Controls.Add(header,0,0);
        layout.Controls.Add(Caption("你的背景  /  WALLPAPER",9,Muted),0,1);
        var card=new Surface{Dock=DockStyle.Fill,Margin=Padding.Empty};var cardLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};cardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,66));cardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,34));card.Controls.Add(cardLayout);
        var info=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=new Padding(0,0,22,0)};foreach(int h in new[]{25,43,45})info.RowStyles.Add(new RowStyle(SizeType.Absolute,h));info.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        info.Controls.Add(Caption("本地素材 · 静音循环",9,Mint),0,0);wallpaperName.Text="选择一张，换个心情";wallpaperName.Font=new Font(Font.FontFamily,17,FontStyle.Bold);wallpaperName.AutoEllipsis=true;wallpaperName.Dock=DockStyle.Fill;wallpaperName.TextAlign=ContentAlignment.MiddleLeft;info.Controls.Add(wallpaperName,0,1);
        media.ReadOnly=true;media.BorderStyle=BorderStyle.None;media.Multiline=true;media.BackColor=card.BackColor;media.ForeColor=Muted;media.Font=new Font(Font.FontFamily,8.5F);media.Dock=DockStyle.Fill;media.Text="点击「换壁纸」选择视频、GIF 或图片";info.Controls.Add(media,0,2);info.Controls.Add(Caption("4K 原画质优先  ·  不兼容时自动适配",9,Muted),0,3);cardLayout.Controls.Add(info,0,0);preview.Dock=DockStyle.Fill;preview.Margin=Padding.Empty;cardLayout.Controls.Add(preview,1,0);layout.Controls.Add(card,0,2);
        var settings=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=2,Margin=new Padding(0,12,0,8)};settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,123));settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,58));settings.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
        settings.Controls.Add(Caption("界面不透明度",10,Ink),0,0);opacity.Minimum=10;opacity.Maximum=100;opacity.Value=72;opacity.TickStyle=TickStyle.None;opacity.AutoSize=false;opacity.Dock=DockStyle.Fill;opacity.Margin=Padding.Empty;opacity.BackColor=BackColor;opacity.ValueChanged+=delegate{percentage.Text=opacity.Value+"%";};settings.Controls.Add(opacity,1,0);percentage.Text="72%";percentage.ForeColor=Mint;percentage.Dock=DockStyle.Fill;percentage.TextAlign=ContentAlignment.MiddleRight;settings.Controls.Add(percentage,2,0);var hint=Caption("数值越低，背景越通透。视频最大 2 GiB，图片最大 512 MiB。",8.5F,Muted);settings.Controls.Add(hint,0,1);settings.SetColumnSpan(hint,3);layout.Controls.Add(settings,0,3);
        commands.Dock=DockStyle.Fill;commands.Margin=new Padding(0,7,0,7);commands.WrapContents=false;
        commands.Controls.Add(MakeButton("换壁纸",true,false,delegate{ChooseAndApply();}));commands.Controls.Add(MakeButton("装载服务",false,false,delegate{Run("Install");}));commands.Controls.Add(MakeButton("卸载皮肤",false,true,delegate{ConfirmRemoval();}));layout.Controls.Add(commands,0,4);
        layout.Controls.Add(Caption("运行记录",9,Muted),0,5);
        var logCard=new Surface{Dock=DockStyle.Fill,Padding=new Padding(14,9,14,9),Margin=Padding.Empty};log.Multiline=true;log.ReadOnly=true;log.BorderStyle=BorderStyle.None;log.ScrollBars=ScrollBars.None;log.Dock=DockStyle.Fill;log.Font=new Font(Font.FontFamily,9);log.ForeColor=Color.FromArgb(179,201,198);log.BackColor=logCard.BackColor;log.Text="准备就绪。首次使用先装载服务，再换壁纸。";log.TextChanged+=delegate{if(log.IsHandleCreated)log.ScrollBars=(log.GetLineFromCharIndex(log.TextLength)+1)*log.Font.Height>log.ClientSize.Height?ScrollBars.Vertical:ScrollBars.None;};logCard.Controls.Add(log);progress.Dock=DockStyle.Bottom;progress.Height=2;progress.Visible=false;progress.Style=ProgressBarStyle.Marquee;logCard.Controls.Add(progress);layout.Controls.Add(logCard,0,6);
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,28));footer.Controls.Add(Caption("v1.2.0  ·  本地处理  ·  关闭管理器后，背景服务继续运行",8,Muted),0,0);var signature=Caption("yz",8,Color.FromArgb(104,126,129));signature.TextAlign=ContentAlignment.MiddleRight;footer.Controls.Add(signature,1,0);layout.Controls.Add(footer,0,7);
        AddTitleBar();
        if(checkStatus){LoadSavedTheme();Shown+=delegate{Run("Status");};}
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy){e.Cancel=true;MessageBox.Show(this,"当前操作尚未结束，请等待完成。",Text);}};
    }
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);try{int dark=1;DwmSetWindowAttribute(Handle,20,ref dark,4);}catch{}}
    void ToggleMaximize(){MaximizedBounds=Screen.FromHandle(Handle).WorkingArea;WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
    void AddTitleBar(){
        var bar=new Panel{Dock=DockStyle.Top,Height=38,BackColor=Color.FromArgb(14,21,25)};
        var controls=new FlowLayoutPanel{Dock=DockStyle.Right,Width=138,FlowDirection=FlowDirection.LeftToRight,WrapContents=false,Margin=Padding.Empty};
        foreach(string symbol in new[]{"—","□","×"}){
            var b=new Button{Text=symbol,Size=new Size(46,36),Margin=Padding.Empty,FlatStyle=FlatStyle.Flat,TabStop=false,ForeColor=Muted,BackColor=bar.BackColor,Font=new Font("Segoe UI",12),AccessibleName=symbol=="—"?"最小化":symbol=="□"?"最大化或还原":"关闭"};
            b.FlatAppearance.BorderSize=0;b.FlatAppearance.MouseOverBackColor=symbol=="×"?Color.FromArgb(144,51,57):Color.FromArgb(39,52,57);
            if(symbol=="—")b.Click+=delegate{WindowState=FormWindowState.Minimized;};else if(symbol=="□")b.Click+=delegate{ToggleMaximize();};else b.Click+=delegate{Close();};controls.Controls.Add(b);
        }
        var title=new Label{Text="Codex 动态背景",Font=new Font("Microsoft YaHei UI",8.5F),ForeColor=Muted,TextAlign=ContentAlignment.MiddleLeft,Dock=DockStyle.Fill,Padding=new Padding(15,0,0,0)};
        MouseEventHandler drag=delegate(object sender,MouseEventArgs e){if(e.Button!=MouseButtons.Left)return;if(e.Clicks==2){ToggleMaximize();return;}ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);};title.MouseDown+=drag;bar.MouseDown+=drag;
        bar.Controls.Add(title);bar.Controls.Add(controls);
        var content=Controls[0];Controls.Remove(content);
        var shell=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Margin=Padding.Empty,Padding=Padding.Empty};shell.RowStyles.Add(new RowStyle(SizeType.Absolute,38));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        bar.Dock=DockStyle.Fill;bar.Margin=Padding.Empty;shell.Controls.Add(bar,0,0);shell.Controls.Add(content,0,1);Controls.Add(shell);
        Shown+=delegate{ActiveControl=commands.Controls[0];};
    }
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var pen=new Pen(Color.FromArgb(49,65,69)))e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);}
    protected override void WndProc(ref Message m){
        base.WndProc(ref m);
        if(m.Msg==0x84&&WindowState==FormWindowState.Normal&&m.Result==(IntPtr)1){
            var point=PointToClient(new Point(unchecked((short)(long)m.LParam),unchecked((short)((long)m.LParam>>16))));int grip=6;
            bool left=point.X<grip,right=point.X>=Width-grip,top=point.Y<grip,bottom=point.Y>=Height-grip;
            if(top&&left)m.Result=(IntPtr)13;else if(top&&right)m.Result=(IntPtr)14;else if(bottom&&left)m.Result=(IntPtr)16;else if(bottom&&right)m.Result=(IntPtr)17;else if(left)m.Result=(IntPtr)10;else if(right)m.Result=(IntPtr)11;else if(top)m.Result=(IntPtr)12;else if(bottom)m.Result=(IntPtr)15;
        }
    }
    Button MakeButton(string title,bool primary,bool destructive,EventHandler action){var b=new ActionButton{Text=title,Primary=primary,Destructive=destructive,AccessibleName=title};b.Click+=action;return b;}
    void LoadSavedTheme(){
        try{
            string active=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"CodexDreamSkin\active-theme");
            var theme=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(active,"theme.json"),Encoding.UTF8));
            object value;if(theme.TryGetValue("name",out value))wallpaperName.Text=Convert.ToString(value);
            if(theme.TryGetValue("art",out value)){
                var art=value as Dictionary<string,object>;object alpha;
                if(art!=null&&art.TryGetValue("surfaceOpacity",out alpha))opacity.Value=Math.Max(opacity.Minimum,Math.Min(opacity.Maximum,(int)Math.Round(Convert.ToDouble(alpha)*100)));
            }
            if(theme.TryGetValue("image",out value)){string file=Path.GetFullPath(Path.Combine(active,Convert.ToString(value)));if(file.StartsWith(active+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){media.Text=file;preview.SetMedia(file);}}
        }catch{}
    }
    void ChooseAndApply(){
        if(busy)return;
        using(var dialog=new OpenFileDialog{Title="选择壁纸并应用",Filter="支持的素材|*.mp4;*.webm;*.gif;*.png;*.jpg;*.jpeg;*.webp|视频|*.mp4;*.webm|GIF 动图|*.gif|图片|*.png;*.jpg;*.jpeg;*.webp",CheckFileExists=true}){
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;media.Text=dialog.FileName;wallpaperName.Text=Path.GetFileNameWithoutExtension(dialog.FileName);preview.SetMedia(dialog.FileName);
        }
        Run("Apply");
    }
    void ConfirmRemoval(){
        if(busy)return;
        if(MessageBox.Show(this,"卸下当前皮肤，并关闭背景自启动和后台恢复？\r\n\r\nCodex 聊天、原始素材和登录资料会保留。以后可重新装载服务。","卸载皮肤",MessageBoxButtons.OKCancel,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.OK)Run("Remove");
    }
    static string Quote(string value){var b=new StringBuilder("\"");int slashes=0;foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='"')b.Append('\\',slashes*2+1).Append(c);else b.Append('\\',slashes).Append(c);slashes=0;}return b.Append('\\',slashes*2).Append('"').ToString();}
    void Run(string action){
        if(busy)return;busy=true;commands.Enabled=false;opacity.Enabled=false;progress.Visible=true;status.ForeColor=Mint;status.Text="●  正在处理";
        string chosen=media.Text,alpha=(opacity.Value/100M).ToString(System.Globalization.CultureInfo.InvariantCulture);
        log.Text=action=="Install"?"正在装载服务并设置自启动…":action=="Apply"?"正在检查素材并更换壁纸…":action=="Remove"?"正在卸下皮肤并关闭背景自启动…":"正在检查服务状态…";
        ThreadPool.QueueUserWorkItem(delegate{
            string result;bool success=false;
            try{
                string ps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
                string args="-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "+Quote(Path.Combine(root,"control.ps1"))+" -Action "+action;
                if(action=="Apply")args+=" -Media "+Quote(chosen)+" -Opacity "+alpha;
                var start=new ProcessStartInfo(ps,args){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
                using(var p=Process.Start(start)){
                    var stdout=new StringBuilder();var stderr=new StringBuilder();
                    p.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data==null)return;stdout.AppendLine(e.Data);if(!IsDisposed)BeginInvoke((MethodInvoker)delegate{if(busy)log.AppendText(Environment.NewLine+e.Data);});};
                    p.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null)stderr.AppendLine(e.Data);};
                    p.BeginOutputReadLine();p.BeginErrorReadLine();p.WaitForExit();success=p.ExitCode==0;result=(stdout.ToString()+Environment.NewLine+stderr.ToString()).Trim();
                    if(result.Length==0)result=success?"操作完成。":"操作失败，请查看使用说明。";
                }
            }catch(Exception error){result=error.Message;}
            if(IsDisposed)return;
            BeginInvoke((MethodInvoker)delegate{
                log.Text=(success?"✓ ":"操作未完成：")+result;busy=false;commands.Enabled=true;opacity.Enabled=true;progress.Visible=false;
                status.ForeColor=success?Mint:Color.FromArgb(242,163,158);
                status.Text=!success?"●  需要处理":action=="Remove"?"○  皮肤已卸载":action=="Status"&&result.Contains("未装载")?"○  等待装载":"●  已就绪";
                if(success&&action=="Apply")LoadSavedTheme();
                if(success&&action=="Remove"){wallpaperName.Text="皮肤已卸载";media.Text="背景自启动已关闭，素材和登录资料已保留";preview.SetMedia("");}
            });
        });
    }
    static Button FindButton(Control parent,string name){foreach(Control child in parent.Controls){var button=child as Button;if(button!=null&&button.AccessibleName==name)return button;var nested=FindButton(child,name);if(nested!=null)return nested;}return null;}
    int TestChrome(string reportPath){
        Show();Application.DoEvents();var minimize=FindButton(this,"最小化");var maximize=FindButton(this,"最大化或还原");var close=FindButton(this,"关闭");
        if(minimize==null||maximize==null||close==null)throw new Exception("Window controls missing");
        minimize.PerformClick();Application.DoEvents();if(WindowState!=FormWindowState.Minimized)throw new Exception("Minimize failed");WindowState=FormWindowState.Normal;
        maximize.PerformClick();Application.DoEvents();if(WindowState!=FormWindowState.Maximized)throw new Exception("Maximize failed");maximize.PerformClick();Application.DoEvents();if(WindowState!=FormWindowState.Normal)throw new Exception("Restore failed");
        int before=opacity.Value;opacity.Value=before-10;if(percentage.Text!=(before-10)+"%")throw new Exception("Opacity feedback failed");
        int edge=SendMessage(Handle,0x84,IntPtr.Zero,(IntPtr)((PointToScreen(new Point(1,1)).Y<<16)|(PointToScreen(new Point(1,1)).X&0xffff))).ToInt32();if(edge!=13)throw new Exception("Resize hit test failed");
        if(FormBorderStyle!=FormBorderStyle.None||commands.Controls.Count!=3)throw new Exception("Unexpected chrome or command count");
        File.WriteAllText(reportPath,"{\"pass\":true,\"minimize\":true,\"maximize\":true,\"restore\":true,\"resize\":true,\"opacityFeedback\":true,\"commands\":3}");close.PerformClick();if(!IsDisposed)throw new Exception("Close failed");return 0;
    }
    [STAThread]static int Main(string[] args){
        SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        bool smoke=args.Length>=2&&args[0]=="--smoke-test",uiTest=args.Length==2&&args[0]=="--ui-test";
        using(var form=new Manager(!smoke&&!uiTest)){
            if(uiTest)return form.TestChrome(args[1]);
            if(smoke){if(args.Length==4)form.ClientSize=new Size(int.Parse(args[2]),int.Parse(args[3]));form.Show();Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(args[1]);}return form.commands.Controls.Count==3?0:1;}
            Application.Run(form);
        }
        return 0;
    }
}
