using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class MreAdminBuilder
{
    private const string PrefabPath = "Assets/MreAdmin/Effect.prefab";
    private const string EffectGuid = "bc15863fb9c24a69a36bb733456cc3b2";
    private static Font font;
    private static int staticLabelSequence;
    private static readonly Color Ink = ColorOf("15201B");
    private static readonly Color TextColor = ColorOf("E9EEE8");
    private static readonly Color Muted = ColorOf("AAB7AE");
    private static readonly Color Lime = ColorOf("BED982");
    private static Color ColorOf(string value) { Color color; if (!ColorUtility.TryParseHtmlString("#" + value, out color)) throw new ArgumentException(value); return color; }

    [MenuItem("MRE/Admin/Создать панель")]
    public static void Generate() { GeneratePrefab(); }

    [MenuItem("MRE/Admin/Собрать Windows bundle")]
    public static void BuildWindows()
    {
        GeneratePrefab();
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        string output = Path.Combine(root, "runtime/ui-builds/MreAdmin/Windows"); Directory.CreateDirectory(output);
        var map = new AssetBundleBuild { assetBundleName = "MreAdmin.unity3d", assetNames = new[] { PrefabPath }, addressableNames = new[] { "Effect" } };
        var manifest = BuildPipeline.BuildAssetBundles(output, new[] { map }, AssetBundleBuildOptions(), BuildTarget.StandaloneWindows64);
        if (manifest == null) throw new InvalidOperationException("Unity не собрал bundle админ-панели.");
        string source = Path.Combine(output, "MreAdmin.unity3d");
        var bundle = AssetBundle.LoadFromFile(source); if (bundle == null) throw new InvalidDataException("Bundle не читается Unity.");
        try { var prefab = bundle.LoadAsset<GameObject>("Effect"); if (prefab == null) throw new InvalidDataException("В bundle нет prefab Effect."); Validate(prefab); }
        finally { bundle.Unload(true); }
        string mod = Path.Combine(output, "MreAdmin"); Directory.CreateDirectory(mod);
        File.Copy(source, Path.Combine(mod, "MreAdmin.unity3d"), true);
        File.WriteAllText(Path.Combine(mod, "MreAdmin.dat"), "GUID " + EffectGuid + "\nType Effect\nID 48626\nAsset_Bundle_Version 6\nExclude_From_Master_Bundle\n", new UTF8Encoding(false));
        using (var sha = SHA256.Create())
        {
            string hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(source))).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(Path.Combine(output, "bundle-build.json"), "{\n  \"effect_id\":48626,\n  \"effect_guid\":\"" + EffectGuid + "\",\n  \"effect_key\":14826,\n  \"unity_version\":\"" + Application.unityVersion + "\",\n  \"target\":\"StandaloneWindows64\",\n  \"bundle_sha256\":\"" + hash + "\",\n  \"prefab_address_verified\":true,\n  \"in_game_verified\":false\n}\n", new UTF8Encoding(false));
        }
        Debug.Log("MRE Admin: bundle собран локально; он не установлен и не опубликован: " + mod);
    }

    private static BuildAssetBundleOptions AssetBundleBuildOptions() { return BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ForceRebuildAssetBundle; }

    private static void GeneratePrefab()
    {
        if (Application.unityVersion != "2022.3.62f3") throw new InvalidOperationException("Нужен Unity 2022.3.62f3.");
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); if (font == null) throw new InvalidOperationException("Встроенный шрифт Unity недоступен.");
        staticLabelSequence = 0;
        Directory.CreateDirectory("Assets/MreAdmin");
        var root = new GameObject("Effect", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup), typeof(GraphicRaycaster));
        try
        {
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            var group = root.GetComponent<CanvasGroup>(); group.interactable = true; group.blocksRaycasts = true;
            var launcher = Panel("AdminLauncherFrame", root.transform, 330, 64, 0, 475, "202E26E8");
            Button("AdminLauncher", launcher, "ОТКРЫТЬ ПАНЕЛЬ MRE", 0, 0, 310, 48, Lime);
            var content = Panel("AdminContent", root.transform, 1540, 930, 0, 0, "111A17F4");
            Label(content, "MRE  /  АДМИНИСТРАТИВНАЯ ПАНЕЛЬ", 0, 422, 1030, 36, 26, Lime, true, true);
            Button("AdminClose", content, "ЗАКРЫТЬ", 590, 422, 112, 42, ColorOf("39483F"));
            Panel("AdminPlayers", content, 390, 730, -535, 22, "19231FEF");
            Label(content, "ЦЕЛЬ ДЕЙСТВИЯ", -535, 365, 350, 26, 16, Muted, true);
            Label(content, "AdminTarget", "Игрок не выбран", -535, 329, 350, 42, 18, TextColor, false);
            for (int i = 0; i < 6; i++)
            {
                int y = 267 - i * 70;
                var row = Panel("AdminPlayerRow" + i, content, 350, 58, -535, y, i % 2 == 0 ? "243229" : "1F2B24");
                Box("AdminPlayerSelected" + i, row, 4, 40, -164, 0, "BED982").gameObject.SetActive(false);
                Button("AdminPlayer" + i, row, "ВЫБРАТЬ", 116, 0, 94, 44, ColorOf("35473B"));
                Label(row, "AdminPlayerName" + i, String.Empty, -95, 5, 145, 24, 14, TextColor, true);
                Label(row, "AdminPlayerId" + i, String.Empty, -95, -17, 145, 18, 10, Muted, false);
            }
            Button("AdminPlayerPrev", content, "‹", -685, -195, 48, 38, ColorOf("35473B"));
            Label(content, "AdminPage", "1", -639, -195, 120, 38, 15, TextColor, true, true);
            Button("AdminPlayerNext", content, "›", -560, -195, 48, 38, ColorOf("35473B"));

            string[] tabs = { "ИГРОК", "РЕЖИМЫ", "ОБЪЕКТ", "МИР И ВЫДАЧА", "ПРОФИЛИ И МОДЕРАЦИЯ", "ИНВЕНТАРЬ И СОБЫТИЯ" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int x = -420 + i * 168;
                Button("AdminTab" + i, content, tabs[i], x, 375, 158, 42, ColorOf("2B3B30"));
                Box("AdminTabSelected" + i, content, 140, 3, x, 350, "BED982");
            }
            Section(content, 0, new[] { new[] { "AdminTp", "К игроку" }, new[] { "AdminBring", "Привести" }, new[] { "AdminHeal", "Лечение" }, new[] { "AdminHealSelf", "Лечить себя" }, new[] { "AdminKick", "Исключить" }, new[] { "AdminSpectate", "Наблюдать" }, new[] { "AdminStopSpectate", "Завершить наблюдение" }, new[] { "AdminFreeze", "Заморозить / разморозить" }, new[] { "AdminKill", "Убить · подтверждение" } });
            Section(content, 1, new[] { new[] { "AdminFly", "Переключить полёт тела" }, new[] { "AdminFreecam", "Переключить Freecam" }, new[] { "AdminGod", "Переключить God" }, new[] { "AdminVanish", "Переключить Vanish" }, new[] { "AdminWorkzone", "Переключить Workzone" }, new[] { "AdminStats", "Переключить статистику" } });
            Section(content, 2, new[] { new[] { "AdminOwner", "Владелец по прицелу" }, new[] { "AdminDoor", "Переключить дверь" }, new[] { "AdminStorage", "Открыть контейнер" }, new[] { "AdminRemove", "Удалить · подтверждение" } });
            Section(content, 3, new[] { new[] { "AdminSave", "Сохранить мир" }, new[] { "AdminDay", "День" }, new[] { "AdminNight", "Ночь" } });
            var modes = Find(content, "AdminSection1");
            Label(modes, "Скорость MreFlight (нужен включённый полёт)", -200, -88, 400, 25, 15, Lime, true);
            Label(modes, "Горизонталь 0.1–10", -260, -128, 185, 22, 12, Muted, false);
            Input(modes, "AdminHorizontalSpeed", "1", -145, -166, 175, 38, 16, 16);
            Label(modes, "Вертикаль 0.1–5", 70, -128, 185, 22, 12, Muted, false);
            Input(modes, "AdminVerticalSpeed", "1", 180, -166, 175, 38, 16, 16);
            Button("AdminFlightSpeed", modes, "ПРИМЕНИТЬ", 360, -166, 150, 40, ColorOf("647B4A"));
            Label(modes, "Текущая горизонтальная / вертикальная скорость", -250, -210, 380, 22, 12, Muted, false);
            Label(modes, "AdminFlightSpeedCurrent", "1 / 1", 160, -210, 180, 22, 13, TextColor, true);
            var section = Find(content, "AdminSection3");
            Label(section, "Объявление события (до 180 символов)", 160, 270, 430, 25, 15, Lime, true);
            Input(section, "AdminAnnouncementText", "Текст объявления", 190, 230, 195, 42, 15, 180);
            Button("AdminAnnounceEvent", section, "ОБЪЯВИТЬ", 370, 230, 150, 42, ColorOf("647B4A"));
            Label(section, "Перезапуск: 1–1440 минут, без запуска сервера", 160, 180, 430, 24, 13, Muted, true);
            Input(section, "AdminRestartMinutes", "10", 195, 140, 130, 42, 15, 4);
            Button("AdminAnnounceRestart", section, "ОБЪЯВИТЬ", 370, 140, 150, 42, ColorOf("647B4A"));
            Label(section, "Выдача предмета выбранному игроку", -200, 100, 400, 30, 17, Lime, true);
            Numeric(section, "AdminItem", "ID предмета", -200, 42);
            Numeric(section, "AdminAmount", "Количество", 130, 42);
            Button("AdminGive", section, "ВЫДАТЬ ВЫБРАННОМУ", -112, -25, 225, 44, Lime);
            Button("AdminGiveSelf", section, "ВЫДАТЬ СЕБЕ", 145, -25, 205, 44, ColorOf("647B4A"));
            Label(section, "Вызов транспорта рядом с собой", -200, -105, 400, 30, 17, Lime, true);
            Numeric(section, "AdminVehicle", "ID транспорта", -200, -165);
            Button("AdminVehicle", section, "ВЫЗВАТЬ ТРАНСПОРТ", 320, -165, 230, 44, ColorOf("647B4A"));
            var staff = Section(content, 4, new string[0]);
            Label(staff, "Действия /staff требуют mre.staff и отдельный mre.staff.<действие>", 0, 285, 760, 28, 13, Muted, true, true);
            string[][] staffButtons = {
                new[] {"AdminStaffList","Поиск профилей"}, new[] {"AdminStaffProfile","Профиль"}, new[] {"AdminStaffHistory","История"},
                new[] {"AdminStaffLogs","Журнал"}, new[] {"AdminStaffNote","Заметка"}, new[] {"AdminStaffWarn","Предупреждение"},
                new[] {"AdminStaffCommend","Благодарность"}, new[] {"AdminStaffKick","Исключить"}, new[] {"AdminStaffBan","Бан"},
                new[] {"AdminStaffMute","Мут"}, new[] {"AdminStaffUnban","Снять бан"}, new[] {"AdminStaffUnmute","Снять мут"}, new[] {"AdminStaffRevoke","Отозвать запись"}
            };
            for (int i = 0; i < staffButtons.Length; i++) Button(staffButtons[i][0], staff, staffButtons[i][1], -270 + (i % 3) * 270, 220 - (i / 3) * 60, 248, 46, ColorOf("304137"));
            Label(staff, "SteamID64 / поиск профиля", -260, -80, 260, 22, 12, Muted, true);
            Input(staff, "AdminStaffQuery", "SteamID64 или имя", -135, -112, 300, 38, 14, 64);
            Label(staff, "Причина", 175, -80, 180, 22, 12, Muted, true);
            Input(staff, "AdminStaffReason", "Причина действия", 330, -112, 300, 38, 14, 200);
            Label(staff, "Длительность ban/mute: 30m, 2h, 7d или perm", -240, -165, 380, 22, 12, Muted, true);
            Input(staff, "AdminStaffDuration", "perm", -75, -198, 220, 38, 14, 16);
            Label(staff, "ID записи для отзыва", 190, -165, 240, 22, 12, Muted, true);
            Input(staff, "AdminStaffRecordId", "ID из истории", 330, -198, 300, 38, 14, 64);
            Button("AdminStaffPrev", staff, "‹ ПРЕДЫДУЩАЯ СТРАНИЦА", -150, -255, 250, 42, ColorOf("35473B"));
            Button("AdminStaffNext", staff, "СЛЕДУЮЩАЯ СТРАНИЦА ›", 150, -255, 250, 42, ColorOf("35473B"));
            var inventory = Section(content, 5, new string[0]);
            Label(inventory, "Инвентарь подключённой цели; восстановление требует живого игрока и одноразового подтверждения", 0, 255, 770, 35, 13, Muted, true, true);
            Button("AdminInventoryInspect", inventory, "ОСМОТР ИНВЕНТАРЯ", -170, 185, 290, 48, ColorOf("304137"));
            Button("AdminInventoryDeaths", inventory, "СПИСОК СНИМКОВ СМЕРТИ", 170, 185, 310, 48, ColorOf("304137"));
            Label(inventory, "Стабильный SnapshotId (скопируйте из списка снимков)", -180, 105, 520, 24, 14, Lime, true);
            Input(inventory, "AdminSnapshotId", "SnapshotId", -90, 65, 440, 42, 15, 64);
            Button("AdminInventoryDetails", inventory, "ПОКАЗАТЬ ДЕТАЛИ СНИМКА", 0, 15, 360, 42, ColorOf("304137"));
            Button("AdminRestorePrepare", inventory, "ПОДГОТОВИТЬ ВОССТАНОВЛЕНИЕ", 0, -35, 430, 48, ColorOf("A24D43"));
            Button("AdminInventoryPrev", inventory, "‹ ПРЕДЫДУЩИЕ 4", -150, -235, 250, 42, ColorOf("35473B"));
            Button("AdminInventoryNext", inventory, "СЛЕДУЮЩИЕ 4 ›", 150, -235, 250, 42, ColorOf("35473B"));
            Label(inventory, "MreAirdrop", 0, -110, 300, 28, 18, Lime, true, true);
            Button("AdminAirdropStatus", inventory, "СТАТУС", -170, -160, 220, 48, ColorOf("304137"));
            Button("AdminAirdropLaunch", inventory, "ЗАПУСТИТЬ ВРУЧНУЮ", 170, -160, 280, 48, ColorOf("647B4A"));
            Label(content, "AdminStatus", "Выберите игрока, затем действие. Результат отобразится здесь.", -10, -400, 1400, 96, 14, TextColor, false);
            var confirm = Panel("AdminConfirmPanel", content, 1180, 190, 0, -205, "38432EF5");
            Label(confirm, "AdminConfirmPrompt", "Подтвердите действие.", 0, 42, 1080, 104, 15, TextColor, true, true);
            Button("AdminConfirm", confirm, "ПОДТВЕРДИТЬ", -150, -68, 250, 42, ColorOf("A24D43"));
            Button("AdminCancelConfirm", confirm, "ОТМЕНА", 150, -68, 190, 42, ColorOf("35473B"));
            Validate(root); PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static RectTransform Section(Transform parent, int index, string[][] actions)
    {
        var section = Panel("AdminSection" + index, parent, 900, 650, 150, -15, "19231FEF");
        for (int i = 0; i < actions.Length; i++)
        {
            float x = index == 3 ? -200 : -200 + (i % 2) * 320;
            float y = index == 3 ? 270 - i * 55 : 220 - (i / 2) * 82;
            Button(actions[i][0], section, actions[i][1], x, y, 290, 56, i == 6 ? ColorOf("6A3933") : ColorOf("304137"));
        }
        return section;
    }

    private static void Input(Transform parent, string name, string initial, float x, float y, float w, float h, int fontSize, int limit)
    {
        var r = Box(name, parent, w, h, x, y, "243229");
        r.GetComponent<Image>().raycastTarget = true;
        var field = r.gameObject.AddComponent<InputField>();
        field.targetGraphic = r.GetComponent<Image>();
        field.characterLimit = limit;
        field.lineType = InputField.LineType.SingleLine;
        field.contentType = InputField.ContentType.Standard;
        field.navigation = new Navigation { mode = Navigation.Mode.None };
        var text = Label(r, name + "Value", initial, 9, 0, w - 22, h - 8, fontSize, TextColor, false);
        text.alignment = TextAnchor.MiddleLeft;
        field.textComponent = text;
        var placeholder = Label(r, name + "Placeholder", "", 9, 0, w - 22, h - 8, fontSize, Muted, false);
        field.placeholder = placeholder;
        field.text = initial;
    }

    private static void Numeric(Transform parent, string name, string label, float x, float y)
    {
        Label(parent, label, x, y + 27, 250, 24, 15, Muted, true);
        Button(name + "Down10", parent, "−10", x, y - 10, 58, 38, ColorOf("35473B"));
        Button(name + "Down", parent, "−", x + 55, y - 10, 42, 38, ColorOf("35473B"));
        Label(parent, name + "Value", "1", x + 117, y - 10, 70, 38, 18, TextColor, true);
        Button(name + "Up", parent, "+", x + 177, y - 10, 42, 38, ColorOf("35473B"));
        Button(name + "Up10", parent, "+10", x + 233, y - 10, 58, 38, ColorOf("35473B"));
    }

    private static RectTransform Panel(string name, Transform parent, float w, float h, float x, float y, string color)
    { var r = Box(name, parent, w, h, x, y, color); return r; }
    private static RectTransform Rect(string name, Transform parent, float w, float h, float x, float y)
    {
        var o = new GameObject(name, typeof(RectTransform)); o.transform.SetParent(parent, false); var r = o.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(.5f, .5f); r.pivot = new Vector2(.5f, .5f); r.sizeDelta = new Vector2(w, h); r.anchoredPosition = new Vector2(x, y); return r;
    }
    private static RectTransform Box(string name, Transform parent, float w, float h, float x, float y, string color)
    { var r = Rect(name, parent, w, h, x, y); var image = r.gameObject.AddComponent<Image>(); image.color = ColorOf(color); image.raycastTarget = false; return r; }
    private static void Button(string name, Transform parent, string label, float x, float y, float w, float h, Color color)
    {
        var r = Box(name, parent, w, h, x, y, ColorUtility.ToHtmlStringRGB(color)); var image = r.GetComponent<Image>(); image.raycastTarget = true;
        var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = image; b.navigation = new Navigation { mode = Navigation.Mode.None };
        Label(r, name + "Text", label, 0, 0, w - 12, h - 8, 15, name == "AdminGive" || name == "AdminConfirm" ? Ink : TextColor, true, true);
    }
    private static Text Label(Transform parent, string name, string value, float x, float y, float w, float h, int size, Color color, bool bold, bool center = false)
    {
        var r = Rect(name, parent, w, h, x, y); var t = r.gameObject.AddComponent<Text>(); t.font = font; t.text = value; t.fontSize = size; t.color = color;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal; t.alignment = center ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft; t.supportRichText = false; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = name == "AdminStatus" || name == "AdminConfirmPrompt" ? VerticalWrapMode.Overflow : VerticalWrapMode.Truncate; return t;
    }
    private static Text Label(Transform parent, string value, float x, float y, float w, float h, int size, Color color, bool bold, bool center = false)
    {
        return Label(parent, "AdminStaticLabel" + staticLabelSequence++, value, x, y, w, h, size, color, bold, center);
    }
    private static RectTransform Find(Transform root, string name) { return root.GetComponentsInChildren<RectTransform>(true).Single(x => x.name == name); }
    private static void Validate(GameObject root)
    {
        var transforms = root.GetComponentsInChildren<RectTransform>(true); var names = transforms.Select(x => x.name).ToArray();
        if (names.Distinct().Count() != names.Length) throw new InvalidDataException("Повторные имена элементов админ UI.");
        string[] required = { "AdminLauncher", "AdminContent", "AdminClose", "AdminTarget", "AdminItemValue", "AdminAmountValue", "AdminVehicleValue", "AdminStatus", "AdminConfirm", "AdminCancelConfirm", "AdminGive", "AdminVehicle", "AdminInventoryPrev", "AdminInventoryNext", "AdminStaffPrev", "AdminStaffNext", "AdminConfirmPrompt" };
        foreach (string n in required) if (transforms.All(x => x.name != n)) throw new InvalidDataException("Нет элемента " + n);
        if (root.GetComponentsInChildren<Button>(true).Length < 60 || root.GetComponentsInChildren<InputField>(true).Length < 9 || root.GetComponent<GraphicRaycaster>() == null) throw new InvalidDataException("Неполный контракт управления.");
        if (root.GetComponentsInChildren<Text>(true).Any(t => t.supportRichText || t.raycastTarget)) throw new InvalidDataException("Rich text и перехват кликов текстом запрещены.");
    }
}
