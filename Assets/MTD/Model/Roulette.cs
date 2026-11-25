using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MTD
{
    public enum RouletteColour
    {
        Green,
        Red,
        Black,
    }

    // One pocket as the table shows it: "0", "00", "000", "0000" or "1" to "36".
    public readonly struct RoulettePocket
    {
        static readonly HashSet<int> redNumbers = new HashSet<int> { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };

        public readonly string Label;
        public readonly RouletteColour Colour;

        RoulettePocket(string label, RouletteColour colour)
        {
            Label = label;
            Colour = colour;
        }

        public bool IsZero => Colour == RouletteColour.Green;

        public static bool TryParse(string text, out RoulettePocket pocket)
        {
            pocket = default;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string label = text.Trim();
            if (label == "0" || label == "00" || label == "000" || label == "0000")
            {
                pocket = new RoulettePocket(label, RouletteColour.Green);
                return true;
            }

            if (label.Length > 2 || label[0] == '0') return false; // "07" and "017" are not pockets.
            if (!int.TryParse(label, NumberStyles.None, CultureInfo.InvariantCulture, out int number)) return false;
            if (number < 1 || number > 36) return false;

            pocket = new RoulettePocket(label, redNumbers.Contains(number) ? RouletteColour.Red : RouletteColour.Black);
            return true;
        }

        public override string ToString()
        {
            return Label ?? "";
        }
    }

    public readonly struct RouletteResult
    {
        public readonly RoulettePocket Pocket;
        public readonly int Multiplier; // 0 when the result has none.
        public readonly Color32? MultiplierColour; // The chip colour DDS shows, when it sent one.

        public RouletteResult(RoulettePocket pocket, int multiplier, Color32? multiplierColour)
        {
            Pocket = pocket;
            Multiplier = multiplier > 0 ? multiplier : 0;
            MultiplierColour = Multiplier > 0 ? multiplierColour : null;
        }

        public bool HasMultiplier => Multiplier > 0;
    }
}
