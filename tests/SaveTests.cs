using System; using System.IO; using MidnightClub.Persistence; using MidnightClub.Core;
class T{ static int fails=0; static void Check(bool c,string n){Console.WriteLine((c?"PASS ":"FAIL ")+n); if(!c)fails++;}
static void Main(){ var dir=Path.Combine(Path.GetTempPath(),"mctest"+Guid.NewGuid().ToString("N")); Log.Init(dir);
 var s=new SaveManager(dir,3); Check(!s.Load(),"fresh dir has no profile");
 var d=new SaveData{Cash=15000}; d.Vehicles.Add(new OwnedVehicle{Id="a",Model="futo"}); d.Vehicles[0].Mods.Add(new ModEntry(11,3)); s.CreateNew(d);
 Check(s.Save(),"first save"); 
 for(int i=0;i<6;i++){ s.Data.Cash+=1; System.Threading.Thread.Sleep(1100); Check(s.Save(),"save "+i);} 
 var bak=Directory.GetFiles(Path.Combine(dir,"saves"),"*.bak"); Check(bak.Length==3,"backups pruned to 3 (got "+bak.Length+")");
 var s2=new SaveManager(dir,3); Check(s2.Load()&&s2.Data.Cash==15006&&s2.Data.Vehicles[0].Mods[0].Index==3,"round-trip cash+mods");
 File.WriteAllText(Path.Combine(dir,"saves","profile.json"),"{garbage");
 var s3=new SaveManager(dir,3); Check(s3.Load()&&s3.Data.Cash==15005,"recovered from newest backup after corruption (cash "+(s3.Data==null?-1:s3.Data.Cash)+")");
 Check(s3.TryBeginTransaction("buy-1")&&!s3.TryBeginTransaction("buy-1"),"duplicate transaction rejected");
 s3.Data.Cash=-5; Check(!s3.Save(),"invalid data (negative cash) refused, good file kept");
 var s4=new SaveManager(dir,3); Check(s4.Load()&&s4.Data.Cash>=0,"profile still valid after refused save");
 Console.WriteLine(fails==0?"ALL PASSED":fails+" FAILED"); Environment.Exit(fails);}}
