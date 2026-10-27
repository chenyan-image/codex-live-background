using System;using System.IO;using System.Text;
internal static class DailyUpdates {
    public static bool TryBegin(string state,DateTime localTime){Directory.CreateDirectory(state);string file=Path.Combine(state,"program-update-check-day.txt"),today=localTime.ToString("yyyy-MM-dd");try{using(var stream=new FileStream(file,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){if(stream.Length>128)throw new InvalidDataException("更新检查记录无效。");var bytes=new byte[(int)stream.Length];stream.Read(bytes,0,bytes.Length);if(Encoding.UTF8.GetString(bytes)==today)return false;bytes=Encoding.UTF8.GetBytes(today);stream.Position=0;stream.Write(bytes,0,bytes.Length);stream.SetLength(bytes.Length);stream.Flush();return true;}}catch(IOException){return false;}}
}
