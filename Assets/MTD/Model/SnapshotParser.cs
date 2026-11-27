using System;
using System.Collections.Generic;
using System.Globalization;

namespace MTD
{
    // Turns a summary message into a snapshot. Bad individual values (an unknown pocket, an unreadable colour) are
    // left out instead of failing the whole message, because one odd entry should not blank a table's widget.
    // Result rows are the exception, because their order carries meaning: see ReadResults.
    public static class SnapshotParser
    {
        const int maxUnreadableTextLength = 24;

        delegate bool ResultReader<T>(MtdResultMessage row, out T result);

        // What ReadResults makes of a message's result rows.
        sealed class ResultRows<T> where T : struct
        {
            public T? Current;
            public string UnreadableCurrent;
            public readonly List<T> History = new List<T>();
            public readonly List<string> Keys = new List<string>(); // One per row, readable or not.
        }

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
            snapshot.MtdTableUniqueName = (message.MTDTableUniqueName ?? "").Trim();
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
            ResultRows<RouletteResult> rows = ReadResults<RouletteResult>(message.Results, ReadRouletteResult, RouletteSnapshot.KeyOf);

            return new RouletteSnapshot
            {
                Variant = variant,
                Current = rows.Current,
                UnreadableCurrentResult = rows.UnreadableCurrent,
                History = rows.History,
                ResultKeys = rows.Keys,
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

        // The first row is the table's current result. If it cannot be read, the snapshot has no current result and
        // says why, so that the result before it is not shown as the latest.
        //
        // An unreadable row further down is left out of the history a widget draws, but it still gets a key of its
        // own. The keys are what the registry compares to tell a new result from a correction, and it compares them
        // by position. Without its key, every row below an unreadable one would sit one place too high: a new
        // result arriving above it would look like an edit, and its repair would look like a result being added.
        static ResultRows<T> ReadResults<T>(List<MtdResultMessage> rows, ResultReader<T> read, Func<T, string> keyOf)
            where T : struct
        {
            ResultRows<T> results = new ResultRows<T>();
            if (rows == null) return results;

            for (int i = 0; i < rows.Count; i++)
            {
                T result = default;
                bool readable = rows[i] != null && read(rows[i], out result);
                string unreadableText = readable ? null : (rows[i]?.Result ?? "").Trim();

                results.Keys.Add(readable ? keyOf(result) : TableSnapshot.UnreadableKey(unreadableText));

                if (i > 0)
                {
                    if (readable) results.History.Add(result);
                }
                else if (readable)
                {
                    results.Current = result;
                }
                else
                {
                    results.UnreadableCurrent = unreadableText.Length > maxUnreadableTextLength ? unreadableText.Substring(0, maxUnreadableTextLength) : unreadableText;
                }
            }

            return results;
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
            ResultRows<SicBoResult> rows = ReadResults<SicBoResult>(message.Results, ReadSicBoResult, SicBoSnapshot.KeyOf);

            return new SicBoSnapshot
            {
                Current = rows.Current,
                UnreadableCurrentResult = rows.UnreadableCurrent,
                History = rows.History,
                ResultKeys = rows.Keys,
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
