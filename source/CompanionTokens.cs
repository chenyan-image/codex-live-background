using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class PetTokenSnapshot {
    public bool Available,Loading;public double Today,SevenDays,ThirtyDays;public double[] Hourly=new double[24],Daily=new double[30];
    public static string Format(double value){return value>=1000000000?(value/1000000000D).ToString("0.##",CultureInfo.InvariantCulture)+"B":value>=1000000?(value/1000000D).ToString("0.##",CultureInfo.InvariantCulture)+"M":value>=1000?(value/1000D).ToString("0.##",CultureInfo.InvariantCulture)+"K":value.ToString("0",CultureInfo.InvariantCulture);}
    public double Total(int range){return range==0?Today:range==1?SevenDays:ThirtyDays;}
    public double[] Series(int range){return range==0?Hourly:range==1?Daily.Skip(23).ToArray():Daily;}
}

// Local numeric counters only. No conversation, arguments or tool outputs are
// retained. The reader supports both old cumulative token_count events and
// per-response token_usage_record events without counting the same usage twice.
internal sealed class PetTokenReader : IDisposable {
    sealed class Sample {public DateTime Time;public double Value;public string Key;public bool Modern;}
    sealed class Cursor {public long Offset;public string Pending="";public bool SkipLine,Fork,SeenOld;public double Previous;public DateTime FirstModern=DateTime.MaxValue;public readonly List<Sample> Samples=new List<Sample>();}
    readonly string home;readonly Dictionary<string,Cursor> files=new Dictionary<string,Cursor>(StringComparer.OrdinalIgnoreCase);DateTime enumerated;volatile bool disposed;
    public PetTokenReader(string codexHome){home=codexHome;}
    public PetTokenSnapshot Read(DateTime? localNow=null){
        DateTime now=localNow??DateTime.Now,min=now.Date.AddDays(-29);bool incomplete=false;
        if((DateTime.UtcNow-enumerated).TotalSeconds>=30){enumerated=DateTime.UtcNow;foreach(string name in new[]{"sessions","archived_sessions"}){string root=Path.Combine(home,name);if(!Directory.Exists(root))continue;try{foreach(string file in Directory.EnumerateFiles(root,"*.jsonl",SearchOption.AllDirectories).Take(20000))if(File.GetLastWriteTime(file)>=min&&!files.ContainsKey(file))files[file]=new Cursor();}catch(IOException){}catch(UnauthorizedAccessException){}}}
        int budget=32*1024*1024;
        foreach(var pair in files){if(disposed)break;try{var info=new FileInfo(pair.Key);if(!info.Exists)continue;if(info.Length<pair.Value.Offset){pair.Value.Offset=0;pair.Value.Pending="";pair.Value.Previous=0;pair.Value.SeenOld=false;pair.Value.Samples.Clear();pair.Value.FirstModern=DateTime.MaxValue;}
            if(info.Length>pair.Value.Offset){if(budget>0)ReadFile(pair.Key,pair.Value,ref budget,min);if(new FileInfo(pair.Key).Length>pair.Value.Offset)incomplete=true;}}
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        var result=new PetTokenSnapshot{Loading=incomplete};var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var cursor in files.Values)foreach(var sample in cursor.Samples){if(!sample.Modern&&sample.Time>=cursor.FirstModern)continue;if(!seen.Add(sample.Key))continue;DateTime time=sample.Time.ToLocalTime();int day=(time.Date-min).Days;if(day<0||day>=30)continue;result.Available=true;result.Daily[day]+=sample.Value;result.ThirtyDays+=sample.Value;if(day>=23)result.SevenDays+=sample.Value;if(time.Date==now.Date){result.Today+=sample.Value;result.Hourly[time.Hour]+=sample.Value;}}
        return result;
    }
    void ReadFile(string path,Cursor cursor,ref int budget,DateTime min){
        using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){file.Position=cursor.Offset;var bytes=new byte[256*1024];
            while(!disposed&&budget>0){int count=file.Read(bytes,0,Math.Min(bytes.Length,budget));if(count==0)break;budget-=count;cursor.Offset=file.Position;string chunk=Encoding.UTF8.GetString(bytes,0,count);string[] lines=(cursor.Pending+chunk).Split('\n');cursor.Pending=lines[lines.Length-1];
                for(int i=0;i<lines.Length-1;i++){if(cursor.SkipLine){cursor.SkipLine=false;continue;}Apply(cursor,lines[i],min);}
                if(cursor.Pending.Length>1024*1024){cursor.Pending="";cursor.SkipLine=true;}
            }
        }
    }
    static void Apply(Cursor cursor,string line,DateTime min){
        if(line.Length>1024*1024||(line.IndexOf("token_count",StringComparison.Ordinal)<0&&line.IndexOf("token_usage_record",StringComparison.Ordinal)<0&&line.IndexOf("session_meta",StringComparison.Ordinal)<0))return;
        object record;try{record=new JavaScriptSerializer{MaxJsonLength=1024*1024}.DeserializeObject(line);}catch(ArgumentException){return;}
        string type=Convert.ToString(PetQuotaSnapshot.Get(record,"type"));object payload=PetQuotaSnapshot.Get(record,"payload");
        if(type=="session_meta"){cursor.Fork=PetQuotaSnapshot.Get(payload,"forked_from_id")!=null;return;}
        DateTime time;if(!DateTime.TryParse(Convert.ToString(PetQuotaSnapshot.Get(record,"timestamp")),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out time))return;time=time.ToUniversalTime();
        double value;string key;bool modern=type=="token_usage_record";
        if(modern){var total=PetQuotaSnapshot.Number(PetQuotaSnapshot.Get(PetQuotaSnapshot.Get(payload,"usage"),"total_tokens"));if(!total.HasValue||total<0)return;value=total.Value;string id=Convert.ToString(PetQuotaSnapshot.Get(payload,"response_id"));key="response|"+(String.IsNullOrEmpty(id)?time.ToString("o")+"|"+value.ToString("R",CultureInfo.InvariantCulture):id);cursor.FirstModern=time<cursor.FirstModern?time:cursor.FirstModern;}
        else if(type=="event_msg"&&Convert.ToString(PetQuotaSnapshot.Get(payload,"type"))=="token_count"){
            object info=PetQuotaSnapshot.Get(payload,"info");var total=PetQuotaSnapshot.Number(PetQuotaSnapshot.Get(PetQuotaSnapshot.Get(info,"total_token_usage"),"total_tokens"));if(!total.HasValue||total<0)return;
            value=total>=cursor.Previous?total.Value-cursor.Previous:0;if(!cursor.SeenOld&&cursor.Fork)value=0;cursor.SeenOld=true;cursor.Previous=total.Value;
            if(value==0)return;key="legacy|"+time.ToString("o")+"|"+total.Value.ToString("R",CultureInfo.InvariantCulture);
        }else return;
        if(time.ToLocalTime()>=min)cursor.Samples.Add(new Sample{Time=time,Value=value,Key=key,Modern=modern});
    }
    public void Dispose(){disposed=true;}
}
