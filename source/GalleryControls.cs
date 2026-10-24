using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Windows.Forms;

internal sealed class TypeFilter : ActionButton {
    public readonly List<object> Items=new List<object>();int selected;
    public event EventHandler SelectedIndexChanged;
    public int SelectedIndex {get{return selected;}set{if(selected==value){if(Items.Count>value)Text=Items[value]+"  ▾";return;}selected=value;Text=Items[value]+"  ▾";if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}}
    protected override void OnClick(EventArgs e){base.OnClick(e);var menu=new ContextMenuStrip{BackColor=Color.FromArgb(27,32,37),ForeColor=Color.FromArgb(225,235,230),ShowImageMargin=false};for(int i=0;i<Items.Count;i++){int index=i;var item=new ToolStripMenuItem(Convert.ToString(Items[i])){Checked=i==selected};item.Click+=delegate{SelectedIndex=index;};menu.Items.Add(item);}menu.Closed+=delegate{menu.Dispose();};menu.Show(this,new Point(0,Height));}
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
