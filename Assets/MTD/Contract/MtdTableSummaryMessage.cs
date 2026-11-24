using System;
using System.Collections.Generic;

namespace MTD
{
    // The message DDS publishes for each table. The wire format is defined in Docs/MTD-Table-Summary-Contract.md;
    // property names here must match it exactly.
    public class MtdTableSummaryMessage
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; }
        public Guid TableId { get; set; }
        public Guid GameId { get; set; }
        public string GameType { get; set; } // Kept as text so a game type MTD has never heard of still parses.
        public string TableName { get; set; }
        public int TableNumber { get; set; }
        public string MTDTableNumber { get; set; }
        public string MTDColourSetting { get; set; }
        public bool TableOpen { get; set; } = true;
        public long MinBet { get; set; }
        public long MaxBet { get; set; }
        public string MinBetText { get; set; }
        public string MaxBetText { get; set; }
        public List<MtdResultMessage> Results { get; set; }
        public List<string> HotNumbers { get; set; }
        public List<string> ColdNumbers { get; set; }
        public Dictionary<string, string> Statistics { get; set; }
        public DateTimeOffset EventTime { get; set; }
    }

    public class MtdResultMessage
    {
        public string Result { get; set; }
        public int? Multiplier { get; set; }
        public string MultiplierColour { get; set; }
    }
}
