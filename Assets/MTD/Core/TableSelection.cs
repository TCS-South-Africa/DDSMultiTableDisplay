using System;
using System.Collections.Generic;

namespace MTD
{
    // Decides which tables may take a slot, and in what order.
    public static class TableSelection
    {
        // A table is eligible when it is fresh, in the display's table list, drawable by the selected MTD UI, and
        // open (unless closed tables are allowed).
        public static List<Guid> GetEligibleOrdered(IEnumerable<TableState> tables, TableIdListSource source, Func<TableSnapshot, bool> canShow, bool skipClosedTables)
        {
            List<Guid> eligible = new List<Guid>();
            foreach (TableState state in tables)
            {
                TableSnapshot table = state.Snapshot;
                if (!state.IsFresh) continue;
                if (skipClosedTables && !table.TableOpen) continue;
                if (!source.Includes(table)) continue;
                if (!canShow(table)) continue;
                eligible.Add(table.TableId);
            }

            eligible.Sort((a, b) => source.Position(a).CompareTo(source.Position(b)));
            return eligible;
        }
    }
}
