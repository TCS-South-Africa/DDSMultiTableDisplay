using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MTD
{
    // The debug UI's widget: everything in the snapshot as plain text. It draws any game, which is what makes it
    // useful for checking a message end to end before a styled widget exists.
    public sealed class DebugTableWidget : TableWidget
    {
        const int maxResults = 12;
        const string red = "#FF6060";
        const string black = "#F0F0F0";
        const string green = "#50D070";
        const string blue = "#60A8FF";
        const string gold = "#F0C040";
        const string dim = "#A0A8B8";

        public Image background;
        public TMP_Text text;

        string lastChange = "";

        protected override void OnBind(TableSnapshot table)
        {
            lastChange = "";
            Draw(table);
        }

        protected override void OnRefresh(TableSnapshot table, TableChange change)
        {
            lastChange = $"{change} at {DateTime.Now:HH:mm:ss}";
            Draw(table);
        }

        protected override void OnColour(Color colour)
        {
            // Darkened so the text stays readable on any table colour.
            if (background != null) background.color = new Color(colour.r * 0.55f, colour.g * 0.55f, colour.b * 0.55f, 1f);
        }

        void Draw(TableSnapshot table)
        {
            if (text == null) return;

            StringBuilder builder = new StringBuilder(512);
            builder.Append("<size=150%><b><noparse>").Append(table.DisplayLabel(UseUniqueName).Replace("<", "<</noparse><noparse>"))
                .Append("</noparse></b></size>\n");
            builder.Append(table.GameType).Append(table.TableOpen ? "" : "  <color=" + red + ">CLOSED</color>").Append('\n');
            builder.Append("MIN ").Append(table.MinBetText).Append("  MAX ").Append(table.MaxBetText).Append("\n\n");

            switch (table)
            {
                case RouletteSnapshot roulette:
                    DrawRoulette(builder, roulette);
                    break;
                case SicBoSnapshot sicBo:
                    DrawSicBo(builder, sicBo);
                    break;
                default:
                    builder.Append("(no widget for this game)\n");
                    break;
            }

            if (lastChange.Length > 0) builder.Append("\n<color=").Append(dim).Append(">").Append(lastChange).Append("</color>");
            text.text = builder.ToString();
        }

        static void DrawRoulette(StringBuilder builder, RouletteSnapshot table)
        {
            int rows = 0;
            if (table.CurrentResultUnreadable)
            {
                AppendUnreadableCurrent(builder, table, "260%");
                rows++;
            }
            else if (table.Current.HasValue)
            {
                AppendRouletteResult(builder, table.Current.Value, isCurrent: true);
                rows++;
            }

            for (int i = 0; i < table.History.Count && rows < maxResults; i++, rows++)
            {
                AppendRouletteResult(builder, table.History[i], isCurrent: false);
            }

            if (rows == 0) builder.Append("(no results)\n");

            if (table.HotNumbers.Count > 0)
            {
                builder.Append("\nHOT ");
                foreach (RoulettePocket pocket in table.HotNumbers) { AppendPocket(builder, pocket); builder.Append(' '); }
            }

            if (table.ColdNumbers.Count > 0)
            {
                builder.Append("\nCOLD ");
                foreach (RoulettePocket pocket in table.ColdNumbers) { AppendPocket(builder, pocket); builder.Append(' '); }
            }

            int sample = table.GetStatistic("SampleSize");
            if (sample > 0)
            {
                builder.Append("\n<color=").Append(red).Append(">R ").Append(Percent(table.GetStatistic("Red"), sample)).Append("</color>")
                    .Append("  B ").Append(Percent(table.GetStatistic("Black"), sample))
                    .Append("  <color=").Append(green).Append(">Z ").Append(Percent(table.GetStatistic("Zero"), sample)).Append("</color>")
                    .Append("  <color=").Append(dim).Append(">n=").Append(sample).Append("</color>\n");
            }
        }

        static void AppendRouletteResult(StringBuilder builder, RouletteResult result, bool isCurrent)
        {
            if (isCurrent) builder.Append("<size=260%><b>");
            AppendPocket(builder, result.Pocket);
            if (isCurrent) builder.Append("</b></size>");
            if (result.HasMultiplier)
            {
                string chip = result.MultiplierColour.HasValue ? MtdColour.ToHex(result.MultiplierColour.Value) : gold;
                builder.Append("  <color=").Append(chip).Append(">x").Append(result.Multiplier).Append("</color>");
            }

            builder.Append('\n');
        }

        // The table has a newer result than any shown below, but DDS sent it in a form this MTD cannot read.
        static void AppendUnreadableCurrent(StringBuilder builder, TableSnapshot table, string size)
        {
            builder.Append("<size=").Append(size).Append("><b>?</b></size>  <color=").Append(dim).Append(">unreadable result");
            if (table.UnreadableCurrentResult.Length > 0) builder.Append(" \"<noparse>").Append(table.UnreadableCurrentResult).Append("</noparse>\"");
            builder.Append("</color>\n");
        }

        static void AppendPocket(StringBuilder builder, RoulettePocket pocket)
        {
            string colour = pocket.Colour == RouletteColour.Red ? red : pocket.Colour == RouletteColour.Green ? green : black;
            builder.Append("<color=").Append(colour).Append('>').Append(pocket.Label).Append("</color>");
        }

        static void DrawSicBo(StringBuilder builder, SicBoSnapshot table)
        {
            int rows = 0;
            if (table.CurrentResultUnreadable)
            {
                AppendUnreadableCurrent(builder, table, "200%");
                rows++;
            }
            else if (table.Current.HasValue)
            {
                AppendSicBoResult(builder, table.Current.Value, isCurrent: true);
                rows++;
            }

            for (int i = 0; i < table.History.Count && rows < maxResults; i++, rows++)
            {
                AppendSicBoResult(builder, table.History[i], isCurrent: false);
            }

            if (rows == 0) builder.Append("(no results)\n");

            if (table.HotTotals.Count > 0) builder.Append("\nHOT ").Append(string.Join(" ", table.HotTotals));
            if (table.ColdTotals.Count > 0) builder.Append("\nCOLD ").Append(string.Join(" ", table.ColdTotals));

            int sample = table.GetStatistic("SampleSize");
            if (sample > 0)
            {
                builder.Append("\n<color=").Append(red).Append(">S ").Append(Percent(table.GetStatistic("Small"), sample)).Append("</color>")
                    .Append("  <color=").Append(blue).Append(">B ").Append(Percent(table.GetStatistic("Big"), sample)).Append("</color>")
                    .Append("  <color=").Append(green).Append(">T ").Append(Percent(table.GetStatistic("Triple"), sample)).Append("</color>")
                    .Append("  <color=").Append(dim).Append(">n=").Append(sample).Append("</color>\n");
            }
        }

        static void AppendSicBoResult(StringBuilder builder, SicBoResult result, bool isCurrent)
        {
            if (isCurrent) builder.Append("<size=200%><b>");
            builder.Append(result.Die1).Append(' ').Append(result.Die2).Append(' ').Append(result.Die3);
            if (isCurrent) builder.Append("</b></size>");
            builder.Append("  <color=").Append(gold).Append('>').Append(result.Total).Append("</color> ");

            switch (result.Outcome)
            {
                case SicBoOutcome.Big:
                    builder.Append("<color=").Append(blue).Append(">BIG</color>");
                    break;
                case SicBoOutcome.Small:
                    builder.Append("<color=").Append(red).Append(">SMALL</color>");
                    break;
                default:
                    builder.Append("<color=").Append(green).Append(">TRIPLE</color>");
                    break;
            }

            builder.Append('\n');
        }

        static string Percent(int count, int sample)
        {
            return Mathf.RoundToInt(100f * count / sample) + "%";
        }
    }
}
