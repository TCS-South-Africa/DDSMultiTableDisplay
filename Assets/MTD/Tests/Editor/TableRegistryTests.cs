using System;
using System.Linq;
using NUnit.Framework;

namespace MTD.Tests
{
    public class TableRegistryTests
    {
        const double timeout = 35;

        static TableSnapshot Snapshot(int table, DateTimeOffset eventTime, params string[] results)
        {
            MtdTableSummaryMessage message = TestSamples.Roulette(table, results);
            message.EventTime = eventTime;
            return SnapshotParser.Parse(message);
        }

        static readonly DateTimeOffset t0 = new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);

        // Applies an "old" list then a "new" one and returns how the registry describes the difference.
        static TableChange ChangeBetween(string[] oldResults, string[] newResults)
        {
            TableRegistry registry = new TableRegistry(timeout);
            registry.Apply(Snapshot(1, t0, oldResults), 0);
            return registry.Apply(Snapshot(1, t0.AddSeconds(1), newResults), 1);
        }

        [Test]
        public void FirstSnapshot_IsAdded()
        {
            TableRegistry registry = new TableRegistry(timeout);

            Assert.AreEqual(TableChange.Added, registry.Apply(Snapshot(1, t0, "17"), 0));
            Assert.AreEqual(1, registry.FreshCount);
            Assert.IsTrue(registry.TryGet(TestSamples.TableId(1), out TableState state));
            Assert.IsTrue(state.IsFresh);
        }

        [Test]
        public void IdenticalSnapshot_IsAHeartbeat()
        {
            Assert.AreEqual(TableChange.Heartbeat, ChangeBetween(new[] { "17", "4" }, new[] { "17", "4" }));
        }

        [Test]
        public void ResultAddedInFront_IsANewResult()
        {
            Assert.AreEqual(TableChange.NewResult, ChangeBetween(new[] { "17", "4" }, new[] { "32", "17", "4" }));
        }

        [Test]
        public void FirstResultAfterAnEmptyHistory_IsANewResult()
        {
            Assert.AreEqual(TableChange.NewResult, ChangeBetween(new string[0], new[] { "32" }));
        }

        [Test]
        public void ResultAddedToAFullList_IsANewResult()
        {
            // The list is at its size limit, so the oldest result drops off as the new one arrives.
            Assert.AreEqual(TableChange.NewResult, ChangeBetween(new[] { "17", "4", "9" }, new[] { "32", "17", "4" }));
        }

        [Test]
        public void TwoResultsAddedBetweenMessages_IsStillANewResult()
        {
            Assert.AreEqual(TableChange.NewResult, ChangeBetween(new[] { "17", "4" }, new[] { "8", "32", "17", "4" }));
            Assert.AreEqual(TableChange.NewResult, ChangeBetween(new[] { "17", "4", "9" }, new[] { "8", "32", "17" }));
        }

        [Test]
        public void RepeatedNumber_IsANewResult()
        {
            Assert.AreEqual(TableChange.NewResult, ChangeBetween(new[] { "17", "4" }, new[] { "17", "17", "4" }));
        }

        [Test]
        public void CurrentResultEdited_IsACorrection()
        {
            Assert.AreEqual(TableChange.Corrected, ChangeBetween(new[] { "17", "4", "9" }, new[] { "18", "4", "9" }));
        }

        [Test]
        public void LastResultRemoved_IsACorrection()
        {
            Assert.AreEqual(TableChange.Corrected, ChangeBetween(new[] { "17", "4", "9" }, new[] { "4", "9" }));
        }

        [Test]
        public void HistoryCleared_IsACorrection()
        {
            Assert.AreEqual(TableChange.Corrected, ChangeBetween(new[] { "17", "4", "9" }, new string[0]));
        }

        [Test]
        public void MultiplierAddedToAnExistingResult_IsACorrection()
        {
            TableRegistry registry = new TableRegistry(timeout);
            MtdTableSummaryMessage message = TestSamples.Roulette(1, "17", "4");
            registry.Apply(SnapshotParser.Parse(message), 0);

            message.Results[0].Multiplier = 50;

            Assert.AreEqual(TableChange.Corrected, registry.Apply(SnapshotParser.Parse(message), 1));
        }

        [Test]
        public void SameResultsButDifferentLimits_IsAnUpdate()
        {
            TableRegistry registry = new TableRegistry(timeout);
            MtdTableSummaryMessage message = TestSamples.Roulette(1, "17", "4");
            registry.Apply(SnapshotParser.Parse(message), 0);

            message.MinBetText = "$25";

            Assert.AreEqual(TableChange.Updated, registry.Apply(SnapshotParser.Parse(message), 1));
        }

        [Test]
        public void SameResultsButDifferentStatistics_IsAnUpdate()
        {
            TableRegistry registry = new TableRegistry(timeout);
            MtdTableSummaryMessage message = TestSamples.Roulette(1, "17", "4");
            message.Statistics = new System.Collections.Generic.Dictionary<string, string> { ["Red"] = "10" };
            registry.Apply(SnapshotParser.Parse(message), 0);

            message.Statistics["Red"] = "11";

            Assert.AreEqual(TableChange.Updated, registry.Apply(SnapshotParser.Parse(message), 1));
        }

        [Test]
        public void ChangedGameIdAlone_IsNotANewResult()
        {
            // DDS makes a new game ID at every Place Your Bets. If it ever sends that ID with unchanged results,
            // MTD must not play a new-result animation.
            TableRegistry registry = new TableRegistry(timeout);
            MtdTableSummaryMessage message = TestSamples.Roulette(1, "17", "4");
            registry.Apply(SnapshotParser.Parse(message), 0);

            message.GameId = Guid.NewGuid();

            Assert.AreEqual(TableChange.Heartbeat, registry.Apply(SnapshotParser.Parse(message), 1));
        }

        [Test]
        public void OlderMessage_IsIgnored()
        {
            TableRegistry registry = new TableRegistry(timeout);
            registry.Apply(Snapshot(1, t0.AddSeconds(10), "32", "17"), 0);

            Assert.AreEqual(TableChange.None, registry.Apply(Snapshot(1, t0, "17"), 1));

            registry.TryGet(TestSamples.TableId(1), out TableState state);
            Assert.AreEqual(2, state.Snapshot.ResultKeys.Count, "The newer snapshot must be kept.");
        }

        [Test]
        public void MessageWithoutEventTime_IsAccepted()
        {
            TableRegistry registry = new TableRegistry(timeout);
            registry.Apply(Snapshot(1, t0, "17"), 0);

            Assert.AreEqual(TableChange.NewResult, registry.Apply(Snapshot(1, default, "32", "17"), 1));
        }

        [Test]
        public void QuietTable_GoesStaleAfterTheTimeout()
        {
            TableRegistry registry = new TableRegistry(timeout);
            registry.Apply(Snapshot(1, t0, "17"), 0);
            int version = registry.MembershipVersion;

            registry.Tick(timeout);
            Assert.AreEqual(1, registry.FreshCount, "Exactly at the timeout the table is still fresh.");
            Assert.AreEqual(version, registry.MembershipVersion);

            registry.Tick(timeout + 0.1);
            Assert.AreEqual(0, registry.FreshCount);
            Assert.Greater(registry.MembershipVersion, version);
        }

        [Test]
        public void HeartbeatKeepsATableFresh()
        {
            TableRegistry registry = new TableRegistry(timeout);
            for (int second = 0; second <= 300; second += 10)
            {
                registry.Apply(Snapshot(1, t0.AddSeconds(second), "17"), second);
                registry.Tick(second + 9);
            }

            Assert.AreEqual(1, registry.FreshCount);
        }

        [Test]
        public void StaleTable_ComesBackAsAdded_EvenWithAnEarlierClock()
        {
            TableRegistry registry = new TableRegistry(timeout);
            registry.Apply(Snapshot(1, t0.AddHours(1), "17"), 0);
            registry.Tick(100);
            int version = registry.MembershipVersion;

            // The table restarted and its clock now reads earlier than before.
            Assert.AreEqual(TableChange.Added, registry.Apply(Snapshot(1, t0, "5"), 101));
            Assert.AreEqual(1, registry.FreshCount);
            Assert.Greater(registry.MembershipVersion, version);
        }

        [Test]
        public void TableQuietForTenMinutes_IsForgotten()
        {
            TableRegistry registry = new TableRegistry(timeout);
            registry.Apply(Snapshot(1, t0, "17"), 0);

            registry.Tick(100);
            Assert.AreEqual(1, registry.Tables.Count());

            registry.Tick(601);
            Assert.AreEqual(0, registry.Tables.Count());
        }

        [Test]
        public void MembershipVersion_ChangesOnlyWhenEligibilityOrOrderCould()
        {
            TableRegistry registry = new TableRegistry(timeout);
            MtdTableSummaryMessage message = TestSamples.Roulette(1, "17");
            registry.Apply(SnapshotParser.Parse(message), 0);
            int version = registry.MembershipVersion;

            message.Results.Insert(0, new MtdResultMessage { Result = "5" });
            registry.Apply(SnapshotParser.Parse(message), 1);
            message.MinBetText = "$50";
            registry.Apply(SnapshotParser.Parse(message), 2);
            Assert.AreEqual(version, registry.MembershipVersion, "Results and limits do not affect which tables show.");

            message.TableOpen = false;
            registry.Apply(SnapshotParser.Parse(message), 3);
            Assert.Greater(registry.MembershipVersion, version);
            version = registry.MembershipVersion;

            message.MTDTableNumber = "42";
            registry.Apply(SnapshotParser.Parse(message), 4);
            Assert.Greater(registry.MembershipVersion, version);
        }
    }
}
