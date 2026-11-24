using System;
using System.Collections.Generic;
using System.Linq;

namespace MTD
{
    // What a newly accepted snapshot means for whoever is showing the table.
    public enum TableChange
    {
        None, // The snapshot was not accepted (it was older than the one already held).
        Added, // A table MTD was not showing: never seen before, or back after going quiet.
        NewResult, // One or more results were added at the top of the list.
        Corrected, // The result list changed in any other way: an edit, a removal, a cleared history.
        Updated, // Same results, but something else a widget draws changed (limits, colour, statistics...).
        Heartbeat, // Nothing visible changed. The table is simply still there.
    }

    public sealed class TableState
    {
        public TableSnapshot Snapshot { get; internal set; }
        public double LastHeardAt { get; internal set; }
        public bool IsFresh { get; internal set; } // False once the table has been quiet for longer than the timeout.
    }

    // Holds the newest snapshot for every table MTD has heard from, and decides when a table has gone quiet.
    // Time is passed in, in seconds on any steadily increasing clock, so this can be tested without waiting.
    public sealed class TableRegistry
    {
        const double forgetAfterSeconds = 600; // A table quiet this long is dropped entirely.

        readonly Dictionary<Guid, TableState> tables = new Dictionary<Guid, TableState>();

        public TableRegistry(double timeoutSeconds)
        {
            TimeoutSeconds = timeoutSeconds;
        }

        public double TimeoutSeconds { get; }

        public IEnumerable<TableState> Tables => tables.Values;

        public int FreshCount => tables.Values.Count(state => state.IsFresh);

        // Goes up whenever the set of fresh tables, or anything that decides a table's eligibility or position in
        // the order, may have changed. Callers compare it with the value they last acted on.
        public int MembershipVersion { get; private set; }

        public bool TryGet(Guid tableId, out TableState state)
        {
            return tables.TryGetValue(tableId, out state);
        }

        public TableChange Apply(TableSnapshot snapshot, double now)
        {
            if (snapshot == null) return TableChange.None;

            if (!tables.TryGetValue(snapshot.TableId, out TableState state))
            {
                tables[snapshot.TableId] = new TableState { Snapshot = snapshot, LastHeardAt = now, IsFresh = true };
                MembershipVersion++;
                return TableChange.Added;
            }

            if (!state.IsFresh)
            {
                // Back after a silence. Take whatever it says now: its clock may have been reset while it was away.
                state.Snapshot = snapshot;
                state.LastHeardAt = now;
                state.IsFresh = true;
                MembershipVersion++;
                return TableChange.Added;
            }

            TableSnapshot previous = state.Snapshot;
            if (snapshot.EventTime != default && previous.EventTime != default && snapshot.EventTime < previous.EventTime)
            {
                return TableChange.None;
            }

            state.Snapshot = snapshot;
            state.LastHeardAt = now;
            if (AffectsMembership(previous, snapshot)) MembershipVersion++;

            return Classify(previous, snapshot);
        }

        // Call regularly. Marks tables that have gone quiet and forgets those quiet for a long time.
        public void Tick(double now)
        {
            List<Guid> forgotten = null;
            foreach (KeyValuePair<Guid, TableState> entry in tables)
            {
                double quietFor = now - entry.Value.LastHeardAt;
                if (entry.Value.IsFresh && quietFor > TimeoutSeconds)
                {
                    entry.Value.IsFresh = false;
                    MembershipVersion++;
                }
                else if (!entry.Value.IsFresh && quietFor > forgetAfterSeconds)
                {
                    (forgotten ??= new List<Guid>()).Add(entry.Key);
                }
            }

            if (forgotten != null)
            {
                foreach (Guid tableId in forgotten) tables.Remove(tableId);
            }
        }

        static bool AffectsMembership(TableSnapshot previous, TableSnapshot current)
        {
            return previous.GameType != current.GameType
                || previous.TableOpen != current.TableOpen
                || previous.MtdTableNumber != current.MtdTableNumber
                || previous.TableName != current.TableName
                || previous.TableNumber != current.TableNumber;
        }

        static TableChange Classify(TableSnapshot previous, TableSnapshot current)
        {
            IReadOnlyList<string> oldKeys = previous.ResultKeys;
            IReadOnlyList<string> newKeys = current.ResultKeys;

            if (!oldKeys.SequenceEqual(newKeys))
            {
                return IsOldListMovedDown(oldKeys, newKeys) ? TableChange.NewResult : TableChange.Corrected;
            }

            return previous.DisplaySignature == current.DisplaySignature ? TableChange.Heartbeat : TableChange.Updated;
        }

        // True when the new list is the old list with one or more results added in front, allowing for old results
        // falling off the end of a list that is at its size limit. The game ID is deliberately not consulted: the
        // shape of the list is enough, and it does not depend on DDS reporting the ID of the right game.
        static bool IsOldListMovedDown(IReadOnlyList<string> oldKeys, IReadOnlyList<string> newKeys)
        {
            if (newKeys.Count == 0) return false;
            if (oldKeys.Count == 0) return true;
            if (newKeys.Count < oldKeys.Count) return false; // Shorter means results were removed.

            for (int added = 1; added < newKeys.Count; added++)
            {
                int overlap = newKeys.Count - added;
                if (overlap > oldKeys.Count) continue;

                bool matches = true;
                for (int i = 0; i < overlap && matches; i++)
                {
                    matches = newKeys[added + i] == oldKeys[i];
                }

                if (matches) return true;
            }

            return false;
        }
    }
}
