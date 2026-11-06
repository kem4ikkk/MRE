using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Rocket.Core.Plugins;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

// Test-only: creates or reads a disposable world and requests server shutdown.
public sealed class SafeMatrixFixture : RocketPlugin {
 static readonly ushort[] Safes={60971,60972,60973};
 protected override void Load() { StartCoroutine(Run()); }
 IEnumerator Run() {
  while(!Level.isLoaded) yield return new WaitForSeconds(1);
  yield return new WaitForSeconds(5);
  string root=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  string exe=Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
  string mode=Environment.GetEnvironmentVariable("MRE_SAFE_MATRIX_MODE");
  if(Provider.serverID!="MRESafeMatrix" || !exe.StartsWith(root,StringComparison.OrdinalIgnoreCase) || (mode!="create" && mode!="verify") || Provider.clients.Count!=0) { Logger.LogError("MATRIX_REFUSED"); yield break; }
  try {
   var choices=Assets.find(EAssetType.ITEM).OfType<ItemAsset>().Where(a=>a.size_x==1 && a.size_y==1 && a.id>0 && a.id<60000).Select(a=>a.id).Distinct().OrderBy(id=>id).Take(3).ToArray();
   if(choices.Length!=3) throw new Exception("need three 1x1 item assets");
   Logger.Log("MATRIX_CHOICES "+string.Join(",",choices));
   int existing=0; foreach(var region in BarricadeManager.regions) existing+=region.drops.Count;
   if(mode=="create") {
    if(existing!=0) throw new Exception("world is not empty");
    for(int i=0;i<3;i++) {
     var asset=Assets.find(EAssetType.ITEM,Safes[i]) as ItemStorageAsset;
     if(asset==null || BarricadeManager.dropBarricade(new Barricade(asset),null,new Vector3(i*3,300,0),0,0,0,1,0)==null) throw new Exception("safe creation failed");
    }
   }
   int mask=0;
   foreach(var region in BarricadeManager.regions) foreach(var drop in region.drops) {
    int i=Array.IndexOf(Safes,drop.asset.id); if(i<0 || (mask&(1<<i))!=0) throw new Exception("unexpected or duplicate object"); mask|=1<<i;
    var storage=drop.interactable as InteractableStorage; var data=drop.GetServersideData();
    if(storage==null || data.owner!=1 || data.group!=0 || drop.model.position!=new Vector3(i*3,300,0)) throw new Exception("object fields mismatch");
    int count=i==0?100:200; byte width=(byte)(i==2?15:10), height=(byte)(i==0?10:20);
    if(storage.items.width!=width || storage.items.height!=height) throw new Exception("grid mismatch");
    if(mode=="create") {
     for(int n=0;n<count;n++) if(!storage.items.tryAddItem(Make(choices,n),false)) throw new Exception("premature add failure "+n);
     int before=storage.items.items.Count;
     bool overflow=storage.items.tryAddItem(Make(choices,count),false);
     if(overflow || storage.items.items.Count!=before) throw new Exception("overflow accepted or mutated count");
     Logger.Log("MATRIX_OVERFLOW safe="+Safes[i]+" rejected=True count="+before);
     storage.rebuildState();
    }
    if(storage.items.items.Count!=count) throw new Exception("record count mismatch");
    var indices=new HashSet<int>(); var positions=new HashSet<int>(); var rows=new List<string>();
    foreach(var jar in storage.items.items) {
     if(jar==null || jar.item==null || jar.item.state==null || jar.item.state.Length!=3) throw new Exception("state mismatch");
     int n=jar.item.state[0]+256*jar.item.state[1];
     if(n>=count || !indices.Add(n) || jar.item.state[2]!=(byte)(n^0xA5) || jar.item.id!=choices[n%3] || jar.item.amount!=1 || jar.item.quality!=(byte)(40+n%61) || jar.rot!=0 || jar.x>=width || jar.y>=height || !positions.Add(jar.x+256*jar.y)) throw new Exception("item field/position mismatch "+n);
     rows.Add(n+":"+jar.item.id+":"+jar.item.amount+":"+jar.item.quality+":"+BitConverter.ToString(jar.item.state)+":"+jar.x+":"+jar.y+":"+jar.rot);
    }
    rows.Sort(StringComparer.Ordinal);
    string hash; using(var sha=SHA256.Create()) hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n",rows)))).Replace("-","");
    Logger.Log("MATRIX_SAFE mode="+mode+" safe="+Safes[i]+" grid="+width+"x"+height+" records="+count+" uniquePositions="+positions.Count+" sha256="+hash);
   }
   if(mask!=7) throw new Exception("missing safes mask="+mask);
   Logger.Log("MATRIX_PASS mode="+mode);
  } catch(Exception e) { Logger.LogError("MATRIX_FAILED "+e); }
  Commander.execute(CSteamID.Nil,"shutdown");
 }
 static Item Make(ushort[] choices,int n) { return new Item(choices[n%3],1,(byte)(40+n%61),new byte[]{(byte)n,(byte)(n>>8),(byte)(n^0xA5)}); }
}

