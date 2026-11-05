using System;
using Rocket.API;

namespace MreChatStyle
{
    public sealed class MreChatStyleConfiguration : IRocketPluginConfiguration
    {
        public string FallbackIconUrl { get; set; }
        public string SystemIconUrl { get; set; }
        public string HomeIconUrl { get; set; }
        public string TpaIconUrl { get; set; }
        public string KitsIconUrl { get; set; }
        public string TradeIconUrl { get; set; }
        public string VaultIconUrl { get; set; }
        public string VoteIconUrl { get; set; }
        public string AirdropIconUrl { get; set; }
        public string StaffIconUrl { get; set; }
        public string AdminIconUrl { get; set; }

        public void LoadDefaults()
        {
            FallbackIconUrl = "https://raw.githubusercontent.com/kem4ikkk/MRE/994f96f53de120df31ae81d76e578ed6597371bb/assets/mre-server-logo-transparent.png";
            const string root = "https://raw.githubusercontent.com/kem4ikkk/MRE/main/assets/chat-icons/";
            SystemIconUrl = root + "system.png";
            HomeIconUrl = root + "home.png";
            TpaIconUrl = root + "tpa.png";
            KitsIconUrl = root + "kits.png";
            TradeIconUrl = root + "trade.png";
            VaultIconUrl = root + "vault.png";
            VoteIconUrl = root + "vote.png";
            AirdropIconUrl = root + "airdrop.png";
            StaffIconUrl = root + "staff.png";
            AdminIconUrl = root + "admin.png";
        }

        public string IconFor(string category)
        {
            string icon;
            switch (category)
            {
                case "HOME": icon = HomeIconUrl; break;
                case "TPA": icon = TpaIconUrl; break;
                case "KITS": icon = KitsIconUrl; break;
                case "TRADE": icon = TradeIconUrl; break;
                case "VAULT": icon = VaultIconUrl; break;
                case "VOTE": icon = VoteIconUrl; break;
                case "AIRDROP": icon = AirdropIconUrl; break;
                case "STAFF": icon = StaffIconUrl; break;
                case "ADMIN": icon = AdminIconUrl; break;
                default: icon = SystemIconUrl; break;
            }
            return IsHttpUrl(icon) ? icon : (IsHttpUrl(FallbackIconUrl) ? FallbackIconUrl : String.Empty);
        }

        private static bool IsHttpUrl(string value)
        {
            Uri uri;
            return !String.IsNullOrWhiteSpace(value) && Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }
}
