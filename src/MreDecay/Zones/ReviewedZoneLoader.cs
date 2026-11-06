using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MreDecay.Zones
{
    // The trusted reviewed-document hash must come from a separate operator-controlled
    // source, not from this document. This parser cannot certify an in-game review.
    public static class ReviewedZoneLoader
    {
        private const string PeiHierarchyHash = "46bc3e3a869346e009b7f6d0fd3ca10a769ab4b2777922e405a442ed8b4054bd";
        private const string PeiChartHash = "757c5a947a3e36a0f17b10a0f1c18013d3e096a7f3dd9675e99b36a0af1623d5";
        // Extracted from visible Level.hierarchy locations for the fixed PEI hash.
        private static readonly Dictionary<long,string> PeiLocations = new Dictionary<long,string> {
            [9]="Summerside Military Base", [10]="Belfast Airport", [11]="Stratford", [12]="Alberton",
            [13]="Wiltshire Farm", [14]="Fernwood Farm", [15]="Wellington Farm", [16]="Charlottetown",
            [17]="Montague", [18]="Kensington Campground", [19]="Confederation Bridge", [20]="O'Leary Prison",
            [21]="Souris Campground", [22]="Cape Rock", [23]="Holman Isle", [24]="Oulton's Isle",
            [25]="Courtin Isle", [26]="Liberation Bridge", [27]="Taylor Beach", [28]="Pirate Cove", [29]="Tignish Farm"
        };
        public static ZoneSet LoadPei(byte[] json, string trustedDocumentHash, byte[] sourcePei, byte[] chart)
        {
            if (sourcePei == null || chart == null || !EqualHash(Hash(sourcePei), PeiHierarchyHash) || !EqualHash(Hash(chart), PeiChartHash)) return null;
            return Load(json, trustedDocumentHash, sourcePei, chart, PeiLocations);
        }
        internal static ZoneSet Load(byte[] json, string trustedDocumentHash, byte[] sourcePei, byte[] chart, IReadOnlyDictionary<long, string> expectedLocations)
        {
            if (json == null || json.Length == 0 || json.Length > 4 * 1024 * 1024 || sourcePei == null || chart == null ||
                expectedLocations == null || expectedLocations.Count == 0 || expectedLocations.Count > 256 ||
                !ValidHash(trustedDocumentHash) || !EqualHash(Hash(json), trustedDocumentHash)) return null;
            try {
                var text = new UTF8Encoding(false, true).GetString(json);
                using (var reader = new JsonTextReader(new StringReader(text)) { MaxDepth = 64, DateParseHandling = DateParseHandling.None }) {
                    var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read() || (string)root["status"] != "VERIFIED" || (string)root["map"] != "PEI" ||
                        !ValidHash((string)root["source_sha256"]) || !ValidHash((string)root["chart_sha256"]) ||
                        !EqualHash(Hash(sourcePei), (string)root["source_sha256"]) || !EqualHash(Hash(chart), (string)root["chart_sha256"])) return null;
                    var entries = root["zones"] as JArray;
                    if (entries == null || entries.Count == 0 || entries.Count != expectedLocations.Count) return null;
                    var zones = new List<NamedZone>();
                    int totalVertices = 0, totalContours = 0;
                    var ids = new HashSet<long>(); var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in entries) {
                        var entry = item as JObject; if (entry == null || entry["instance_id"] == null || entry["instance_id"].Type != JTokenType.Integer) return null;
                        long id = (long)entry["instance_id"]; string name = (string)entry["name"];
                        string expectedName;
                        if (id <= 0 || String.IsNullOrWhiteSpace(name) || !ids.Add(id) || !names.Add(name) ||
                            !expectedLocations.TryGetValue(id, out expectedName) || !String.Equals(name, expectedName, StringComparison.Ordinal)) return null;
                        var polygons = entry["polygons"] as JArray;
                        if (polygons == null || polygons.Count == 0 || polygons.Count > 256 || (totalContours += polygons.Count) > 256) return null;
                        var contours = new List<PolygonZone>();
                        for (int i = 0; i < polygons.Count; i++) {
                            var vertices = polygons[i] as JArray;
                            if (vertices == null || vertices.Count < 3 || vertices.Count > 256 || (totalVertices += vertices.Count) > 4096) return null;
                            var points = new List<ZonePoint>();
                            foreach (var value in vertices) {
                                var vertex = value as JObject;
                                if (vertex == null || !Number(vertex["x"]) || !Number(vertex["z"])) return null;
                                points.Add(new ZonePoint((double)vertex["x"], (double)vertex["z"]));
                            }
                            contours.Add(new PolygonZone(id + "/" + i, points));
                        }
                        zones.Add(new NamedZone(name, contours));
                    }
                    return new ZoneSet(zones);
                }
            } catch (Exception e) when (e is JsonException || e is ArgumentException || e is OverflowException || e is InvalidCastException) { return null; }
        }
        private static bool Number(JToken value) { return value != null && (value.Type == JTokenType.Integer || value.Type == JTokenType.Float); }
        private static bool ValidHash(string s) { if (s == null || s.Length != 64) return false; foreach (char c in s) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F')) return false; return true; }
        private static bool EqualHash(string a, string b) { return String.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
    }
}
