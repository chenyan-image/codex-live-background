using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
internal static class ThemeNameLibraryTest {
 static int Main(string[] args){
  string root=args[0],name=String.Concat(Enumerable.Repeat(Char.ConvertFromUtf32(0x1F319),4096));
  if(WallpaperLibrary.ValidateName(name)!=name)throw new Exception("Emoji boundary changed");
  bool rejected=false;try{WallpaperLibrary.ValidateName(name+"x");}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("4097 accepted");
  string directory=Path.Combine(root,"library"),image=Path.Combine(root,"background.png");var library=new WallpaperLibrary(directory);var entry=library.Add(image,name,"local",null,"");library.Applied(entry);
  if(new WallpaperLibrary(directory).Snapshot()[0].Name!=name)throw new Exception("Long library name lost after reopening");
  string workshop=Path.Combine(root,@"steamapps\workshop\content\431960\1");Directory.CreateDirectory(workshop);File.Copy(image,Path.Combine(workshop,"wallpaper.png"));
  string chinese=new String('\u957f',4096);File.WriteAllText(Path.Combine(workshop,"project.json"),new JavaScriptSerializer().Serialize(new {title=chinese,type="image",file="wallpaper.png"}),Encoding.UTF8);
  var scan=SteamWallpapers.Scan(new[]{root},()=>false);if(scan.Entries.Count!=1||scan.Entries[0].Name!=chinese)throw new Exception("Steam title truncated");
  var old=library.Add(scan.Entries[0].Path,"previous short title","steam",null,"");library.Applied(scan.Entries[0]);if(new WallpaperLibrary(directory).Snapshot().First(x=>x.Id==old.Id).Name!=chinese)throw new Exception("Steam history retained truncated title");
  // One round-trip at the existing 2000-record cap checks the larger index bound
  // without repeatedly saving a growing fixture.
  var index=new LibraryIndex();string escaped=new String('<',4096);for(int i=0;i<2000;i++)index.Entries.Add(new WallpaperEntry{Id="name-"+i,Name=escaped,Path=image,Source="local",Kind="image"});
  string capacity=Path.Combine(root,"capacity");Directory.CreateDirectory(capacity);File.WriteAllText(Path.Combine(capacity,"wallpaper-library.json"),new JavaScriptSerializer{MaxJsonLength=64*1024*1024}.Serialize(index),new UTF8Encoding(false));
  var full=new WallpaperLibrary(capacity);if(full.Snapshot().Count!=2000||full.Warning!="")throw new Exception("Large-name index failed to open");full.Applied(full.Snapshot()[0]);
  var reopened=new WallpaperLibrary(capacity);if(reopened.Snapshot().Count!=2000||reopened.Snapshot()[1999].Name!=escaped)throw new Exception("Large-name index failed to persist");
  return 0;
 }
}
