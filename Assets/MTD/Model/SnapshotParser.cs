using System;
using System.Collections.Generic;
using System.Globalization;

namespace MTD
{
    // Turns a summary message into a snapshot. Bad individual values (an unknown pocket, an unreadable colour) are
    // dropped instead of failing the whole message, because one odd entry should not blank a table's widget.
    // The one exception is the first result row: see ReadResults.
    public static class SnapshotParser
    {
        const int maxUnreadableTextLength = 24;

        delegate bool ResultReader<T>(MtdResultMessage row, out T result);

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
            ReadResults(message.Results, ReadRouletteResult, out RouletteResult? current, out string unreadableCurrent, out List<RouletteResult> history);

            return new RouletteSnapshot
            {
                Variant = variant,
                Current = current,
                UnreadableCurrentResult = unreadableCurrent,
                History = history,
                HotNumbers = ParsePockets(message.HotNumbers),
                ColdNumbers = ParsePockets(message.ColdNumbers),
            };
        }

        static bool ReadRouletteResult(MtdResultMessage row, out RouletteResult result)
        {
            result = default;
            if (!RoulettePocket.TryParse(row.Result, out RoulettePocket pocket)) return false;

            result = new RouletteResult(pocket, row.Multiplier ?? 0, MtdColour.ParseOrNull(row.MultiplierColour));
            return true;
        }

        // The first row is the table's current result, and it is the one row that is never dropped. If it cannot be
        // read, the snapshot has no current result and says why, so that the result before it is not shown as the
        // latest. An unreadable row further down is dropped, which only shortens the history.
        static void ReadResults<T>(List<MtdResultMessage> rows, ResultReader<T> read, out T? current, out string unreadableCurrent, out List<T> history)
            where T : struct
        {
            current = null;
            unreadableCurrent = null;
            history = new List<T>();
            if (rows == null) return;

            for (int i = 0; i < rows.Count; i++)
            {
                T result = default;
                bool readable = rows[i] != null && read(rows[i], out result);

                if (i > 0)
                {
                    if (readable) history.Add(result);
                }
                else if (readable)
                {
                    current = result;
                }
                else
                {
                    string text = (rows[i]?.Result ?? "").Trim();
                    unreadableCurrent = text.Length > maxUnreadableTextLength ? text.Substring(0, maxUnreadableTextLength) : text;
                }
            }
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
            ReadResults(message.Results, ReadSicBoResult, out SicBoResult? current, out string unreadableCurrent, out List<SicBoResult> history);

            return new SicBoSnapshot
            {
                Current = current,
                UnreadableCurrentResult = unreadableCurrent,
                History = history,
                HotTotals = ParseTotals(message.HotNumbers),
                ColdTotals = ParseTotals(message.ColdNumbers),
            };
        }

        static bool ReadSicBoResult(MtdResultMessage row, out SicBoResult result)
        {
            return SicBoResult.TryParse(row.Result, out result);
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
