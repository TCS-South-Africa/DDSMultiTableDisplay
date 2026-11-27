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
        NewResult, // One or more results were added at the top of the list. The newest may be one MTD cannot read.
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
                || previous.TableOpen != current.TableOpen;
        }

        static TableChange Classify(TableSnapshot previous, TableSnapshot current)
        {
            if (!previous.ResultKeys.SequenceEqual(current.ResultKeys)) return ClassifyResultChange(previous, current);

            return previous.DisplaySignature == current.DisplaySignature ? TableChange.Heartbeat : TableChange.Updated;
        }

        // The result list changed: was a result added, or was the list edited?
        //
        // Two readings are weighed. "Added": the old list reappears some places further down the new one, so that
        // many results are new. "Edited": the lists are the same length and differ in some places, so that many
        // results were changed where they stood. Whichever reading needs fewer changes wins. [7, 11, 7] becoming
        // [9, 11, 7] is one edit or two additions, so it is a correction; a full history moving down one place is
        // one addition or a list of edits, so it is a new result.
        //
        // The game ID only settles a draw (repeated values at the top of the list can make both readings cost the
        // same): the same game cannot have produced a new result. Deciding by shape first means a publisher that
        // reports the wrong game's ID still cannot make a correction animate, or stop a new result from animating.
        static TableChange ClassifyResultChange(TableSnapshot previous, TableSnapshot current)
        {
            IReadOnlyList<string> oldKeys = previous.ResultKeys;
            IReadOnlyList<string> newKeys = current.ResultKeys;

            if (newKeys.Count < oldKeys.Count) return TableChange.Corrected; // Results were removed, or the history was cleared.
            if (oldKeys.Count == 0) return TableChange.NewResult;

            bool gameIdsKnown = previous.GameId != Guid.Empty && current.GameId != Guid.Empty;
            bool sameGame = gameIdsKnown && previous.GameId == current.GameId;
            bool newGame = gameIdsKnown && previous.GameId != current.GameId;

            // With a new game, every result having been replaced is still a possible "added" reading: on a list
            // that holds only the current result, that is what each new result looks like.
            int added = CountAddedInFront(oldKeys, newKeys, allowNoOverlap: newGame);
            if (added == 0) return TableChange.Corrected;

            if (newKeys.Count != oldKeys.Count) return TableChange.NewResult; // The list grew, so "edited" is not a reading.

            int edited = 0;
            for (int i = 0; i < newKeys.Count; i++)
            {
                if (newKeys[i] != oldKeys[i]) edited++;
            }

            if (added != edited) return added < edited ? TableChange.NewResult : TableChange.Corrected;
            return sameGame ? TableChange.Corrected : TableChange.NewResult;
        }

        // How many results were put in front of the old list to make the new one, allowing for old results falling
        // off the end of a list that is at its size limit. The smallest such number, or 0 when the old list does not
        // reappear further down. The new list is never shorter than the old one here, and neither is empty.
        internal static int CountAddedInFront(IReadOnlyList<string> oldKeys, IReadOnlyList<string> newKeys, bool allowNoOverlap)
        {
            for (int added = 1; added < newKeys.Count; added++)
            {
                int overlap = newKeys.Count - added;
                if (overlap > oldKeys.Count) continue;

                bool matches = true;
                for (int i = 0; i < overlap && matches; i++)
                {
                    matches = newKeys[added + i] == oldKeys[i];
                }

                if (matches) return added;
            }

            return allowNoOverlap ? newKeys.Count : 0;
        }
    }
}
