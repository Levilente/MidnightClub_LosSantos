using System; using System.IO; using System.Linq; using MidnightClub.Racing; using MidnightClub.Core;
class T4{ static int f=0; static void C(bool c,string n){Console.WriteLine((c?"PASS ":"FAIL ")+n); if(!c)f++;}
static void Main(){ var dir=Path.Combine(Path.GetTempPath(),"mcrace"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); Log.Init(dir);
 var lib=RaceLibrary.Load(dir);
 C(lib.Races.Count==16,"16 races generated ("+lib.Races.Count+")");
 C(lib.Races.All(r=>r.Points.Count>=2),"every race has start + finish");
 C(lib.Races.Where(r=>r.Kind==RaceType.Circuit).All(r=>r.Points.Count==3),"circuits have a via point");
 C(lib.Races.Select(r=>r.Id).Distinct().Count()==16,"unique ids");
 C(lib.Championships.All(c=>c.Races.All(id=>lib.Races.Any(r=>r.Id==id))),"championship rounds reference real races");
 C(lib.Races.Zip(lib.Races.Skip(1),(a,b)=>b.RequiredRep>=a.RequiredRep).All(x=>x),"REP requirements ascend");
 var again=RaceLibrary.Load(dir);
 C(again.Races.Count==16 && again.Races[3].Name==lib.Races[3].Name && again.Races[0].Points[1][0]==lib.Races[0].Points[1][0],"races.json round-trips");
 C(lib.Races.All(r=>{var a=r.Points[0];var b=r.Points[1];var d=Math.Sqrt((a[0]-b[0])*(a[0]-b[0])+(a[1]-b[1])*(a[1]-b[1]));return d>=600;}),"start and finish are far enough apart");
 Console.WriteLine(File.ReadAllText(Path.Combine(dir,"races.json")).Substring(0,300));
 Console.WriteLine(f==0?"ALL PASSED":f+" FAILED"); Environment.Exit(f);}}
