using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MreEssentials
{
    // Только чтение разрешенных полей. Не выполняет патчи и не меняет игровой мир.
    public static class RuntimeDiagnostics
    {
        private const string MoreHomesOwner = "com.restoremonarchy.morehomes";
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        private static object ReadMember(Type type, object instance, string name)
        {
            PropertyInfo property = type.GetProperty(name, Members);
            if (property != null) return property.GetValue(instance, null);
            FieldInfo field = type.GetField(name, Members);
            if (field != null) return field.GetValue(instance);
            throw new MissingMemberException(type.FullName, name);
        }

        private static void Record(IDictionary<string, object> target, string key, Func<object> read)
        {
            try { target[key] = read(); }
            catch (Exception error) { target[key + "_error"] = error.GetType().Name + ": " + error.Message; }
        }

        private static string Signature(MethodBase method)
        {
            return (method.DeclaringType == null ? "<unknown>" : method.DeclaringType.FullName) + "." + method.Name +
                "(" + String.Join(", ", method.GetParameters().Select(p => p.ParameterType.FullName).ToArray()) + ")";
        }

        public static IDictionary<string, object> Capture(Type providerType, Type barricadeType, object configuration, IEnumerable<Assembly> assemblies)
        {
            var root = new Dictionary<string, object>();
            root["captured_at_utc"] = DateTime.UtcNow.ToString("o");
            root["purpose"] = "Проверка runtime API и установленных патчей; не функциональная проверка кроватей игроками.";
            var provider = new Dictionary<string, object>();
            root["provider"] = provider;
            Record(provider, "mode", () => Convert.ToString(ReadMember(providerType, null, "mode"), CultureInfo.InvariantCulture));
            Record(provider, "map", () => Convert.ToString(ReadMember(providerType, null, "map"), CultureInfo.InvariantCulture));
            Record(provider, "max_players", () => ReadMember(providerType, null, "maxPlayers"));
            Record(provider, "steam_admin_owner_id", () => {
                Type adminList = providerType.Assembly.GetType("SDG.Unturned.SteamAdminlist", true);
                return ReadMember(adminList, null, "ownerID").ToString();
            });
            Record(provider, "steam_admin_owner_has_admin", () => {
                Type adminList = providerType.Assembly.GetType("SDG.Unturned.SteamAdminlist", true);
                object owner = ReadMember(adminList, null, "ownerID");
                MethodInfo checkAdmin = adminList.GetMethod("checkAdmin", BindingFlags.Public | BindingFlags.Static, null, new[] { owner.GetType() }, null);
                if (checkAdmin == null) throw new MissingMethodException("SteamAdminlist.checkAdmin");
                return checkAdmin.Invoke(null, new[] { owner });
            });
            Record(provider, "group_cap", () => {
                object modeConfig = ReadMember(providerType, null, "modeConfigData");
                object gameplay = ReadMember(modeConfig.GetType(), modeConfig, "Gameplay");
                return ReadMember(gameplay.GetType(), gameplay, "Max_Group_Members");
            });
            var gameplaySettings = new Dictionary<string, object>();
            root["gameplay_settings"] = gameplaySettings;
            foreach (var sectionFields in new Dictionary<string, string[]>
            {
                { "Items", new[] { "Spawn_Chance", "Quality_Full_Chance", "Has_Durability", "Clothing_Has_Durability", "Weapons_Have_Durability", "Food_Spawns_At_Full_Quality", "Water_Spawns_At_Full_Quality", "Clothing_Spawns_At_Full_Quality", "Weapons_Spawn_At_Full_Quality", "Default_Spawns_At_Full_Quality", "Gun_Bullets_Full_Chance", "Magazine_Bullets_Full_Chance", "Crate_Bullets_Full_Chance" } },
                { "Zombies", new[] { "Loot_Chance", "Min_Drops", "Max_Drops" } },
                { "Players", new[] { "Spawn_With_Max_Skills", "Spawn_With_Stamina_Skills", "Lose_Skills_PvP", "Lose_Skills_PvE", "Lose_Skill_Levels_PvP", "Lose_Skill_Levels_PvE" } }
            })
            {
                string sectionName = sectionFields.Key;
                var section = new Dictionary<string, object>();
                gameplaySettings[sectionName] = section;
                foreach (string fieldName in sectionFields.Value)
                {
                    string key = fieldName;
                    Record(section, key, () => {
                        object modeConfig = ReadMember(providerType, null, "modeConfigData");
                        object nativeSection = ReadMember(modeConfig.GetType(), modeConfig, sectionName);
                        return ReadMember(nativeSection.GetType(), nativeSection, key);
                    });
                }
            }

            var settings = new Dictionary<string, object>();
            root["mre_configuration"] = settings;
            foreach (string field in new[] { "VoteDayEnabled", "NightOnly", "MinimumPlayers", "RequiredPercent", "VoteDurationSeconds", "VoteCooldownSeconds", "AutoSaveEnabled", "AutoSaveIntervalSeconds", "LocalControlEnabled", "MaxSkillsEnabled" })
            {
                string fieldName = field;
                Record(settings, fieldName, () => ReadMember(configuration.GetType(), configuration, fieldName));
            }

            var bed = new Dictionary<string, object>();
            root["barricade_manager"] = bed;
            Record(bed, "server_set_bed_owner_methods", () => barricadeType.GetMethods(Members)
                .Where(m => m.Name == "ServerSetBedOwnerInternal").Select(Signature).ToArray());
            Record(bed, "server_set_bed_owner_six_parameters_exists", () => barricadeType.GetMethods(Members)
                .Any(m => m.Name == "ServerSetBedOwnerInternal" && m.GetParameters().Length == 6));

            var patches = new Dictionary<string, object>();
            root["more_homes_harmony"] = patches;
            patches["owner_contains"] = MoreHomesOwner;
            try
            {
                Type harmony = assemblies.Select(a => a.GetType("HarmonyLib.Harmony", false)).FirstOrDefault(t => t != null);
                if (harmony == null) throw new TypeLoadException("HarmonyLib.Harmony не загружена.");
                patches["harmony_assembly"] = harmony.Assembly.FullName;
                MethodInfo getMethods = harmony.GetMethod("GetAllPatchedMethods", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                MethodInfo getPatchInfo = harmony.GetMethod("GetPatchInfo", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(MethodBase) }, null);
                if (getMethods == null || getPatchInfo == null) throw new MissingMethodException("Harmony introspection API недоступен.");
                var found = new List<string>();
                var ownersFound = new HashSet<string>();
                IEnumerable patchedMethods = (IEnumerable)getMethods.Invoke(null, null);
                foreach (MethodBase method in patchedMethods)
                {
                    object info = getPatchInfo.Invoke(null, new object[] { method });
                    if (info == null) continue;
                    IEnumerable owners = (IEnumerable)ReadMember(info.GetType(), info, "Owners");
                    bool matched = false;
                    foreach (object ownerValue in owners)
                    {
                        string owner = Convert.ToString(ownerValue, CultureInfo.InvariantCulture);
                        if (owner.IndexOf(MoreHomesOwner, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        ownersFound.Add(owner);
                        matched = true;
                    }
                    if (matched) found.Add(Signature(method));
                }
                patches["matched_owners"] = ownersFound.OrderBy(o => o).ToArray();
                patches["matched_method_count"] = found.Count;
                patches["matched_methods"] = found.OrderBy(m => m).ToArray();
            }
            catch (Exception error) { patches["error"] = error.GetType().Name + ": " + error.Message; }
            return root;
        }

        private static string Quote(string value)
        {
            var output = new StringBuilder("\"");
            foreach (char character in value)
            {
                if (character == '"') output.Append("\\\"");
                else if (character == '\\') output.Append("\\\\");
                else if (character < 32) output.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                else output.Append(character);
            }
            return output.Append('"').ToString();
        }

        public static string ToJson(object value)
        {
            if (value == null) return "null";
            if (value is string || value is Enum) return Quote(value.ToString());
            if (value is bool) return (bool)value ? "true" : "false";
            var dictionary = value as IDictionary;
            if (dictionary != null)
            {
                var properties = new List<string>();
                foreach (DictionaryEntry entry in dictionary) properties.Add(Quote((string)entry.Key) + ":" + ToJson(entry.Value));
                return "{" + String.Join(",", properties.ToArray()) + "}";
            }
            var list = value as IEnumerable;
            if (list != null)
            {
                var items = new List<string>();
                foreach (object item in list) items.Add(ToJson(item));
                return "[" + String.Join(",", items.ToArray()) + "]";
            }
            if (value is IFormattable) return ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
            return Quote(value.ToString());
        }
    }
}
