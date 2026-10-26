using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed class PetOfflineChat : Form {
    public PetOfflineChat(string name,Func<string,string> reply){Text=name+" · 离线互动";ShowInTaskbar=false;FormBorderStyle=FormBorderStyle.FixedToolWindow;StartPosition=FormStartPosition.CenterScreen;ClientSize=new Size(390,205);BackColor=Color.FromArgb(20,24,30);ForeColor=Color.White;Font=new Font("Microsoft YaHei UI",9);
        var description=new Label{Text="本地文案互动 · 不调用 Codex · 不保存对话",AutoSize=false,Bounds=new Rectangle(18,17,354,28),ForeColor=Color.LightSteelBlue};var input=new TextBox{Bounds=new Rectangle(18,55,354,28),MaxLength=300};var output=new Label{Bounds=new Rectangle(18,94,354,57),Text="你好呀，我在这里陪你。"};var send=new Button{Text="聊一句",Bounds=new Rectangle(277,160,95,30)};send.Click+=delegate{if(!String.IsNullOrWhiteSpace(input.Text)){output.Text=reply(input.Text);input.Clear();}};Controls.AddRange(new Control[]{description,input,output,send});AcceptButton=send;
    }
}
internal sealed class PetBehaviorSettings : Form {
    readonly PetBehaviorProfile profile;readonly CheckBox whisper=new CheckBox(),work=new CheckBox(),quota=new CheckBox();readonly NumericUpDown interval=new NumericUpDown();readonly Dictionary<string,NumericUpDown> weights=new Dictionary<string,NumericUpDown>();
    public PetBehaviorSettings(PetPack pack,PetBehaviorProfile value){profile=value;profile.Normalize();Text=pack.Name+" · 互动与动画";ShowInTaskbar=false;StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;BackColor=Color.FromArgb(20,24,30);ForeColor=Color.FromArgb(226,235,245);Font=new Font("Microsoft YaHei UI",9);ClientSize=new Size(390,440);
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(20)};var rows=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,RowCount=0};rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,72));rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,28));scroll.Controls.Add(rows);Controls.Add(scroll);
        Action<Control,Control> add=(label,input)=>{int row=rows.RowCount++;rows.RowStyles.Add(new RowStyle(SizeType.Absolute,34));label.Dock=DockStyle.Fill;rows.Controls.Add(label,0,row);if(input!=null){input.Dock=DockStyle.Fill;rows.Controls.Add(input,1,row);}else rows.SetColumnSpan(label,2);};
        whisper.Text="自动碎碎念（离线）";whisper.Checked=profile.WhisperEnabled;work.Text="跟随任务状态";work.Checked=profile.WorkEnabled;quota.Text="真实额度事件动画";quota.Checked=profile.QuotaEnabled;add(whisper,null);add(work,null);add(quota,null);
        interval.Minimum=30;interval.Maximum=86400;interval.Value=profile.WhisperSeconds;add(new Label{Text="碎碎念间隔 / 秒"},interval);add(new Label{Text="随机动作权重 · 0 表示不选中",ForeColor=Color.LightSteelBlue},null);
        Action<string,int> weight=(key,initial)=>{var input=new NumericUpDown{Minimum=0,Maximum=100,Value=Math.Max(0,Math.Min(100,initial))};weights[key]=input;add(new Label{Text=key},input);};weight("待机",profile.IdleWeight);weight("转向",profile.TurnWeight);weight("移动",profile.MoveWeight);
        var director=new PetAnimationDirector(pack,profile,1);foreach(var category in director.Spec.categories??new PetActionCategory[0]){int chosen;weight(category.name,profile.CategoryWeights.TryGetValue(category.name,out chosen)?chosen:category.weight);}
        var save=new Button{Text="保存",Height=34,Dock=DockStyle.Bottom,FlatStyle=FlatStyle.Flat};Controls.Add(save);save.BringToFront();save.Click+=delegate{profile.WhisperEnabled=whisper.Checked;profile.WorkEnabled=work.Checked;profile.QuotaEnabled=quota.Checked;profile.WhisperSeconds=(int)interval.Value;profile.NextWhisperUtc=DateTime.UtcNow.AddSeconds(profile.WhisperSeconds);profile.IdleWeight=(int)weights["待机"].Value;profile.TurnWeight=(int)weights["转向"].Value;profile.MoveWeight=(int)weights["移动"].Value;foreach(var pair in weights.Where(p=>p.Key!="待机"&&p.Key!="转向"&&p.Key!="移动"))profile.CategoryWeights[pair.Key]=(int)pair.Value.Value;DialogResult=DialogResult.OK;Close();};
    }
}
