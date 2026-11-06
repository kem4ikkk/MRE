using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Rocket.Core.Plugins;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger=Rocket.Core.Logging.Logger;
public sealed class SafeIdDiagnostic : RocketPlugin {
 protected override void Load() { StartCoroutine(Run()); }
 IEnumerator Run() {
  while(!Level.isLoaded) yield return new WaitForSeconds(1); yield return new WaitForSeconds(5);
  string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  if(Provider.serverID!="MRESafeIds" || !Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName).StartsWith(temp,StringComparison.OrdinalIgnoreCase) || Environment.GetEnvironmentVariable("MRE_SAFE_ID_TEST")!="YES" || Provider.clients.Count!=0) {Logger.LogError("SAFE_IDS_REFUSED"); yield break;}
  ushort[] ids={60971,60972,60973}; string[] guids={"bf233036b60d403c836deb8ac0f03292","1668b7ad4220431488a714a85719c1ee","3b716600a696477487302a6faf595464"};
  for(int i=0;i<3;i++) {var a=Assets.find(EAssetType.ITEM,ids[i]); var b=Assets.find(new Guid(guids[i])); Logger.Log("SAFE_IDS id="+ids[i]+" idFree="+(a==null)+" guidFree="+(b==null));}
  Commander.execute(CSteamID.Nil,"shutdown");
 }
}
