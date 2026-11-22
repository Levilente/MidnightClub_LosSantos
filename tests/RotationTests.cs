using System; using System.Linq; using System.Collections.Generic; using MidnightClub.Dealerships;
class T3{ static int f=0; static void C(bool c,string n){Console.WriteLine((c?"PASS ":"FAIL ")+n); if(!c)f++;}
static void Main(){
 foreach (var cfg in new[]{ new{id="pdm",n=37,min=8,max=12}, new{id="luxury",n=120,min=2,max=3}, new{id="carmeet",n=5,min=3,max=6}, new{id="southern",n=300,min=15,max=20} })
 {
  var pool=Enumerable.Range(0,cfg.n).ToList();
  bool distinct=true, sized=true; var firstSeen=new Dictionary<int,int>();
  int rotations = (int)Math.Ceiling(cfg.n/(double)cfg.min)+1;
  for(int r=0;r<rotations;r++){ var pick=RotationMath.Pick(cfg.id,pool,cfg.min,cfg.max,r);
    if(pick.Distinct().Count()!=pick.Count) distinct=false;
    if(pick.Count<Math.Min(cfg.min,cfg.n)||pick.Count>Math.Min(cfg.max,cfg.n)) sized=false;
    foreach(var x in pick) if(!firstSeen.ContainsKey(x)) firstSeen[x]=r; }
  C(distinct, cfg.id+": no duplicate car within a rotation");
  C(sized, cfg.id+": stock size within min/max");
  C(firstSeen.Count==cfg.n, cfg.id+": every car offered within "+rotations+" rotations ("+firstSeen.Count+"/"+cfg.n+")");
  C(RotationMath.Pick(cfg.id,pool,cfg.min,cfg.max,7).SequenceEqual(RotationMath.Pick(cfg.id,pool,cfg.min,cfg.max,7)), cfg.id+": deterministic (reload can't reroll)");
 }
 var a=RotationMath.Pick("pdm",Enumerable.Range(0,37).ToList(),8,12,3); var b=RotationMath.Pick("southern",Enumerable.Range(0,37).ToList(),8,12,3);
 C(!a.SequenceEqual(b),"different dealerships get different stock");
 Console.WriteLine(f==0?"ALL PASSED":f+" FAILED"); Environment.Exit(f);}}
