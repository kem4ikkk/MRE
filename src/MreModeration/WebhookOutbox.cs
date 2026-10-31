using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MreModeration
{
    public sealed class WebhookOutbox
    {
        private sealed class Entry {public string Id,Content;public int Attempts;public DateTime NextUtc;}
        private readonly string path;
        private readonly object gate=new object();
        private List<Entry> entries;
        public WebhookOutbox(string path)
        {
            this.path=path;entries=new List<Entry>();if(!File.Exists(path))return;
            JArray array;using(var reader=new JsonTextReader(new StringReader(File.ReadAllText(path)))){reader.DateParseHandling=DateParseHandling.None;array=JArray.Load(reader);}
            foreach(JObject row in array)entries.Add(new Entry {Id=(string)row["id"],Content=(string)row["content"],Attempts=(int)row["attempts"],NextUtc=DateTime.ParseExact((string)row["next_utc"],"o",CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind)});
            if(entries.Count>1000||entries.Any(e=>e.Id==null||e.Content==null||e.Content.Length>1800||e.Attempts<0||e.NextUtc.Kind!=DateTimeKind.Utc))throw new InvalidDataException("Поврежден webhook outbox.");
        }
        public static bool SafeEndpoint(string value)
        {
            Uri uri;return Uri.TryCreate(value,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&(uri.Host=="discord.com"||uri.Host=="discordapp.com")&&uri.Port==443&&uri.AbsolutePath.StartsWith("/api/webhooks/",StringComparison.Ordinal)&&uri.AbsolutePath.Split('/').Length>=5&&String.IsNullOrEmpty(uri.UserInfo)&&String.IsNullOrEmpty(uri.Query)&&String.IsNullOrEmpty(uri.Fragment);
        }
        private void Save(List<Entry> next)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));var array=new JArray();foreach(var e in next)array.Add(new JObject {{"id",e.Id},{"content",e.Content},{"attempts",e.Attempts},{"next_utc",e.NextUtc.ToString("o",CultureInfo.InvariantCulture)}});
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{byte[] bytes=new UTF8Encoding(false).GetBytes(array.ToString(Formatting.None));using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}if(File.Exists(path))File.Replace(temporary,path,path+".previous");else File.Move(temporary,path);entries=next;}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        public void Enqueue(string id,string content,DateTime now)
        {
            lock(gate){if(entries.Any(e=>e.Id==id))return;if(entries.Count>=1000)throw new InvalidOperationException("Очередь webhook заполнена.");var next=new List<Entry>(entries);next.Add(new Entry {Id=id,Content=StaffPolicy.Clean(content,1800),NextUtc=now});Save(next);}
        }
        public bool Pump(DateTime now,Func<string,bool> send)
        {
            Entry selected;lock(gate){selected=entries.FirstOrDefault(e=>e.NextUtc<=now);if(selected==null)return false;}
            bool success;try{success=send(selected.Content);}catch{success=false;}
            lock(gate){var next=new List<Entry>(entries);next.Remove(selected);if(!success){next.Add(new Entry {Id=selected.Id,Content=selected.Content,Attempts=selected.Attempts+1,NextUtc=now.AddSeconds(Math.Min(3600,Math.Pow(2,Math.Min(12,selected.Attempts+1))))});}Save(next);}return true;
        }
        public static bool SendDiscord(string endpoint,string content)
        {
            if(!SafeEndpoint(endpoint))throw new InvalidOperationException("Недопустимый webhook endpoint.");
            var request=(HttpWebRequest)WebRequest.Create(endpoint+"?wait=true");request.Method="POST";request.ContentType="application/json";request.Timeout=5000;request.ReadWriteTimeout=5000;request.AllowAutoRedirect=false;
            // allowed_mentions пуст: текст наказания не пингует пользователей/роли.
            byte[] body=new UTF8Encoding(false).GetBytes(new JObject {{"content",content},{"allowed_mentions",new JObject {{"parse",new JArray()}}}}.ToString(Formatting.None));request.ContentLength=body.Length;
            using(var stream=request.GetRequestStream())stream.Write(body,0,body.Length);
            using(var response=(HttpWebResponse)request.GetResponse())return (int)response.StatusCode>=200&&(int)response.StatusCode<300;
        }
        public int Count {get{lock(gate)return entries.Count;}}
    }
}
