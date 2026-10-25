using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
internal static class MsiUiProbe {
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern uint MsiOpenPackageEx(string path,uint flags,out uint handle);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern uint MsiDoAction(uint handle,string action);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern uint MsiSetProperty(uint handle,string name,string value);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern uint MsiGetProperty(uint handle,string name,StringBuilder value,ref uint size);
    [DllImport("msi.dll")]static extern uint MsiSetInternalUI(uint level,IntPtr parent);
    [DllImport("msi.dll")]static extern uint MsiCreateRecord(uint fields);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern uint MsiRecordSetString(uint record,uint field,string value);
    [DllImport("msi.dll")]static extern uint MsiRecordSetInteger(uint record,uint field,int value);
    [DllImport("msi.dll")]static extern int MsiProcessMessage(uint handle,uint kind,uint record);
    [DllImport("msi.dll")]static extern uint MsiCloseHandle(uint handle);
    static void Check(uint code,string step){if(code!=0)throw new Exception(step+": "+code);}
    static void Action(uint handle,string action){uint code=MsiDoAction(handle,action);if(code!=0)throw new Exception(action+": "+code);}
    static void Message(uint h,string action,string text,int ticks){
        uint r=MsiCreateRecord(3);try{MsiRecordSetString(r,1,action);MsiRecordSetString(r,2,text);MsiProcessMessage(h,0x08000000,r);}finally{MsiCloseHandle(r);}
        r=MsiCreateRecord(4);try{MsiRecordSetInteger(r,1,0);MsiRecordSetInteger(r,2,100);MsiRecordSetInteger(r,3,0);MsiRecordSetInteger(r,4,0);MsiProcessMessage(h,0x0A000000,r);MsiRecordSetInteger(r,1,2);MsiRecordSetInteger(r,2,ticks);MsiProcessMessage(h,0x0A000000,r);}finally{MsiCloseHandle(r);}
    }
    static void Wait(string root,string phase){File.WriteAllText(Path.Combine(root,"phase.txt"),phase);DateTime until=DateTime.UtcNow.AddSeconds(150);while(!File.Exists(Path.Combine(root,"next-"+phase))){Application.DoEvents();Thread.Sleep(30);if(DateTime.UtcNow>until)throw new Exception("Probe timeout: "+phase);}}
    [STAThread]static int Main(string[] args){
        uint h=0;
        try{
            string root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);
            MsiSetInternalUI(5,IntPtr.Zero);Check(MsiOpenPackageEx(Path.GetFullPath(args[0]),1,out h),"open");
            uint size=255;var name=new StringBuilder(256);Check(MsiGetProperty(h,"ProductName",name,ref size),"identity");
            if(name.ToString()!="Codex Background isolated package test")throw new Exception("Not an isolated probe package");
            MsiSetProperty(h,"ProductName","Codex 动态背景 · 界面验证（不会安装）");MsiSetProperty(h,"LAUNCHMANAGER","0");MsiSetProperty(h,"INSTALLDIR",Path.Combine(root,"never-installed"));
            Action(h,"CostInitialize");Action(h,"FileCost");Action(h,"CostFinalize");Action(h,"ProgressDlg");
            Message(h,"InstallFiles","正在复制程序和后台组件…",35);Wait(root,"copy");
            Message(h,"InstallBackground","正在配置并验证背景服务，请稍候…",80);Wait(root,"service");
            File.WriteAllText(Path.Combine(root,"phase.txt"),"result");
            if(args.Length>2&&args[2]=="failure"){MsiSetProperty(h,"MsiLogFileLocation",Path.Combine(root,"probe-only.log"));Action(h,"FailureDlg");}else Action(h,"SuccessDlg");
            File.WriteAllText(Path.Combine(root,"result.json"),"{\"pass\":true,\"installationExecuted\":false,\"modelessProgressReturned\":true,\"resultReturned\":true}");return 0;
        }catch(Exception error){File.WriteAllText(Path.Combine(args[1],"error.txt"),error.ToString());return 1;}finally{if(h!=0)MsiCloseHandle(h);}
    }
}
