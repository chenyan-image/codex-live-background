using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Reflection;using System.Web.Script.Serialization;
internal static class RepositoryRenameTests {
    static int checks;static JavaScriptSerializer Json=new JavaScriptSerializer();
    static void Need(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static SoftwareRelease Release(string repository){return new SoftwareRelease{version="1.7.0.19",tag="v1.7.0.1",url="https://github.com/"+repository+"/releases/download/v1.7.0.1/CodexHub-1.7.0.1-Update.zip",sha256=new string('a',64),bytes=12345};}
    static object ApiRelease(SoftwareRelease release,string repository,bool prerelease=false){return new{draft=false,prerelease=prerelease,tag_name=release.tag,assets=new object[]{new{name="CodexHub-Update.json",browser_download_url="https://github.com/"+repository+"/releases/download/"+release.tag+"/CodexHub-Update.json"},new{name=Path.GetFileName(release.url),browser_download_url="https://github.com/"+repository+"/releases/download/"+release.tag+"/"+Path.GetFileName(release.url),size=release.bytes,digest="sha256:"+release.sha256}}};}
    static int Main(string[] args){try{
        var canonical=Release(SoftwareUpdate.Repository);var legacy=Release(SoftwareUpdate.LegacyRepository);
        Need(SoftwareUpdate.ParseRelease(Json.Serialize(canonical)).tag=="v1.7.0.1","Canonical stable metadata was rejected");
        Need(SoftwareUpdate.ParseRelease(Json.Serialize(legacy)).tag=="v1.7.0.1","Legacy metadata was rejected");
        Need(SoftwareUpdate.SameReleaseLocation(canonical.url,legacy.url),"Rename aliases do not match the same asset");
        Need(!SoftwareUpdate.SameReleaseLocation(canonical.url,legacy.url.Replace("chenyan-image/","foreign/")),"Foreign repository gained access");
        Need(!SoftwareUpdate.SameReleaseLocation(canonical.url,legacy.url.Replace("v1.7.0.1/","v1.7.0.2/")),"Different release tags were treated as equivalent");
        int reads=0;var fromApi=SoftwareUpdate.FindRelease(new Version(1,7,0,18),(url,max)=>{if(url==SoftwareUpdate.StableChannel)throw new UpdateHttpFailure("raw.githubusercontent.com",404,DateTime.MinValue);if(url==SoftwareUpdate.ReleasesApi)return Json.Serialize(new[]{ApiRelease(legacy,SoftwareUpdate.Repository)});reads++;return Json.Serialize(legacy);},()=>DateTime.UtcNow,new UpdateLookupState());
        Need(fromApi!=null&&fromApi.version=="1.7.0.19"&&reads==1,"Canonical API/legacy manifest bridge failed");
        var preview=new SoftwareRelease{version="1.8.0.99",tag="v1.8.0-preview.1",url="https://github.com/"+SoftwareUpdate.Repository+"/releases/download/v1.8.0-preview.1/CodexHub-1.8.0-preview.1-Update.zip",bytes=12345,sha256=new string('b',64)};
        var stable=SoftwareUpdate.FindRelease(new Version(1,7,0,18),(url,max)=>{if(url==SoftwareUpdate.StableChannel)throw new UpdateHttpFailure("raw.githubusercontent.com",404,DateTime.MinValue);if(url==SoftwareUpdate.ReleasesApi)return Json.Serialize(new[]{ApiRelease(preview,SoftwareUpdate.Repository,true),ApiRelease(canonical,SoftwareUpdate.Repository)});if(url.Contains("preview"))throw new Exception("Preview asset should not be read");return Json.Serialize(canonical);},()=>DateTime.UtcNow,new UpdateLookupState());
        Need(stable!=null&&stable.tag=="v1.7.0.1","Stable lookup selected a prerelease");
        Need(SoftwareUpdate.FindRelease(new Version(1,7,0,18),(url,max)=>Json.Serialize(preview),()=>DateTime.UtcNow,new UpdateLookupState())==null,"A preview raw channel was offered as stable");
        var old=Assembly.LoadFile(Path.Combine(Path.GetFullPath(args[0]),"CodexLiveBackground.exe"));var flags=BindingFlags.Static|BindingFlags.NonPublic;var updater=old.GetType("SoftwareUpdate",true);var state=Activator.CreateInstance(old.GetType("UpdateLookupState",true),true);
        Func<string,int,string> oldFetch=(url,max)=>Json.Serialize(legacy);Func<DateTime> clock=()=>DateTime.UtcNow;
        var method=updater.GetMethods(flags).Single(m=>m.Name=="FindRelease"&&m.GetParameters().Length==4);var offered=method.Invoke(null,new object[]{new Version(1,7,0,18),oldFetch,clock,state});
        Need(offered!=null&&Convert.ToString(offered.GetType().GetProperty("version").GetValue(offered,null))=="1.7.0.19","Published 1.7.0 cannot consume the compatibility channel");
        Console.WriteLine(Json.Serialize(new{pass=true,checks,canonicalAndLegacyAccepted=true,foreignRepositoryRejected=true,stableOnly=true,publishedClientCanUpgrade=true,actualAppsChanged=false}));return 0;
    }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
