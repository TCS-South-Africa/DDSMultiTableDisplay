using System;
using System.Collections.Generic;
using System.Globalization;

namespace MTD
{
    // Turns a summary message into a snapshot. Bad individual values (an unknown pocket, an unreadable colour) are
    // dropped instead of failing the whole message, because one odd entry should not blank a table's widget.
    public static class SnapshotParser
    {
        // Returns null only when the message cannot identify a table.
        public static TableSnapshot Parse(MtdTableSummaryMessage message)
        {
            if (message == null || message.TableId == Guid.Empty) return null;

            GameFamily family = GameTypes.GetFamily(message.GameType, out RouletteVariant variant);
            TableSnapshot snapshot;
            switch (family)
            {
                case GameFamily.Roulette:
                    snapshot = ParseRoulette(message, variant);
                    break;
                case GameFamily.SicBo:
                    snapshot = ParseSicBo(message);
                    break;
                default:
                    snapshot = new UnsupportedSnapshot();
                    break;
            }

            snapshot.TableId = message.TableId;
            snapshot.GameId = message.GameId;
            snapshot.GameType = (message.GameType ?? "").Trim();
            snapshot.Family = family;
            snapshot.TableName = (message.TableName ?? "").Trim();
            snapshot.TableNumber = message.TableNumber;
            snapshot.MtdTableNumber = (message.MTDTableNumber ?? "").Trim();
            snapshot.Colour = MtdColour.ParseOrNull(message.MTDColourSetting);
            snapshot.TableOpen = message.TableOpen;
            snapshot.MinBet = message.MinBet;
            snapshot.MaxBet = message.MaxBet;
            snapshot.MinBetText = message.MinBetText ?? "";
            snapshot.MaxBetText = message.MaxBetText ?? "";
            snapshot.EventTime = message.EventTime;
            if (message.Statistics != null && message.Statistics.Count > 0)
            {
                snapshot.Statistics = new Dictionary<string, string>(message.Statistics);
            }

            return snapshot;
        }

        static RouletteSnapshot ParseRoulette(MtdTableSummaryMessage message, RouletteVariant variant)
        {
            List<RouletteResult> results = new List<RouletteResult>();
            if (message.Results != null)
            {
                foreach (MtdResultMessage entry in message.Results)
                {
                    if (entry == null || !RoulettePocket.TryParse(entry.Result, out RoulettePocket pocket)) continue;
                    results.Add(new RouletteResult(pocket, entry.Multiplier ?? 0, MtdColour.ParseOrNull(entry.MultiplierColour)));
                }
            }

            return new RouletteSnapshot
            {
                Variant = variant,
                Results = results,
                HotNumbers = ParsePockets(message.HotNumbers),
                ColdNumbers = ParsePockets(message.ColdNumbers),
            };
        }

        static List<RoulettePocket> ParsePockets(List<string> labels)
        {
            List<RoulettePocket> pockets = new List<RoulettePocket>();
            if (labels == null) return pockets;

            foreach (string label in labels)
            {
                if (RoulettePocket.TryParse(label, out RoulettePocket pocket)) pockets.Add(pocket);
            }

            return pockets;
        }

        static SicBoSnapshot ParseSicBo(MtdTableSummaryMessage message)
        {
            List<SicBoResult> results = new List<SicBoResult>();
            if (message.Results != null)
            {
                foreach (MtdResultMessage entry in message.Results)
                {
                    if (entry != null && SicBoResult.TryParse(entry.Result, out SicBoResult result)) results.Add(result);
                }
            }

            return new SicBoSnapshot
            {
                Results = results,
                HotTotals = ParseTotals(message.HotNumbers),
                ColdTotals = ParseTotals(message.ColdNumbers),
            };
        }

        // Three dice total 3 to 18.
        static List<int> ParseTotals(List<string> labels)
        {
            List<int> totals = new List<int>();
            if (labels == null) return totals;

            foreach (string label in labels)
            {
                if (int.TryParse((label ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int total)
                    && total >= 3 && total <= 18)
                {
                    totals.Add(total);
                }
            }

            return totals;
        }
    }
}
