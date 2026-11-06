using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Rocket.Core.Plugins;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger=Rocket.Core.Logging.Logger;
// Only use in a disposable test server. Automatically shuts it down.
public sealed class SafeClientPackageDiagnostic : RocketPlugin {
 protected override void Load() { StartCoroutine(Run()); }
 IEnumerator Run() {
  while(!Level.isLoaded) yield return new WaitForSeconds(1); yield return new WaitForSeconds(5);
  string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  if(Provider.serverID!="MREClientPackage" || !Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName).StartsWith(temp,StringComparison.OrdinalIgnoreCase) || Environment.GetEnvironmentVariable("MRE_CLIENT_PACKAGE_TEST")!="YES" || Provider.clients.Count!=0) {Logger.LogError("CLIENT_PACKAGE_REFUSED"); yield break;}
  try {
   ushort[] ids={60971,60972,60973}; string[] guids={"bf233036b60d403c836deb8ac0f03292","1668b7ad4220431488a714a85719c1ee","3b716600a696477487302a6faf595464"};
   for(int i=0;i<3;i++) {var asset=Assets.find(EAssetType.ITEM,ids[i]); if(!(asset is ItemStorageAsset) || !ReferenceEquals(asset,Assets.find(new Guid(guids[i])))) throw new Exception("safe identity "+ids[i]); Logger.Log("CLIENT_PACKAGE_SAFE id="+ids[i]+" guidSame=True");}
   var effect=Assets.find(EAssetType.EFFECT,48623); if(!(effect is EffectAsset) || !ReferenceEquals(effect,Assets.find(new Guid("5d4628c25c73441a89ef5aed4db47713")))) throw new Exception("effect identity");
   Logger.Log("CLIENT_PACKAGE_EFFECT id=48623 guidSame=True"); Logger.Log("CLIENT_PACKAGE_PASS");
  } catch(Exception e){Logger.LogError("CLIENT_PACKAGE_FAILED "+e);}
  Commander.execute(CSteamID.Nil,"shutdown");
 }
}
