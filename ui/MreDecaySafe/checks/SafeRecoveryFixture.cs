using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Rocket.Core.Plugins;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;
public sealed class SafeRecoveryFixture : RocketPlugin {
 static readonly ushort[] Ids={60971,60972,60973};
 protected override void Load() { StartCoroutine(Run()); }
 IEnumerator Run() {
  while(!Level.isLoaded) yield return new WaitForSeconds(1);
  yield return new WaitForSeconds(5);
  string exe=Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
  string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  string mode=Environment.GetEnvironmentVariable("MRE_SAFE_RECOVERY_MODE");
  if(Provider.serverID!="MRESafeRecovery" || !exe.StartsWith(temp,StringComparison.OrdinalIgnoreCase) || (mode!="create" && mode!="verify") || Provider.clients.Count!=0) { Logger.LogError("RECOVERY_REFUSED"); yield break; }
  try {
   int existing=0; foreach(var region in BarricadeManager.regions) existing+=region.drops.Count;
   if(mode=="create") {
    if(existing!=0) throw new Exception("world is not empty");
    for(int i=0;i<Ids.Length;i++) {
     var asset=Assets.find(EAssetType.ITEM,Ids[i]) as ItemStorageAsset;
     if(asset==null) throw new Exception("missing safe "+Ids[i]);
     if(BarricadeManager.dropBarricade(new Barricade(asset),null,new Vector3(i*3,300,0),0,0,0,1,0)==null) throw new Exception("drop failed");
    }
   }
   int seen=0; int mask=0;
   foreach(var region in BarricadeManager.regions) foreach(var drop in region.drops) {
    int i=Array.IndexOf(Ids,drop.asset.id); if(i<0 || (mask & (1<<i))!=0) throw new Exception("unexpected or duplicate object"); mask |= 1<<i;
    var storage=drop.interactable as InteractableStorage; var data=drop.GetServersideData();
    if(storage==null || data.owner!=1 || data.group!=0 || drop.model.position != new Vector3(i*3,300,0)) throw new Exception("object mismatch");
    if(mode=="create") { if(!storage.items.tryAddItem(new Item(16,EItemOrigin.ADMIN),false)) throw new Exception("add failed"); storage.rebuildState(); }
    if(storage.items.items.Count!=1 || storage.items.items[0].item.id!=16) throw new Exception("contents mismatch");
    var jar=storage.items.items[0]; if(jar.item.amount!=1 || jar.item.quality!=100 || jar.item.state==null || jar.item.state.Length!=0 || jar.x!=0 || jar.y!=0 || jar.rot!=0) throw new Exception("item fields mismatch"); Logger.Log("RECOVERY_ITEM mode="+mode+" safe="+Ids[i]+" grid="+storage.items.width+"x"+storage.items.height+" id="+jar.item.id+" amount="+jar.item.amount+" quality="+jar.item.quality+" x="+jar.x+" y="+jar.y+" state="+BitConverter.ToString(jar.item.state));
    if(storage.items.width!=(i==2?15:10) || storage.items.height!=(i==0?10:20)) throw new Exception("grid mismatch");
    seen++;
   }
   if(seen!=3 || mask!=7) throw new Exception("wrong count or ids "+seen+" mask="+mask);
   Logger.Log("RECOVERY_PASS mode="+mode+" count="+seen);
  } catch(Exception e) { Logger.LogError("RECOVERY_FAILED "+e); }
  Commander.execute(CSteamID.Nil,"shutdown");
 }
}

