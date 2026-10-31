using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MreInventoryAudit
{
    // Снимки и состояния выдачи сохраняются до изменения игрового инвентаря.
    public sealed class DeathLedger
    {
        private readonly string directory;
        public DeathLedger(string directory) { this.directory=directory; System.IO.Directory.CreateDirectory(directory); }
        private string PathFor(ulong player) { return Path.Combine(directory,player.ToString(CultureInfo.InvariantCulture)+".json"); }
        private List<DeathSnapshot> Load(ulong player)
        {
            string path=PathFor(player); if(!File.Exists(path))return new List<DeathSnapshot>();
            JArray array;
            using(var reader=new JsonTextReader(new StringReader(File.ReadAllText(path)))) { reader.DateParseHandling=DateParseHandling.None; array=JArray.Load(reader); }
            if(array.Count>200)throw new InvalidDataException("Слишком большой журнал смертей.");
            var result=new List<DeathSnapshot>(); var ids=new HashSet<string>();
            foreach(JObject row in array) {
                var snapshot=new DeathSnapshot {Id=(string)row["id"],PlayerId=player,Status=(string)row["status"],RestoredBy=UInt64.Parse((string)row["restored_by"],CultureInfo.InvariantCulture),AtUtc=DateTime.ParseExact((string)row["at_utc"],"o",CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind)};
                Guid parsed;
                if(!Guid.TryParseExact(snapshot.Id,"N",out parsed)||!ids.Add(snapshot.Id)||snapshot.AtUtc.Kind!=DateTimeKind.Utc||!new[]{"available","pending","completed"}.Contains(snapshot.Status))throw new InvalidDataException("Поврежденный снимок смерти.");
                foreach(JObject item in (JArray)row["items"]) {
                    var saved=new StoredItem {Id=(ushort)item["id"],Amount=(byte)item["amount"],Quality=(byte)item["quality"],Page=(byte)item["page"],X=(byte)item["x"],Y=(byte)item["y"],Rotation=(byte)item["rotation"],WornSlot=(string)item["worn_slot"],State=Convert.FromBase64String((string)item["state"])};
                    if(saved.Id==0||saved.Quality>100||saved.Rotation>3||saved.State.Length>65535)throw new InvalidDataException("Поврежденные данные предмета.");
                    snapshot.Items.Add(saved);
                }
                if(snapshot.Items.Count>512)throw new InvalidDataException("Слишком большой снимок инвентаря.");
                result.Add(snapshot);
            }
            return result;
        }
        private void Save(ulong player,List<DeathSnapshot> snapshots)
        {
            var array=new JArray();
            foreach(var snapshot in snapshots) {
                var items=new JArray();foreach(var item in snapshot.Items)items.Add(new JObject { ["id"]=item.Id,["amount"]=item.Amount,["quality"]=item.Quality,["page"]=item.Page,["x"]=item.X,["y"]=item.Y,["rotation"]=item.Rotation,["worn_slot"]=item.WornSlot,["state"]=Convert.ToBase64String(item.State) });
                array.Add(new JObject {["id"]=snapshot.Id,["status"]=snapshot.Status,["restored_by"]=snapshot.RestoredBy.ToString(CultureInfo.InvariantCulture),["at_utc"]=snapshot.AtUtc.ToString("o",CultureInfo.InvariantCulture),["items"]=items});
            }
            string path=PathFor(player),temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                byte[] bytes=new UTF8Encoding(false).GetBytes(array.ToString(Formatting.None));
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                if(File.Exists(path))File.Replace(temporary,path,path+".previous");else File.Move(temporary,path);
            }finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        public DeathSnapshot Capture(ulong player,DateTime now,IEnumerable<StoredItem> inventory)
        {
            if(now.Kind!=DateTimeKind.Utc)throw new ArgumentException("Требуется UTC.");
            var snapshot=new DeathSnapshot {Id=Guid.NewGuid().ToString("N"),PlayerId=player,AtUtc=now,Status="available"};
            foreach(var item in inventory) {
                if(item==null||item.Id==0||item.Quality>100||item.Rotation>3||item.State==null||item.State.Length>65535)throw new InvalidDataException("Некорректный предмет снимка.");
                snapshot.Items.Add(item.Copy());
            }
            if(snapshot.Items.Count>512)throw new InvalidDataException("Слишком большой снимок.");
            var list=Load(player);list.Insert(0,snapshot);
            // Pending никогда не удаляется автоматически: результат выдачи мог быть неопределенным.
            while(list.Count>20) {int index=list.FindLastIndex(s=>s.Status!="pending");if(index<=0)throw new InvalidDataException("Слишком много несверенных выдач.");list.RemoveAt(index);}
            Save(player,list);return snapshot.Copy();
        }
        public DeathSnapshot[] List(ulong player) {return Load(player).Select(s=>s.Copy()).ToArray();}
        public DeathSnapshot Get(ulong player,string id) {return Load(player).FirstOrDefault(s=>s.Id==id);}
        public bool Reserve(ulong player,string id,ulong actor)
        {
            var list=Load(player);var snapshot=list.FirstOrDefault(s=>s.Id==id);if(snapshot==null||snapshot.Status!="available")return false;
            snapshot.Status="pending";snapshot.RestoredBy=actor;Save(player,list);return true;
        }
        public void Complete(ulong player,string id,ulong actor) {ChangePending(player,id,actor,"completed");}
        public void Cancel(ulong player,string id,ulong actor) {ChangePending(player,id,actor,"available");}
        private void ChangePending(ulong player,string id,ulong actor,string state)
        {
            var list=Load(player);var snapshot=list.FirstOrDefault(s=>s.Id==id);
            if(snapshot==null||snapshot.Status!="pending"||snapshot.RestoredBy!=actor)throw new InvalidOperationException("Нет соответствующего резервирования.");
            snapshot.Status=state;if(state=="available")snapshot.RestoredBy=0;Save(player,list);
        }
    }
    public interface IInventoryDelivery {bool Preflight();bool Deliver();bool Rollback();}
    public static class RestoreTransaction
    {
        public static string Execute(DeathLedger ledger,ulong player,string snapshot,ulong actor,IInventoryDelivery delivery)
        {
            DeathSnapshot entry=ledger.Get(player,snapshot);if(entry==null||entry.Status!="available")return "unavailable";
            if(!delivery.Preflight())return "inventory_full";
            if(!ledger.Reserve(player,snapshot,actor))return "unavailable";
            try {if(!delivery.Deliver())throw new InvalidOperationException("Неполная выдача компенсации.");ledger.Complete(player,snapshot,actor);return "completed";}
            catch {if(delivery.Rollback())ledger.Cancel(player,snapshot,actor);throw;}
        }
    }
}
