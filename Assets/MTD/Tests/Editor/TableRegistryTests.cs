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

        static readonly Guid gameA = new Guid("aaaaaaaa-0000-4000-8000-00000000000a");
        static readonly Guid gameB = new Guid("bbbbbbbb-0000-4000-8000-00000000000b");

        // The same, with the game ID of each message chosen by the test. Guid.Empty stands for "DDS sent none".
        static TableChange ChangeBetween(Guid oldGame, string[] oldResults, Guid newGame, string[] newResults)
        {
            MtdTableSummaryMessage oldMessage = TestSamples.Roulette(1, oldResults);
            oldMessage.GameId = oldGame;
            oldMessage.EventTime = t0;
            MtdTableSummaryMessage newMessage = TestSamples.Roulette(1, newResults);
            newMessage.GameId = newGame;
            newMessage.EventTime = t0.AddSeconds(1);

            TableRegistry registry = new TableRegistry(timeout);
            registry.Apply(SnapshotParser.Parse(oldMessage), 0);
            return registry.Apply(SnapshotParser.Parse(newMessage), 1);
        }

        // A result is edited, and the same number happens to appear further down. Read as "two results added" the
        // lists also line up, which is how this used to be mistaken for a new spin.
        [Test]
        public void EditedTopResult_WithTheOldValueRepeatedFurtherDown_IsACorrection()
        {
            string[] before = { "7", "11", "7" };
            string[] after = { "9", "11", "7" };

            Assert.AreEqual(TableChange.Corrected, ChangeBetween(gameA, before, gameA, after), "Same game.");
            Assert.AreEqual(TableChange.Corrected, ChangeBetween(gameA, before, gameB, after), "One edit explains it better than two additions, whatever the game ID says.");
            Assert.AreEqual(TableChange.Corrected, ChangeBetween(Guid.Empty, before, Guid.Empty, after), "No game IDs.");
        }

        // One edit and one addition both explain these lists equally well. Only here does the game ID decide.
        [Test]
        public void WhenTheListsCanBeReadEitherWay_TheGameIdDecides()
        {
            string[] before = { "7", "7", "7" };
            string[] after = { "9", "7", "7" };

            Assert.AreEqual(TableChange.Corrected, ChangeBetween(gameA, before, gameA, after), "The same game cannot have produced a new result.");
            Assert.AreEqual(TableChange.NewResult, ChangeBetween(gameA, before, gameB, after));
            Assert.AreEqual(TableChange.NewResult, ChangeBetween(Guid.Empty, before, Guid.Empty, after), "Without game IDs, a new result is far the likelier.");
        }

        [Test]
        public void HistoryMovedDown_IsANewResult_EvenIfThePublisherRepeatsTheGameId()
        {
            Assert.AreEqual(TableChange.NewResult, ChangeBetween(gameA, new[] { "17", "4", "9" }, gameA, new[] { "32", "17", "4" }));
            Assert.AreEqual(TableChange.NewResult, ChangeBetween(gameA, new[] { "17", "4" }, gameA, new[] { "32", "17", "4" }));
        }

        [Test]
        public void ListHoldingOnlyTheCurrentResult_FollowsTheGameId()
        {
            Assert.AreEqual(TableChange.NewResult, ChangeBetween(gameA, new[] { "7" }, gameB, new[] { "9" }));
            Assert.AreEqual(TableChange.Corrected, ChangeBetween(gameA, new[] { "7" }, gameA, new[] { "9" }));
            Assert.AreEqual(TableChange.Corrected, ChangeBetween(Guid.Empty, new[] { "7" }, Guid.Empty, new[] { "9" }));
        }

        [Test]
        public void UnreadableNewResult_IsANewResult_AndItsRepairIsACorrection()
        {
            TableRegistry registry = new TableRegistry(timeout);
            registry.Apply(Snapshot(1, t0, "24", "5"), 0);

            MtdTableSummaryMessage unreadable = TestSamples.Roulette(1, "bad", "24", "5");
            unreadable.EventTime = t0.AddSeconds(1);
            Assert.AreEqual(TableChange.NewResult, registry.Apply(SnapshotParser.Parse(unreadable), 1));

            Assert.IsTrue(registry.TryGet(TestSamples.TableId(1), out TableState state));
            Assert.IsNull(((RouletteSnapshot)state.Snapshot).Current, "The table has a new result MTD cannot show; 24 is no longer the latest.");

            MtdTableSummaryMessage repaired = TestSamples.Roulette(1, "17", "24", "5");
            repaired.GameId = unreadable.GameId;
            repaired.EventTime = t0.AddSeconds(2);
            Assert.AreEqual(TableChange.Corrected, registry.Apply(SnapshotParser.Parse(repaired), 2));
        }

        // A row MTD cannot read ("bad") is not drawn, but it still counts as a row when two lists are compared. When
        // it was dropped, every result below it sat one place too high, and that reversed the answer both ways.
        [Test]
        public void NewResult_AboveARowMtdCannotRead_IsANewResult()
        {
            string[] before = { "bad", "24", "5" };
            string[] after = { "17", "bad", "24", "5" };

            Assert.AreEqual(TableChange.NewResult, ChangeBetween(gameA, before, gameB, after), "The reported case: a new spin with a new game ID.");
            Assert.AreEqual(TableChange.NewResult, ChangeBetween(gameA, before, gameA, after));
            Assert.AreEqual(TableChange.NewResult, ChangeBetween(Guid.Empty, before, Guid.Empty, after));

            // The same spin on a list that is at its size limit, so the oldest result drops off the end.
            Assert.AreEqual(TableChange.NewResult, ChangeBetween(gameA, before, gameB, new[] { "17", "bad", "24" }));
        }

        [Test]
        public void NewResult_AboveAnUnreadableHistoryRow_IsANewResult()
        {
            Assert.AreEqual(TableChange.NewResult, ChangeBetween(gameA, new[] { "7", "bad", "11" }, gameB, new[] { "9", "7", "bad", "11" }));
            Assert.AreEqual(TableChange.NewResult, ChangeBetween(gameA, new[] { "7", "bad", "11" }, gameB, new[] { "7", "7", "bad", "11" }), "A repeat of the number on top.");
        }

        [Test]
        public void UnreadableHistoryRow_Repaired_IsACorrection()
        {
            string[] before = { "7", "bad", "11" };
            string[] after = { "7", "7", "11" };

            Assert.AreEqual(TableChange.Corrected, ChangeBetween(gameA, before, gameA, after), "The reported case: repaired in the same round.");
            Assert.AreEqual(TableChange.Corrected, ChangeBetween(gameA, before, gameB, after));
            Assert.AreEqual(TableChange.Corrected, ChangeBetween(Guid.Empty, before, Guid.Empty, after));
        }

        [Test]
        public void HistoryRow_BecomingUnreadable_IsACorrection()
        {
            Assert.AreEqual(TableChange.Corrected, ChangeBetween(gameA, new[] { "7", "7", "11" }, gameA, new[] { "7", "bad", "11" }));
            Assert.AreEqual(TableChange.Corrected, ChangeBetween(gameA, new[] { "7", "bad", "11" }, gameA, new[] { "7", "worse", "11" }), "Still unreadable, but not the same row as before.");
        }

        [Test]
        public void UnreadableHistoryRow_SentAgainUnchanged_IsAHeartbeat()
        {
            Assert.AreEqual(TableChange.Heartbeat, ChangeBetween(new[] { "7", "bad", "11" }, new[] { "7", "bad", "11" }));
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

            message.MTDTableUniqueName = "Salon East";
            Assert.AreEqual(TableChange.Updated, registry.Apply(SnapshotParser.Parse(message), 4));
            Assert.AreEqual(version, registry.MembershipVersion);
        }
    }
}
