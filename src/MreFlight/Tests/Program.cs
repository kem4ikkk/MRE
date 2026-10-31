// SPDX-License-Identifier: GPL-3.0-only
using MreFlight;
int passed=0;
void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);passed++;}
void Reject(Action action,string name){bool rejected=false;try{action();}catch(ArgumentOutOfRangeException){rejected=true;}Check(rejected,name);}
var original=new MovementValues {Gravity=0.7f,Speed=2.3f,Jump=1.4f};
var flight=new FlightState(original);
original.Gravity=99; // The session must own an independent pre-flight snapshot.
var idle=flight.Tick(false,false,false,true,true);
Check(idle.Gravity==0&&idle.Speed==1&&idle.Jump==1.4f,"entering hover does not overwrite saved jump settings");
Check(flight.Tick(true,false,false,true,true).Gravity<0,"jump can cause ascent rather than just disabling gravity");
Check(flight.Tick(false,true,true,true,true).Gravity>0,"sprint while looking down enables descent");
Check(flight.Tick(false,true,false,true,true).Gravity==0,"sprint looking forward does not leave descent stuck");
Check(flight.Tick(false,false,true,true,true).Gravity==0,"releasing descent input returns to hover");
Check(flight.Tick(true,true,true,true,true).Gravity<0,"simultaneous input uses defined ascent priority");
flight.SetSpeeds(3.5f,2.2f);
Check(flight.Tick(true,false,false,true,true).Gravity==-2.2f&&flight.Tick(false,false,false,true,true).Speed==3.5f,"speed update applies to ongoing physical flight");
var revoked=flight.Tick(true,false,false,false,true);
Check(!flight.Active&&revoked.Gravity==0.7f&&revoked.Speed==2.3f&&revoked.Jump==1.4f,"permission revocation restores exact pre-flight values");
revoked.Speed=101;
Check(flight.Stop().Speed==2.3f,"repeated cleanup is idempotent and returns independent values");
Check(flight.Tick(true,true,true,true,true).Gravity==0.7f,"a stopped session cannot resume via stale input");
var replaced=new FlightState(new MovementValues {Gravity=1.2f,Speed=1.1f,Jump=0.8f});
Check(replaced.Tick(false,false,false,true,false).Gravity==1.2f&&!replaced.Active,"player-session replacement triggers cleanup");
Reject(()=>new FlightState(new MovementValues(),float.NaN,1),"NaN horizontal speed rejected");
Reject(()=>new FlightState(new MovementValues(),1,float.PositiveInfinity),"infinite vertical speed rejected");
Reject(()=>new FlightState(new MovementValues(),0,1),"zero speed rejected");
Reject(()=>new FlightState(new MovementValues(),1,5.1f),"vertical speed ceiling enforced");
var bounded=new FlightState(new MovementValues {Gravity=1,Speed=1,Jump=1},2,2);
Reject(()=>bounded.SetSpeeds(100,2),"out-of-range change rejected");
Check(bounded.HorizontalSpeed==2&&bounded.VerticalSpeed==2&&bounded.Active,"rejected change preserves active flight configuration");
Console.WriteLine("PASS "+passed+" flight state/cleanup checks");
