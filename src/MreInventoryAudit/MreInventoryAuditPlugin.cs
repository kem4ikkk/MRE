using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Rocket.API;
using Rocket.Core.Plugins;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using Logger=Rocket.Core.Logging.Logger;

namespace MreInventoryAudit
{
    public sealed class MreInventoryAuditPlugin : RocketPlugin
    {
        private sealed class Intent { public string Token,Snapshot; public ulong Target; public Player ActorPlayer,TargetPlayer; public DateTime Expires; }
        public static MreInventoryAuditPlugin Instance {get;private set;}
        public event Action<ulong,string> DeathCaptured;
        public event Action<ulong,ulong,string> InventoryRestored;
        private readonly Dictionary<ulong,Intent> intents=new Dictionary<ulong,Intent>();
        private DeathLedger ledger;
        private int gameThread;
        protected override void Load()
        {
            gameThread=Thread.CurrentThread.ManagedThreadId;
            ledger=new DeathLedger(System.IO.Path.Combine(Directory,"deaths"));
            PlayerLife.OnPreDeath+=OnPreDeath;
            Provider.onServerDisconnected+=Disconnected;
            Instance=this;
            Logger.Log("[MRE] MreInventoryAudit: inspectinventory, previewdeath, restoredeath; снимок до штатного выпадения вещей.");
        }
        protected override void Unload() {PlayerLife.OnPreDeath-=OnPreDeath;Provider.onServerDisconnected-=Disconnected;intents.Clear();Instance=null;}
        private void Disconnected(CSteamID id) {intents.Remove(id.m_SteamID);foreach(var key in intents.Where(p=>p.Value.Target==id.m_SteamID).Select(p=>p.Key).ToArray())intents.Remove(key);}
        private void RequireThread() {if(Thread.CurrentThread.ManagedThreadId!=gameThread)throw new InvalidOperationException("Требуется главный игровой поток.");}
        private static bool Online(UnturnedPlayer player) {return player!=null&&player.Player!=null&&Provider.clients.Any(p=>p!=null&&p.player==player.Player&&p.playerID.steamID==player.CSteamID);}
        private void Authorize(UnturnedPlayer actor,string permission)
        {
            RequireThread();if(!Online(actor)||!(actor.IsAdmin||(actor.HasPermission("mre.admin")&&actor.HasPermission(permission))))throw new UnauthorizedAccessException("Нет доступа к действию с инвентарем.");
        }
        private static SteamPlayer Target(ulong id) {return Provider.clients.FirstOrDefault(p=>p!=null&&p.player!=null&&p.playerID.steamID.m_SteamID==id);}
        private static List<StoredItem> CaptureItems(Player player)
        {
            var items=new List<StoredItem>();
            for(byte page=0;page<PlayerInventory.STORAGE;page++) {
                Items inventory=player.inventory.items[page];if(inventory==null)continue;
                foreach(var jar in inventory.items)items.Add(new StoredItem {Id=jar.item.id,Amount=jar.item.amount,Quality=jar.item.quality,State=(byte[])jar.item.state.Clone(),Page=page,X=jar.x,Y=jar.y,Rotation=jar.rot});
            }
            var c=player.clothing;
            AddClothing(items,"shirt",c.shirt,c.shirtQuality,c.shirtState);
            AddClothing(items,"pants",c.pants,c.pantsQuality,c.pantsState);
            AddClothing(items,"hat",c.hat,c.hatQuality,c.hatState);
            AddClothing(items,"backpack",c.backpack,c.backpackQuality,c.backpackState);
            AddClothing(items,"vest",c.vest,c.vestQuality,c.vestState);
            AddClothing(items,"mask",c.mask,c.maskQuality,c.maskState);
            AddClothing(items,"glasses",c.glasses,c.glassesQuality,c.glassesState);
            return items;
        }
        private static void AddClothing(List<StoredItem> items,string slot,ushort id,byte quality,byte[] state)
        {
            if(id!=0)items.Add(new StoredItem {Id=id,Amount=1,Quality=quality,State=state==null?new byte[0]:(byte[])state.Clone(),Page=255,WornSlot=slot});
        }
        private void OnPreDeath(PlayerLife life)
        {
            try {
                var snapshot=ledger.Capture(life.channel.owner.playerID.steamID.m_SteamID,DateTime.UtcNow,CaptureItems(life.player));
                Publish(DeathCaptured,snapshot.PlayerId,snapshot.Id);
            }catch(Exception error){Logger.LogException(error,"[MRE] Снимок смерти не сохранен; автоматическая компенсация этой смерти недоступна.");}
        }
        private static InventoryView View(ulong player,DateTime at,string id,string status,IEnumerable<StoredItem> items)
        {
            return new InventoryView {PlayerId=player,AtUtc=at,SnapshotId=id,RestoreStatus=status,Items=items.Select(item=>{
                ItemAsset asset=Assets.find(EAssetType.ITEM,item.Id) as ItemAsset;
                return new InventoryItemView {Id=item.Id,Name=asset!=null?asset.itemName:((string)"ID "+item.Id),Amount=item.Amount,Quality=item.Quality,Page=item.Page,X=item.X,Y=item.Y,Rotation=item.Rotation,WornSlot=item.WornSlot};
            }).ToArray()};
        }
        public InventoryView Inspect(UnturnedPlayer actor,ulong target)
        {
            Authorize(actor,"mre.inventory.inspect");SteamPlayer player=Target(target);if(player==null)throw new InvalidOperationException("Игрок не подключен.");
            Logger.Log("[MRE INVENTORY] inspect actor="+actor.CSteamID.m_SteamID+" target="+target);
            return View(target,DateTime.UtcNow,null,null,CaptureItems(player.player));
        }
        public InventoryView[] GetDeaths(UnturnedPlayer actor,ulong target)
        {
            Authorize(actor,"mre.inventory.deaths");return ledger.List(target).Select(s=>View(target,s.AtUtc,s.Id,s.Status,s.Items)).ToArray();
        }
        public RestorePreview PrepareRestore(UnturnedPlayer actor,ulong target,int deathNumber)
        {
            Authorize(actor,"mre.inventory.restore");SteamPlayer player=Target(target);
            if(player==null||player.player.life.isDead)throw new InvalidOperationException("Для компенсации игрок должен быть подключен и жив.");
            DeathSnapshot[] deaths=ledger.List(target);
            if(deathNumber<1||deathNumber>deaths.Length)throw new InvalidOperationException("Нет смерти с таким номером.");
            DeathSnapshot snapshot=deaths[deathNumber-1];if(snapshot.Status!="available")throw new InvalidOperationException("Снимок уже выдан либо требует сверки после сбоя.");
            var intent=new Intent {Token=Guid.NewGuid().ToString("N"),Target=target,Snapshot=snapshot.Id,ActorPlayer=actor.Player,TargetPlayer=player.player,Expires=DateTime.UtcNow.AddSeconds(30)};
            intents[actor.CSteamID.m_SteamID]=intent;
            return new RestorePreview {Token=intent.Token,ExpiresUtc=intent.Expires,Inventory=View(target,snapshot.AtUtc,snapshot.Id,snapshot.Status,snapshot.Items)};
        }
        public string ConfirmRestore(UnturnedPlayer actor,string token)
        {
            Authorize(actor,"mre.inventory.restore");Intent intent;
            if(!intents.TryGetValue(actor.CSteamID.m_SteamID,out intent))return "Нет ожидающей компенсации.";
            intents.Remove(actor.CSteamID.m_SteamID);
            if(intent.Token!=token||intent.Expires<=DateTime.UtcNow||intent.ActorPlayer!=actor.Player)return "Подтверждение неверно или истекло.";
            SteamPlayer target=Target(intent.Target);if(target==null||target.player!=intent.TargetPlayer||target.player.life.isDead)return "Игрок вышел, сменил сессию или умер.";
            DeathSnapshot snapshot=ledger.Get(intent.Target,intent.Snapshot);if(snapshot==null)return "Снимок больше недоступен.";
            string outcome=RestoreTransaction.Execute(ledger,intent.Target,intent.Snapshot,actor.CSteamID.m_SteamID,new SnapshotDelivery(target.player.inventory,snapshot));
            if(outcome=="inventory_full")return "В инвентаре игрока недостаточно места для всей компенсации. Ничего не выдано.";
            if(outcome!="completed")return "Снимок уже выдан либо требует сверки.";
            Logger.Log("[MRE INVENTORY] restored actor="+actor.CSteamID.m_SteamID+" target="+intent.Target+" snapshot="+intent.Snapshot);
            var listeners=InventoryRestored;
            if(listeners!=null)foreach(Action<ulong,ulong,string> callback in listeners.GetInvocationList())try{callback(actor.CSteamID.m_SteamID,intent.Target,intent.Snapshot);}catch(Exception error){Logger.LogException(error,"[MRE] Ошибка подписчика InventoryRestored.");}
            return "Все вещи снимка выданы в свободные места инвентаря. Экипируйте одежду вручную.";
        }
        private static void Publish(Action<ulong,string> listeners,ulong player,string snapshot) {if(listeners!=null)foreach(Action<ulong,string> callback in listeners.GetInvocationList())try{callback(player,snapshot);}catch(Exception error){Logger.LogException(error,"[MRE] Ошибка подписчика DeathCaptured.");}}
        public static ulong ResolveTarget(string query)
        {
            ulong id;if(UInt64.TryParse(query,out id)&&id!=0)return id;
            var matches=Provider.clients.Where(p=>p!=null&&(p.playerID.characterName.Equals(query,StringComparison.OrdinalIgnoreCase)||p.playerID.playerName.Equals(query,StringComparison.OrdinalIgnoreCase))).ToArray();
            if(matches.Length!=1)throw new InvalidOperationException("Укажите SteamID64 либо уникальное полное имя.");return matches[0].playerID.steamID.m_SteamID;
        }
    }
}
