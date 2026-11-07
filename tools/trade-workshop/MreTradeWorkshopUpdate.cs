using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Steamworks;

internal static class MreTradeWorkshopUpdate
{
    private const ulong WorkshopFileId = 3834982706;
    private const uint UnturnedAppId = 304930;
    private const string ConfirmationProtocol = "nonce-input-v1";
    private static CallResult<SubmitItemUpdateResult_t>? callback;
    private static bool completed;
    private static bool failed;

    private static int Main(string[] args)
    {
        if (args.Length != 2 || (args[0] != "validate-only" && args[0] != "publish"))
        {
            Console.Error.WriteLine("Использование: MreTradeWorkshopUpdate validate-only|publish <папка-пакета>");
            return 2;
        }

        string package = Path.GetFullPath(args[1]);
        string content = Path.Combine(package, "Content");
        string receiptPath = Path.Combine(package, "package.json");
        if (!ValidatePackage(package, content, receiptPath)) return 2;
        if (args[0] == "validate-only") return 0;

        try
        {
            if (!SteamAPI.Init())
            {
                Console.Error.WriteLine("Steam API не подключилась. Открой Steam и войди в аккаунт владельца Workshop.");
                return 1;
            }
            if (!SteamUser.BLoggedOn())
            {
                Console.Error.WriteLine("В Steam нет активного входа.");
                return 1;
            }

            UGCUpdateHandle_t update = SteamUGC.StartItemUpdate(new AppId_t(UnturnedAppId), new PublishedFileId_t(WorkshopFileId));
            if (!SteamUGC.SetItemContent(update, content) ||
                !SteamUGC.SetItemTags(update, new[] { "Object", "Effect" }) ||
                !SteamUGC.RemoveItemKeyValueTags(update, "compatibility_version") ||
                !SteamUGC.AddItemKeyValueTag(update, "compatibility_version", "6") ||
                !SteamUGC.SetItemVisibility(update, ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic))
            {
                Console.Error.WriteLine("Steam отклонил подготовку публичного обновления Workshop.");
                return 1;
            }

            callback = CallResult<SubmitItemUpdateResult_t>.Create((result, ioFailure) =>
            {
                Console.WriteLine($"Workshop item {WorkshopFileId}: {result.m_eResult}; IO={ioFailure}");
                if (result.m_bUserNeedsToAcceptWorkshopLegalAgreement)
                    Console.WriteLine("Steam требует принять соглашение Workshop в клиенте; receipt оставлен в состоянии prepared.");
                failed = ioFailure || result.m_eResult != EResult.k_EResultOK || result.m_bUserNeedsToAcceptWorkshopLegalAgreement;
                completed = true;
            });

            callback.Set(SteamUGC.SubmitItemUpdate(update,
                "Исправлено подтверждение обмена: используется одноразовый токен текущего предложения."));
            DateTime deadline = DateTime.UtcNow.AddMinutes(5);
            while (!completed && DateTime.UtcNow < deadline)
            {
                SteamAPI.RunCallbacks();
                Thread.Sleep(100);
            }
            if (!completed)
            {
                Console.Error.WriteLine("Steam не вернул итог за 5 минут. Сначала проверьте Workshop; не отправляйте обновление повторно вслепую.");
                return 1;
            }
            if (failed) return 1;

            RecordPublishedReceipt(receiptPath);
            Console.WriteLine($"Обновление {WorkshopFileId} опубликовано как Public. Название и описание item не менялись.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Ошибка Workshop: " + error.GetType().Name + ": " + error.Message);
            return 1;
        }
        finally
        {
            SteamAPI.Shutdown();
        }
    }

    private static bool ValidatePackage(string package, string content, string receiptPath)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(receiptPath));
            JsonElement receipt = doc.RootElement;
            if (receipt.GetProperty("workshop_file_id").GetUInt64() != WorkshopFileId ||
                receipt.GetProperty("schema_version").GetInt32() != 2 ||
                receipt.GetProperty("confirmation_protocol").GetString() != ConfirmationProtocol ||
                receipt.GetProperty("published").GetBoolean() ||
                receipt.GetProperty("upload_status").GetString() != "prepared" ||
                receipt.GetProperty("in_game_verified").GetBoolean())
            {
                Console.Error.WriteLine("Receipt не подтверждает подготовленное nonce-обновление для целевого Workshop item.");
                return false;
            }

            string id = File.ReadAllText(Path.Combine(package, "published-item-id.txt"), Encoding.ASCII).Trim();
            if (id != WorkshopFileId.ToString())
            {
                Console.Error.WriteLine("В пакете указан другой Workshop ID.");
                return false;
            }
            if (!File.Exists(Path.Combine(package, "MreTrade-preview.jpg")) ||
                !File.Exists(Path.Combine(content, "Object.meta")) ||
                File.ReadAllBytes(Path.Combine(content, "Object.meta")) is not [0])
            {
                Console.Error.WriteLine("Пакет не содержит ожидаемые preview/Object.meta.");
                return false;
            }

            string[] expected = { "Object.meta", "Bundles/MreTrade/MreTrade.dat", "Bundles/MreTrade/MreTrade.unity3d" };
            string[] actual = Directory.GetFiles(content, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(content, path).Replace('\\', '/')).OrderBy(path => path).ToArray();
            if (!actual.SequenceEqual(expected.OrderBy(path => path)))
            {
                Console.Error.WriteLine("В Content обнаружен неожиданный набор файлов.");
                return false;
            }

            string bundle = Path.Combine(content, "Bundles", "MreTrade", "MreTrade.unity3d");
            string dat = Path.Combine(content, "Bundles", "MreTrade", "MreTrade.dat");
            string bundleHash = Hash(bundle);
            string datHash = Hash(dat);
            if (!StringComparer.OrdinalIgnoreCase.Equals(bundleHash, receipt.GetProperty("bundle_sha256").GetString()) ||
                !StringComparer.OrdinalIgnoreCase.Equals(datHash, receipt.GetProperty("dat_sha256").GetString()) ||
                !File.ReadAllBytes(bundle).AsSpan().StartsWith("UnityFS\0"u8))
            {
                Console.Error.WriteLine("Bundle/dat не совпадают с receipt или bundle не является UnityFS.");
                return false;
            }
            string[] datLines = File.ReadAllLines(dat, Encoding.UTF8).Where(line => !String.IsNullOrWhiteSpace(line)).ToArray();
            string[] expectedDat = { "GUID a986933995df4d29816efcc37f87b220", "Type Effect", "ID 48622", "Asset_Bundle_Version 6", "Exclude_From_Master_Bundle" };
            if (!datLines.SequenceEqual(expectedDat))
            {
                Console.Error.WriteLine("MreTrade.dat не соответствует контракту Effect.");
                return false;
            }

            Console.WriteLine($"Пакет проверен: item={WorkshopFileId}, SHA256={bundleHash}, Protocol={ConfirmationProtocol}");
            return true;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Не удалось проверить пакет: " + error.GetType().Name + ": " + error.Message);
            return false;
        }
    }

    private static void RecordPublishedReceipt(string receiptPath)
    {
        JsonObject receipt = JsonNode.Parse(File.ReadAllText(receiptPath))!.AsObject();
        TimeZoneInfo moscow = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        DateTime moscowNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, moscow);
        receipt["published"] = true;
        receipt["upload_status"] = "steam_client_ok";
        receipt["uploaded_at"] = moscowNow.ToString("yyyy-MM-dd HH:mm:ss") + " Europe/Moscow";
        receipt["workshop_visibility"] = "Public (set and accepted by Steam update)";
        receipt["public_page_verified"] = false;
        receipt["downloaded_payload_verified"] = false;
        receipt["downloaded_payload_sha256_match"] = false;
        receipt["in_game_verified"] = false;
        string temporary = receiptPath + ".tmp";
        File.WriteAllText(temporary, receipt.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
        File.Move(temporary, receiptPath, true);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
