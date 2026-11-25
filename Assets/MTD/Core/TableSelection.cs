using System;
using System.Collections.Generic;
using System.Globalization;

namespace MTD
{
    // Decides which tables may take a slot, and in what order.
    public static class TableSelection
    {
        // A table is eligible when it is fresh, in the display's table list, drawable by the selected MTD UI, and
        // open (unless closed tables are allowed).
        public static List<Guid> GetEligibleOrdered(IEnumerable<TableState> tables, ITableListSource source, Func<TableSnapshot, bool> canShow, bool skipClosedTables)
        {
            List<TableSnapshot> eligible = new List<TableSnapshot>();
            foreach (TableState state in tables)
            {
                TableSnapshot table = state.Snapshot;
                if (!state.IsFresh) continue;
                if (skipClosedTables && !table.TableOpen) continue;
                if (!source.Includes(table)) continue;
                if (!canShow(table)) continue;
                eligible.Add(table);
            }

            eligible.Sort(Compare);

            List<Guid> ordered = new List<Guid>(eligible.Count);
            foreach (TableSnapshot table in eligible) ordered.Add(table.TableId);
            return ordered;
        }

        // MTD table number first: numbers in numeric order, then any that are not numbers in alphabetical order.
        // Ties fall to table name, table number and finally the ID, so the order never depends on arrival.
        public static int Compare(TableSnapshot a, TableSnapshot b)
        {
            bool aIsNumber = int.TryParse(a.MtdTableNumber, NumberStyles.None, CultureInfo.InvariantCulture, out int aNumber);
            bool bIsNumber = int.TryParse(b.MtdTableNumber, NumberStyles.None, CultureInfo.InvariantCulture, out int bNumber);

            int result;
            if (aIsNumber && bIsNumber) result = aNumber.CompareTo(bNumber);
            else if (aIsNumber != bIsNumber) result = aIsNumber ? -1 : 1;
            else result = string.Compare(a.MtdTableNumber, b.MtdTableNumber, StringComparison.OrdinalIgnoreCase);

            if (result == 0) result = string.Compare(a.TableName, b.TableName, StringComparison.OrdinalIgnoreCase);
            if (result == 0) result = a.TableNumber.CompareTo(b.TableNumber);
            if (result == 0) result = a.TableId.CompareTo(b.TableId);
            return result;
        }
    }
}
