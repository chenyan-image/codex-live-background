using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Windows.Forms;

internal sealed class TypeFilter : ActionButton {
    public readonly List<object> Items=new List<object>();int selected;
    readonly ContextMenuStrip menu=new ContextMenuStrip{BackColor=Color.FromArgb(27,32,37),ForeColor=Color.FromArgb(225,235,230),ShowImageMargin=false};
    public event EventHandler SelectedIndexChanged;
    public int SelectedIndex {get{return selected;}set{if(selected==value){if(Items.Count>value)Text=Items[value]+"  ▾";return;}selected=value;Text=Items[value]+"  ▾";if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}}
    protected override void OnClick(EventArgs e){
        base.OnClick(e);if(IsDisposed||Disposing||Items.Count==0)return;if(menu.Visible){menu.Close();return;}
        for(int i=menu.Items.Count-1;i>=0;i--){var old=menu.Items[i];menu.Items.RemoveAt(i);old.Dispose();}
        for(int i=0;i<Items.Count;i++){int index=i;var item=new ToolStripMenuItem(Convert.ToString(Items[i])){Checked=i==selected};item.Click+=delegate{SelectedIndex=index;};menu.Items.Add(item);}
        menu.Font=Font;menu.Show(this,new Point(0,Height));
    }
    // WinForms continues using the popup after Closed returns. Keep it alive
    // until its owning filter is disposed instead of disposing inside Closed.
    protected override void Dispose(bool disposing){if(disposing)menu.Dispose();base.Dispose(disposing);}
}

internal sealed class NavigationButton : Button {
    public bool Selected;public string Glyph;
    bool hover;
    public NavigationButton(){DoubleBuffered=true;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Height=46;Cursor=Cursors.Hand;TextAlign=ContentAlignment.MiddleLeft;Margin=new Padding(0,2,0,2);}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.Clear(Parent.BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
        if(Selected||hover)using(var p=IconAssets.Round(new RectangleF(0,0,Width-1,Height-1),10))using(var b=new SolidBrush(Selected?Color.FromArgb(31,47,44):Color.FromArgb(25,28,31)))g.FillPath(b,p);
        Color color=Selected?Color.FromArgb(147,232,197):Color.FromArgb(140,150,157);
        using(var pen=new Pen(color,1.6F)){
            if(Glyph=="recent"){g.DrawEllipse(pen,17,14,18,18);g.DrawLines(pen,new[]{new Point(26,17),new Point(26,23),new Point(30,25)});}
            else if(Glyph=="steam"){g.DrawEllipse(pen,25,12,10,10);g.DrawEllipse(pen,16,26,7,7);g.DrawLine(pen,22,27,28,21);g.DrawLine(pen,15,27,10,24);}
            else if(Glyph=="settings"){g.DrawLine(pen,16,18,36,18);g.DrawLine(pen,16,28,36,28);g.DrawEllipse(pen,20,15,6,6);g.DrawEllipse(pen,29,25,6,6);}
            else if(Glyph=="pet"){g.DrawEllipse(pen,20,24,12,9);g.DrawEllipse(pen,15,17,5,6);g.DrawEllipse(pen,22,12,5,7);g.DrawEllipse(pen,30,16,5,6);}
            else{g.DrawRectangle(pen,16,14,20,17);g.DrawLines(pen,new[]{new Point(17,29),new Point(23,22),new Point(28,27),new Point(32,23),new Point(35,27)});g.DrawEllipse(pen,28,17,3,3);}
        }
        TextRenderer.DrawText(g,Text,Font,new Rectangle(48,0,Width-51,Height),Selected?Color.FromArgb(225,245,233):Color.FromArgb(161,171,178),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
    }
}

internal sealed class WallpaperTile : UserControl {
    public readonly WallpaperEntry Entry;
    public readonly WallpaperPreview Picture=new WallpaperPreview();
    public bool Selected;bool hover;
    public WallpaperTile(WallpaperEntry entry){
        Entry=entry;DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw|ControlStyles.AllPaintingInWmPaint,true);BackColor=Color.FromArgb(22,25,28);Cursor=Cursors.Hand;TabStop=true;AccessibleName=entry.Name;AccessibleRole=AccessibleRole.ListItem;
        Picture.Animate=false;Picture.Cursor=Cursors.Hand;Picture.BackColor=BackColor;Controls.Add(Picture);Picture.Click+=delegate{OnClick(EventArgs.Empty);};Picture.DoubleClick+=delegate{OnDoubleClick(EventArgs.Empty);};Picture.MouseEnter+=delegate{hover=true;Invalidate();};Picture.MouseLeave+=delegate{hover=false;Invalidate();};
        Picture.SetMedia(!String.IsNullOrEmpty(entry.Thumbnail)&&System.IO.File.Exists(entry.Thumbnail)?entry.Thumbnail:entry.Animated?"":entry.Path);
    }
    public void PerformClick(){OnClick(EventArgs.Empty);}
    protected override AccessibleObject CreateAccessibilityInstance(){return new TileAccess(this);}
    sealed class TileAccess : ControlAccessibleObject {
        readonly WallpaperTile owner;public TileAccess(WallpaperTile tile):base(tile){owner=tile;}
        public override string DefaultAction {get{return "预览壁纸";}}
        public override void DoDefaultAction(){owner.PerformClick();}
    }
    protected override void OnResize(EventArgs e){base.OnResize(e);Picture.Bounds=new Rectangle(7,7,Math.Max(1,Width-14),Math.Max(1,Height-57));}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space){PerformClick();e.Handled=true;}base.OnKeyDown(e);}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.Clear(Parent.BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var p=IconAssets.Round(new RectangleF(1,1,Width-3,Height-3),12))using(var b=new SolidBrush(BackColor))using(var pen=new Pen(Selected?Color.FromArgb(139,229,191):hover?Color.FromArgb(72,87,89):Color.FromArgb(38,42,47),Selected?2:1)){g.FillPath(b,p);g.DrawPath(pen,p);}
        TextRenderer.DrawText(g,Entry.Name,Font,new Rectangle(12,Height-44,Width-40,21),Entry.Available?Color.FromArgb(226,233,231):Color.FromArgb(151,153,155),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        string type=!Entry.Available?"源文件不可用":Entry.Note!=null&&Entry.Note.StartsWith("场景")?"场景预览图":Entry.Animated?"动态背景":Entry.Source=="builtin"?"内置背景":"静态背景";
        using(var font=new Font(Font.FontFamily,8F))TextRenderer.DrawText(g,type,font,new Rectangle(12,Height-23,Width-24,18),Color.FromArgb(124,141,147),TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
        if(Selected)using(var b=new SolidBrush(Color.FromArgb(139,229,191)))g.FillEllipse(b,Width-26,Height-38,8,8);
        if(Entry.Animated){int x=Width-39;using(var b=new SolidBrush(Color.FromArgb(185,12,18,22)))g.FillEllipse(b,x,17,23,23);using(var pen=new Pen(Color.FromArgb(220,235,229),1.2F))g.DrawPolygon(pen,new[]{new Point(x+9,23),new Point(x+9,34),new Point(x+16,28)});}
    }
}

// Keep a large Steam catalog scrollable without allocating a control or decoding
// a thumbnail for every workshop item. Cards exist only around the visible rows.
internal sealed class ContinuousWallpaperGallery : Panel {
    readonly Panel viewport=new Panel();
    readonly GalleryScrollBar scrollbar=new GalleryScrollBar();
    readonly Dictionary<int,WallpaperTile> cards=new Dictionary<int,WallpaperTile>();
    readonly List<WallpaperEntry> entries=new List<WallpaperEntry>();
    string selectedId;int columns=1,tileWidth=180,tileHeight=154,offset;bool arranging;
    public event Action<WallpaperEntry> EntrySelected;
    public int ScrollOffset {get{return offset;}}
    public int MaximumOffset {get{return scrollbar.Maximum;}}
    public int RealizedCount {get{return cards.Count;}}
    public int ColumnCount {get{return columns;}}
    public int RowHeight {get{return tileHeight+12;}}
    public IEnumerable<WallpaperTile> VisibleCards {get{return cards.Values;}}
    internal GalleryScrollBar Scrollbar {get{return scrollbar;}}
    public ContinuousWallpaperGallery(){
        DoubleBuffered=true;TabStop=true;AccessibleName="Wallpaper Engine 连续壁纸列表";
        viewport.Dock=DockStyle.Fill;scrollbar.Dock=DockStyle.Right;scrollbar.Width=14;Controls.Add(viewport);Controls.Add(scrollbar);
        scrollbar.ValueChanged+=delegate{offset=scrollbar.Value;RealizeCards();};
        viewport.MouseWheel+=delegate(object sender,MouseEventArgs e){Wheel(e);};
    }
    public void SetEntries(IEnumerable<WallpaperEntry> items,string selected){
        ClearCards();entries.Clear();entries.AddRange(items);selectedId=selected;offset=0;ArrangeCards();
    }
    public void SetSelected(string id){selectedId=id;foreach(var tile in cards.Values){tile.Selected=tile.Entry.Id==id;tile.Invalidate();}}
    public void UpdateThumbnail(string id,string path){foreach(var tile in cards.Values)if(tile.Entry.Id==id)tile.Picture.SetMedia(path);}
    public void ScrollTo(int value){scrollbar.Value=value;}
    void ClearCards(){foreach(var tile in cards.Values)tile.Dispose();cards.Clear();}
    protected override void Dispose(bool disposing){if(disposing)ClearCards();base.Dispose(disposing);}
    protected override void OnResize(EventArgs e){base.OnResize(e);ArrangeCards();}
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);foreach(var tile in cards.Values)tile.Font=Font;}
    void ArrangeCards(){
        if(arranging||viewport==null||scrollbar==null||ClientSize.Width<1)return;arranging=true;
        try{
            int available=Math.Max(1,ClientSize.Width-scrollbar.Width-8);
            columns=available>1050?4:available>=570?3:2;
            tileWidth=Math.Max(1,(available-(columns-1)*12)/columns);tileHeight=Math.Max(100,(int)(tileWidth*.56)+54);
            int total=((entries.Count+columns-1)/columns)*(tileHeight+12);if(total>0)total-=12;
            int requested=offset;scrollbar.SetRange(Math.Max(0,total-viewport.ClientSize.Height),viewport.ClientSize.Height);
            scrollbar.Value=requested;offset=scrollbar.Value;RealizeCards();
        }finally{arranging=false;}
    }
    void RealizeCards(){
        if(viewport.ClientSize.Height<1)return;
        int firstRow=offset/RowHeight,lastRow=(offset+viewport.ClientSize.Height-1)/RowHeight+1;
        int start=firstRow*columns,end=Math.Min(entries.Count,(lastRow+1)*columns);
        var stale=new List<int>();foreach(int index in cards.Keys)if(index<start||index>=end)stale.Add(index);
        viewport.SuspendLayout();
        try{
            foreach(int index in stale){cards[index].Dispose();cards.Remove(index);}
            for(int index=start;index<end;index++){
                WallpaperTile tile;
                if(!cards.TryGetValue(index,out tile)){
                    tile=new WallpaperTile(entries[index]){Font=Font,Selected=entries[index].Id==selectedId};
                    var clicked=tile;tile.Click+=delegate{if(EntrySelected!=null)EntrySelected(clicked.Entry);};
                    tile.MouseWheel+=delegate(object sender,MouseEventArgs e){Wheel(e);};
                    tile.Picture.MouseWheel+=delegate(object sender,MouseEventArgs e){Wheel(e);};
                    cards.Add(index,tile);viewport.Controls.Add(tile);
                }
                tile.SetBounds((index%columns)*(tileWidth+12),(index/columns)*RowHeight-offset,tileWidth,tileHeight);
            }
        }finally{viewport.ResumeLayout();}
    }
    void Wheel(MouseEventArgs e){
        var handled=e as HandledMouseEventArgs;if(handled!=null&&handled.Handled)return;
        int lines=SystemInformation.MouseWheelScrollLines;
        int distance=lines<0?Math.Max(1,viewport.ClientSize.Height-RowHeight/2):Math.Max(1,lines)*36;
        ScrollTo(offset-(int)Math.Round(e.Delta/120.0*distance));if(handled!=null)handled.Handled=true;
    }
    protected override void OnMouseWheel(MouseEventArgs e){Wheel(e);base.OnMouseWheel(e);}
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData){
        switch(keyData){case Keys.PageDown:ScrollTo(offset+viewport.ClientSize.Height-24);return true;case Keys.PageUp:ScrollTo(offset-viewport.ClientSize.Height+24);return true;case Keys.Control|Keys.Home:ScrollTo(0);return true;case Keys.Control|Keys.End:ScrollTo(MaximumOffset);return true;default:return base.ProcessCmdKey(ref msg,keyData);}
    }
}

internal sealed class GalleryScrollBar : Control {
    int current,maximum,viewportHeight,grab;bool dragging;
    public event EventHandler ValueChanged;
    public int Maximum {get{return maximum;}}
    public int Value {get{return current;}set{int next=Math.Max(0,Math.Min(maximum,value));if(next==current)return;current=next;Invalidate();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}}
    internal Rectangle Thumb {
        get{int track=Math.Max(1,Height-8),height=Math.Min(track,Math.Max(34,(int)((long)track*viewportHeight/Math.Max(1,maximum+viewportHeight))));return new Rectangle(4,4+(maximum==0?0:(int)((long)(track-height)*current/maximum)),6,height);}
    }
    public GalleryScrollBar(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);TabStop=true;AccessibleRole=AccessibleRole.ScrollBar;AccessibleName="壁纸列表垂直滚动条";Cursor=Cursors.Hand;}
    public void SetRange(int max,int page){maximum=Math.Max(0,max);viewportHeight=Math.Max(1,page);Enabled=maximum>0;Value=current;Invalidate();}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Parent.BackColor);if(maximum==0)return;var thumb=Thumb;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var track=new SolidBrush(Color.FromArgb(27,33,37)))e.Graphics.FillRectangle(track,6,4,2,Math.Max(1,Height-8));using(var path=IconAssets.Round(thumb,3))using(var brush=new SolidBrush(dragging?Color.FromArgb(140,226,193):Color.FromArgb(76,104,101)))e.Graphics.FillPath(brush,path);}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left||maximum==0)return;Focus();var thumb=Thumb;if(!thumb.Contains(e.Location)){Value+=e.Y<thumb.Top?-viewportHeight:viewportHeight;return;}dragging=true;grab=e.Y-thumb.Top;Capture=true;Invalidate();}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!dragging)return;int travel=Math.Max(1,Height-8-Thumb.Height);Value=(int)Math.Round((e.Y-grab-4)*(double)maximum/travel);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left){dragging=false;Capture=false;Invalidate();}}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture){dragging=false;Invalidate();}}
    protected override void OnMouseWheel(MouseEventArgs e){Value-=(int)Math.Round(e.Delta/120.0*108);var handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;base.OnMouseWheel(e);}
    protected override bool IsInputKey(Keys key){return key==Keys.Up||key==Keys.Down||key==Keys.PageUp||key==Keys.PageDown||key==Keys.Home||key==Keys.End||base.IsInputKey(key);}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);switch(e.KeyCode){case Keys.Up:Value-=36;break;case Keys.Down:Value+=36;break;case Keys.PageUp:Value-=viewportHeight;break;case Keys.PageDown:Value+=viewportHeight;break;case Keys.Home:Value=0;break;case Keys.End:Value=maximum;break;default:return;}e.Handled=true;e.SuppressKeyPress=true;}
}
