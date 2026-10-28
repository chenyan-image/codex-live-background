using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

internal sealed partial class Manager {
    readonly Panel sparkPage=new Panel();readonly CheckBox sparkAuto=new CheckBox();readonly TextBox sparkName=new TextBox();
    readonly Label sparkState=new Label();readonly DataGridView sparkGrid=new DataGridView();
    readonly Button sparkAdd=new ActionButton(),sparkRemove=new ActionButton(),sparkStart=new ActionButton(),sparkInspect=new ActionButton(),sparkCancel=new ActionButton();
    readonly System.Windows.Forms.Timer sparkTimer=new System.Windows.Forms.Timer{Interval=2500};
    SparkStore sparkStore;SparkSettings sparkSettings;SparkRunner sparkRunner;CancellationTokenSource sparkCancellation;bool sparkSync,sparkDisposed;DateTime sparkNextAuto=DateTime.MinValue;IntPtr sparkAutoWindow;
    bool SparkKeepsManagerActive {get{return sparkCancellation!=null||sparkRunner!=null&&(sparkRunner.Running||sparkSettings.AutoStart&&sparkSettings.Contacts.Any(c=>c.Enabled)&&DouyinDesktop.HasForegroundWindow());}}
    void PositionSparkPreview(){var window=DouyinDesktop.FindOpenWindow();if(window==IntPtr.Zero)return;var area=Screen.FromHandle(window).WorkingArea;StartPosition=FormStartPosition.Manual;Location=new Point(area.Left+Math.Max(0,(area.Width-Width)/2),area.Top+Math.Max(0,(area.Height-Height)/2));}
    void BuildSparkPage(Panel pages){
        sparkPage.Dock=DockStyle.Fill;sparkPage.Visible=false;pages.Controls.Add(sparkPage);
        var layout=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Margin=Padding.Empty};
        foreach(int size in new[]{52,48,46})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,size));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));foreach(int size in new[]{20,46,68})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,size));sparkPage.Controls.Add(layout);
        var introRow=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};introRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));introRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));introRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,128));var intro=Caption("点击开始，自动打开抖音并逐人续火花；无需安装 Codex。",10,Ink);intro.Padding=new Padding(2,12,0,0);introRow.Controls.Add(intro,0,0);var locate=new ActionButton{Text="定位抖音",Dock=DockStyle.Fill,Margin=new Padding(6,4,0,10)};locate.Click+=delegate{LocateDouyin();};introRow.Controls.Add(locate,1,0);layout.Controls.Add(introRow,0,0);
        sparkAuto.Text="打开抖音时自动续火花（每天每人最多一次）";sparkAuto.Dock=DockStyle.Fill;sparkAuto.AutoSize=false;sparkAuto.AccessibleName="打开抖音时自动续火花";sparkAuto.CheckedChanged+=delegate{if(sparkSync||sparkSettings==null)return;sparkSettings.AutoStart=sparkAuto.Checked;if(SaveSpark())sparkState.Text=sparkAuto.Checked?"已开启：CodexHub 在托盘运行时，等待抖音切到前台。":"自动续火花已关闭。";};layout.Controls.Add(sparkAuto,0,1);
        var addRow=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};addRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,108));addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,108));
        var input=new Surface{Dock=DockStyle.Fill,Padding=new Padding(12,10,10,8),BackColor=Color.FromArgb(24,28,33),Margin=new Padding(0,0,10,2)};sparkName.BorderStyle=BorderStyle.None;sparkName.Dock=DockStyle.Fill;sparkName.BackColor=input.BackColor;sparkName.ForeColor=Ink;sparkName.MaxLength=512;sparkName.AccessibleName="抖音联系人名称";sparkName.HandleCreated+=delegate{SetCueText(sparkName.Handle,0x1501,IntPtr.Zero,"填写抖音备注或昵称，与消息列表完全一致");};input.Controls.Add(sparkName);addRow.Controls.Add(input,0,0);
        sparkAdd.Text="添加名单";sparkAdd.Dock=DockStyle.Fill;sparkAdd.Margin=new Padding(0,0,8,2);sparkAdd.Click+=delegate{AddSparkContact();};addRow.Controls.Add(sparkAdd,1,0);
        sparkRemove.Text="移除选中";sparkRemove.Dock=DockStyle.Fill;sparkRemove.Margin=new Padding(0,0,0,2);sparkRemove.Click+=delegate{if(sparkRunner!=null&&sparkRunner.Running)return;foreach(DataGridViewRow row in sparkGrid.SelectedRows.Cast<DataGridViewRow>().ToArray()){var contact=row.Tag as SparkContact;if(contact!=null)sparkSettings.Contacts.Remove(contact);}SaveSpark();RefreshSpark();};addRow.Controls.Add(sparkRemove,2,0);layout.Controls.Add(addRow,0,2);
        sparkGrid.Dock=DockStyle.Fill;sparkGrid.BackgroundColor=Color.FromArgb(18,22,26);sparkGrid.BorderStyle=BorderStyle.None;sparkGrid.AllowUserToAddRows=sparkGrid.AllowUserToDeleteRows=false;sparkGrid.RowHeadersVisible=false;sparkGrid.AutoGenerateColumns=false;sparkGrid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;sparkGrid.MultiSelect=false;sparkGrid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;sparkGrid.EnableHeadersVisualStyles=false;sparkGrid.GridColor=Color.FromArgb(42,52,57);sparkGrid.ColumnHeadersHeight=36;sparkGrid.RowTemplate.Height=40;
        sparkGrid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=Color.FromArgb(29,37,41),ForeColor=Ink,SelectionBackColor=Color.FromArgb(29,37,41)};sparkGrid.DefaultCellStyle=new DataGridViewCellStyle{BackColor=Color.FromArgb(20,25,29),ForeColor=Ink,SelectionBackColor=Color.FromArgb(35,59,53),SelectionForeColor=Ink,Padding=new Padding(8,0,6,0)};
        sparkGrid.Columns.Add(new DataGridViewCheckBoxColumn{Name="enabled",HeaderText="启用",FillWeight=10,MinimumWidth=56});sparkGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="name",HeaderText="联系人",ReadOnly=true,FillWeight=27});sparkGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="status",HeaderText="状态",ReadOnly=true,FillWeight=22});sparkGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="detail",HeaderText="处理说明",ReadOnly=true,FillWeight=65});
        sparkGrid.CurrentCellDirtyStateChanged+=delegate{if(sparkGrid.IsCurrentCellDirty)sparkGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);};sparkGrid.CellValueChanged+=delegate(object sender,DataGridViewCellEventArgs e){if(sparkSync||e.RowIndex<0||e.ColumnIndex!=0)return;var contact=sparkGrid.Rows[e.RowIndex].Tag as SparkContact;if(contact!=null){contact.Enabled=Convert.ToBoolean(sparkGrid.Rows[e.RowIndex].Cells[0].Value);SaveSpark();}};
        layout.Controls.Add(sparkGrid,0,3);
        var actions=new BufferedFlow{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};sparkStart.Text="开始续火花";((ActionButton)sparkStart).Primary=true;sparkInspect.Text="检查名单（不发送）";sparkCancel.Text="停止任务";sparkStart.Width=150;sparkInspect.Width=180;sparkCancel.Width=130;actions.Controls.AddRange(new Control[]{sparkStart,sparkInspect,sparkCancel});layout.Controls.Add(actions,0,5);
        sparkStart.Click+=delegate{StartSpark(false);};sparkInspect.Click+=delegate{StartSpark(true);};sparkCancel.Click+=delegate{if(sparkCancellation!=null)sparkCancellation.Cancel();};
        sparkState.Dock=DockStyle.Fill;sparkState.ForeColor=Mint;sparkState.Padding=new Padding(2,10,4,0);sparkState.AutoEllipsis=true;sparkState.Text="等待设置名单。自动模式默认关闭；检查名单不会发送消息。";layout.Controls.Add(sparkState,0,6);
    }
    void InitializeSpark(bool live){
        sparkStore=new SparkStore(stateDirectory);sparkSettings=sparkStore.Load();sparkRunner=new SparkRunner(new DouyinDesktop(),new SparkPetGuide(()=>pets==null?null:pets.Window),s=>sparkStore.Save(s));sparkRunner.DiagnosticDirectory=stateDirectory;sparkRunner.Changed+=RefreshSpark;
        sparkSync=true;sparkAuto.Checked=sparkSettings.AutoStart;sparkSync=false;RefreshSpark();if(sparkStore.Warning!=null)sparkState.Text=sparkStore.Warning;
        if(live){sparkTimer.Tick+=delegate{AutoSpark();};sparkTimer.Start();}
    }
    bool SaveSpark(){try{sparkStore.Save(sparkSettings);return true;}catch(Exception error){sparkState.Text="名单未保存："+error.Message;return false;}}
    void LocateDouyin(){if(sparkSettings==null||sparkCancellation!=null)return;using(var dialog=new OpenFileDialog{Title="选择抖音桌面客户端 douyin.exe",Filter="抖音程序 (douyin.exe)|douyin.exe",CheckFileExists=true})if(dialog.ShowDialog(this)==DialogResult.OK){string path=DouyinInstall.Executable(dialog.FileName);if(path==null){sparkState.Text="请选择抖音的 douyin.exe 程序。";return;}sparkSettings.DouyinPath=path;if(SaveSpark())sparkState.Text="已保存抖音位置；点击开始会自动打开。";}}
    void AddSparkContact(){
        if(sparkSettings==null||sparkRunner.Running)return;string name=SparkSettings.NameKey(sparkName.Text);if(String.IsNullOrEmpty(name))return;
        if(sparkSettings.Contacts.Any(c=>String.Equals(c.Name,name,StringComparison.OrdinalIgnoreCase))){sparkState.Text="名单中已有此人。";return;}if(sparkSettings.Contacts.Count>=200){sparkState.Text="名单最多支持 200 人。";return;}
        var item=new SparkContact{Name=name};sparkSettings.Contacts.Add(item);if(!SaveSpark()){sparkSettings.Contacts.Remove(item);return;}sparkName.Clear();RefreshSpark();
    }
    void RefreshSpark(){
        if(sparkDisposed||IsDisposed||sparkSettings==null)return;if(InvokeRequired){Ui(RefreshSpark);return;}bool running=sparkCancellation!=null||sparkRunner!=null&&sparkRunner.Running;sparkSync=true;try{
            sparkGrid.SuspendLayout();bool same=sparkGrid.Rows.Count==sparkSettings.Contacts.Count&&sparkGrid.Rows.Cast<DataGridViewRow>().Select(row=>((SparkContact)row.Tag).Id).SequenceEqual(sparkSettings.Contacts.Select(contact=>contact.Id));
            if(!same){sparkGrid.Rows.Clear();foreach(var contact in sparkSettings.Contacts){int index=sparkGrid.Rows.Add(contact.Enabled,contact.Name,contact.Status,contact.Detail);sparkGrid.Rows[index].Tag=contact;}}
            else for(int i=0;i<sparkSettings.Contacts.Count;i++){var contact=sparkSettings.Contacts[i];var row=sparkGrid.Rows[i];row.Cells[0].Value=contact.Enabled;row.Cells[1].Value=contact.Name;row.Cells[2].Value=contact.Status;row.Cells[3].Value=contact.Detail;}
            sparkAdd.Enabled=sparkRemove.Enabled=sparkName.Enabled=sparkAuto.Enabled=!running;sparkGrid.ReadOnly=running;sparkStart.Enabled=sparkInspect.Enabled=!running;sparkCancel.Enabled=running;
            if(sparkRunner!=null)sparkState.Text=sparkRunner.State;
        }finally{sparkGrid.ResumeLayout();sparkSync=false;}
    }
    async void StartSpark(bool inspect){
        if(sparkDisposed||sparkRunner==null||sparkRunner.Running||sparkCancellation!=null)return;if(!sparkSettings.Contacts.Any(c=>c.Enabled)){sparkState.Text="请先添加并启用至少一位联系人。";return;}
        if(pets==null||pets.Window==null||!pets.Preferences.Enabled){sparkState.Text="请先在桌面宠物中开启一个宠物。";return;}if(!SaveSpark())return;
        sparkCancellation=new CancellationTokenSource();var active=sparkCancellation;RefreshSpark();
        // The manager must leave the input foreground before the automation
        // starts. Status updates below never activate or rebuild this window.
        bool returnToManager=Visible;HideToTray();
        try{var launcher=new DouyinAppLauncher(sparkSettings.DouyinPath,path=>{sparkSettings.DouyinPath=path;SaveSpark();});await SparkStartFlow.Run(launcher,sparkRunner,sparkSettings,inspect,active.Token);}
        catch(OperationCanceledException){sparkRunner.State="任务已停止。";}catch(Exception error){sparkRunner.State="任务未开始："+error.Message;}
        finally{active.Dispose();if(sparkCancellation==active)sparkCancellation=null;if(!sparkDisposed){RefreshSpark();if(returnToManager)RestoreWindow();}}
    }
    void AutoSpark(){
        if(sparkDisposed||sparkRunner==null||sparkRunner.Running||sparkCancellation!=null||!sparkSettings.AutoStart||DateTime.UtcNow<sparkNextAuto||busy||updatingSoftware||petImporting||ImportingWallpapers||HubPopupMenu.AnyOpen)return;
        if(!DouyinDesktop.HasForegroundWindow()){sparkAutoWindow=IntPtr.Zero;return;}
        // The day journal suppresses sends. This backoff suppresses repeated missing-name scans.
        string today=DateTime.Now.ToString("yyyy-MM-dd");if(!sparkSettings.Contacts.Any(c=>c.Enabled&&c.LastAttemptDay!=today&&c.LastSentDay!=today))return;
        if(sparkAutoWindow!=IntPtr.Zero)return;sparkAutoWindow=new IntPtr(1);sparkNextAuto=DateTime.UtcNow.AddMinutes(30);StartSpark(false);
    }
    void StopSpark(){if(sparkDisposed)return;sparkDisposed=true;sparkTimer.Stop();sparkTimer.Dispose();if(sparkCancellation!=null)sparkCancellation.Cancel();if(pets!=null&&pets.Window!=null)pets.Window.EndSparkGuide();}
}
