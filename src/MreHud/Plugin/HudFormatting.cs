using System;
using System.Text;

namespace MreHud
{
    // Чистая логика представления: не зависит от Unity, Steam и состояния сервера.
    public static class HudFormatting
    {
        public static int FilledSegments(int value, int maximum, int segments)
        {
            if (maximum <= 0 || segments <= 0 || value <= 0) return 0;
            if (value >= maximum) return segments;
            return (int)Math.Ceiling((double)value / maximum * segments);
        }

        public static string PlayerName(string raw)
        {
            if (String.IsNullOrWhiteSpace(raw)) return "Игрок";
            var text = new StringBuilder();
            foreach (char c in raw)
            {
                if (Char.IsControl(c) || c == '\u2028' || c == '\u2029') continue;
                // Имена не могут подставлять rich text или переводить карточку на новую строку.
                text.Append(c == '<' ? '‹' : c == '>' ? '›' : c);
                if (text.Length >= 32) break;
            }
            return text.Length == 0 ? "Игрок" : text.ToString();
        }

        public static string BuildingName(string raw)
        {
            return String.IsNullOrWhiteSpace(raw) ? "Постройка" : PlayerName(raw);
        }

        public static string MagazineName(int id, string name)
        {
            if (id < 0) return "МАГАЗИН НЕИЗВЕСТЕН";
            if (id == 0) return "МАГАЗИН НЕ УСТАНОВЛЕН";
            if (String.IsNullOrWhiteSpace(name)) return "МАГАЗИН НЕИЗВЕСТЕН";
            var clean = new StringBuilder();
            foreach (char c in name)
            {
                if (Char.IsControl(c) || c == '\u2028' || c == '\u2029') continue;
                clean.Append(c == '<' ? '‹' : c == '>' ? '›' : c);
                if (clean.Length >= 32) break;
            }
            string result = clean.ToString().Trim();
            return result.Length == 0 ? "МАГАЗИН НЕИЗВЕСТЕН" : result;
        }

        public static int Ammo(byte[] state)
        {
            // GunStateIndices.AMMO, проверено по установленной версии и официальному SDK SDG.
            return state != null && state.Length > 10 ? state[10] : -1;
        }

        public static int MagazineId(byte[] state)
        {
            // GunStateIndices.MAGAZINE_ID: little endian, никаких предположений о полном state.
            return state != null && state.Length > 9 ? (ushort)(state[8] | state[9] << 8) : -1;
        }
    }
}
