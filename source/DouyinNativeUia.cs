using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using N=Interop.UIAutomationClient;

internal static class WindowsUiaRuntime {
    static bool initialized;
    public static void Initialize(){if(initialized)return;initialized=true;AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs args){if(new AssemblyName(args.Name).Name!="Interop.UIAutomationClient")return null;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexHub.UIAutomationInterop")){if(stream==null)return null;using(var bytes=new MemoryStream()){stream.CopyTo(bytes);return Assembly.Load(bytes.ToArray());}}};}
}
internal sealed class DouyinNativeUia : IDisposable {
    [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
    readonly N.IUIAutomation client;readonly List<object> objects=new List<object>();
    const int Runtime=30000,Bounds=30001,Type=30003,Name=30005,Enabled=30010,AutomationId=30011,Offscreen=30022,Value=30045;
    T Own<T>(T value){if(value!=null)objects.Add(value);return value;}
    public DouyinNativeUia(){client=Own((N.IUIAutomation)Activator.CreateInstance(System.Type.GetTypeFromCLSID(typeof(N.CUIAutomation8Class).GUID)));try{var options=(N.IUIAutomation2)client;options.ConnectionTimeout=2500;options.TransactionTimeout=6000;options.AutoSetFocus=0;}catch(InvalidCastException){}}
    static string Text(object value){return value as string??"";}
    public List<SparkNode> Read(IntPtr window,Rectangle bounds,CancellationToken stop){
        SparkWorkerHost.Phase("连接原生控件");var root=Own(client.ElementFromHandle(window));var cache=Own(client.CreateCacheRequest());cache.TreeScope=N.TreeScope.TreeScope_Element;cache.AutomationElementMode=N.AutomationElementMode.AutomationElementMode_Full;
        foreach(int property in new[]{Runtime,Bounds,Type,Name,Enabled,AutomationId,Offscreen})cache.AddProperty(property);
        SparkWorkerHost.Phase("扫描原生消息界面");var filter=Own(client.CreateTrueCondition());var elements=Own(root.FindAllBuildCache(N.TreeScope.TreeScope_Descendants,filter,cache));
        int count=elements.Length;if(count>12000)throw new InvalidOperationException("抖音控件树过大，请关闭多余消息弹窗后重试。");var result=new List<SparkNode>();SparkWorkerHost.Phase("读取原生缓存");bool chatHeader=false;float scale=1;try{scale=Math.Max(1,GetDpiForWindow(window)/96F);}catch(EntryPointNotFoundException){}
        for(int i=0;i<count;i++){
            stop.ThrowIfCancellationRequested();var element=elements.GetElement(i);try{
                if(Convert.ToBoolean(element.GetCachedPropertyValue(Offscreen)))continue;int type=Convert.ToInt32(element.GetCachedPropertyValue(Type));string kind=type==50020?"Text":type==50004?"Edit":type==50026?"Group":type==50006?"Image":type==50007?"ListItem":type==50000?"Button":null;if(kind==null)continue;
                var rectangle=element.CachedBoundingRectangle;var area=Rectangle.FromLTRB(rectangle.left,rectangle.top,rectangle.right,rectangle.bottom);if(area.Width<1||area.Height<1||!bounds.IntersectsWith(area))continue;
                var runtime=element.GetCachedPropertyValue(Runtime) as int[];if(runtime==null)continue;string name=Text(element.GetCachedPropertyValue(Name)),id=Text(element.GetCachedPropertyValue(AutomationId)),value="";if(id=="ei-conversation-header-title-bar")chatHeader=true;
                if(kind=="Edit"&&name=="搜索"){string text;if(ReadSearchValue(element,out text))value=text;}
                else if(chatHeader&&(kind=="Group"||kind=="Edit")&&area.Top>bounds.Bottom-110*scale&&area.Height<=80*scale&&area.Left>bounds.Right-360*scale)value=Text(element.GetCurrentPropertyValue(Value));
                result.Add(new SparkNode{Key=String.Join(".",runtime),Name=name,Id=id,Kind=kind,Bounds=area,Enabled=Convert.ToBoolean(element.GetCachedPropertyValue(Enabled)),Value=value});
            }catch(COMException){}finally{Marshal.FinalReleaseComObject(element);}
        }
        // Chromium retains hidden controls with offscreen=false. A search box
        // is usable only when point lookup still resolves to an actual edit.
        result.RemoveAll(node=>node.Kind=="Edit"&&node.Name=="搜索"&&SearchElement(node.Center)==null);
        return result;
    }
    N.IUIAutomationElement SearchElement(Point point){
        var element=Own(client.ElementFromPoint(new N.tagPOINT{x=point.X,y=point.Y}));var walker=Own(client.ControlViewWalker);
        for(int depth=0;depth<4&&element!=null;depth++,element=Own(walker.GetParentElement(element))){
            if(Convert.ToInt32(element.GetCurrentPropertyValue(Type))==50004&&Text(element.GetCurrentPropertyValue(Name))=="搜索")return element;
        }return null;
    }
    public string SearchValueAt(SparkNode field){var element=SearchElement(field.Center);string value;return element!=null&&ReadSearchValue(element,out value)?value:null;}
    public string NameAt(Point point){
        var element=Own(client.ElementFromPoint(new N.tagPOINT{x=point.X,y=point.Y}));if(element==null)return null;
        int type=element.CurrentControlType;if(type!=50020&&type!=50007&&type!=50000&&type!=50026)return null;
        return element.CurrentName;
    }
    public string HeaderTitle(IntPtr window){
        var root=Own(client.ElementFromHandle(window));var filter=Own(client.CreatePropertyCondition(AutomationId,"ei-conversation-header-title-bar"));
        var header=Own(root.FindFirst(N.TreeScope.TreeScope_Descendants,filter));if(header==null)return null;
        var area=header.CurrentBoundingRectangle;var walker=Own(client.ControlViewWalker);var sibling=Own(walker.GetPreviousSiblingElement(header));
        for(int i=0;i<5&&sibling!=null;i++,sibling=Own(walker.GetPreviousSiblingElement(sibling))){
            if(sibling.CurrentControlType!=50020)continue;string name=sibling.CurrentName;var box=sibling.CurrentBoundingRectangle;
            if(!String.IsNullOrWhiteSpace(name)&&!System.Text.RegularExpressions.Regex.IsMatch(name,@"^(?:\d+|重燃.*)$")&&Math.Abs((box.top+box.bottom)-(area.top+area.bottom))<50)return name;
        }return null;
    }
    object Pattern(N.IUIAutomationElement element,int id){try{return Own(element.GetCurrentPattern(id));}catch(COMException){return null;}}
    bool ReadSearchValue(N.IUIAutomationElement element,out string value){
        value=null;try{var pattern=Pattern(element,10002) as N.IUIAutomationValuePattern;if(pattern!=null){value=pattern.CurrentValue;return value!=null;}}catch(COMException){}
        try{var legacy=Pattern(element,10018) as N.IUIAutomationLegacyIAccessiblePattern;if(legacy!=null){value=legacy.CurrentValue;if(value!=null)return true;}}catch(COMException){}
        try{var text=Pattern(element,10014) as N.IUIAutomationTextPattern;if(text!=null){var range=Own(text.DocumentRange);value=range.GetText(1024);return value!=null;}}catch(COMException){}
        try{value=element.GetCurrentPropertyValue(Value) as string;return value!=null;}catch(COMException){return false;}
    }
    public ISparkSearchEditor SearchEditor(SparkNode field,Func<bool> foreground,Action focus,Action<string> type){return new SearchInput(this,field,foreground,focus,type);}
    sealed class SearchInput : ISparkSearchEditor {
        readonly DouyinNativeUia owner;readonly SparkNode field;readonly Func<bool> foreground;readonly Action focus;readonly Action<string> type;
        public SearchInput(DouyinNativeUia native,SparkNode node,Func<bool> active,Action click,Action<string> keyboard){owner=native;field=node;foreground=active;focus=click;type=keyboard;}
        void Guard(){if(!foreground())throw new SparkFocusLost();}
        // Douyin can echo ValuePattern.SetValue without firing Chromium's
        // search/change event. Focused keyboard input updates the actual list.
        public bool TryDirect(string value){Guard();return false;}
        public void Focus(){Guard();focus();}
        public bool Focused {get{Guard();try{var element=owner.Own(owner.client.GetFocusedElement());if(element==null)return false;var walker=owner.Own(owner.client.ControlViewWalker);bool hasFocus=element.CurrentHasKeyboardFocus!=0;
            for(int depth=0;depth<4&&element!=null;depth++,element=owner.Own(walker.GetParentElement(element))){if(element.CurrentControlType!=50004)continue;var b=element.CurrentBoundingRectangle;var center=new Point((b.left+b.right)/2,(b.top+b.bottom)/2);var expected=field.Bounds;expected.Inflate(5,5);return expected.Contains(center)&&(hasFocus||element.CurrentHasKeyboardFocus!=0);}return false;
        }catch(COMException){return false;}}}
        public void Type(string value){Guard();if(!Focused)throw new SparkContactError("搜索框焦点已改变，已停止输入。");type(value);}
        public bool TryRead(out string value){Guard();var element=owner.SearchElement(field.Center);if(element==null){value=null;return false;}return owner.ReadSearchValue(element,out value);}
    }
    public void Dispose(){for(int i=objects.Count-1;i>=0;i--)try{if(Marshal.IsComObject(objects[i]))Marshal.FinalReleaseComObject(objects[i]);}catch(InvalidComObjectException){}objects.Clear();}
}
