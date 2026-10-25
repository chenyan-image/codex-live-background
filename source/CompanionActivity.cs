using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class PetActivity {
    public string Label="Codex · 陪伴中";public bool Active;public int ActiveCount;
}
// Only event kinds, timestamps and tool names are retained. Conversation text
// is neither displayed nor written to another file; no log contents leave PC.
internal sealed class PetActivityReader : IDisposable {
    sealed class Cursor {public long Offset;public string Pending="";public bool Active,Waiting,Subagent;public string Phase="思考";public DateTime Updated;}
    readonly string directory;readonly Dictionary<string,Cursor> cursors=new Dictionary<string,Cursor>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> pending=new HashSet<string>(StringComparer.OrdinalIgnoreCase);readonly object gate=new object();
    FileSystemWatcher watcher;bool initial=true,disposed;
    public PetActivityReader(string home){directory=Path.Combine(home,"sessions");if(Directory.Exists(directory)){
        watcher=new FileSystemWatcher(directory,"*.jsonl"){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName|NotifyFilters.Size};watcher.Changed+=Queue;watcher.Created+=Queue;watcher.Renamed+=delegate(object sender,RenamedEventArgs e){Queue(sender,e);};watcher.Error+=delegate{lock(gate)initial=true;};watcher.EnableRaisingEvents=true;
    }}
    void Queue(object sender,FileSystemEventArgs e){lock(gate){if(!disposed&&pending.Count<40)pending.Add(e.FullPath);}}
    public PetActivity Read(){lock(gate){
        if(disposed)return new PetActivity();
        if(initial){initial=false;if(Directory.Exists(directory))try{foreach(var file in Directory.EnumerateFiles(directory,"*.jsonl",SearchOption.AllDirectories).Take(10000).Select(path=>new FileInfo(path)).Where(info=>info.LastWriteTimeUtc>DateTime.UtcNow.AddHours(-12)).OrderByDescending(info=>info.LastWriteTimeUtc).Take(16))pending.Add(file.FullName);}catch(IOException){}catch(UnauthorizedAccessException){}}
        string[] files=pending.ToArray();pending.Clear();foreach(string file in files)ReadFile(file);
        var live=cursors.Values.Where(c=>!c.Subagent&&c.Updated>DateTime.UtcNow.AddMinutes(-20)).OrderByDescending(c=>c.Updated).ToArray();var busy=live.Where(c=>c.Active).ToArray();
        if(busy.Length>0)return new PetActivity{Active=true,ActiveCount=busy.Length,Label="执行/"+busy[0].Phase+(busy.Length>1?" +"+(busy.Length-1):"")};
        if(live.Length>0&&live[0].Waiting)return new PetActivity{Label="等待确认"};return new PetActivity();
    }}
    void ReadFile(string path){
        try{Cursor cursor;if(!cursors.TryGetValue(path,out cursor)){cursor=new Cursor();cursors[path]=cursor;}
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
                if(file.Length<cursor.Offset){cursor.Offset=0;cursor.Pending="";cursor.Active=false;}
                if(cursor.Offset==0&&file.Length>256*1024){var header=new byte[(int)Math.Min(65536,file.Length)];int count=file.Read(header,0,header.Length);string text=Encoding.UTF8.GetString(header,0,count);int newline=text.IndexOf('\n');if(newline>=0)Apply(cursor,text.Substring(0,newline));file.Position=file.Length-256*1024;cursor.Pending="";}
                else file.Position=cursor.Offset;
                int length=(int)Math.Min(512*1024,file.Length-file.Position);if(length<=0)return;var bytes=new byte[length];int read=file.Read(bytes,0,length);cursor.Offset=file.Position;
                string[] lines=(cursor.Pending+Encoding.UTF8.GetString(bytes,0,read)).Split('\n');cursor.Pending=lines[lines.Length-1];if(cursor.Pending.Length>1024*1024)cursor.Pending="";
                for(int i=0;i<lines.Length-1;i++)Apply(cursor,lines[i]);
            }
        }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}
        if(cursors.Count>64)foreach(string key in cursors.OrderBy(pair=>pair.Value.Updated).Take(cursors.Count-64).Select(pair=>pair.Key).ToArray())cursors.Remove(key);
    }
    static void Apply(Cursor cursor,string line){
        if(line.Length>1024*1024)return;object record;try{record=new JavaScriptSerializer{MaxJsonLength=1024*1024}.DeserializeObject(line);}catch(ArgumentException){return;}
        string type=Convert.ToString(PetQuotaSnapshot.Get(record,"type"));object payload=PetQuotaSnapshot.Get(record,"payload");DateTime time;if(!DateTime.TryParse(Convert.ToString(PetQuotaSnapshot.Get(record,"timestamp")),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out time))time=DateTime.UtcNow;time=time.ToUniversalTime();
        if(type=="session_meta"){var source=PetQuotaSnapshot.Get(payload,"source");cursor.Subagent=source is Dictionary<string,object>&&PetQuotaSnapshot.Get(source,"subagent")!=null;return;}
        string kind=Convert.ToString(PetQuotaSnapshot.Get(payload,"type"));bool changed=false;
        if(type=="event_msg"){
            if(kind=="task_started"||kind=="agent_reasoning"){cursor.Active=true;cursor.Waiting=false;cursor.Phase="思考";changed=true;}
            else if(kind=="exec_command_begin"){cursor.Active=true;cursor.Phase="命令";changed=true;}
            else if(kind=="exec_command_end"){cursor.Active=true;cursor.Phase="思考";changed=true;}
            else if(kind=="task_complete"||kind=="turn_aborted"){cursor.Active=cursor.Waiting=false;changed=true;}
            else if(kind=="exec_approval_request"||kind=="apply_patch_approval_request"){cursor.Active=false;cursor.Waiting=true;changed=true;}
        }else if(type=="response_item"&&(kind=="function_call"||kind=="custom_tool_call")){
            string name=Convert.ToString(PetQuotaSnapshot.Get(payload,"name"));cursor.Active=true;cursor.Waiting=false;cursor.Phase=name.IndexOf("exec",StringComparison.OrdinalIgnoreCase)>=0?"命令":"工具";changed=true;
        }else if(type=="response_item"&&kind=="reasoning"){
            cursor.Active=true;cursor.Waiting=false;cursor.Phase="思考";changed=true;
        }else if(type=="response_item"&&kind=="message"&&Convert.ToString(PetQuotaSnapshot.Get(payload,"role"))=="assistant"&&Convert.ToString(PetQuotaSnapshot.Get(payload,"phase"))=="final"){
            cursor.Active=cursor.Waiting=false;changed=true;
        }else if(type=="response_item"&&cursor.Active&&(kind=="function_call_output"||kind=="custom_tool_call_output"||kind=="message")){
            // Keep long, still-running turns fresh without retaining their content.
            changed=true;
        }
        if(changed)cursor.Updated=time;
    }
    public void Dispose(){lock(gate){disposed=true;if(watcher!=null){watcher.Dispose();watcher=null;}pending.Clear();cursors.Clear();}}
}
