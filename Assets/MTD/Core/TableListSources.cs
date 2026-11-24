using System;
using System.Collections.Generic;
using System.Globalization;

namespace MTD
{
    // Answers "may this display show this table?".
    public interface ITableListSource
    {
        string Description { get; } // For the status display.
        int Version { get; } // Goes up whenever the answer for any table may have changed.
        bool Includes(TableSnapshot table);
    }

    // Every table that publishes a summary, optionally narrowed to a set of MTD table numbers.
    public sealed class DiscoveryTableListSource : ITableListSource
    {
        readonly MtdNumberFilter filter;

        public DiscoveryTableListSource(MtdNumberFilter filter = null)
        {
            this.filter = filter ?? MtdNumberFilter.All;
        }

        public string Description => filter.IsEmpty ? "all discovered tables" : $"discovered tables with MTD number {filter}";

        public int Version => 0;

        public bool Includes(TableSnapshot table)
        {
            return filter.Matches(table.MtdTableNumber);
        }
    }

    // A fixed set of table IDs, replaced whenever the T-Connect server sends this display a new table list.
    public sealed class TableIdListSource : ITableListSource
    {
        HashSet<Guid> tableIds = new HashSet<Guid>();

        public bool HasList { get; private set; } // False until the first list arrives.

        public int Count => tableIds.Count;

        public string Description => HasList ? $"T-Connect table list ({tableIds.Count} tables)" : "waiting for the T-Connect table list";

        public int Version { get; private set; }

        public void SetTables(IEnumerable<Guid> ids)
        {
            tableIds = new HashSet<Guid>(ids ?? new Guid[0]);
            tableIds.Remove(Guid.Empty);
            HasList = true;
            Version++;
        }

        public bool Includes(TableSnapshot table)
        {
            return tableIds.Contains(table.TableId);
        }
    }

    // The "MTD Table Numbers" setting: numbers and ranges separated by commas, such as "1-6, 19".
    // An empty filter matches every table.
    public sealed class MtdNumberFilter
    {
        public static readonly MtdNumberFilter All = new MtdNumberFilter("");

        readonly List<(int from, int to)> ranges = new List<(int, int)>();
        readonly HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly string text;

        MtdNumberFilter(string text)
        {
            this.text = text;
        }

        public bool IsEmpty => ranges.Count == 0 && names.Count == 0;

        public static MtdNumberFilter Parse(string text)
        {
            MtdNumberFilter filter = new MtdNumberFilter((text ?? "").Trim());

            foreach (string rawToken in filter.text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = rawToken.Trim();
                if (token.Length == 0) continue;

                int dash = token.IndexOf('-', 1); // From 1 so a leading minus is not read as a range.
                if (dash > 0
                    && TryParseNumber(token.Substring(0, dash), out int from)
                    && TryParseNumber(token.Substring(dash + 1), out int to))
                {
                    filter.ranges.Add((Math.Min(from, to), Math.Max(from, to)));
                }
                else if (TryParseNumber(token, out int number))
                {
                    filter.ranges.Add((number, number));
                }
                else
                {
                    filter.names.Add(token); // MTD numbers are text on the wire, so "A1" is a legal one.
                }
            }

            return filter;
        }

        public bool Matches(string mtdTableNumber)
        {
            if (IsEmpty) return true;

            string value = (mtdTableNumber ?? "").Trim();
            if (TryParseNumber(value, out int number))
            {
                foreach ((int from, int to) in ranges)
                {
                    if (number >= from && number <= to) return true;
                }
            }

            return names.Contains(value);
        }

        public override string ToString()
        {
            return text;
        }

        static bool TryParseNumber(string text, out int number)
        {
            return int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out number);
        }
    }
}
