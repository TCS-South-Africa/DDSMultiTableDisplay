using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MTD
{
    // One table's state as of one summary message, already parsed for display. Snapshots are never changed after
    // SnapshotParser builds them; a newer message makes a new snapshot.
    public abstract class TableSnapshot
    {
        static readonly IReadOnlyDictionary<string, string> noStatistics = new Dictionary<string, string>();

        string displaySignature;

        public Guid TableId { get; internal set; }
        public Guid GameId { get; internal set; }
        public string GameType { get; internal set; } = "";
        public GameFamily Family { get; internal set; }
        public string TableName { get; internal set; } = "";
        public int TableNumber { get; internal set; }
        public string MtdTableNumber { get; internal set; } = "";
        public Color32? Colour { get; internal set; } // Null when DDS sent no colour or one that does not parse.
        public bool TableOpen { get; internal set; } = true;
        public long MinBet { get; internal set; }
        public long MaxBet { get; internal set; }
        public string MinBetText { get; internal set; } = "";
        public string MaxBetText { get; internal set; } = "";
        public IReadOnlyDictionary<string, string> Statistics { get; internal set; } = noStatistics;
        public DateTimeOffset EventTime { get; internal set; }

        // One text key per result, newest first. Lets the registry tell a new result from a correction without
        // knowing the game.
        public abstract IReadOnlyList<string> ResultKeys { get; }

        // A statistic count, or the fallback when DDS did not send that key or it is not a number.
        public int GetStatistic(string key, int fallback = 0)
        {
            if (Statistics.TryGetValue(key, out string text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                return value;
            }

            return fallback;
        }

        // Everything a widget could draw, as one string. Two snapshots with the same signature look the same.
        internal string DisplaySignature => displaySignature ??= BuildDisplaySignature();

        string BuildDisplaySignature()
        {
            StringBuilder builder = new StringBuilder(256);
            builder.Append(GameType).Append('|').Append(TableName).Append('|').Append(TableNumber).Append('|')
                .Append(MtdTableNumber).Append('|').Append(Colour.HasValue ? MtdColour.ToHex(Colour.Value) : "").Append('|')
                .Append(TableOpen).Append('|').Append(MinBet).Append('|').Append(MaxBet).Append('|')
                .Append(MinBetText).Append('|').Append(MaxBetText).Append('|');
            builder.Append(string.Join(",", ResultKeys)).Append('|');
            AppendExtraSignature(builder);
            foreach (KeyValuePair<string, string> statistic in Statistics.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                builder.Append(statistic.Key).Append('=').Append(statistic.Value).Append(';');
            }

            return builder.ToString();
        }

        protected virtual void AppendExtraSignature(StringBuilder builder)
        {
        }
    }

    public sealed class RouletteSnapshot : TableSnapshot
    {
        static readonly RouletteResult[] noResults = new RouletteResult[0];
        static readonly RoulettePocket[] noPockets = new RoulettePocket[0];

        IReadOnlyList<string> resultKeys;

        public RouletteVariant Variant { get; internal set; }
        public IReadOnlyList<RouletteResult> Results { get; internal set; } = noResults; // Newest first.
        public IReadOnlyList<RoulettePocket> HotNumbers { get; internal set; } = noPockets; // Hottest first.
        public IReadOnlyList<RoulettePocket> ColdNumbers { get; internal set; } = noPockets; // Coldest first.

        public override IReadOnlyList<string> ResultKeys => resultKeys ??= Results
            .Select(result => result.HasMultiplier
                ? $"{result.Pocket.Label}x{result.Multiplier}{(result.MultiplierColour.HasValue ? MtdColour.ToHex(result.MultiplierColour.Value) : "")}"
                : result.Pocket.Label)
            .ToArray();

        protected override void AppendExtraSignature(StringBuilder builder)
        {
            builder.Append(string.Join(",", HotNumbers.Select(pocket => pocket.Label))).Append('|')
                .Append(string.Join(",", ColdNumbers.Select(pocket => pocket.Label))).Append('|');
        }
    }

    public sealed class SicBoSnapshot : TableSnapshot
    {
        static readonly SicBoResult[] noResults = new SicBoResult[0];
        static readonly int[] noTotals = new int[0];

        IReadOnlyList<string> resultKeys;

        public IReadOnlyList<SicBoResult> Results { get; internal set; } = noResults; // Newest first.
        public IReadOnlyList<int> HotTotals { get; internal set; } = noTotals; // Hottest first.
        public IReadOnlyList<int> ColdTotals { get; internal set; } = noTotals; // Coldest first.

        public override IReadOnlyList<string> ResultKeys => resultKeys ??= Results.Select(result => result.ToString()).ToArray();

        protected override void AppendExtraSignature(StringBuilder builder)
        {
            builder.Append(string.Join(",", HotTotals)).Append('|').Append(string.Join(",", ColdTotals)).Append('|');
        }
    }

    // A table whose game MTD does not draw (Blackjack for now, and anything unknown). It is kept so the table can be
    // counted and reported, but it never becomes eligible for a slot.
    public sealed class UnsupportedSnapshot : TableSnapshot
    {
        static readonly string[] noKeys = new string[0];

        public override IReadOnlyList<string> ResultKeys => noKeys;
    }
}
