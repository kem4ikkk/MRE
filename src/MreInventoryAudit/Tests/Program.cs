using MreInventoryAudit;

int checks=0;
void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);checks++;}
void MustFail(Action action,string message){bool failed=false;try{action();}catch{failed=true;}Check(failed,message);}
string directory=Path.Combine(Path.GetTempPath(),"mre-inventory-tests-"+Guid.NewGuid().ToString("N"));
var ledger=new DeathLedger(directory);
DateTime now=new DateTime(2026,10,30,10,0,0,DateTimeKind.Utc);
StoredItem Item(){return new StoredItem {Id=95,Amount=1,Quality=100,Page=2,State=new byte[]{1,2,3}};}
var source=Item();var death=ledger.Capture(1,now,new[]{source});source.State[0]=99;
Check(ledger.Get(1,death.Id).Items[0].State[0]==1,"capture stores independent state bytes");
var copy=ledger.Get(1,death.Id);copy.Items[0].State[0]=99;
Check(ledger.Get(1,death.Id).Items[0].State[0]==1,"returned DTO cannot alter durable snapshot");
Check(new DeathLedger(directory).Get(1,death.Id).AtUtc==now,"snapshot survives new ledger with exact UTC");
var delivery=new FakeDelivery {OnDeliver=()=>Check(ledger.Get(1,death.Id).Status=="pending","reservation durable before inventory delivery")};
Check(RestoreTransaction.Execute(ledger,1,death.Id,7,delivery)=="completed"&&delivery.DeliverCalls==1,"successful restoration completes once");
var repeat=new FakeDelivery();
Check(RestoreTransaction.Execute(new DeathLedger(directory),1,death.Id,7,repeat)=="unavailable"&&repeat.DeliverCalls==0,"restart cannot repeat completed compensation");
death=ledger.Capture(1,now,new[]{Item()});var full=new FakeDelivery {HasSpace=false};
Check(RestoreTransaction.Execute(ledger,1,death.Id,7,full)=="inventory_full"&&ledger.Get(1,death.Id).Status=="available"&&full.DeliverCalls==0,"full inventory reserves nothing and grants nothing");
var partial=new FakeDelivery {Delivered=false};
MustFail(()=>RestoreTransaction.Execute(ledger,1,death.Id,7,partial),"partial inventory failure propagated");
Check(partial.RollbackCalls==1&&ledger.Get(1,death.Id).Status=="available","verified rollback allows explicit later retry");
var uncertain=new FakeDelivery {Delivered=false,RolledBack=false};
MustFail(()=>RestoreTransaction.Execute(ledger,1,death.Id,7,uncertain),"uncertain delivery failure propagated");
Check(new DeathLedger(directory).Get(1,death.Id).Status=="pending","unverified rollback remains pending after restart");
var blocked=new FakeDelivery();
Check(RestoreTransaction.Execute(ledger,1,death.Id,7,blocked)=="unavailable"&&blocked.DeliverCalls==0,"pending compensation blocks a second admin grant");
MustFail(()=>ledger.Complete(1,death.Id,8),"different administrator cannot complete another reservation");
for(int i=0;i<30;i++)ledger.Capture(1,now.AddMinutes(i+1),new[]{Item()});
Check(ledger.List(1).Length==20&&ledger.Get(1,death.Id).Status=="pending","retention keeps uncertain compensation instead of dropping it");
Check(ledger.List(2).Length==0,"players have separate death journals");
MustFail(()=>ledger.Capture(2,DateTime.SpecifyKind(now,DateTimeKind.Local),new[]{Item()}),"non UTC snapshot rejected");
File.WriteAllText(Path.Combine(directory,"2.json"),"corrupt");
var badDelivery=new FakeDelivery();
MustFail(()=>RestoreTransaction.Execute(ledger,2,"unknown",7,badDelivery),"corrupt history refuses restoration");
Check(badDelivery.DeliverCalls==0,"corrupt journal does not alter inventory");
Console.WriteLine("PASS: "+checks+" inventory ledger checks");

sealed class FakeDelivery:IInventoryDelivery
{
    public bool HasSpace=true,Delivered=true,RolledBack=true;
    public int DeliverCalls,RollbackCalls;
    public Action OnDeliver;
    public bool Preflight()=>HasSpace;
    public bool Deliver(){DeliverCalls++;OnDeliver?.Invoke();return Delivered;}
    public bool Rollback(){RollbackCalls++;return RolledBack;}
}
