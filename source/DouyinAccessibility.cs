using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

// All selectors are scoped to one foreground Douyin HWND. There are no fixed
// screen coordinates, account APIs, cookies or background message endpoints.
internal static class SparkLocator {
    static bool Near(double a,double b,double tolerance){return Math.Abs(a-b)<=tolerance;}
    public static SparkView Describe(IntPtr handle,Rectangle bounds,bool foreground,List<SparkNode> nodes,float dpi=1){
        dpi=Math.Max(.75F,dpi);float factor=dpi;var original=bounds;
        var logical=nodes.Select(n=>new SparkNode{Key=n.Key,Name=n.Name,Id=n.Id,Kind=n.Kind,Value=n.Value,Enabled=n.Enabled,Bounds=Rectangle.FromLTRB((int)((n.Bounds.Left-bounds.Left)/factor),(int)((n.Bounds.Top-bounds.Top)/factor),(int)Math.Ceiling((n.Bounds.Right-bounds.Left)/factor),(int)Math.Ceiling((n.Bounds.Bottom-bounds.Top)/factor))}).ToList();
        var view=DescribeLogical(handle,new Rectangle(0,0,(int)(bounds.Width/factor),(int)(bounds.Height/factor)),foreground,logical);
        Func<Rectangle,Rectangle> project=r=>r.IsEmpty?Rectangle.Empty:Rectangle.FromLTRB(original.Left+(int)(r.Left*factor),original.Top+(int)(r.Top*factor),original.Left+(int)Math.Ceiling(r.Right*factor),original.Top+(int)Math.Ceiling(r.Bottom*factor));
        Func<SparkNode,SparkNode> node=n=>n==null?null:new SparkNode{Key=n.Key,Name=n.Name,Kind=n.Kind,Id=n.Id,Value=n.Value,Enabled=n.Enabled,Bounds=project(n.Bounds)};
        view.Bounds=original;view.Nodes=nodes;view.MessageButton=node(view.MessageButton);view.SearchBox=node(view.SearchBox);view.Composer=node(view.Composer);view.Back=node(view.Back);view.Emoji=node(view.Emoji);view.Spark=node(view.Spark);view.Contacts=view.Contacts.Select(node).ToList();view.ListBounds=project(view.ListBounds);view.ChatBounds=project(view.ChatBounds);return view;
    }
    static SparkView DescribeLogical(IntPtr handle,Rectangle bounds,bool foreground,List<SparkNode> nodes){
        var view=new SparkView{Window=handle,Bounds=bounds,Foreground=foreground,Nodes=nodes};
        view.MessageButton=nodes.Where(n=>n.Name=="消息"&&n.Bounds.Top<bounds.Top+150).OrderBy(n=>n.Bounds.Top).FirstOrDefault();
        var search=nodes.Where(n=>n.Kind=="Edit"&&n.Name=="搜索"&&n.Bounds.Left>bounds.Left+bounds.Width*.45&&n.Bounds.Top<bounds.Top+220).OrderBy(n=>n.Bounds.Top).FirstOrDefault();
        if(search!=null){
            view.SearchBox=search;view.ListBounds=Rectangle.FromLTRB(Math.Max(bounds.Left,search.Bounds.Left-20),search.Bounds.Bottom+4,Math.Min(bounds.Right,search.Bounds.Right+48),bounds.Bottom-12);
            foreach(var avatar in nodes.Where(n=>n.Kind=="ListItem"&&view.ListBounds.Contains(n.Center)&&n.Bounds.Width>=20&&n.Bounds.Width<=70)){
                var name=nodes.Where(n=>n.Kind=="Text"&&n.Bounds.Left>=avatar.Bounds.Right&&n.Bounds.Right<=view.ListBounds.Right&&Near(n.Bounds.Top,avatar.Bounds.Top,18)&&!Regex.IsMatch(n.Name??"",@"^(?:重燃|\d\d?:\d\d|\d+$)")).OrderBy(n=>n.Bounds.Left).FirstOrDefault();
                if(name!=null&&!view.Contacts.Any(n=>n.Key==name.Key)){
                    var open=nodes.FirstOrDefault(n=>n.Name=="发消息"&&n.Bounds.Left>name.Bounds.Left&&view.ListBounds.Contains(n.Center)&&Near(n.Center.Y,name.Center.Y,24));
                    view.Contacts.Add(open==null?name:new SparkNode{Key=name.Key,Name=name.Name,Kind=name.Kind,Bounds=open.Bounds,Enabled=open.Enabled});
                }
            }
            view.SearchComplete=view.Contacts.Count>0||nodes.Any(n=>view.ListBounds.Contains(n.Center)&&Regex.IsMatch(n.Name??"",@"^(?:暂无.*(?:结果|联系人|用户)|没有.*(?:结果|联系人|用户)|未找到.*|无搜索结果)$"));
        }
        view.Composer=nodes.Where(n=>(n.Kind=="Group"||n.Kind=="Edit")&&((n.Value??"").TrimStart().StartsWith("发送消息",StringComparison.Ordinal)||(n.Name??"")=="发送消息")&&n.Bounds.Top>bounds.Top+bounds.Height*.5).OrderByDescending(n=>n.Bounds.Width).FirstOrDefault();
        var header=nodes.FirstOrDefault(n=>n.Id=="ei-conversation-header-title-bar");
        if(header==null)view.Composer=null;
        if(view.Composer!=null&&header!=null){
            var input=view.Composer.Bounds;int left=Math.Max(bounds.Left,input.Left-20),right=bounds.Right-8;
            view.ChatBounds=Rectangle.FromLTRB(left,Math.Max(bounds.Top,header.Bounds.Top-18),right,bounds.Bottom-8);
            var title=nodes.Where(n=>n.Kind=="Text"&&n.Bounds.Left>=left+58&&n.Bounds.Right<=right-32&&Near(n.Center.Y,header.Center.Y,20)&&!Regex.IsMatch(n.Name??"",@"^(?:重燃|\d+$)")).OrderBy(n=>n.Bounds.Left).FirstOrDefault();
            view.HeaderName=title==null?null:title.Name;
            // Some Chromium builds expose the conversation list underneath the
            // chat overlay. Its search input must not win over the visible chat.
            var overlap=Rectangle.Intersect(view.ListBounds,view.ChatBounds);
            if(!view.ListBounds.IsEmpty&&overlap.Width>view.ListBounds.Width*.5){view.SearchBox=null;view.ListBounds=Rectangle.Empty;view.Contacts.Clear();view.SearchComplete=false;}
            view.Back=new SparkNode{Key="@back",Name="返回消息列表",Kind="Point",Bounds=new Rectangle(left+12,header.Center.Y-10,20,20)};
            var candidates=nodes.Where(n=>n.Kind=="Image"&&n.Bounds.Width>=12&&n.Bounds.Width<=42&&n.Bounds.Height>=12&&n.Bounds.Height<=42&&n.Center.X>input.Right-38&&n.Bounds.Right<=right&&Near(n.Center.Y,input.Top+input.Height/2,8)).ToArray();
            var icons=candidates.Where(n=>!candidates.Any(parent=>parent!=n&&parent.Bounds.Width>n.Bounds.Width*1.3&&parent.Bounds.Contains(n.Center))).OrderByDescending(n=>n.Center.X).ToArray();
            if(icons.Length>=2){var image=icons[1];view.Emoji=new SparkNode{Key="@emoji",Name="表情",Kind="Point",Bounds=image.Bounds};}
            var tiles=nodes.Where(n=>n.Name=="续火花"&&n.Kind=="Group"&&n.Bounds.Width>=24&&n.Bounds.Width<=160&&n.Bounds.Height>=24&&n.Bounds.Top>view.ChatBounds.Top+30&&view.ChatBounds.Contains(n.Center)).ToArray();
            if(tiles.Length==1)view.Spark=tiles[0];
            foreach(var node in nodes.Where(n=>(n.Name=="续火花"||n.Name=="[续火花]")&&(n.Kind=="Image"||n.Kind=="Text")&&view.ChatBounds.Contains(n.Center)&&n.Bounds.Top<input.Top&&n.Bounds.Top>header.Bounds.Bottom+12))view.SentMarkers.Add(node.Key);
        }
        view.Ready=view.MessageButton!=null||view.HasList||view.Composer!=null;return view;
    }
}
internal sealed class DouyinDesktop : ISparkDesktop {
    delegate bool EnumCallback(IntPtr handle,IntPtr value);
    [StructLayout(LayoutKind.Sequential)]struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct NativePoint {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]struct MouseInput {public int X,Y;public uint Data,Flags,Time;public IntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)]struct KeyInput {public ushort VirtualKey,Scan;public uint Flags,Time;public IntPtr Extra;}
    [StructLayout(LayoutKind.Explicit)]struct InputUnion {[FieldOffset(0)]public MouseInput Mouse;[FieldOffset(0)]public KeyInput Key;}
    [StructLayout(LayoutKind.Sequential)]struct Input {public uint Type;public InputUnion Data;}
    [DllImport("user32.dll")]static extern bool EnumWindows(EnumCallback callback,IntPtr value);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr window,int mode);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,System.Text.StringBuilder title,int maximum);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,Input[] values,int size);
    [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
    readonly SparkWorkerClient worker=new SparkWorkerClient();IntPtr target;string query="";
    internal static int GuideRight;
    public static bool HasForegroundWindow(){return FindWindow()!=IntPtr.Zero;}
    internal static IntPtr ForegroundOwner {get{var active=GetForegroundWindow();var owner=GetAncestor(active,3);return owner==IntPtr.Zero?active:owner;}}
    static bool ForegroundMatches(IntPtr window){return window!=IntPtr.Zero&&ForegroundOwner==window;}
    static string ForegroundProcess(){try{uint pid;GetWindowThreadProcessId(GetForegroundWindow(),out pid);using(var process=Process.GetProcessById((int)pid))return process.ProcessName;}catch{return "未识别";}}
    internal static IntPtr FindOpenWindow(){
        IntPtr found=IntPtr.Zero;EnumWindows(delegate(IntPtr handle,IntPtr unused){var title=new System.Text.StringBuilder(256);GetWindowText(handle,title,title.Capacity);if(title.ToString().IndexOf("抖音",StringComparison.OrdinalIgnoreCase)<0)return true;
            uint pid;GetWindowThreadProcessId(handle,out pid);try{using(var process=Process.GetProcessById((int)pid)){if(!String.Equals(process.ProcessName,"douyin",StringComparison.OrdinalIgnoreCase))return true;Rect rect;if(IsIconic(handle)||GetWindowRect(handle,out rect)&&rect.Right-rect.Left>=480&&rect.Bottom-rect.Top>=400)found=handle;}}catch(ArgumentException){}catch(InvalidOperationException){}return found==IntPtr.Zero;},IntPtr.Zero);return found;
    }
    internal static void Activate(IntPtr window){ShowWindowAsync(window,IsIconic(window)?9:5);SetForegroundWindow(window);}
    static IntPtr FindWindow(){
        var foreground=ForegroundOwner;IntPtr found=IntPtr.Zero;
        EnumWindows(delegate(IntPtr handle,IntPtr unused){if(handle!=foreground||!IsWindowVisible(handle)||IsIconic(handle))return true;uint pid;GetWindowThreadProcessId(handle,out pid);try{using(var process=Process.GetProcessById((int)pid)){if(!String.Equals(process.ProcessName,"douyin",StringComparison.OrdinalIgnoreCase))return true;Rect bounds;if(GetWindowRect(handle,out bounds)&&bounds.Right-bounds.Left>=480&&bounds.Bottom-bounds.Top>=400)found=handle;}}catch(ArgumentException){}catch(InvalidOperationException){}return found==IntPtr.Zero;},IntPtr.Zero);return found;
    }
    public async Task<SparkView> Read(CancellationToken stop){var reply=await worker.Request(new SparkWorkerRequest{Operation="read",Query=query},stop);target=new IntPtr(reply.Window);GuideRight=reply.ListBounds.IsEmpty?reply.ChatBounds.IsEmpty?reply.Bounds.Right-360:reply.ChatBounds.Left:reply.ListBounds.Left;return reply.View();}
    public async Task Click(SparkNode node,CancellationToken stop){await worker.Request(new SparkWorkerRequest{Operation="click",Window=target.ToInt64(),Node=node,Query=query},stop);}
    public async Task Search(SparkNode field,string name,CancellationToken stop){await worker.Request(new SparkWorkerRequest{Operation="search",Window=target.ToInt64(),Node=field,Query=name},stop);query=name;}
    internal static SparkWorkerReply ExecuteWorker(SparkWorkerRequest request){
        IntPtr dpi=IntPtr.Zero;try{try{dpi=SetThreadDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
            var desktop=new DouyinDesktop();desktop.target=new IntPtr(request.Window);desktop.query=request.Query??"";
            if(request.Operation=="read")return SparkWorkerReply.From(desktop.ReadCore(CancellationToken.None));
            if(request.Operation=="inspect"){var known=FindOpenWindow();if(known==IntPtr.Zero||known.ToInt64()!=request.Window)throw new SparkFocusLost();return SparkWorkerReply.From(desktop.ReadCore(CancellationToken.None,known));}
            if(request.Window==0||request.Node==null)throw new InvalidDataException("控件请求没有绑定抖音窗口。");
            if(request.Operation=="click")desktop.ClickCore(request.Node,CancellationToken.None);
            else if(request.Operation=="search"){if(String.IsNullOrWhiteSpace(request.Query)||request.Query.Length>512)throw new InvalidDataException("联系人名称无效。");desktop.SearchCore(request.Node,request.Query,CancellationToken.None);}
            else throw new InvalidDataException("不支持的控件操作。");return new SparkWorkerReply();
        }finally{if(dpi!=IntPtr.Zero)SetThreadDpiAwarenessContext(dpi);}
    }
    SparkView ReadCore(CancellationToken stop,IntPtr inspect=default(IntPtr)){
        var handle=inspect!=IntPtr.Zero?inspect:FindWindow();if(handle==IntPtr.Zero)throw new InvalidOperationException("未找到前台的抖音主窗口（当前前台进程："+ForegroundProcess()+"），请切到抖音后重试。");target=handle;Rect rect;if(!GetWindowRect(handle,out rect))throw new SparkFocusLost();
        Rectangle bounds=Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom);List<SparkNode> nodes;
        float dpi=1;try{dpi=GetDpiForWindow(handle)/96F;}catch(EntryPointNotFoundException){}
        // OCR reads only the message sidebar. Individual native point lookups
        // verify names/search values without traversing the full Chromium tree.
        if(inspect==IntPtr.Zero&&ForegroundMatches(handle))try{
            SparkWorkerHost.Phase("视觉定位消息栏");using(var native=new DouyinNativeUia()){
                var visual=DouyinVision.Read(handle,bounds,dpi,query,native.NameAt,native.SearchValueAt,()=>ForegroundMatches(handle),stop,()=>native.HeaderTitle(handle));
                if(visual.Ready)return visual;
            }
        }catch(NotSupportedException){}catch(TypeLoadException){}catch(FileNotFoundException){}catch(COMException){}
        SparkWorkerHost.Phase("兼容控件定位");using(var native=new DouyinNativeUia())nodes=native.Read(handle,bounds,stop);
        var view=SparkLocator.Describe(handle,bounds,ForegroundMatches(handle),nodes,dpi);
        if(view.Composer!=null)view.Composer.Value="";
        // The helper reply contains controls and names, never chat-body nodes.
        view.Nodes=view.Contacts.Concat(new[]{view.MessageButton,view.SearchBox,view.Composer,view.Back,view.Emoji,view.Spark}).Where(n=>n!=null).GroupBy(n=>n.Key).Select(g=>g.First()).ToList();return view;
    }
    SparkNode Revalidate(SparkNode old,CancellationToken stop){
        var expected=target;var view=ReadCore(stop);if(view.Window!=expected||!view.Foreground)throw new SparkFocusLost();
        if((old.Key=="v:spark"||old.Name=="续火花")&&(String.IsNullOrEmpty(query)||!view.IsChat(query)))throw new SparkContactError("续火花前联系人已改变，未执行发送。");
        var current=CurrentTarget(view,old);
        if(current==null||!current.Enabled)throw new SparkContactError("控件位置已变化，已跳过，避免误点。");
        return current;
    }
    internal static SparkNode CurrentTarget(SparkView view,SparkNode old){
        SparkNode current=null;
        if(old.Name=="返回消息列表")current=view.Back;else if(old.Name=="表情")current=view.Emoji;
        else if(old.Name=="续火花")current=view.Spark;else if(old.Kind=="Edit"&&old.Name=="搜索")current=view.SearchBox;
        else if(old.Name=="消息")current=view.MessageButton;
        else {var names=view.Contacts.Where(n=>String.Equals(n.Name,old.Name,StringComparison.Ordinal)).ToArray();if(names.Length>1)return null;if(names.Length==1){current=names[0];var row=Rectangle.Intersect(new Rectangle(0,old.Bounds.Top,1,old.Bounds.Height),new Rectangle(0,current.Bounds.Top,1,current.Bounds.Height));if(row.Height>=Math.Min(old.Bounds.Height,current.Bounds.Height)*.4)return current;}}
        if(current==null)current=view.Nodes.FirstOrDefault(n=>n.Key==old.Key&&n.Name==old.Name&&n.Kind==old.Kind);
        if(current==null)return null;var hit=current.Bounds;hit.Inflate(5,5);
        // OCR can bound just the first name character while UIA bounds the
        // whole text. Both must still cover the same observed clickable point.
        var overlap=Rectangle.Intersect(old.Bounds,current.Bounds);double smaller=Math.Min((double)old.Bounds.Width*old.Bounds.Height,(double)current.Bounds.Width*current.Bounds.Height);
        return hit.Contains(old.Center)||smaller>0&&(double)overlap.Width*overlap.Height>=smaller*.4?current:null;
    }
    void ClickCore(SparkNode node,CancellationToken stop){
        ClickVerified(Revalidate(node,stop),stop);
    }
    void ClickVerified(SparkNode current,CancellationToken stop){
        stop.ThrowIfCancellationRequested();var point=current.Center;
        var hit=WindowFromPoint(new NativePoint{X=point.X,Y=point.Y});if(!ForegroundMatches(target)||GetAncestor(hit,2)!=target&&GetAncestor(hit,3)!=target)throw new SparkFocusLost();
        // Press/release are one SendInput batch, so cancellation cannot leave a held button.
        if(!SetCursorPos(point.X,point.Y)||!ForegroundMatches(target))throw new SparkFocusLost();
        var clicks=new[]{new Input{Data=new InputUnion{Mouse=new MouseInput{Flags=2}}},new Input{Data=new InputUnion{Mouse=new MouseInput{Flags=4}}}};
        if(SendInput(2,clicks,Marshal.SizeOf(typeof(Input)))!=2)throw new InvalidOperationException("Windows 未接受点击，请使用相同权限运行抖音和 CodexHub。");
    }
    void SearchCore(SparkNode field,string name,CancellationToken stop){
        var current=Revalidate(field,stop);stop.ThrowIfCancellationRequested();using(var native=new DouyinNativeUia())SparkSearchInput.Replace(native.SearchEditor(current,()=>ForegroundMatches(target),()=>{ClickVerified(current,stop);Thread.Sleep(80);},value=>TypeSearch(value,stop)),name,()=>Thread.Sleep(90));
    }
    static Input Key(ushort code,bool up,bool unicode=false){return new Input{Type=1,Data=new InputUnion{Key=new KeyInput{VirtualKey=unicode?(ushort)0:code,Scan=unicode?code:(ushort)0,Flags=(uint)((unicode?4:0)|(up?2:0))}}};}
    void TypeSearch(string value,CancellationToken stop){
        stop.ThrowIfCancellationRequested();if(!ForegroundMatches(target))throw new SparkFocusLost();if(new[]{0x10,0x11,0x12}.Any(key=>(GetAsyncKeyState(key)&0x8000)!=0))throw new SparkContactError("请松开 Shift、Ctrl 或 Alt 后重试。");
        var select=new[]{Key(0x11,false),Key(0x41,false),Key(0x41,true),Key(0x11,true)};
        if(SendInput((uint)select.Length,select,Marshal.SizeOf(typeof(Input)))!=select.Length){var release=new[]{Key(0x11,true)};SendInput(1,release,Marshal.SizeOf(typeof(Input)));throw new SparkContactError("Windows 未接受搜索框按键，未执行发送。");}
        stop.ThrowIfCancellationRequested();if(!ForegroundMatches(target))throw new SparkFocusLost();var text=new List<Input>();foreach(char unit in value){text.Add(Key(unit,false,true));text.Add(Key(unit,true,true));}
        if(SendInput((uint)text.Count,text.ToArray(),Marshal.SizeOf(typeof(Input)))!=text.Count)throw new SparkContactError("搜索文字未完整输入，未执行发送。");
    }
}
