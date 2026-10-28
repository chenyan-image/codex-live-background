using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
internal sealed class UiaImportSink : ITypeLibImporterNotifySink {
    public void ReportEvent(ImporterEventKind kind,int code,string text){}
    public Assembly ResolveRef(object library){throw new InvalidOperationException("Unexpected external type library");}
}
internal static class ExportWindowsUiaInterop {
    [DllImport("oleaut32.dll",CharSet=CharSet.Unicode,PreserveSig=false)]static extern void LoadTypeLibEx(string file,int flags,out ITypeLib library);
    static int Main(string[] args){try{Directory.CreateDirectory(args[0]);ITypeLib library;LoadTypeLibEx(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"UIAutomationCore.dll"),2,out library);string path=Path.Combine(args[0],"Interop.UIAutomationClient.dll");var assembly=new TypeLibConverter().ConvertTypeLibToAssembly(library,path,TypeLibImporterFlags.None,new UiaImportSink(),null,null,null,null);assembly.Save(Path.GetFileName(path));var text=new System.Text.StringBuilder();foreach(string name in new[]{"IUIAutomation","IUIAutomation2","IUIAutomationElement","IUIAutomationElementArray","IUIAutomationCacheRequest","IUIAutomationValuePattern","tagRECT","tagPOINT","TreeScope","CUIAutomation8Class"}){Type type=null;foreach(var candidate in assembly.GetTypes())if(candidate.Name==name){type=candidate;break;}text.AppendLine(name+": "+type);if(type==null)continue;foreach(var method in type.GetMethods())text.AppendLine(method.ToString());foreach(var field in type.GetFields())text.AppendLine(field.ToString());}File.WriteAllText(Path.Combine(args[0],"signatures.txt"),text.ToString());return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
