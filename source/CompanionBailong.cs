using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class RemotePetMaterials {
    public static string ObtainRemielle(string root,string state,Action<string> progress,Func<bool> cancelled){
        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        var spec=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,@"source\assets\remielle-materials.json")));string revision=Convert.ToString(spec["revision"]);
        if(revision.Length!=40||revision.Any(c=>!Uri.IsHexDigit(c)))throw new InvalidDataException("形象版本清单无效。");string directory=Path.Combine(state,"pet-downloads","remielle-"+revision.Substring(0,12));Directory.CreateDirectory(directory);
        foreach(var item in (IEnumerable)spec["files"]){var file=(Dictionary<string,object>)item;string name=Convert.ToString(file["name"]),url=Convert.ToString(file["url"]),expected=Convert.ToString(file["sha256"]);long length=Convert.ToInt64(file["bytes"]);
            if(name!=Path.GetFileName(name)||!url.StartsWith("https://raw.githubusercontent.com/HanaAyane/remielle-codex-pet/"+revision+"/",StringComparison.Ordinal)||length<1||length>32*1024*1024||expected.Length!=64)throw new InvalidDataException("形象下载清单无效。");string target=Path.Combine(directory,name);
            if(File.Exists(target)&&new FileInfo(target).Length==length&&Hash(target)==expected)continue;if(cancelled())throw new OperationCanceledException();progress("从作者原仓库准备小蕾米："+name);
            string partial=target+".partial";var request=(HttpWebRequest)WebRequest.Create(url);request.UserAgent="CodexHub/1.7";request.Timeout=15000;request.ReadWriteTimeout=15000;
            using(var response=request.GetResponse())using(var input=response.GetResponseStream())using(var output=File.Create(partial)){var buffer=new byte[65536];long total=0;int count;while((count=input.Read(buffer,0,buffer.Length))>0){if(cancelled())throw new OperationCanceledException();total+=count;if(total>length)throw new InvalidDataException("形象文件超过清单大小。");output.Write(buffer,0,count);}}
            if(new FileInfo(partial).Length!=length||Hash(partial)!=expected)throw new InvalidDataException("作者素材校验失败，请重试。");if(File.Exists(target))File.Delete(target);File.Move(partial,target);
        }
        File.WriteAllText(Path.Combine(directory,"source.json"),new JavaScriptSerializer().Serialize(new{author=spec["author"],source=spec["source"],revision,modification="Original author notes AI-assisted redrawing and manual edits; CodexHub converts atlas locally for playback. See original ASSET-USAGE.md and NOTICE.md."}),new UTF8Encoding(false));
        return Path.Combine(directory,"pet.json");
    }
    static string Hash(string path){using(var stream=File.OpenRead(path))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
}
internal static class BailongMaterials {
    public static string FindLocal(){
        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"BailongEnhanced\materials");
        if(!Directory.Exists(folder))return null;
        foreach(string tier in new[]{"light","hd"}){string root=Path.Combine(folder,tier);if(!Directory.Exists(root))continue;foreach(string item in Directory.GetDirectories(root).OrderByDescending(x=>x))if(File.Exists(Path.Combine(item,"素材清单.json")))return item;}
        return null;
    }
    public static string Obtain(string root,string state,Action<string> progress,Func<bool> cancelled){
        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        string local=FindLocal();if(local!=null){progress("找到本机白龙素材，正在准备完整动作…");return local;}
        var spec=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,@"source\assets\bailong-materials.json")));
        string directory=Path.Combine(state,"pet-downloads");Directory.CreateDirectory(directory);string zip=Path.Combine(directory,"bailong-original-light.zip"),partial=zip+".partial";
        bool valid=File.Exists(zip)&&new FileInfo(zip).Length==Convert.ToInt64(spec["bytes"])&&Hash(zip)==Convert.ToString(spec["sha256"]);
        if(!valid){
            using(var client=new WebClient()){
                client.Headers[HttpRequestHeader.UserAgent]="CodexHub/1.7";int previous=-1;client.DownloadProgressChanged+=delegate(object sender,DownloadProgressChangedEventArgs e){if(cancelled()){client.CancelAsync();return;}if(e.ProgressPercentage!=previous){previous=e.ProgressPercentage;progress("从作者原发布下载白龙动作包（约589 MiB）："+previous+"%");}};
                client.DownloadFileTaskAsync(new Uri(Convert.ToString(spec["url"])),partial).GetAwaiter().GetResult();
            }
            if(cancelled())throw new OperationCanceledException();if(new FileInfo(partial).Length!=Convert.ToInt64(spec["bytes"])||Hash(partial)!=Convert.ToString(spec["sha256"]))throw new InvalidDataException("作者动作包校验未通过，请重试。");if(File.Exists(zip))File.Delete(zip);File.Move(partial,zip);
        }
        string extracted=Path.Combine(directory,"bailong-light-verified");Directory.CreateDirectory(extracted);
        using(var archive=ZipFile.OpenRead(zip)){
            var manifest=archive.Entries.Where(e=>e.Name=="素材清单.json").ToArray();if(manifest.Length!=1||manifest[0].Length>1024*1024)throw new InvalidDataException("白龙动作包清单无效。");
            string prefix=manifest[0].FullName.Substring(0,manifest[0].FullName.Length-manifest[0].Name.Length);
            foreach(var entry in archive.Entries){if(cancelled())throw new OperationCanceledException();string relative=entry.FullName.StartsWith(prefix,StringComparison.Ordinal)?entry.FullName.Substring(prefix.Length):"";if(relative.Length==0||relative.StartsWith("/")||relative.Contains(":")||relative.Replace('\\','/').Split('/').Any(p=>p==".."))continue;
                bool include=relative=="素材清单.json"||(relative.StartsWith("quality-v",StringComparison.Ordinal)&&relative.EndsWith(".webp",StringComparison.OrdinalIgnoreCase));if(!include)continue;if(entry.Length>80*1024*1024)throw new InvalidDataException("白龙单个动作文件过大。");string target=Path.GetFullPath(Path.Combine(extracted,relative));if(!target.StartsWith(Path.GetFullPath(extracted)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("白龙动作路径无效。");Directory.CreateDirectory(Path.GetDirectoryName(target));entry.ExtractToFile(target,true);
            }
        }return extracted;
    }
    static string Hash(string path){using(var file=File.OpenRead(path))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
    static Dictionary<string,object> Item(object value){return value as Dictionary<string,object>;}
    public static string Plan(string input,string state){
        string manifest=Path.Combine(input,"素材清单.json");if(!File.Exists(manifest)||new FileInfo(manifest).Length>1024*1024)throw new InvalidDataException("请选择含素材清单.json的白龙素材目录。");
        var json=new JavaScriptSerializer{MaxJsonLength=1024*1024}.Deserialize<Dictionary<string,object>>(File.ReadAllText(manifest));var items=new List<Dictionary<string,object>>();items.Add(Item(json["idle"]));object value;
        if(json.TryGetValue("additionalIdleAnimations",out value))foreach(object item in (IEnumerable)value)items.Add(Item(item));
        if(json.TryGetValue("variants",out value))foreach(object item in (IEnumerable)value)items.Add(Item(item));
        foreach(string kind in new[]{"dragAnimation","landingAnimation","quotaAnimation"})if(json.TryGetValue(kind,out value)&&value!=null)items.Add(Item(value));
        if(json.TryGetValue("workAnimations",out value))foreach(object item in (IEnumerable)value)items.Add(Item(item));
        // Some author manifests call this collection workingAnimations.
        if(json.TryGetValue("workingAnimations",out value))foreach(object item in (IEnumerable)value)items.Add(Item(item));
        if(items.Count<15||items.Count>40)throw new InvalidDataException("白龙完整动作清单无效。");
        string stage=Path.Combine(state,"pet-imports",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);var actions=new List<object>();
        foreach(var item in items){string relative=item.ContainsKey("playbackFile")?Convert.ToString(item["playbackFile"]):Convert.ToString(item["file"]).Replace(".webm",".webp");string path=Path.GetFullPath(Path.Combine(input,relative));if(!path.StartsWith(Path.GetFullPath(input).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(path)||Path.GetExtension(path).ToLowerInvariant()!=".webp")throw new InvalidDataException("白龙动作文件缺失或路径无效。");
            actions.Add(new{id=Convert.ToString(item["id"]),name=item.ContainsKey("name")?Convert.ToString(item["name"]):Convert.ToString(item["id"]),input=path,file="action-"+actions.Count+".gif",durationMs=Convert.ToInt32(item["durationMs"])});
        }
        string request=Path.Combine(stage,"bailong-request.json");File.WriteAllText(request,new JavaScriptSerializer().Serialize(new{stateRoot=state,stage,actions}),new UTF8Encoding(false));return request;
    }
}
internal sealed partial class Manager {
    void LoadRemielle(){
        if(petImporting||pets==null)return;petImporting=true;importButton.Enabled=false;previewHint.Text="正在准备作者的小蕾米原素材…";
        ThreadPool.QueueUserWorkItem(delegate{try{string input=RemotePetMaterials.ObtainRemielle(root,stateDirectory,text=>Ui(delegate{previewHint.Text=text;}),()=>cancelPetImport);Ui(delegate{petImporting=false;ImportPetFile(input,"xiaolemi");});}catch(Exception error){Ui(delegate{petImporting=false;importButton.Enabled=true;previewHint.Text="形象准备未完成："+error.Message;});}});
    }
    void ImportBailong(string inputFolder=null){
        if(petImporting||pets==null)return;petImporting=true;importButton.Enabled=false;previewHint.Text="正在查找白龙原动作素材…";
        ThreadPool.QueueUserWorkItem(delegate{string stage=null;try{
            string folder=inputFolder??BailongMaterials.Obtain(root,stateDirectory,message=>Ui(delegate{previewHint.Text=message;}),()=>cancelPetImport);string request=BailongMaterials.Plan(folder,stateDirectory);stage=Path.GetDirectoryName(request);
            var info=new ProcessStartInfo(Path.Combine(root,@"engine\runtime\node\node.exe"),PetCommandQuote(Path.Combine(root,@"engine\scripts\prepare-bailong.mjs"))+" "+PetCommandQuote(request)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            PetProcessScope scope;lock(petImportGate){if(cancelPetImport)throw new OperationCanceledException();scope=petImportProcess=new PetProcessScope(info);}
            using(scope){var process=scope.Process;var stderr=process.StandardError.ReadToEndAsync();string line;while((line=process.StandardOutput.ReadLine())!=null){if(line.StartsWith("DREAM_PROGRESS:")){string text=line.Substring(15);Ui(delegate{previewHint.Text=text;});}}process.WaitForExit();if(process.ExitCode!=0)throw new IOException(stderr.Result.Trim());}
            lock(petImportGate){if(petImportProcess==scope)petImportProcess=null;}if(cancelPetImport)throw new OperationCanceledException();
            string destination=Path.Combine(stateDirectory,"pets","bailong");Directory.CreateDirectory(Path.GetDirectoryName(destination));if(Directory.Exists(destination))throw new IOException("本机白龙副本已经存在，请重新选择该形象。");File.Delete(request);Directory.Move(stage,destination);stage=null;var pack=PetPack.Read(destination,false);
            Ui(delegate{pets.Reload();BuildPetCards();pets.Select(pack);previewHint.Text="白龙娘已就绪，右键可选择 "+pack.Actions.Count+" 段完整动作。";});
        }catch(Exception error){Ui(delegate{previewHint.Text="白龙准备未完成："+error.Message;});}finally{if(stage!=null)PetImporter.DeleteStage(stage,stateDirectory);Ui(delegate{petImporting=false;importButton.Enabled=true;});}});
    }
}
