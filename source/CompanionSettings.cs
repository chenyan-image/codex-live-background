using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed class PetBehaviorSettings : Form {
    readonly PetBehaviorProfile profile;readonly CheckBox work=new CheckBox(),quota=new CheckBox();readonly Dictionary<string,NumericUpDown> weights=new Dictionary<string,NumericUpDown>();
    public PetBehaviorSettings(PetPack pack,PetBehaviorProfile value){profile=value;profile.Normalize();Text=pack.Name+" · 互动与动画";ShowInTaskbar=false;StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;BackColor=Color.FromArgb(20,24,30);ForeColor=Color.FromArgb(226,235,245);Font=new Font("Microsoft YaHei UI",9);ClientSize=new Size(390,440);
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(20)};var rows=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,RowCount=0};rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,72));rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,28));scroll.Controls.Add(rows);Controls.Add(scroll);
        Action<Control,Control> add=(label,input)=>{int row=rows.RowCount++;rows.RowStyles.Add(new RowStyle(SizeType.Absolute,34));label.Dock=DockStyle.Fill;rows.Controls.Add(label,0,row);if(input!=null){input.Dock=DockStyle.Fill;rows.Controls.Add(input,1,row);}else rows.SetColumnSpan(label,2);};
        work.Text="跟随任务状态";work.Checked=profile.WorkEnabled;quota.Text="真实额度事件动画";quota.Checked=profile.QuotaEnabled;add(work,null);add(quota,null);
        add(new Label{Text="分类轮换优先级 · 0 表示不选中",ForeColor=Color.LightSteelBlue},null);
        Action<string,int> weight=(key,initial)=>{var input=new NumericUpDown{Minimum=0,Maximum=100,Value=Math.Max(0,Math.Min(100,initial))};weights[key]=input;add(new Label{Text=key},input);};
        var director=new PetAnimationDirector(pack,profile,1);foreach(var category in director.Spec.categories??new PetActionCategory[0]){int chosen;weight(category.name,profile.CategoryWeights.TryGetValue(category.name,out chosen)?chosen:category.weight);}
        var save=new Button{Text="保存",Height=34,Dock=DockStyle.Bottom,FlatStyle=FlatStyle.Flat};Controls.Add(save);save.BringToFront();save.Click+=delegate{profile.WorkEnabled=work.Checked;profile.QuotaEnabled=quota.Checked;foreach(var pair in weights)profile.CategoryWeights[pair.Key]=(int)pair.Value.Value;DialogResult=DialogResult.OK;Close();};
    }
}
