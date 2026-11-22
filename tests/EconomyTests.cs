using System; using System.IO; using MidnightClub.Persistence; using MidnightClub.Core; using MidnightClub.Economy;
class T2{ static int f=0; static void C(bool c,string n){Console.WriteLine((c?"PASS ":"FAIL ")+n); if(!c)f++;}
static void Main(){ var dir=Path.Combine(Path.GetTempPath(),"mceco"+Guid.NewGuid().ToString("N")); Log.Init(dir);
 var s=new SaveManager(dir,3); s.CreateNew(new SaveData{Cash=10000}); var m=new TransactionManager(s);
 int events=0; EventBus.Subscribe<MoneyChangedEvent>(e=>events++);
 C(!m.TrySpend(20000,"garage","too expensive","g1"),"cannot overspend"); C(s.Data.Cash==10000,"cash untouched after refusal");
 C(m.TrySpend(8000,"garage","buy","g1"),"spend ok"); C(s.Data.Cash==2000,"cash deducted");
 s.Data.Cash+=100000; C(!m.TrySpend(8000,"garage","buy again","g1"),"same transaction id cannot be charged twice");
 C(!m.TrySpend(-5,"x","negative",null),"negative amount refused");
 C(m.ChargeUpTo(999999,"repair","tow")==102000 && s.Data.Cash==0,"ChargeUpTo never goes below zero");
 C(m.Earn(5000,250,"race","win","race-1") && !m.Earn(5000,250,"race","win","race-1"),"reward dedupe");
 C(s.Data.Cash==5000&&s.Data.Rep==250,"balances after reward");
 C(s.Data.Ledger.Count==3 && events==3,"ledger + events recorded ("+s.Data.Ledger.Count+","+events+")");
 var s2=new SaveManager(dir,3); C(s2.Load()&&s2.Data.Ledger.Count>=1,"ledger persisted by purchase save");
 Console.WriteLine(f==0?"ALL PASSED":f+" FAILED"); Environment.Exit(f);}}
