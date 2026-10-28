using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
internal static class ServiceStatusLogTests {
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Get(Form form,string name){return form.GetType().GetField(name,Hidden).GetValue(form);}
    static void Set(Form form,string name,object value){form.GetType().GetField(name,Hidden).SetValue(form,value);}
    static void Pump(int ms){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<ms){Application.DoEvents();Thread.Sleep(10);}}
    static void UntilDone(Form form){var watch=Stopwatch.StartNew();while((bool)Get(form,"checkingStatus")&&watch.ElapsedMilliseconds<12000)Pump(10);if((bool)Get(form,"checkingStatus"))throw new Exception("Fixture status command timed out");Pump(200);}
    [STAThread]static int Main(string[] args){try{
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        string folder=Path.GetDirectoryName(Path.GetFullPath(args[1]));Directory.CreateDirectory(folder);string fixture=Path.Combine(folder,"isolated-command");Directory.CreateDirectory(fixture);
        string script="param([string]$Action,[int]$ManagerPid)\r\n$mode=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'mode.txt'))\r\nif($mode -eq 'process'){[Console]::Error.WriteLine('fixture-process-failure');exit 8}\r\n$bad=$mode -eq 'logical'\r\nWrite-Output ('DREAM_STATUS:'+(@{installed=$true;connected=(-not $bad);serviceRunning=$true;autostart=$true;version='fixture';background='image-ready';connectionError=$bad;message=$(if($bad){'fixture-logical-failure'}else{'fixture-ready'})}|ConvertTo-Json -Compress))\r\n";
        File.WriteAllText(Path.Combine(fixture,"control.ps1"),script,new UTF8Encoding(true));
        var type=Assembly.LoadFile(Path.GetFullPath(args[0])).GetType("Manager",true);var results=new List<object>();bool pass=true;
        using(var form=(Form)Activator.CreateInstance(type,Hidden,null,new object[]{false},null)){
            var handle=form.Handle;Set(form,"root",fixture);Set(form,"liveStatusEnabled",true);var log=(TextBox)Get(form,"log");
            foreach(string action in new[]{"AutoConnect","Connection"}){File.WriteAllText(Path.Combine(fixture,"mode.txt"),"healthy");log.Text="last-user-operation";type.GetMethod("Run",Hidden).Invoke(form,new object[]{action});bool startedQuiet=log.Text=="last-user-operation";UntilDone(form);bool completedQuiet=log.Text=="last-user-operation";pass&=startedQuiet&&completedQuiet;results.Add(new{action,startedQuiet,completedQuiet,finalLog=log.Text});}
            File.WriteAllText(Path.Combine(fixture,"mode.txt"),"healthy");type.GetMethod("Run",Hidden).Invoke(form,new object[]{"Connect"});UntilDone(form);bool manualVisible=log.Text.Contains("fixture-ready");pass&=manualVisible;results.Add(new{action="Connect",manualVisible});
            File.WriteAllText(Path.Combine(fixture,"mode.txt"),"process");log.Text="last-user-operation";type.GetMethod("Run",Hidden).Invoke(form,new object[]{"AutoConnect"});UntilDone(form);bool processFailureVisible=log.Text.Contains("fixture-process-failure");pass&=processFailureVisible;results.Add(new{action="AutoConnect-process-failure",processFailureVisible,finalLog=log.Text});
            File.WriteAllText(Path.Combine(fixture,"mode.txt"),"logical");type.GetMethod("Run",Hidden).Invoke(form,new object[]{"AutoConnect"});UntilDone(form);bool logicalFailureVisible=((Control)Get(form,"previewHint")).Text.Contains("fixture-logical-failure")&&((Control)Get(form,"status")).Text.Contains("连接未完成");pass&=logicalFailureVisible;results.Add(new{action="AutoConnect-logical-failure",logicalFailureVisible});
            if(form.Visible)throw new Exception("Probe displayed a real window");
        }
        File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{pass,results,realServicesModified=false,liveSettingsChanged=false,noWindowsShown=true}));return pass?0:1;
    }catch(Exception error){File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{pass=false,error=error.ToString()}));return 1;}}
}
