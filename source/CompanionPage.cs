using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class PetCard : Control {
    public readonly PetPack Pack;readonly PetAtlas atlas;public bool Selected;int frame;
    public PetCard(PetPack pack){Pack=pack;if(pack.Available)atlas=new PetAtlas(pack,true);DoubleBuffered=true;Size=new Size(166,205);Margin=new Padding(0,0,12,12);Cursor=Cursors.Hand;AccessibleName=pack.Name;AccessibleRole=AccessibleRole.RadioButton;TabStop=true;}
    public void Animate(){if(Visible){frame++;Invalidate();}}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space){OnClick(EventArgs.Empty);e.Handled=true;}}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.Clear(Parent.BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var shape=IconAssets.Round(new RectangleF(1,1,Width-3,Height-3),14))using(var fill=new SolidBrush(Selected?Color.FromArgb(27,43,43):Color.FromArgb(21,25,32)))using(var border=new Pen(Selected?Color.FromArgb(135,235,194):Color.FromArgb(42,49,60),Selected?2:1)){g.FillPath(fill,shape);g.DrawPath(border,shape);}
        float ratio=Math.Min((Width-24)/192F,(Height-57)/208F);float width=192*ratio,height=208*ratio;g.InterpolationMode=InterpolationMode.HighQualityBicubic;if(atlas!=null)atlas.Draw(g,new RectangleF((Width-width)/2,6,width,height),Selected?3:0,frame/10D);else{using(var logo=IconAssets.Create(64))g.DrawImage(logo,(Width-64)/2,45,64,64);}
        var name=Pack.Name;
        TextRenderer.DrawText(g,name,Font,new Rectangle(12,Height-48,Width-24,23),Color.FromArgb(231,240,238),TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
        using(var small=new Font(Font.FontFamily,8F))TextRenderer.DrawText(g,!Pack.Available?"点击加载作者原动作包":Pack.ProceduralMotion?Pack.Actions.Count+" 个表情 / 轻动画":Pack.Actions!=null?Pack.Actions.Count+" 项动作 / 素材":Pack.Builtin?"内置 · 9 种基础动作":"本地导入 · 动态形象",small,new Rectangle(12,Height-22,Width-24,17),Color.FromArgb(137,155,170),TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
    }
    protected override void Dispose(bool disposing){if(disposing&&atlas!=null)atlas.Dispose();base.Dispose(disposing);}
}
internal sealed class PetCardGallery : Panel {
    readonly Panel viewport=new Panel{Dock=DockStyle.Fill};readonly GalleryScrollBar scrollbar=new GalleryScrollBar{Dock=DockStyle.Right,Width=16};readonly List<PetCard> cards=new List<PetCard>();bool arranging;
    public PetCardGallery(){DoubleBuffered=true;Controls.Add(viewport);Controls.Add(scrollbar);scrollbar.AccessibleName="桌面宠物列表滚动条";scrollbar.ValueChanged+=delegate{Arrange();};viewport.SizeChanged+=delegate{Arrange();};}
    public void AddCard(PetCard card){cards.Add(card);viewport.Controls.Add(card);card.MouseWheel+=Wheel;Arrange();}
    public void ClearCards(){cards.Clear();viewport.Controls.Clear();scrollbar.Value=0;}
    void Wheel(object sender,MouseEventArgs e){scrollbar.Value-=(int)Math.Round(e.Delta/120D*108);var handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;}
    protected override void OnMouseWheel(MouseEventArgs e){Wheel(this,e);base.OnMouseWheel(e);}
    void Arrange(){if(arranging||viewport.Width<2)return;arranging=true;try{
        float scale=1;using(var graphics=CreateGraphics())scale=graphics.DpiX/96F;int width=(int)(166*scale),height=(int)(205*scale),gap=(int)(12*scale),columns=Math.Max(1,(viewport.Width+gap)/(width+gap));int rows=(cards.Count+columns-1)/columns;
        scrollbar.SetRange(Math.Max(0,rows*(height+gap)-gap-viewport.Height),viewport.Height);
        for(int i=0;i<cards.Count;i++)cards[i].Bounds=new Rectangle((i%columns)*(width+gap),(i/columns)*(height+gap)-scrollbar.Value,width,height);
    }finally{arranging=false;}}
}
internal sealed class PetController : IDisposable {
    readonly string root,state;readonly Control owner;readonly bool live;
    readonly PetQuotaReader reader=new PetQuotaReader();readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=120000},activityTimer=new System.Windows.Forms.Timer{Interval=1000};readonly PetActivityReader activity;readonly PetTokenReader tokenReader;
    bool checking;volatile bool readingActivity,disposed,readingSurface;int selectionVersion;string preparingId,windowId;PetSurface cachedSurface;readonly System.Windows.Forms.Timer surfaceTimer=new System.Windows.Forms.Timer{Interval=250};public double LastSwitchMilliseconds;public string SwitchError;public PetPreferences Preferences;public List<PetPack> Packs;public PetWindow Window;
    public PetQuotaSnapshot Quota;public PetTokenSnapshot Tokens;public string QuotaStatus="尚未读取";
    public bool IsPreparing{get{return preparingId!=null;}}
    public Dictionary<string,long> LastSwitchStages=new Dictionary<string,long>();
    public event Action Changed,SettingsRequested;
    public PetController(Control ui,string appRoot,string stateRoot,bool enabled){owner=ui;root=appRoot;state=stateRoot;live=enabled;Preferences=PetPreferences.Load(state);Packs=PetPack.Discover(root,state);timer.Tick+=delegate{if(Preferences.Enabled&&Preferences.ShowQuota)Refresh();};if(live){string home=Environment.GetEnvironmentVariable("CODEX_HOME")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");activity=new PetActivityReader(home,root);tokenReader=new PetTokenReader(home);activityTimer.Tick+=delegate{ReadActivity();};}}
    void Raise(){if(Changed!=null)Changed();}
    public void Start(){if(!live||disposed)return;surfaceTimer.Tick+=delegate{ReadSurface();};surfaceTimer.Start();ReadSurface();Apply(false);timer.Start();activityTimer.Start();ReadActivity();}
    void ReadSurface(){if(disposed||readingSurface||!Preferences.CodexOnly)return;readingSurface=true;ThreadPool.QueueUserWorkItem(delegate{try{var found=CodexPetSurface.Find();if(!disposed&&owner.IsHandleCreated)owner.BeginInvoke((MethodInvoker)delegate{if(!disposed)cachedSurface=found;});}catch(InvalidOperationException){}finally{readingSurface=false;}});}
    void ReadActivity(){if(disposed||readingActivity||activity==null)return;readingActivity=true;ThreadPool.QueueUserWorkItem(delegate{try{var value=activity.Read();var tokens=tokenReader.Read();if(!disposed&&owner.IsHandleCreated)owner.BeginInvoke((MethodInvoker)delegate{if(!disposed){Tokens=tokens;if(Window!=null){Window.Activity=value;Window.Tokens=tokens;}}});}catch(InvalidOperationException){}catch(IOException){}catch(UnauthorizedAccessException){}finally{readingActivity=false;}});}
    public void Apply(bool rebuild){
        if(disposed)return;Preferences.Scale=Math.Max(70,Math.Min(150,Preferences.Scale));Preferences.CapsuleScale=Math.Max(70,Math.Min(150,Preferences.CapsuleScale));Preferences.Save(state);
        if(live){
            var pack=Packs.FirstOrDefault(x=>x.Id==Preferences.SelectedId&&x.Available)??Packs.FirstOrDefault(x=>x.Available);
            if(Preferences.Enabled&&pack!=null&&(Window==null||rebuild||windowId!=pack.Id)&&preparingId!=pack.Id){Preferences.SelectedId=pack.Id;PrepareWindow(pack);}
            if(Window!=null){if(!Preferences.Enabled)Window.Hide();else Window.AdvanceOnce();}
            if(Preferences.Enabled&&Preferences.ShowQuota&&Quota==null)Refresh();
        }Raise();
    }
    void PrepareWindow(PetPack pack){int version=++selectionVersion;preparingId=pack.Id;SwitchError=null;var clock=Stopwatch.StartNew();var profile=Preferences.ForPet(pack.Id);IntPtr uiHandle=owner.Handle;
        ThreadPool.QueueUserWorkItem(delegate{PetAtlas prepared=null;Exception failure=null;try{prepared=new PetAtlas(pack);var spec=new PetAnimationDirector(pack,profile,1);prepared.PrepareDrag(spec.DragRow);var ready=Stopwatch.StartNew();while(!prepared.DragReady&&ready.ElapsedMilliseconds<1500&&!disposed)Thread.Sleep(10);}catch(Exception error){failure=error;}
            if(disposed||owner.IsDisposed){if(prepared!=null)prepared.Dispose();return;}try{owner.BeginInvoke((MethodInvoker)delegate{FinishWindow(pack,version,prepared,failure,clock);});}catch(InvalidOperationException){if(prepared!=null)prepared.Dispose();}
        });
    }
    void FinishWindow(PetPack pack,int version,PetAtlas prepared,Exception failure,Stopwatch clock){PetWindow next=null;var uiClock=Stopwatch.StartNew();LastSwitchStages=new Dictionary<string,long>();try{
        if(disposed||version!=selectionVersion||!Preferences.Enabled||Preferences.SelectedId!=pack.Id){if(prepared!=null)prepared.Dispose();return;}if(failure!=null)throw failure;
        var previous=Window;RectangleF anchor=previous==null?RectangleF.Empty:previous.VisibleBodyScreenBounds;if(previous!=null){Preferences.X=previous.Left;Preferences.Y=previous.Top;}next=PetWindow.Create(pack,Preferences,root,prepared);LastSwitchStages["createWindow"]=uiClock.ElapsedMilliseconds;prepared=null;next.TrackLiveSurface=true;next.SurfaceProvider=()=>cachedSurface;next.Quota=Quota;next.Tokens=Tokens;next.QuotaStatus=QuotaStatus;next.QuotaRefreshing=checking;
        next.OpenSettings+=delegate{if(SettingsRequested!=null)SettingsRequested();};next.ScopeRequested+=SetScope;next.RefreshQuota+=Refresh;next.PreferenceChanged+=delegate{Preferences.Save(state);Raise();};next.NotifyQuota(Quota);next.AdvanceOnce();LastSwitchStages["firstFrame"]=uiClock.ElapsedMilliseconds;
        if(!anchor.IsEmpty&&next.Visible){var body=next.VisibleBodyScreenBounds;next.Motion.Position=new PointF(next.Left+anchor.Left+anchor.Width/2-body.Left-body.Width/2,next.Top+anchor.Bottom-body.Bottom);next.AdvanceOnce();}LastSwitchStages["position"]=uiClock.ElapsedMilliseconds;Window=next;windowId=pack.Id;if(previous!=null)previous.Dispose();LastSwitchStages["disposeOld"]=uiClock.ElapsedMilliseconds;Preferences.X=next.Left;Preferences.Y=next.Top;Preferences.Save(state);LastSwitchMilliseconds=clock.Elapsed.TotalMilliseconds;
    }catch(Exception error){if(prepared!=null)prepared.Dispose();if(next!=null&&Window!=next)next.Dispose();if(!disposed)SwitchError="宠物未能载入："+error.Message;}finally{if(version==selectionVersion)preparingId=null;if(!disposed)Raise();}}
    public async void Refresh(){
        if(disposed||checking||!live)return;checking=true;if(Window!=null)Window.QuotaRefreshing=true;QuotaStatus="正在刷新真实额度…";Raise();
        try{var value=await reader.ReadAsync();if(disposed)return;Quota=value;if(Window!=null)Window.NotifyQuota(value);QuotaStatus="更新于 "+value.CheckedUtc.ToLocalTime().ToString("HH:mm:ss");}
        catch(OperationCanceledException){return;}catch(Exception error){if(disposed)return;QuotaStatus=error.Message;}
        finally{checking=false;if(!disposed){if(Window!=null){Window.QuotaRefreshing=false;Window.Quota=Quota;Window.QuotaStatus=QuotaStatus;}Raise();}}
    }
    public void Select(PetPack pack){Preferences.SelectedId=pack.Id;Apply(true);}
    public void SetScope(bool codexOnly){Preferences.CodexOnly=codexOnly;if(Window!=null)Window.SetScope(codexOnly);else{Preferences.Save(state);Raise();}}
    public void Reload(){Packs=PetPack.Discover(root,state);Raise();}
    public void Dispose(){if(disposed)return;disposed=true;selectionVersion++;timer.Stop();timer.Dispose();surfaceTimer.Stop();surfaceTimer.Dispose();activityTimer.Stop();activityTimer.Dispose();if(activity!=null)activity.Dispose();if(tokenReader!=null)tokenReader.Dispose();reader.Dispose();if(Window!=null){Preferences.X=Window.Left;Preferences.Y=Window.Top;Window.Dispose();Window=null;}Preferences.Save(state);}
}
internal sealed partial class Manager {
    readonly Panel petPage=new Panel();readonly PetCardGallery petCards=new PetCardGallery();readonly List<PetCard> petTiles=new List<PetCard>();
    readonly Label petSummary=new Label(),petQuotaStatus=new Label(),petQuotaValues=new Label();
    readonly CheckBox petEnabled=new CheckBox(),petRoaming=new CheckBox(),petSound=new CheckBox(),petQuotaVisible=new CheckBox();
    readonly RadioButton petInCodex=new RadioButton(),petOnDesktop=new RadioButton();
    readonly TrackBar petSize=new TrackBar(),petCapsuleSize=new TrackBar();readonly Label petSizeLabel=new Label(),petCapsuleSizeLabel=new Label();
    readonly Button petWave=new ActionButton(),petJump=new ActionButton(),petRefresh=new ActionButton();
    readonly System.Windows.Forms.Timer petPreviewTimer=new System.Windows.Forms.Timer{Interval=120};
    string petImportProgress;PetController pets;bool petSync,petImporting,companionLive,companionStopped;volatile bool cancelPetImport;readonly object petImportGate=new object();PetProcessScope petImportProcess;
    void InitializeCompanion(bool live){
        companionLive=live;pets=new PetController(this,root,stateDirectory,live);pets.Changed+=SyncPetSettings;pets.SettingsRequested+=delegate{RestoreWindow();Navigate("pets");};BuildPetCards();SyncPetSettings();
        Shown+=delegate{if(companionLive)pets.Start();};petPreviewTimer.Tick+=delegate{string message=Interlocked.Exchange(ref petImportProgress,null);if(message!=null)previewHint.Text=message;if(petPage.Visible&&Visible)foreach(var tile in petTiles)tile.Animate();};petPreviewTimer.Start();
    }
    void BuildPetPage(Panel pages){
        petPage.Dock=DockStyle.Fill;petPage.Visible=false;pages.Controls.Add(petPage);
        var layout=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,306));petPage.Controls.Add(layout);
        var left=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=new Padding(0,0,20,0)};left.RowStyles.Add(new RowStyle(SizeType.Absolute,35));left.RowStyles.Add(new RowStyle(SizeType.Percent,100));left.RowStyles.Add(new RowStyle(SizeType.Absolute,45));layout.Controls.Add(left,0,0);
        petSummary.Dock=DockStyle.Fill;petSummary.ForeColor=Muted;petSummary.Font=new Font(Font.FontFamily,8.5F);left.Controls.Add(petSummary,0,0);
        petCards.Dock=DockStyle.Fill;petCards.BackColor=BackColor;left.Controls.Add(petCards,0,1);
        var sources=new Label{Dock=DockStyle.Fill,Text="内置素材 + 本地导入 · 支持 ZIP 或 pet.json",ForeColor=Color.FromArgb(107,129,146),Font=new Font(Font.FontFamily,8F)};left.Controls.Add(sources,0,2);
        var settings=new Surface{Dock=DockStyle.Fill,Padding=new Padding(16),AutoScroll=true,BackColor=Color.FromArgb(20,24,30)};layout.Controls.Add(settings,1,0);
        var rows=new BufferedTable{Dock=DockStyle.Top,Height=580,ColumnCount=1,RowCount=18,Margin=Padding.Empty};foreach(int height in new[]{28,29,29,29,28,24,29,29,27,36,27,36,26,40,42,36,40,42})rows.RowStyles.Add(new RowStyle(SizeType.Absolute,height));settings.Controls.Add(rows);
        rows.Controls.Add(Caption("陪伴方式",12,Ink),0,0);
        var checks=new[]{petEnabled,petRoaming,petSound,petQuotaVisible};string[] checkLabels={"显示桌面宠物","允许随机行走","点击互动音效","显示上方额度圆环"};int[] indices={1,2,3,4};
        for(int i=0;i<checks.Length;i++){var check=checks[i];check.Text=checkLabels[i];check.ForeColor=Ink;check.Dock=DockStyle.Fill;check.Margin=Padding.Empty;check.CheckedChanged+=delegate{if(petSync||pets==null)return;pets.Preferences.Enabled=petEnabled.Checked;pets.Preferences.Roaming=petRoaming.Checked;pets.Preferences.Sound=petSound.Checked;pets.Preferences.ShowQuota=petQuotaVisible.Checked;pets.Apply(false);};rows.Controls.Add(check,0,indices[i]);}
        rows.Controls.Add(Caption("活动范围",9,Muted),0,5);
        petInCodex.Text="仅在 Codex 窗口内";petOnDesktop.Text="整个桌面（可跨屏拖动）";
        foreach(var option in new[]{petInCodex,petOnDesktop}){option.Dock=DockStyle.Fill;option.ForeColor=Ink;option.Margin=Padding.Empty;option.CheckedChanged+=delegate{if(petSync||pets==null||!((RadioButton)option).Checked)return;pets.SetScope(petInCodex.Checked);};}
        rows.Controls.Add(petInCodex,0,6);rows.Controls.Add(petOnDesktop,0,7);petSizeLabel.Dock=DockStyle.Fill;petSizeLabel.ForeColor=Muted;rows.Controls.Add(petSizeLabel,0,8);
        petSize.Minimum=70;petSize.Maximum=150;petSize.TickStyle=TickStyle.None;petSize.Dock=DockStyle.Fill;petSize.SmallChange=5;petSize.LargeChange=10;petSize.BackColor=settings.BackColor;petSize.ValueChanged+=delegate{petSizeLabel.Text="宠物大小  "+petSize.Value+"%";};petSize.MouseUp+=delegate{ApplyPetScale();};petSize.KeyUp+=delegate{ApplyPetScale();};rows.Controls.Add(petSize,0,9);
        petCapsuleSizeLabel.Dock=DockStyle.Fill;petCapsuleSizeLabel.ForeColor=Muted;rows.Controls.Add(petCapsuleSizeLabel,0,10);
        petCapsuleSize.Minimum=70;petCapsuleSize.Maximum=150;petCapsuleSize.TickStyle=TickStyle.None;petCapsuleSize.Dock=DockStyle.Fill;petCapsuleSize.SmallChange=5;petCapsuleSize.LargeChange=10;petCapsuleSize.BackColor=settings.BackColor;petCapsuleSize.ValueChanged+=delegate{petCapsuleSizeLabel.Text="胶囊大小  "+petCapsuleSize.Value+"%";};petCapsuleSize.MouseUp+=delegate{ApplyPetScale();};petCapsuleSize.KeyUp+=delegate{ApplyPetScale();};rows.Controls.Add(petCapsuleSize,0,11);
        rows.Controls.Add(Caption("始终置顶 · 胶囊看任务，圆环看额度",8,Muted),0,12);
        petQuotaValues.Dock=DockStyle.Fill;petQuotaValues.ForeColor=Mint;petQuotaValues.Font=new Font(Font.FontFamily,10F,FontStyle.Bold);rows.Controls.Add(petQuotaValues,0,13);
        petQuotaStatus.Dock=DockStyle.Fill;petQuotaStatus.ForeColor=Muted;petQuotaStatus.Font=new Font(Font.FontFamily,8F);petQuotaStatus.AutoEllipsis=true;rows.Controls.Add(petQuotaStatus,0,14);
        var actions=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));petWave.Text="打招呼";petJump.Text="随机动作";petWave.Dock=petJump.Dock=DockStyle.Fill;petWave.Margin=new Padding(0,0,6,3);petJump.Margin=new Padding(6,0,0,3);petWave.Click+=delegate{if(pets!=null&&pets.Window!=null)pets.Window.Greet();};petJump.Click+=delegate{if(pets!=null&&pets.Window!=null)pets.Window.RandomAction();};actions.Controls.Add(petWave,0,0);actions.Controls.Add(petJump,1,0);rows.Controls.Add(actions,0,15);
        petRefresh.Text="刷新真实额度";petRefresh.Dock=DockStyle.Fill;petRefresh.Margin=new Padding(0,5,0,0);petRefresh.Click+=delegate{if(pets!=null)pets.Refresh();};rows.Controls.Add(petRefresh,0,16);
        var behaviorButton=new ActionButton{Text="互动与动画设置",Dock=DockStyle.Fill,Margin=new Padding(0,5,0,0)};behaviorButton.Click+=delegate{if(pets==null)return;var pack=pets.Packs.FirstOrDefault(p=>p.Id==pets.Preferences.SelectedId&&p.Available);if(pack==null)return;using(var dialog=new PetBehaviorSettings(pack,pets.Preferences.ForPet(pack.Id))){if(dialog.ShowDialog(this)==DialogResult.OK)pets.Apply(false);}};rows.Controls.Add(behaviorButton,0,17);
    }
    void ApplyPetScale(){if(pets==null||petSync)return;if(pets.Preferences.Scale!=petSize.Value||pets.Preferences.CapsuleScale!=petCapsuleSize.Value){pets.Preferences.Scale=petSize.Value;pets.Preferences.CapsuleScale=petCapsuleSize.Value;pets.Apply(false);}}
    void BuildPetCards(){
        foreach(var tile in petTiles)tile.Dispose();petTiles.Clear();petCards.ClearCards();if(pets==null)return;
        foreach(var pack in pets.Packs){try{var tile=new PetCard(pack){Font=Font,Selected=pets.Preferences.SelectedId==pack.Id};tile.Click+=delegate{try{if(tile.Pack.Optional){if(tile.Pack.Id=="bailong")ImportBailong();}else pets.Select(tile.Pack);}catch(Exception error){MessageBox.Show(this,error.Message,"CodexHub");}};petTiles.Add(tile);petCards.AddCard(tile);}catch(Exception error){petQuotaStatus.Text=error.Message;}}
        petSummary.Text=petTiles.Count(tile=>tile.Pack.Available)+" 个可用形象 · 点击切换 · 拖动移动 · 双击互动";
    }
    void SyncPetSettings(){
        if(IsDisposed||pets==null)return;if(InvokeRequired){Ui(SyncPetSettings);return;}petSync=true;try{
            var settings=pets.Preferences;petEnabled.Checked=settings.Enabled;petRoaming.Checked=settings.Roaming;petSound.Checked=settings.Sound;petQuotaVisible.Checked=settings.ShowQuota;petInCodex.Checked=settings.CodexOnly;petOnDesktop.Checked=!settings.CodexOnly;petSize.Value=settings.Scale;petSizeLabel.Text="宠物大小  "+settings.Scale+"%";petCapsuleSize.Value=settings.CapsuleScale;petCapsuleSizeLabel.Text="胶囊大小  "+settings.CapsuleScale+"%";
            foreach(var tile in petTiles){tile.Selected=tile.Pack.Id==settings.SelectedId;tile.Invalidate();}
            var quota=pets.Quota;petQuotaValues.Text=(quota==null||quota.Short==null?"短周期 未知":quota.Short.Label+"  "+quota.Short.Percent)+"\r\n"+(quota==null||quota.Week==null?"本周 未知":quota.Week.Label+"  "+quota.Week.Percent);petQuotaStatus.Text=pets.IsPreparing?"正在准备形象，原宠物继续显示…":pets.SwitchError??(pets.QuotaStatus+(quota!=null&&quota.Stale?"（读数已过期）":""));
        }finally{petSync=false;}
    }
    void ImportPet(){
        if(petImporting||pets==null)return;using(var dialog=new OpenFileDialog{Filter="Codex 宠物包 / 白龙素材清单|*.zip;pet.json;素材清单.json",Title="导入桌面宠物"}){
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;ImportPetFile(dialog.FileName);
        }
    }
    void ImportPetFile(string input,string stableId=null){
            if(petImporting||pets==null||cancelPetImport||updatingSoftware)return;if(Path.GetFileName(input)=="素材清单.json"){ImportBailong(Path.GetDirectoryName(input));return;}petImporting=true;importButton.Enabled=false;previewHint.Text="正在校验并准备宠物图集…";
            ImportWork.Start(delegate{string stage=null;try{
                stage=PetImporter.Extract(input,stateDirectory);var json=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(stage,"pet.json")));string atlas=Path.Combine(stage,Convert.ToString(json["spritesheetPath"]));
                if(Path.GetExtension(atlas).Equals(".webp",StringComparison.OrdinalIgnoreCase)){
                    string request=Path.Combine(stage,"convert-request.json");File.WriteAllText(request,new JavaScriptSerializer().Serialize(new{input=atlas,output=Path.Combine(stage,"spritesheet.png"),stateRoot=stateDirectory}),new UTF8Encoding(false));
                    var info=new ProcessStartInfo(Path.Combine(root,@"engine\runtime\node\node.exe"),PetCommandQuote(Path.Combine(root,@"engine\scripts\convert-pet.mjs"))+" "+PetCommandQuote(request)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
                    PetProcessScope scope;lock(petImportGate){if(cancelPetImport)throw new OperationCanceledException();scope=petImportProcess=new PetProcessScope(info,true);}
                    using(scope){var process=scope.Process;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(240000)){scope.Cancel();throw new TimeoutException("宠物图集准备超时，请检查网络后重试。");}if(process.ExitCode!=0)throw new IOException(stderr.Result.Trim());}lock(petImportGate){if(petImportProcess==scope)petImportProcess=null;}File.Delete(request);
                }
                if(cancelPetImport)throw new OperationCanceledException();
                var pack=PetImporter.Finish(stage,stateDirectory,stableId);stage=null;Ui(delegate{pets.Reload();BuildPetCards();pets.Select(pack);previewHint.Text="已导入："+pack.Name;});
            }catch(Exception error){Ui(delegate{previewHint.Text="宠物导入未完成："+error.Message;});}finally{if(stage!=null)PetImporter.DeleteStage(stage,stateDirectory);Ui(delegate{petImportProgress=null;petImporting=false;importButton.Enabled=true;});}});
    }
    static string PetCommandQuote(string value){return "\""+value.Replace("\"","\\\"")+"\"";}
    void StopCompanion(){if(companionStopped)return;companionStopped=true;cancelPetImport=true;lock(petImportGate){if(petImportProcess!=null){petImportProcess.Cancel();petImportProcess=null;}}petPreviewTimer.Stop();petPreviewTimer.Dispose();if(pets!=null){pets.Dispose();pets=null;}}
    void StartCompanionPreview(string directory){
        if(pets!=null)pets.Dispose();stateDirectory=Path.GetFullPath(directory);Directory.CreateDirectory(stateDirectory);companionLive=true;
        library=new WallpaperLibrary(stateDirectory);library.RemapBuiltins(builtinEntries);
        pets=new PetController(this,root,stateDirectory,true);pets.Changed+=SyncPetSettings;pets.SettingsRequested+=delegate{RestoreWindow();Navigate("pets");};
        BuildPetCards();SyncPetSettings();Navigate("pets");InitializeTray();pets.Start();
    }
}
