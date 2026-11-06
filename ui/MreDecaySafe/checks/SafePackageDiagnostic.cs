using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Rocket.Core.Plugins;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger=Rocket.Core.Logging.Logger;
public sealed class SafePackageDiagnostic : RocketPlugin {
 protected override void Load() { StartCoroutine(Run()); }
 IEnumerator Run() {
  while(!Level.isLoaded) yield return new WaitForSeconds(1); yield return new WaitForSeconds(5);
  string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  if(Provider.serverID!="MRESafePackage" || !Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName).StartsWith(temp,StringComparison.OrdinalIgnoreCase) || Environment.GetEnvironmentVariable("MRE_SAFE_PACKAGE_TEST")!="YES" || Provider.clients.Count!=0) {Logger.LogError("SAFE_PACKAGE_REFUSED"); yield break;}
  try {
   ushort[] ids={60971,60972,60973}; string[] guids={"bf233036b60d403c836deb8ac0f03292","1668b7ad4220431488a714a85719c1ee","3b716600a696477487302a6faf595464"};
   for(int i=0;i<3;i++) {
    var a=Assets.find(EAssetType.ITEM,ids[i]) as ItemStorageAsset;
    if(a==null || !ReferenceEquals(a,Assets.find(new Guid(guids[i])))) throw new Exception("asset identity "+ids[i]);
    var type=a.GetType(); var flags=BindingFlags.Instance|BindingFlags.NonPublic;
    var x=(byte)type.GetField("_storage_x",flags).GetValue(a); var y=(byte)type.GetField("_storage_y",flags).GetValue(a);
    if(x!=(i==2?15:10) || y!=(i==0?10:20)) throw new Exception("grid "+ids[i]);
    FieldInfo field=null; for(Type t=type;t!=null && field==null;t=t.BaseType) field=t.GetField("_barricade",flags|BindingFlags.DeclaredOnly);
    var model=field==null?null:field.GetValue(a) as GameObject;
    if(model==null || model.GetComponentsInChildren<Collider>(true).Length!=1) throw new Exception("barricade "+ids[i]);
    Logger.Log("SAFE_PACKAGE_ITEM id="+ids[i]+" guid="+guids[i]+" grid="+x+"x"+y+" barricadeCollider=1");
   }
   Logger.Log("SAFE_PACKAGE_PASS");
  } catch(Exception e) {Logger.LogError("SAFE_PACKAGE_FAILED "+e);}
  Commander.execute(CSteamID.Nil,"shutdown");
 }
}
