using System;
using System.Collections;
using System.Reflection;
using Rocket.Core.Plugins;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;
public sealed class SafeDiagnostic : RocketPlugin {
 protected override void Load() { StartCoroutine(Check()); }
 private IEnumerator Check() {
  yield return new WaitForSeconds(65);
  ushort[] ids={60971,60972,60973}; string[] guids={"bf233036b60d403c836deb8ac0f03292","1668b7ad4220431488a714a85719c1ee","3b716600a696477487302a6faf595464"};
  foreach(var i in new[]{0,1,2}) try {
   var asset=Assets.find(EAssetType.ITEM,ids[i]); var byGuid=Assets.find(new Guid(guids[i]));
   Logger.Log("SAFE_DIAG id="+ids[i]+" type="+(asset==null?"NULL":asset.GetType().FullName)+" guidSame="+(asset!=null && ReferenceEquals(asset,byGuid)));
   if(asset==null) continue;
   for(Type t=asset.GetType(); t!=null && t!=typeof(object); t=t.BaseType) {
    foreach(var f in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)) {
      if(f.Name.IndexOf("storage",StringComparison.OrdinalIgnoreCase)<0 && !typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType)) continue;
      object v; try {v=f.GetValue(asset);} catch {continue;} var u=v as UnityEngine.Object;
      Logger.Log("SAFE_FIELD id="+ids[i]+" name="+f.Name+" type="+f.FieldType.Name+" value="+(u!=null?u.name:(v==null?"NULL":v.ToString())));
      var go=v as GameObject; var tr=v as Transform; if(go==null && tr!=null) go=tr.gameObject;
      if(go!=null) Logger.Log("SAFE_MODEL id="+ids[i]+" name="+f.Name+" renderers="+go.GetComponentsInChildren<Renderer>(true).Length+" colliders="+go.GetComponentsInChildren<Collider>(true).Length);
    }
   }
  } catch(Exception ex) {Logger.LogError("SAFE_DIAG_EXCEPTION "+ex);}
  Logger.Log("SAFE_DIAG_DONE requesting shutdown"); Commander.execute(CSteamID.Nil,"shutdown");
 }
}
