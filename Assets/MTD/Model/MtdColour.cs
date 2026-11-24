using System.Globalization;
using UnityEngine;

namespace MTD
{
    public static class MtdColour
    {
        // Reads "#RRGGBB" or "#RRGGBBAA"; the hash is optional. Anything else is rejected, so the caller can fall
        // back to the MTD UI's default colour.
        public static bool TryParse(string text, out Color32 colour)
        {
            colour = default;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string hex = text.Trim();
            if (hex[0] == '#') hex = hex.Substring(1);
            if (hex.Length != 6 && hex.Length != 8) return false;

            if (!uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value)) return false;

            if (hex.Length == 6)
            {
                colour = new Color32((byte)(value >> 16), (byte)(value >> 8), (byte)value, 255);
            }
            else
            {
                colour = new Color32((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
            }

            return true;
        }

        public static Color32? ParseOrNull(string text)
        {
            return TryParse(text, out Color32 colour) ? colour : (Color32?)null;
        }

        public static string ToHex(Color32 colour)
        {
            return $"#{colour.r:X2}{colour.g:X2}{colour.b:X2}";
        }
    }
}
