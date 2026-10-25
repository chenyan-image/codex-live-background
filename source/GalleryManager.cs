using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed partial class Manager {
    WallpaperLibrary library;List<WallpaperEntry> builtinEntries=new List<WallpaperEntry>(),steamEntries=new List<WallpaperEntry>();
    WallpaperEntry selected;string page="local",typeFilter="all",stateDirectory;int pageNumber,previewGeneration,scanGeneration,renderedCapacity;bool scanning,checkingStatus;
    readonly List<NavigationButton> navigation=new List<NavigationButton>();
    readonly FlowLayoutPanel gallery=new BufferedFlow();
    readonly ContinuousWallpaperGallery steamGallery=new ContinuousWallpaperGallery();
    readonly Panel libraryPage=new Panel(),servicePage=new Panel(),inspector=new Surface();
    readonly Label pageTitle=new Label(),pageSubtitle=new Label(),countLabel=new Label(),pageLabel=new Label(),previewHint=new Label(),sourceLabel=new Label(),connectionDetail=new Label(),emptyLabel=new Label();
    readonly TextBox search=new TextBox();readonly Button importButton=new ActionButton(),scanButton=new ActionButton(),chooseSteam=new ActionButton(),applyButton=new ActionButton(),removeEntry=new ActionButton(),previousPage=new ActionButton(),nextPage=new ActionButton();
    readonly TypeFilter filters=new TypeFilter();readonly System.Windows.Forms.Timer connectionTimer=new System.Windows.Forms.Timer{Interval=20000};
    MediaPreviewJob previewJob;readonly List<WallpaperTile> tiles=new List<WallpaperTile>();
    void BuildGallery(bool checkStatus){
        Text="Codex 动态背景 · 1.5.4.3";Font=new Font("Microsoft YaHei UI",9.5F);ForeColor=Ink;BackColor=Color.FromArgb(12,14,17);
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;FormBorderStyle=FormBorderStyle.None;Padding=new Padding(1);ClientSize=new Size(1310,816);MinimumSize=new Size(1090,720);StartPosition=FormStartPosition.CenterScreen;
        DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw|ControlStyles.AllPaintingInWmPaint,true);
        try{Icon=new Icon(Path.Combine(root,"CodexBackground.ico"),32,32);}catch{}
        stateDirectory=checkStatus?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexDreamSkin"):Path.Combine(Path.GetTempPath(),"CodexBackgroundUi-"+Guid.NewGuid().ToString("N"));
        library=new WallpaperLibrary(stateDirectory);builtinEntries=WallpaperLibrary.Builtins(root);library.RemapBuiltins(builtinEntries);
        var shell=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,210));shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(shell);
        var sidebar=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(17,25,17,15),BackColor=Color.FromArgb(17,20,24),Margin=Padding.Empty};
        foreach(int h in new[]{90,30,218})sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,h));sidebar.RowStyles.Add(new RowStyle(SizeType.Percent,100));sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,86));sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,24));shell.Controls.Add(sidebar,0,0);
        var brand=new BufferedTable{Dock=DockStyle.Fill,RowCount=2,ColumnCount=2,Margin=Padding.Empty};brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,42));brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));brand.RowStyles.Add(new RowStyle(SizeType.Absolute,37));brand.RowStyles.Add(new RowStyle(SizeType.Percent,100));brand.Controls.Add(new PictureBox{Image=IconAssets.Create(33),SizeMode=PictureBoxSizeMode.CenterImage,Dock=DockStyle.Fill},0,0);brand.Controls.Add(Caption("Codex",19,Ink),1,0);var tag=Caption("动态背景 / YOUR SPACE",8,Muted);tag.Padding=new Padding(6,5,0,0);brand.Controls.Add(tag,0,1);brand.SetColumnSpan(tag,2);sidebar.Controls.Add(brand,0,0);
        var browse=Caption("壁纸空间",8,Color.FromArgb(95,110,118));browse.Padding=new Padding(14,0,0,0);sidebar.Controls.Add(browse,0,1);
        var nav=new BufferedFlow{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};
        string[] ids={"local","steam","history","settings"},names={"本地壁纸","Wallpaper Engine","使用记录","服务与设置"},glyphs={"library","steam","recent","settings"};
        for(int i=0;i<ids.Length;i++){string id=ids[i];var button=new NavigationButton{Text=names[i],Glyph=glyphs[i],Tag=id,Width=176,Selected=i==0,AccessibleName=names[i]};button.Click+=delegate{Navigate(id);};navigation.Add(button);nav.Controls.Add(button);}sidebar.Controls.Add(nav,0,2);
        var connection=new Panel{Dock=DockStyle.Fill,Padding=new Padding(12,8,8,4),Cursor=Cursors.Hand};status.Text=checkStatus?"○  正在检测 Codex":"○  尚未检测";status.Font=new Font(Font.FontFamily,9F,FontStyle.Bold);status.ForeColor=Muted;status.Dock=DockStyle.Top;status.Height=25;connectionDetail.Text="点击重新检测连接";connectionDetail.ForeColor=Muted;connectionDetail.Font=new Font(Font.FontFamily,8F);connectionDetail.Dock=DockStyle.Fill;connection.Controls.Add(connectionDetail);connection.Controls.Add(status);EventHandler recheck=delegate{Run("Connection");};connection.Click+=recheck;status.Click+=recheck;connectionDetail.Click+=recheck;sidebar.Controls.Add(connection,0,4);sidebar.Controls.Add(Caption("1.5.4.3  ·  LOCAL FIRST",7.5F,Color.FromArgb(81,96,106)),0,5);
        var main=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(29,17,28,8),Margin=Padding.Empty};main.RowStyles.Add(new RowStyle(SizeType.Absolute,87));main.RowStyles.Add(new RowStyle(SizeType.Percent,100));main.RowStyles.Add(new RowStyle(SizeType.Absolute,48));main.RowStyles.Add(new RowStyle(SizeType.Absolute,20));shell.Controls.Add(main,1,0);
        var heading=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=Padding.Empty};heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));heading.RowStyles.Add(new RowStyle(SizeType.Absolute,41));heading.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        pageTitle.Text="我的壁纸";pageTitle.Font=new Font(Font.FontFamily,23F,FontStyle.Bold);pageTitle.Dock=DockStyle.Fill;heading.Controls.Add(pageTitle,0,0);pageSubtitle.Text="把喜欢的风景，留在每一次对话里。";pageSubtitle.Font=new Font(Font.FontFamily,8.5F);pageSubtitle.ForeColor=Muted;pageSubtitle.Dock=DockStyle.Fill;heading.Controls.Add(pageSubtitle,0,1);
        importButton.Text="＋  导入壁纸";((ActionButton)importButton).Primary=true;importButton.Dock=DockStyle.Fill;importButton.Margin=new Padding(0,5,0,20);importButton.AccessibleName="导入壁纸";importButton.Click+=delegate{ChooseAndImport();};heading.Controls.Add(importButton,1,0);heading.SetRowSpan(importButton,2);main.Controls.Add(heading,0,0);
        var pages=new Panel{Dock=DockStyle.Fill};main.Controls.Add(pages,0,1);libraryPage.Dock=DockStyle.Fill;pages.Controls.Add(libraryPage);servicePage.Dock=DockStyle.Fill;servicePage.Visible=false;pages.Controls.Add(servicePage);
        var content=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,292));libraryPage.Controls.Add(content);
        var grid=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=new Padding(0,0,22,0)};grid.RowStyles.Add(new RowStyle(SizeType.Absolute,50));grid.RowStyles.Add(new RowStyle(SizeType.Percent,100));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,40));content.Controls.Add(grid,0,0);
        var tools=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};tools.RowStyles.Add(new RowStyle(SizeType.Percent,100));tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,112));
        var searchBox=new Surface{Dock=DockStyle.Fill,BackColor=Color.FromArgb(24,28,33),Padding=new Padding(12,9,8,5),Margin=new Padding(0,0,11,10)};search.Dock=DockStyle.Fill;search.BorderStyle=BorderStyle.None;search.BackColor=searchBox.BackColor;search.ForeColor=Ink;search.AccessibleName="搜索壁纸";search.HandleCreated+=delegate{SetCueText(search.Handle,0x1501,IntPtr.Zero,"搜索壁纸名称…");};search.TextChanged+=delegate{pageNumber=0;RenderGallery();};searchBox.Controls.Add(search);tools.Controls.Add(searchBox,0,0);
        filters.Items.AddRange(new object[]{"全部类型","动态背景","静态背景"});filters.SelectedIndex=0;filters.FlatStyle=FlatStyle.Flat;filters.BackColor=Color.FromArgb(27,32,37);filters.ForeColor=Ink;filters.Dock=DockStyle.Fill;filters.Margin=new Padding(0,0,0,10);filters.SelectedIndexChanged+=delegate{typeFilter=filters.SelectedIndex==1?"animated":filters.SelectedIndex==2?"static":"all";pageNumber=0;RenderGallery();};tools.Controls.Add(filters,1,0);grid.Controls.Add(tools,0,0);
        var galleryHost=new Panel{Dock=DockStyle.Fill};gallery.Dock=DockStyle.Fill;gallery.AutoScroll=false;gallery.WrapContents=true;gallery.Padding=Padding.Empty;gallery.Margin=Padding.Empty;gallery.Resize+=delegate{ResizeTiles();};galleryHost.Controls.Add(gallery);steamGallery.Dock=DockStyle.Fill;steamGallery.Visible=false;steamGallery.EntrySelected+=SelectEntry;galleryHost.Controls.Add(steamGallery);emptyLabel.Text="这里还没有壁纸。\r\n点击右上角导入，或扫描 Wallpaper Engine。";emptyLabel.Dock=DockStyle.Fill;emptyLabel.TextAlign=ContentAlignment.MiddleCenter;emptyLabel.ForeColor=Muted;emptyLabel.Visible=false;galleryHost.Controls.Add(emptyLabel);grid.Controls.Add(galleryHost,0,1);
        var pager=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty};pager.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));pager.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,49));pager.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,62));pager.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,49));countLabel.Dock=DockStyle.Fill;countLabel.ForeColor=Muted;countLabel.Font=new Font(Font.FontFamily,8F);countLabel.TextAlign=ContentAlignment.MiddleLeft;pager.Controls.Add(countLabel,0,0);previousPage.Text="‹";previousPage.Dock=DockStyle.Fill;previousPage.Margin=new Padding(0,5,0,0);previousPage.Click+=delegate{if(pageNumber>0){pageNumber--;RenderGallery();}};pager.Controls.Add(previousPage,1,0);pageLabel.Dock=DockStyle.Fill;pageLabel.ForeColor=Muted;pageLabel.TextAlign=ContentAlignment.MiddleCenter;pager.Controls.Add(pageLabel,2,0);nextPage.Text="›";nextPage.Dock=DockStyle.Fill;nextPage.Margin=new Padding(0,5,0,0);nextPage.Click+=delegate{pageNumber++;RenderGallery();};pager.Controls.Add(nextPage,3,0);grid.Controls.Add(pager,0,2);
        inspector.Dock=DockStyle.Fill;inspector.Margin=new Padding(0,0,0,0);inspector.Padding=new Padding(16);inspector.BackColor=Color.FromArgb(20,24,28);content.Controls.Add(inspector,1,0);BuildInspector();BuildServicePage();
        var bottom=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=new Padding(0,8,0,0)};bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,128));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,138));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));scanButton.Text="刷新壁纸";scanButton.Dock=DockStyle.Fill;scanButton.Margin=new Padding(0,0,10,0);scanButton.Click+=delegate{if(page=="steam")ScanSteam();else RenderGallery();};bottom.Controls.Add(scanButton,0,0);chooseSteam.Text="定位 Steam 目录";chooseSteam.Dock=DockStyle.Fill;chooseSteam.Margin=new Padding(0,0,0,0);chooseSteam.Visible=false;chooseSteam.Click+=delegate{PickSteamFolder();};bottom.Controls.Add(chooseSteam,1,0);var signature=Caption("yz",8,Color.FromArgb(91,107,112));signature.TextAlign=ContentAlignment.MiddleRight;bottom.Controls.Add(signature,2,0);main.Controls.Add(bottom,0,2);
        previewHint.Text="选中卡片后预览，点击应用切换背景。";previewHint.Font=new Font(Font.FontFamily,8F);previewHint.ForeColor=Muted;previewHint.Dock=DockStyle.Fill;previewHint.AutoEllipsis=true;main.Controls.Add(previewHint,0,3);
        AddTitleBar();
        FormClosing+=delegate(object sender,FormClosingEventArgs e){connectionTimer.Stop();Interlocked.Increment(ref scanGeneration);CancelPreview();cancelStatus=true;lock(statusLock){try{if(statusProcess!=null&&!statusProcess.HasExited)statusProcess.Kill();}catch(InvalidOperationException){}}if(busy){closeRequested=true;e.Cancel=true;Hide();}};
        FormClosed+=delegate{connectionTimer.Dispose();};
        if(checkStatus){LoadSavedTheme();ImportCurrentTheme();if(selected==null){var recent=library.Snapshot().Where(x=>!String.IsNullOrEmpty(x.LastUsedUtc)).OrderByDescending(x=>x.LastUsedUtc).FirstOrDefault();if(recent!=null)SelectEntry(recent);}}RenderGallery();
        if(selected==null&&builtinEntries.Count>0)SelectEntry(builtinEntries[0]);
        if(!String.IsNullOrEmpty(library.Warning))log.Text=library.Warning;
        if(checkStatus){Shown+=delegate{Run("Connection");};connectionTimer.Tick+=delegate{if(!busy&&!checkingStatus)Run("Connection");};connectionTimer.Start();}
    }
    void BuildInspector(){
        inspector.AutoScroll=true;var layout=new BufferedTable{Dock=DockStyle.Top,Height=540,ColumnCount=1,RowCount=11,Margin=Padding.Empty};int[] heights={20,135,42,23,42,32,28,32,42,12,32};foreach(int h in heights)layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));inspector.Controls.Add(layout);
        layout.Controls.Add(Caption("壁纸预览",8.5F,Muted),0,0);preview.Dock=DockStyle.Fill;preview.Margin=new Padding(0,0,0,6);preview.BackColor=inspector.BackColor;layout.Controls.Add(preview,0,1);
        wallpaperName.Text="选择一张壁纸";wallpaperName.Font=new Font(Font.FontFamily,15F,FontStyle.Bold);wallpaperName.Dock=DockStyle.Fill;wallpaperName.AutoEllipsis=true;wallpaperName.TextAlign=ContentAlignment.MiddleLeft;layout.Controls.Add(wallpaperName,0,2);sourceLabel.Dock=DockStyle.Fill;sourceLabel.ForeColor=Mint;sourceLabel.Font=new Font(Font.FontFamily,8F);layout.Controls.Add(sourceLabel,0,3);
        media.Multiline=true;media.ReadOnly=true;media.BorderStyle=BorderStyle.None;media.BackColor=inspector.BackColor;media.ForeColor=Muted;media.Font=new Font(Font.FontFamily,8F);media.Dock=DockStyle.Fill;layout.Controls.Add(media,0,4);
        var alphaHeading=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};alphaHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));alphaHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,46));alphaHeading.Controls.Add(Caption("聊天框不透明度",8.5F,Ink),0,0);percentage.Text="72%";percentage.TextAlign=ContentAlignment.MiddleRight;percentage.ForeColor=Mint;percentage.Dock=DockStyle.Fill;alphaHeading.Controls.Add(percentage,1,0);layout.Controls.Add(alphaHeading,0,5);
        opacity.Minimum=10;opacity.Maximum=100;opacity.Value=72;opacity.Dock=DockStyle.Fill;opacity.Margin=Padding.Empty;opacity.BackColor=inspector.BackColor;opacity.ValueChanged+=delegate{percentage.Text=opacity.Value+"%";};layout.Controls.Add(opacity,0,6);layout.Controls.Add(Caption("数值越低，聊天框背景越透明。",7.5F,Muted),0,7);
        applyButton.Text="应用这张壁纸";((ActionButton)applyButton).Primary=true;applyButton.Dock=DockStyle.Fill;applyButton.Margin=Padding.Empty;applyButton.AccessibleName="应用壁纸";applyButton.Click+=delegate{ApplySelected();};layout.Controls.Add(applyButton,0,8);
        removeEntry.Text="从记录中移除";removeEntry.FlatStyle=FlatStyle.Flat;removeEntry.Dock=DockStyle.Top;removeEntry.Height=30;removeEntry.Font=new Font(Font.FontFamily,8F);removeEntry.Margin=Padding.Empty;removeEntry.Click+=delegate{ForgetSelected();};layout.Controls.Add(removeEntry,0,10);
    }
    void BuildServicePage(){
        var layout=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(0,8,0,0),Margin=Padding.Empty};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,64));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));servicePage.Controls.Add(layout);
        layout.Controls.Add(Caption("后台服务",16,Ink),0,0);layout.Controls.Add(Caption("装载后随 Windows 登录和 Codex 启动恢复，关闭管理器不影响背景。",9,Muted),0,1);
        commands.Dock=DockStyle.Fill;commands.WrapContents=false;commands.Margin=Padding.Empty;commands.Controls.Add(MakeButton("装载服务",true,false,delegate{Run("Install");}));commands.Controls.Add(MakeButton("卸载皮肤",false,false,delegate{ConfirmRemoval();}));commands.Controls.Add(MakeButton("卸载服务",false,true,delegate{ConfirmServiceRemoval();}));commands.Controls.Add(MakeButton("重新检测",false,false,delegate{Run("Connection");}));foreach(Control b in commands.Controls){b.Width=153;b.Height=42;}layout.Controls.Add(commands,0,2);layout.Controls.Add(Caption("运行记录",9,Muted),0,3);
        var card=new Surface{Dock=DockStyle.Fill,Padding=new Padding(15),BackColor=Color.FromArgb(20,24,28),Margin=Padding.Empty};log.Multiline=true;log.ReadOnly=true;log.BorderStyle=BorderStyle.None;log.ScrollBars=ScrollBars.Vertical;log.Dock=DockStyle.Fill;log.ForeColor=Muted;log.BackColor=card.BackColor;log.Font=new Font(Font.FontFamily,9F);log.Text="准备就绪。导入、选择，再点击应用壁纸。";card.Controls.Add(log);progress.Dock=DockStyle.Bottom;progress.Height=3;progress.Visible=false;progress.Style=ProgressBarStyle.Marquee;card.Controls.Add(progress);layout.Controls.Add(card,0,4);
    }
    void Navigate(string destination){
        page=destination;pageNumber=0;foreach(var item in navigation){item.Selected=Convert.ToString(item.Tag)==page;item.Invalidate();}
        bool settings=page=="settings";libraryPage.Visible=!settings;servicePage.Visible=settings;importButton.Visible=!settings;chooseSteam.Visible=page=="steam";scanButton.Visible=!settings;scanButton.Text=page=="steam"?"扫描 / 刷新":"刷新壁纸";
        pageTitle.Text=page=="local"?"我的壁纸":page=="steam"?"Wallpaper Engine":page=="history"?"最近使用":"服务与设置";
        pageSubtitle.Text=page=="local"?"把喜欢的风景，留在每一次对话里。":page=="steam"?"发现 Steam 壁纸，保留原文件，轻松应用。":page=="history"?"用过的风景，都在这里。":"背景常驻，管理器随时可以关闭。";
        if(!settings)RenderGallery();if(page=="steam"&&!scanning&&steamEntries.Count==0)ScanSteam();
    }
    List<WallpaperEntry> VisibleEntries(){
        IEnumerable<WallpaperEntry> entries=page=="steam"?steamEntries:page=="history"?library.Snapshot().Where(x=>!String.IsNullOrEmpty(x.LastUsedUtc)).OrderByDescending(x=>x.LastUsedUtc):builtinEntries.Concat(library.Snapshot().Where(x=>x.Source!="steam"&&x.Source!="builtin").OrderByDescending(x=>x.AddedUtc));
        if(typeFilter=="animated")entries=entries.Where(x=>x.Animated);else if(typeFilter=="static")entries=entries.Where(x=>!x.Animated);
        string term=search.Text.Trim();if(term.Length>0)entries=entries.Where(x=>x.Name.IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0);
        return entries.GroupBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).Select(x=>x.First()).ToList();
    }
    void RenderGallery(){
        if(library==null||page=="settings")return;var entries=VisibleEntries();bool continuous=page=="steam";gallery.Visible=!continuous;steamGallery.Visible=continuous;previousPage.Visible=pageLabel.Visible=nextPage.Visible=!continuous;
        if(continuous){steamGallery.SetEntries(entries,selected==null?null:selected.Id);countLabel.Text=entries.Count+" 张 · 向下滚动浏览";emptyLabel.Visible=entries.Count==0;if(emptyLabel.Visible)emptyLabel.BringToFront();else steamGallery.BringToFront();return;}
        renderedCapacity=GalleryColumns()*3;int pages=Math.Max(1,(entries.Count+renderedCapacity-1)/renderedCapacity);pageNumber=Math.Max(0,Math.Min(pageNumber,pages-1));gallery.SuspendLayout();
        foreach(var tile in tiles)tile.Dispose();tiles.Clear();gallery.Controls.Clear();
        foreach(var entry in entries.Skip(pageNumber*renderedCapacity).Take(renderedCapacity)){var tile=new WallpaperTile(entry){Font=Font,Selected=selected!=null&&selected.Id==entry.Id,Margin=new Padding(0,0,12,12)};tile.Click+=delegate{SelectEntry(tile.Entry);};tile.DoubleClick+=delegate{SelectEntry(tile.Entry);};tiles.Add(tile);gallery.Controls.Add(tile);}
        countLabel.Text=entries.Count+" 张"+(page=="history"?" · 按使用时间排序":" · 点击卡片预览");pageLabel.Text=(pageNumber+1)+" / "+pages;previousPage.Enabled=pageNumber>0;nextPage.Enabled=pageNumber+1<pages;emptyLabel.Visible=entries.Count==0;if(emptyLabel.Visible)emptyLabel.BringToFront();ResizeTiles();gallery.ResumeLayout();
    }
    int GalleryColumns(){return gallery.ClientSize.Width>1050?4:gallery.ClientSize.Width>=570?3:2;}
    void ResizeTiles(){if(page=="steam"||tiles.Count==0||gallery.ClientSize.Width<1)return;int columns=GalleryColumns();if(renderedCapacity!=columns*3){RenderGallery();return;}int width=Math.Max(150,(gallery.ClientSize.Width-20-(columns-1)*12)/columns),height=Math.Max(100,Math.Min((int)(width*.56)+54,(gallery.ClientSize.Height-36)/3));foreach(var tile in tiles)tile.Size=new Size(width,height);}
    void CancelPreview(){Interlocked.Increment(ref previewGeneration);if(previewJob!=null){previewJob.Dispose();previewJob=null;}}
    void SelectEntry(WallpaperEntry entry){
        if(busy||entry==null)return;CancelPreview();selected=entry;wallpaperName.Text=entry.Name;media.Text=Path.GetFileName(entry.Path);sourceLabel.Text=entry.Source=="builtin"?(entry.Note??"内置背景"):entry.Source=="steam"?entry.Note:entry.Animated?"本地动态背景 · 静音循环":"本地静态背景";
        applyButton.Enabled=entry.Available;removeEntry.Enabled=library.Snapshot().Any(x=>x.Id==entry.Id);previewHint.Text=entry.Available?entry.Animated?"正在准备静音循环预览…":"已选中，点击应用壁纸。":"源文件已移动或磁盘离线，可重新导入；移除记录不会删除原文件。";
        foreach(var tile in tiles){tile.Selected=tile.Entry.Id==entry.Id;tile.Invalidate();}
        steamGallery.SetSelected(entry.Id);
        string thumbnail=!String.IsNullOrEmpty(entry.Thumbnail)&&File.Exists(entry.Thumbnail)?entry.Thumbnail:entry.Animated?"":entry.Path;preview.SetMedia(thumbnail);
        if(!entry.Available)return;
        if(entry.Animated||Path.GetExtension(entry.Path).Equals(".webp",StringComparison.OrdinalIgnoreCase)){
            int generation=previewGeneration;var work=new MediaPreviewJob();previewJob=work;
            work.Start(root,stateDirectory,entry.Path,message=>Ui(delegate{if(generation==previewGeneration)previewHint.Text=message;}),(file,error)=>Ui(delegate{
                if(generation!=previewGeneration||IsDisposed)return;
                if(!String.IsNullOrEmpty(file)&&File.Exists(file)){preview.SetMedia(file);try{library.RememberPreview(entry,file);}catch(Exception ex){log.Text=ex.Message;}foreach(var tile in tiles)if(tile.Entry.Id==entry.Id)tile.Picture.SetMedia(file);steamGallery.UpdateThumbnail(entry.Id,file);previewHint.Text=Path.GetExtension(file)==".png"?"已使用静态缩略图；应用时保留完整视频。":"静音循环预览 · 应用时保留完整时长。";}
                else{previewHint.Text="预览暂不可用，可以继续尝试应用壁纸。";log.Text="预览："+error;}
            }));
        }
    }
    void ChooseAndImport(){
        if(busy)return;using(var dialog=new OpenFileDialog{Title="导入本地壁纸",Filter="支持的素材|*.mp4;*.webm;*.gif;*.png;*.jpg;*.jpeg;*.webp",CheckFileExists=true,Multiselect=true}){
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;WallpaperEntry last=null;try{foreach(string file in dialog.FileNames)last=library.Add(file,null,"local",null,"本地文件引用；不会复制或删除原文件");Navigate("local");SelectEntry(last);}catch(Exception error){MessageBox.Show(this,error.Message,"导入壁纸");}
        }
    }
    void ApplySelected(){if(busy||selected==null)return;if(!selected.Available){previewHint.Text="源文件不可用，请重新导入或连接磁盘。";return;}CancelPreview();Run("Apply");}
    void AppliedToHistory(WallpaperEntry entry){if(entry==null)return;try{library.Applied(entry);}catch(Exception error){log.AppendText(Environment.NewLine+"壁纸已应用，但使用记录保存失败："+error.Message);}previewHint.Text="✓ 已应用 "+entry.Name+"；关闭管理器后背景继续运行。";removeEntry.Enabled=true;if(page=="history")RenderGallery();}
    void ForgetSelected(){if(busy||selected==null)return;try{library.Forget(selected.Id);removeEntry.Enabled=false;previewHint.Text="已移除记录，原壁纸文件保留。";RenderGallery();}catch(Exception error){previewHint.Text="记录保存失败："+error.Message;log.Text=error.ToString();}}
    void ImportCurrentTheme(){
        if(library.CurrentThemeMigrated)return;
        try{if(library.Snapshot().Count==0&&File.Exists(media.Text)){string thumbnail=null;var saved=Path.Combine(stateDirectory,"manager-preview.json");if(File.Exists(saved)){var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(saved));object p;if(data.TryGetValue("path",out p))thumbnail=Convert.ToString(p);}var entry=library.Add(media.Text,wallpaperName.Text,"recovered",thumbnail,"从当前已应用壁纸恢复");library.Applied(entry);selected=entry;SelectEntry(entry);}library.FinishCurrentThemeMigration();}catch(Exception error){log.Text=error.Message;}
    }
    void PickSteamFolder(){using(var dialog=new FolderBrowserDialog{Description="选择 Steam 库目录，或 steamapps/workshop/content/431960 文件夹"})if(dialog.ShowDialog(this)==DialogResult.OK)try{library.AddSteamRoot(dialog.SelectedPath);ScanSteam();}catch(Exception error){previewHint.Text="目录保存失败："+error.Message;}}
    void ScanSteam(){
        if(scanning)return;scanning=true;scanButton.Enabled=false;int generation=++scanGeneration;previewHint.Text="正在查找 Steam 库和 Wallpaper Engine 壁纸…";var extra=library.SteamRoots();
        ThreadPool.QueueUserWorkItem(delegate{SteamScanResult result=null;string error=null;try{result=SteamWallpapers.Scan(SteamWallpapers.DiscoverRoots(extra),()=>IsDisposed||generation!=scanGeneration);}catch(Exception ex){error=ex.Message;}Ui(delegate{scanning=false;scanButton.Enabled=true;if(generation!=scanGeneration)return;if(result==null){previewHint.Text="扫描失败："+error;return;}steamEntries=result.Entries;if(page=="steam")RenderGallery();previewHint.Text=result.WorkshopRoots.Count==0?"未找到 Wallpaper Engine 壁纸库，可点击定位 Steam 目录。":"发现 "+result.Entries.Count+" 张壁纸"+(result.Unsupported>0?"；场景项目仅提供预览图，不能播放 scene.pkg。":"。")+(result.Truncated?" 已达到扫描上限。":"");});});
    }
    void UpdateConnection(string json){
        try{
            var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);object value;
            bool connected=data.TryGetValue("connected",out value)&&Convert.ToBoolean(value),installed=data.TryGetValue("installed",out value)&&Convert.ToBoolean(value),service=data.TryGetValue("serviceRunning",out value)&&Convert.ToBoolean(value);
            string version=data.TryGetValue("version",out value)?Convert.ToString(value):"",background=data.TryGetValue("background",out value)?Convert.ToString(value):"unknown";
            status.Text=connected?"●  Codex 连接就绪":installed?"○  等待 Codex 连接":"○  未检测到 Codex";status.ForeColor=connected?Mint:Muted;
            connectionDetail.Text=(installed?"Codex "+version:"请安装官方稳定版")+Environment.NewLine+(service?"背景服务运行中":"背景服务未装载")+(connected?Environment.NewLine+BackgroundStatusText(background):"");
        }catch{status.Text="○  连接检测失败";status.ForeColor=Muted;}
    }
    static string BackgroundStatusText(string state){switch(state){case "playing":return "视频播放中";case "image-ready":return "图片背景已就绪";case "waiting-visible":return "视频等待窗口显示";case "not-applied":return "尚未应用壁纸";case "loading":return "壁纸加载中";case "paused":return "视频当前暂停";case "reduced-motion":return "减少动态效果：视频暂停";case "blocked":return "视频播放被阻止";case "error":return "壁纸出错，请重新应用";case "stalled":return "视频暂未推进，请重试";default:return "壁纸状态暂不可用";}}
    void SetBusyControls(bool value){commands.Enabled=!value;opacity.Enabled=!value;importButton.Enabled=!value;applyButton.Enabled=!value&&selected!=null&&selected.Available;removeEntry.Enabled=!value&&selected!=null&&library.Snapshot().Any(x=>x.Id==selected.Id);}
    int TestSteamScroll(string report){
        Show();for(int i=0;i<12;i++){Thread.Sleep(25);Application.DoEvents();}
        for(int i=0;i<5000;i++)steamEntries.Add(new WallpaperEntry{Id="scroll-fixture-"+i,Name=i==4999?"最后一张 · 完整壁纸库":"风景 "+(i+1).ToString("D4"),Path=builtinEntries[i%6].Path,Thumbnail=builtinEntries[i%6].Path,Source="steam",Kind=i%2==0?"video":"image",Note="测试目录 · 原创背景示例"});
        Navigate("steam");Application.DoEvents();
        if(previousPage.Visible||nextPage.Visible||pageLabel.Visible||gallery.Visible||!steamGallery.Visible)throw new Exception("Steam pagination was not replaced");
        if(steamGallery.MaximumOffset<=0||steamGallery.ScrollOffset!=0||steamGallery.RealizedCount>=30)throw new Exception("Steam list did not virtualize 5000 entries");
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var wheel=new HandledMouseEventArgs(MouseButtons.None,0,20,20,-120);
        typeof(ContinuousWallpaperGallery).GetMethod("OnMouseWheel",flags).Invoke(steamGallery,new object[]{wheel});
        if(!wheel.Handled||steamGallery.ScrollOffset<=0)throw new Exception("Mouse wheel did not scroll");
        // Wheel events over the thumbnail must scroll exactly once.
        int before=steamGallery.ScrollOffset;var picture=steamGallery.VisibleCards.First().Picture;
        typeof(Control).GetMethod("OnMouseWheel",flags).Invoke(picture,new object[]{new HandledMouseEventArgs(MouseButtons.None,0,20,20,-120)});
        if(steamGallery.ScrollOffset-before!=before)throw new Exception("Thumbnail wheel event did not scroll exactly once");
        var bar=steamGallery.Scrollbar;bar.Value=0;Rectangle thumb=bar.Thumb;
        typeof(GalleryScrollBar).GetMethod("OnMouseDown",flags).Invoke(bar,new object[]{new MouseEventArgs(MouseButtons.Left,1,thumb.X+2,thumb.Y+2,0)});
        typeof(GalleryScrollBar).GetMethod("OnMouseMove",flags).Invoke(bar,new object[]{new MouseEventArgs(MouseButtons.Left,0,thumb.X+2,bar.Height-1,0)});
        typeof(GalleryScrollBar).GetMethod("OnMouseUp",flags).Invoke(bar,new object[]{new MouseEventArgs(MouseButtons.Left,1,thumb.X+2,bar.Height-1,0)});
        if(steamGallery.ScrollOffset!=steamGallery.MaximumOffset)throw new Exception("Scrollbar drag did not reach the last row");
        var last=steamGallery.VisibleCards.Single(x=>x.Entry.Id=="scroll-fixture-4999");last.PerformClick();
        if(selected.Id!=last.Entry.Id||wallpaperName.Text!=last.Entry.Name||!last.Selected)throw new Exception("Last card selection failed");
        if(last.Bottom>last.Parent.ClientSize.Height||last.Bottom<=0)throw new Exception("Last row is clipped");
        foreach(var tile in steamGallery.VisibleCards)if(tile.Left<0||tile.Right>tile.Parent.ClientSize.Width)throw new Exception("Horizontal overflow");
        steamGallery.ScrollTo(0);steamGallery.ScrollTo(steamGallery.MaximumOffset);
        if(!steamGallery.VisibleCards.Single(x=>x.Entry.Id==selected.Id).Selected)throw new Exception("Selection lost after recycling cards");
        typeof(GalleryScrollBar).GetMethod("OnKeyDown",flags).Invoke(bar,new object[]{new KeyEventArgs(Keys.Home)});
        if(steamGallery.ScrollOffset!=0)throw new Exception("Keyboard Home did not reset scroll");
        typeof(GalleryScrollBar).GetMethod("OnKeyDown",flags).Invoke(bar,new object[]{new KeyEventArgs(Keys.End)});
        if(steamGallery.ScrollOffset!=steamGallery.MaximumOffset)throw new Exception("Keyboard End did not reach the last entry");
        search.Text="最后一张";Application.DoEvents();if(steamGallery.RealizedCount!=1||steamGallery.ScrollOffset!=0||steamGallery.MaximumOffset!=0)throw new Exception("Search did not reset scroll");
        search.Text="";steamGallery.ScrollTo(steamGallery.MaximumOffset);filters.SelectedIndex=2;
        if(VisibleEntries().Count!=2500||steamGallery.ScrollOffset!=0)throw new Exception("Static filter did not reset scroll");
        filters.SelectedIndex=1;if(VisibleEntries().Count!=2500)throw new Exception("Dynamic filter failed");filters.SelectedIndex=0;
        var large=steamEntries;steamEntries=large.Take(53).ToList();for(int i=0;i<steamEntries.Count;i++){steamEntries[i].Name=builtinEntries[i%6].Name+" · "+(i+1).ToString("D2");steamEntries[i].Kind="image";steamEntries[i].Note="Wallpaper Engine · 示例壁纸";}RenderGallery();Application.DoEvents();
        steamGallery.ScrollTo(0);SelectEntry(steamEntries[3]);
        for(int i=0;i<20;i++){Thread.Sleep(25);Application.DoEvents();}
        using(var image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(0,0,Width,Height));image.Save(Path.ChangeExtension(report,".png"));}
        // More local entries than one page, with unique references.
        Directory.CreateDirectory(stateDirectory);
        for(int i=0;i<10;i++){string path=Path.Combine(stateDirectory,"scroll-local-"+i+".jpg");File.Copy(builtinEntries[0].Path,path);library.Add(path,"本地条目 "+i,"local",path,null);}
        Navigate("local");Application.DoEvents();if(!nextPage.Visible||!nextPage.Enabled||steamGallery.Visible||tiles.Count!=renderedCapacity)throw new Exception("Local pagination changed");
        File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{pass=true,entries=5000,continuousSteam=true,noPageButtons=true,wheel=true,thumbnailWheelOnce=true,dragToLast=true,keyboard=true,lastCardSelectable=true,selectionPersists=true,searchResetsScroll=true,typeFilterResetsScroll=true,boundedCards=true,noHorizontalOverflow=true,localPaginationPreserved=true,oldWindowTestsRepeated=false}));return 0;
    }
    int TestGallery(string report){
        Show();for(int i=0;i<40;i++){Thread.Sleep(25);Application.DoEvents();}
        if(builtinEntries.Count<6||tiles.Count!=Math.Min(builtinEntries.Count,renderedCapacity)||!preview.HasPicture)throw new Exception("Built-in gallery did not load");
        var target=tiles[2];target.AccessibilityObject.DoDefaultAction();Application.DoEvents();
        if(selected.Id!=target.Entry.Id||wallpaperName.Text!=target.Entry.Name)throw new Exception("Card click did not select wallpaper");
        search.Text="雾林";if(tiles.Count!=1)throw new Exception("Gallery search failed");search.Text="";filters.SelectedIndex=1;if(tiles.Count!=builtinEntries.Count(x=>x.Animated))throw new Exception("Dynamic filter failed");filters.SelectedIndex=0;
        library.Applied(selected);Navigate("history");if(tiles.Count!=1)throw new Exception("History did not show successful application");
        var reopened=new WallpaperLibrary(stateDirectory);if(reopened.Snapshot().Count!=1||String.IsNullOrEmpty(reopened.Snapshot()[0].LastUsedUtc))throw new Exception("History did not persist");
        string original=selected.Path;ForgetSelected();if(tiles.Count!=0||!File.Exists(original))throw new Exception("Removing history deleted the source");
        UpdateConnection("{\"installed\":true,\"connected\":false,\"serviceRunning\":true,\"version\":\"26.1016\"}");if(status.ForeColor==Mint)throw new Exception("Unverified connection shown green");
        UpdateConnection("{\"installed\":true,\"connected\":true,\"serviceRunning\":true,\"version\":\"26.1016\"}");if(status.ForeColor!=Mint)throw new Exception("Verified connection not displayed");
        Navigate("settings");if(!servicePage.Visible||libraryPage.Visible||commands.Controls.Count!=4)throw new Exception("Service commands unavailable");Navigate("local");
        if(filters.Bottom>filters.Parent.ClientSize.Height)throw new Exception("Filter clipped by toolbar");
        string fixtures=Path.Combine(stateDirectory,"gallery-fixtures");Directory.CreateDirectory(fixtures);for(int i=0;i<10;i++){string path=Path.Combine(fixtures,"fixture-"+i+".jpg");File.Copy(builtinEntries[0].Path,path);library.Add(path,"分页条目 "+i,"local",path,null);}RenderGallery();Application.DoEvents();
        if(!nextPage.Enabled||tiles.Count!=renderedCapacity)throw new Exception("Gallery pagination missing");foreach(var tile in tiles)if(tile.Bottom>gallery.ClientSize.Height||tile.Right>gallery.ClientSize.Width)throw new Exception("Gallery card clipped");nextPage.PerformClick();if(pageNumber!=1||previousPage.Enabled!=true)throw new Exception("Gallery next page failed");
        File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{pass=true,builtins=builtinEntries.Count,cardClick=true,search=true,typeFilter=true,historyPersists=true,sourcePreserved=true,verifiedStatusOnly=true,serviceCommands=true,pagination=true,cardsFit=true}));return 0;
    }
}
