using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Web.Script.Serialization;
internal static class DouyinStandaloneTests {
    static int checks;static void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
    static object Field(object value,string name){return value.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(value);}
    [STAThread]static int Main(string[] args){
        string old=Environment.GetEnvironmentVariable("CODEX_HOME");try{
            Directory.CreateDirectory(args[1]);string poison=Path.Combine(args[1],"no-codex-installed");File.WriteAllText(poison,"not a Codex home");Environment.SetEnvironmentVariable("CODEX_HOME",poison);
            using(var owner=new Control())using(var pet=new PetController(owner,args[0],Path.Combine(args[1],"profile"),true,true)){
                Check(pet.Standalone,"independent mode recorded");Check(Field(pet,"reader")==null,"no quota reader");Check(Field(pet,"activity")==null,"no Codex session watcher");Check(Field(pet,"tokenReader")==null,"no token reader");
                Check(!pet.Preferences.CodexOnly&&!pet.Preferences.FollowCodex,"no Codex surface or lifetime dependency");pet.SetScope(true);Check(!pet.Preferences.CodexOnly,"independent mode cannot accidentally require a Codex surface");pet.Refresh();Check(Field(pet,"reader")==null,"quota click does not create a Codex reader");
                Check(!((Timer)Field(pet,"surfaceTimer")).Enabled&&!((Timer)Field(pet,"activityTimer")).Enabled,"no Codex polling timers");Check(pet.Packs.Count>0,"bundled pets available without Codex");
            }
            using(var owner=new Control())using(var normal=new PetController(owner,args[0],Path.Combine(args[1],"normal-profile"),false)){Check(!normal.Standalone&&Field(normal,"reader")!=null,"original Codex mode retained");}
            Check(File.ReadAllText(poison)=="not a Codex home","Codex home not changed");File.WriteAllText(Path.Combine(args[1],"result.json"),new JavaScriptSerializer().Serialize(new{pass=true,checks=checks,codexRequired=false,windowsShown=0}));Console.WriteLine("PASS "+checks+" independent-mode checks");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{Environment.SetEnvironmentVariable("CODEX_HOME",old);}
    }
}
