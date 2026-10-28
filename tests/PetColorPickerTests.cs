using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Color input and layout regressions only. Every form remains hidden; all
// Manager preferences belong to its built-in isolated preview state.
internal static class PetColorPickerTests {
    static int checks;const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
    static void Need(bool condition,string error){checks++;if(!condition)throw new Exception(error);}
    static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,Hidden).GetValue(target);}
    static object Invoke(object target,string name,params object[] args){try{return target.GetType().GetMethod(name,Hidden).Invoke(target,args);}catch(TargetInvocationException error){throw error.InnerException??error;}}
    static void Click(Button button){typeof(Button).GetMethod("OnClick",Hidden).Invoke(button,new object[]{EventArgs.Empty});}
    static bool Key(Form form,Keys key){var message=Message.Create(form.Handle,0x100,(IntPtr)key,IntPtr.Zero);return (bool)typeof(HubColorPicker).GetMethod("ProcessCmdKey",Hidden).Invoke(form,new object[]{message,key});}
    static int Delta(Color a,Color b){return Math.Max(Math.Abs(a.R-b.R),Math.Max(Math.Abs(a.G-b.G),Math.Abs(a.B-b.B)));}
    static void MathProof(){
        Color[] known={Color.Red,Color.Yellow,Color.Lime,Color.Cyan,Color.Blue,Color.Magenta};
        for(int i=0;i<known.Length;i++)Need(Delta(HubColorMath.FromHsv(i*60,1,1),known[i])<=1,"HSV primary conversion failed: "+i);
        Need(Delta(HubColorMath.FromHsv(360,1,1),Color.Red)<=1,"Hue endpoint is not circular");
        Need(Delta(HubColorMath.FromHsv(200,0,.5),Color.FromArgb(128,128,128))<=1,"Zero saturation does not produce gray");
        Need(Delta(HubColorMath.FromHsv(120,1,0),Color.Black)==0,"Zero value does not produce black");
        foreach(var color in known.Concat(new[]{Color.Black,Color.White,Color.FromArgb(128,128,128),Color.FromArgb(78,143,228),Color.FromArgb(221,45,137)})){
            double h,s,v;HubColorMath.ToHsv(color,out h,out s,out v);
            Need(h>=0&&h<=360&&s>=0&&s<=1&&v>=0&&v<=1,"RGB conversion produced an out-of-range HSV value");
            Need(Delta(HubColorMath.FromHsv(h,s,v),color)<=1,"RGB/HSV roundtrip lost color: "+color);
        }
    }
    static void InputProof(){
        using(var picker=new HubColorPicker(Color.FromArgb(78,143,228),"中间胶囊")){
            var hex=Field<TextBox>(picker,"hexInput");var red=Field<TextBox>(picker,"redInput");var green=Field<TextBox>(picker,"greenInput");var blue=Field<TextBox>(picker,"blueInput");var confirm=Field<Button>(picker,"confirmButton");var validation=Field<Label>(picker,"validation");
            Need(picker.SelectedColor.ToArgb()==Color.FromArgb(78,143,228).ToArgb(),"Picker did not preserve initial color");
            hex.Text="#5C8EFF";Need(picker.SelectedColor.ToArgb()==Color.FromArgb(92,142,255).ToArgb(),"HEX input does not update candidate color");
            Need(red.Text=="92"&&green.Text=="142"&&blue.Text=="255","HEX input does not synchronize RGB fields");
            hex.Text="dd2d89";Need(picker.SelectedColor.ToArgb()==Color.FromArgb(221,45,137).ToArgb(),"HEX without prefix or lowercase is rejected");
            red.Text="17";green.Text="34";blue.Text="51";Need(picker.SelectedColor.ToArgb()==Color.FromArgb(17,34,51).ToArgb(),"RGB input does not update candidate color");
            Need(hex.Text.TrimStart('#').Equals("112233",StringComparison.OrdinalIgnoreCase),"RGB input does not synchronize HEX");
            foreach(string invalid in new[]{"","#","#12","#12345","#GGHHJJ","#12345678"}){
                hex.Text="#112233";Color before=picker.SelectedColor;hex.Text=invalid;
                Need(!confirm.Enabled&&!String.IsNullOrWhiteSpace(validation.Text),"Invalid HEX can be confirmed or has no inline error: "+invalid);
                Need(picker.SelectedColor.ToArgb()==before.ToArgb(),"Invalid HEX changed candidate color");
                Need(Key(picker,Keys.Enter)&&picker.DialogResult!=DialogResult.OK,"Enter bypassed invalid HEX validation");
            }
            foreach(string invalid in new[]{"-1","256","1.5","abc",""}){
                hex.Text="#112233";red.Text=invalid;Need(!confirm.Enabled&&!String.IsNullOrWhiteSpace(validation.Text),"Invalid RGB can be confirmed: "+invalid);
                Click(confirm);Need(picker.DialogResult!=DialogResult.OK,"Confirm click bypassed invalid RGB validation");
            }
            hex.Text="#C92278";Need(confirm.Enabled&&String.IsNullOrWhiteSpace(validation.Text),"Corrected input does not clear validation");
            Need(picker.AcceptButton==confirm&&picker.CancelButton==Field<Button>(picker,"cancelButton"),"Dialog keyboard buttons are not assigned");
            Key(picker,Keys.Enter);Need(picker.DialogResult==DialogResult.OK&&picker.SelectedColor.ToArgb()==Color.FromArgb(201,34,120).ToArgb(),"Enter did not confirm the chosen color");
        }
        using(var picker=new HubColorPicker(Color.Blue,"右侧圆环")){Field<TextBox>(picker,"hexInput").Text="#22AA66";Click(Field<Button>(picker,"cancelButton"));Need(picker.DialogResult==DialogResult.Cancel,"Cancel button accepted candidate color");}
        using(var picker=new HubColorPicker(Color.Blue,"统一配色")){Field<TextBox>(picker,"hexInput").Text="#22AA66";Need(Key(picker,Keys.Escape)&&picker.DialogResult==DialogResult.Cancel,"Esc did not cancel the color picker");}
        using(var picker=new HubColorPicker(Color.Blue,"左侧按钮")){Field<TextBox>(picker,"hexInput").Text="#FF6600";Click(Field<Button>(picker,"confirmButton"));Need(picker.DialogResult==DialogResult.OK&&picker.SelectedColor.ToArgb()==Color.FromArgb(255,102,0).ToArgb(),"Confirm button lost selected color");}
        using(var picker=new HubColorPicker(Color.Blue,"键盘调色")){
            var plane=Field<HubSaturationPlane>(picker,"plane");var hue=Field<HubHueStrip>(picker,"hueStrip");
            plane.SetState(120,.4,.5);var right=new KeyEventArgs(Keys.Right);Invoke(plane,"OnKeyDown",right);
            Need(right.Handled&&Math.Abs(plane.Saturation-.41)<.00001,"SV plane does not support saturation arrow keys");
            var up=new KeyEventArgs(Keys.Up|Keys.Shift);Invoke(plane,"OnKeyDown",up);Need(up.Handled&&Math.Abs(plane.Value-.55)<.00001,"SV plane does not support accelerated value keys");
            hue.Hue=359;right=new KeyEventArgs(Keys.Right);Invoke(hue,"OnKeyDown",right);Need(right.Handled&&Math.Abs(hue.Hue-1)<.00001,"Hue keyboard adjustment does not wrap");
            double h,s,v;HubColorMath.ToHsv(picker.SelectedColor,out h,out s,out v);Need(Math.Abs(h-hue.Hue)<2,"Hue keyboard change does not update selected color");
        }
    }
    static string Colors(PetPreferences prefs){return prefs.HubColor+"|"+prefs.CapsuleColor+"|"+prefs.RingColor;}
    static void RoutingProof(){
        using(var manager=(Form)Activator.CreateInstance(typeof(Manager),Hidden,null,new object[]{false},null)){
            var controller=Field<PetController>(manager,"pets");string state=Field<string>(manager,"stateDirectory");
            Need(!Field<bool>(manager,"liveStatusEnabled")&&!manager.Visible,"Color route fixture is a live or visible manager");
            Invoke(manager,"ApplyPetColor",PetColorTarget.All,"#4466CC");Need(Colors(controller.Preferences)=="#4466CC|#4466CC|#4466CC","Unified target does not update all components");
            Invoke(manager,"ApplyPetColor",PetColorTarget.Hub,"#FF3300");Need(Colors(controller.Preferences)=="#FF3300|#4466CC|#4466CC","Hub target alters other components");
            Invoke(manager,"ApplyPetColor",PetColorTarget.Capsule,"#00CC77");Need(Colors(controller.Preferences)=="#FF3300|#00CC77|#4466CC","Capsule target alters other components");
            Invoke(manager,"ApplyPetColor",PetColorTarget.Ring,"#AA2288");Need(Colors(controller.Preferences)=="#FF3300|#00CC77|#AA2288","Ring target alters other components");
            string preferences=Path.Combine(state,"pet-settings.json"),before=File.ReadAllText(preferences),colors=Colors(controller.Preferences);
            using(var picker=new HubColorPicker(ColorTranslator.FromHtml(controller.Preferences.RingColor),"右侧圆环")){Field<TextBox>(picker,"hexInput").Text="#112233";Key(picker,Keys.Escape);Need(picker.DialogResult==DialogResult.Cancel,"Cancel route fixture failed");}
            Need(Colors(controller.Preferences)==colors&&File.ReadAllText(preferences)==before,"Cancelled picker changed Manager preferences");
            Need(Colors(PetPreferences.Load(state))==colors,"Target selections do not persist in isolated preferences");
        }
    }
    static IEnumerable<Control> Descendants(Control root){foreach(Control child in root.Controls){yield return child;foreach(var nested in Descendants(child))yield return nested;}}
    static Rectangle InForm(Control control,Control root){var bounds=control.Bounds;for(var parent=control.Parent;parent!=null&&parent!=root;parent=parent.Parent)bounds.Offset(parent.Left,parent.Top);return bounds;}
    static Rectangle VisibleClip(Control control,Control root){var visible=InForm(control,root);for(var parent=control.Parent;parent!=null;parent=parent.Parent){var clip=parent.ClientRectangle;if(parent!=root)clip.Offset(InForm(parent,root).Location);visible.Intersect(clip);if(parent==root)break;}return visible;}
    static int BrightTextPixels(Bitmap image,Rectangle bounds){int count=0;var clipped=Rectangle.Intersect(new Rectangle(Point.Empty,image.Size),bounds);for(int y=clipped.Top;y<clipped.Bottom;y++)for(int x=clipped.Left;x<clipped.Right;x++){Color pixel=image.GetPixel(x,y);if(pixel.R>160&&pixel.G>160&&pixel.B>160)count++;}return count;}
    static Bitmap HiddenTree(Control control){
        IntPtr handle=control.Handle;var image=new Bitmap(Math.Max(1,control.Width),Math.Max(1,control.Height),PixelFormat.Format32bppArgb);control.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
        // WM_PRINT skips descendants whose top-level Form is hidden. Render each
        // child explicitly, back to front, without ever making its HWND visible.
        using(var graphics=Graphics.FromImage(image)){
            graphics.SetClip(control.ClientRectangle);
            for(int i=control.Controls.Count-1;i>=0;i--){var child=control.Controls[i];if(child.Width<1||child.Height<1)continue;using(var layer=HiddenTree(child))graphics.DrawImageUnscaled(layer,child.Left,child.Top);}
        }return image;
    }
    static Bitmap Render(Form picker,string file){IntPtr handle=picker.Handle;foreach(var control in Descendants(picker)){IntPtr child=control.Handle;}picker.PerformLayout();foreach(var control in Descendants(picker))control.PerformLayout();var image=HiddenTree(picker);image.Save(file,ImageFormat.Png);return image;}
    static bool Disposed(Bitmap image){if(image==null)return true;try{image.GetPixel(0,0);return false;}catch(ArgumentException){return true;}catch(ObjectDisposedException){return true;}}
    static void CacheDisposed(object control,Bitmap previous,string field){Need(Disposed(previous),"Color bitmap was not released: "+field);}
    static void LayoutAndCacheProof(string output){
        foreach(float scale in new[]{1F,1.25F,1.5F,2F}){
            var picker=new HubColorPicker(Color.FromArgb(78,143,228),"中间胶囊");var area=Screen.AllScreens.OrderBy(screen=>screen.WorkingArea.Left).First().WorkingArea;var owner=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(area.Left+30,area.Top+30,Math.Max(100,area.Width-60),Math.Max(100,area.Height-60))};picker.Owner=owner;Bitmap planeBitmap=null,hueBitmap=null;
            try{
                picker.AutoScaleMode=AutoScaleMode.None;picker.Scale(new SizeF(scale,scale));
                // A large inherited UI font in addition to the scaled geometry
                // catches rows whose text height was accidentally hard-coded.
                picker.Font=new Font(picker.Font.FontFamily,picker.Font.Size*scale*1.1F);
                Invoke(picker,"OnLoad",EventArgs.Empty);Need(area.Contains(picker.Bounds),"Picker does not fit its owner's working area at "+scale+"x");
                using(var image=Render(picker,Path.Combine(output,"color-picker-"+scale.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+"x.png"))){
                    Need(image.Width>400&&image.Height>400,"Picker render is empty");
                    foreach(var input in new[]{Field<TextBox>(picker,"hexInput"),Field<TextBox>(picker,"redInput"),Field<TextBox>(picker,"greenInput"),Field<TextBox>(picker,"blueInput")}){
                        var bounds=InForm(input,picker);Need(VisibleClip(input,picker)==bounds,"Color value is clipped by an ancestor at "+scale+"x: "+input.AccessibleName+", bounds="+bounds+", visible="+VisibleClip(input,picker));
                        Need(BrightTextPixels(image,bounds)>=10,"Color value text is not visible in the composed picker at "+scale+"x: "+input.AccessibleName);
                    }
                }
                var plane=Field<Control>(picker,"plane");var hue=Field<Control>(picker,"hueStrip");
                var close=Descendants(picker).Single(control=>control.AccessibleName=="取消配色");
                var required=new Control[]{Field<TextBox>(picker,"hexInput"),Field<TextBox>(picker,"redInput"),Field<TextBox>(picker,"greenInput"),Field<TextBox>(picker,"blueInput"),Field<Button>(picker,"confirmButton"),Field<Button>(picker,"cancelButton"),plane,hue,close};
                foreach(var control in required){var bounds=InForm(control,picker);Need(bounds.Width>5&&bounds.Height>5&&picker.ClientRectangle.Contains(bounds),"Picker clips a required control at "+scale+"x: "+control.Name+" "+control.Text);}
                for(int i=0;i<required.Length;i++)for(int j=i+1;j<required.Length;j++){var a=InForm(required[i],picker);var b=InForm(required[j],picker);Need(!a.IntersectsWith(b),"Picker fields overlap at "+scale+"x");}
                planeBitmap=Field<Bitmap>(plane,"bitmap");hueBitmap=Field<Bitmap>(hue,"bitmap");Need(planeBitmap!=null&&hueBitmap!=null,"Color gradients are not rendered: plane="+plane.Bounds+" cached="+(planeBitmap!=null)+", hue="+hue.Bounds+" cached="+(hueBitmap!=null));
                int colorful=0;using(var image=new Bitmap(planeBitmap)){for(int y=0;y<image.Height;y+=Math.Max(1,image.Height/20))for(int x=0;x<image.Width;x+=Math.Max(1,image.Width/20)){Color pixel=image.GetPixel(x,y);if(Math.Abs(pixel.R-pixel.G)>20||Math.Abs(pixel.G-pixel.B)>20)colorful++;}}Need(colorful>20,"SV plane is blank or grayscale");
                var planeControl=(HubSaturationPlane)plane;planeControl.SetState((planeControl.Hue+90)%360,planeControl.Saturation,planeControl.Value);Need(Disposed(planeBitmap),"Changing hue did not release the SV bitmap");
                using(var image=HiddenTree(picker)){}planeBitmap=Field<Bitmap>(plane,"bitmap");Need(planeBitmap!=null&&Object.ReferenceEquals(hueBitmap,Field<Bitmap>(hue,"bitmap")),"Gradient cache was not rebuilt selectively");
                plane.Width-=3;hue.Width-=3;Need(Disposed(planeBitmap)&&Disposed(hueBitmap),"Resizing the color controls did not release old bitmaps");
                using(var image=HiddenTree(picker)){}planeBitmap=Field<Bitmap>(plane,"bitmap");hueBitmap=Field<Bitmap>(hue,"bitmap");Need(planeBitmap!=null&&hueBitmap!=null,"Gradient cache did not recover after resize");
                Need(!picker.Visible&&!owner.Visible,"Hidden color picker test displayed a window");
            }finally{picker.Dispose();owner.Dispose();}
            CacheDisposed(null,planeBitmap,"plane.bitmap");CacheDisposed(null,hueBitmap,"hueStrip.bitmap");
        }
    }
    [STAThread]static int Main(string[] args){string report=args.Length>0?args[0]:Path.Combine(Path.GetTempPath(),"PetColorPickerTests.json");try{
        Application.EnableVisualStyles();string output=Path.GetDirectoryName(report);MathProof();InputProof();RoutingProof();LayoutAndCacheProof(output);
        File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{pass=true,checks,hexRgbHsv=true,validationBlocksConfirmation=true,confirmAndCancel=true,targetsPreserved=true,escapeAndEnter=true,hiddenLayoutScales=new[]{1D,1.25,1.5,2},inputValuesVisibleInAncestorClip=true,largeFont=true,gradientCacheReleased=true,windowsShown=false,serviceActions=false,actualUserPreferencesChanged=false,oldResizeTestsRepeated=false}));return 0;
    }catch(Exception error){File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{pass=false,checks,error=error.ToString()}));return 1;}}
}
