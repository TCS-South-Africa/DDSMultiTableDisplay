using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MTD.Tests
{
    public class TransportTests
    {
        [Test]
        public void TableSummaryFilter_MatchesDdsTopicForm()
        {
            Assert.AreEqual("tcsjh/crown/table/+/mtdsummary", MtdTopics.TableSummaryFilter("Crown"));
            Assert.AreEqual("tcsjh/testsite/table/+/mtdsummary", MtdTopics.TableSummaryFilter(""));
            Assert.AreEqual("tcsjh/testsite/table/+/mtdsummary", MtdTopics.TableSummaryFilter(null));

            Guid tableId = TestSamples.TableId(7);
            Assert.AreEqual($"tcsjh/crown/table/{tableId}/mtdsummary", MtdTopics.TableSummary(" CROWN ", tableId));
        }

        [Test]
        public void NatsSubject_DropsTheSiteNameAndTranslatesWildcards()
        {
            Assert.AreEqual("tcsjh.table.*.mtdsummary", MtdTopics.ToNatsSubject(MtdTopics.TableSummaryFilter("crown")));

            Guid tableId = TestSamples.TableId(7);
            Assert.AreEqual($"tcsjh.table.{tableId}.mtdsummary", MtdTopics.ToNatsSubject(MtdTopics.TableSummary("crown", tableId)));

            Assert.AreEqual("tcsjh.table.>", MtdTopics.ToNatsSubject("tcsjh/crown/table/#"));
            Assert.AreEqual("other.topic", MtdTopics.ToNatsSubject("other/topic"));
        }

        [Test]
        public void TableListKey_MatchesTheServersKey()
        {
            Guid displayId = new Guid("0f6f2f8e-3d0b-4b7b-9a2e-7c1d5e8f9a01");

            Assert.AreEqual("tcsjh.services.tconnect.display.0f6f2f8e-3d0b-4b7b-9a2e-7c1d5e8f9a01.table-list", MtdTopics.TableListKey(displayId));
        }

        [TestCase("tcsjh/site/table/+/mtdsummary", "tcsjh/site/table/abc/mtdsummary", true)]
        [TestCase("tcsjh/site/table/+/mtdsummary", "tcsjh/site/table/abc/gamesummary", false)]
        [TestCase("tcsjh/site/table/+/mtdsummary", "tcsjh/other/table/abc/mtdsummary", false)]
        [TestCase("tcsjh/site/table/+/mtdsummary", "tcsjh/site/table/abc/def/mtdsummary", false)]
        [TestCase("tcsjh/site/table/+/mtdsummary", "tcsjh/site/table/abc", false)]
        [TestCase("tcsjh/site/#", "tcsjh/site/table/abc/mtdsummary", true)]
        [TestCase("tcsjh/site/table/abc", "tcsjh/site/table/abc", true)]
        public void MqttFilter_Matches(string filter, string topic, bool expected)
        {
            Assert.AreEqual(expected, MtdTopics.MqttFilterMatches(filter, topic));
        }

        [TestCase("ROULETTE")]
        [TestCase("ROULETTE_XTRA")]
        [TestCase("ROULETTE_POWER")]
        [TestCase("SICBO")]
        public void SimulatedTable_BuildsASummaryThatFollowsTheContract(string gameType)
        {
            SimulatedTable table = new SimulatedTable(4, gameType, "AR", "#102030", 1000, 1000000, seed: 7);
            for (int i = 0; i < 50; i++) table.PlayGame();

            // Through JSON and back, exactly as a real message travels.
            byte[] payload = TestSamples.Bytes(MtdJson.Serialize(table.BuildSummary(DateTimeOffset.UtcNow)));
            Assert.IsTrue(MtdJson.TryDeserialize(payload, out MtdTableSummaryMessage message, out string error), error);
            TableSnapshot snapshot = SnapshotParser.Parse(message);

            Assert.AreEqual(20, message.Results.Count, "The contract sends at most 20 results.");
            Assert.AreEqual(20, snapshot.ResultKeys.Count, "Every simulated result must be one MTD can read.");
            Assert.LessOrEqual(message.HotNumbers.Count, 8);
            Assert.LessOrEqual(message.ColdNumbers.Count, 8);
            Assert.AreEqual("$10", snapshot.MinBetText);
            Assert.AreEqual("$10,000", snapshot.MaxBetText);

            int sampleSize = snapshot.GetStatistic("SampleSize");
            Assert.AreEqual(200, sampleSize);

            if (snapshot is RouletteSnapshot roulette)
            {
                Assert.AreEqual(sampleSize, roulette.GetStatistic("Red") + roulette.GetStatistic("Black") + roulette.GetStatistic("Zero"));
                Assert.AreEqual(sampleSize, roulette.GetStatistic("Odd") + roulette.GetStatistic("Even") + roulette.GetStatistic("Zero"));
                Assert.AreEqual(sampleSize, roulette.GetStatistic("Low") + roulette.GetStatistic("High") + roulette.GetStatistic("Zero"));
                Assert.AreEqual(sampleSize, roulette.GetStatistic("Dozen1") + roulette.GetStatistic("Dozen2") + roulette.GetStatistic("Dozen3") + roulette.GetStatistic("Zero"));
                Assert.AreEqual(sampleSize, roulette.GetStatistic("Column1") + roulette.GetStatistic("Column2") + roulette.GetStatistic("Column3") + roulette.GetStatistic("Zero"));
                Assert.AreEqual(8, roulette.HotNumbers.Count);

                bool everyResultHasMultiplier = roulette.AllResults().All(result => result.HasMultiplier);
                bool noResultHasMultiplier = roulette.AllResults().All(result => !result.HasMultiplier);
                if (roulette.Variant == RouletteVariant.PowerReel) Assert.IsTrue(everyResultHasMultiplier);
                if (roulette.Variant == RouletteVariant.Standard) Assert.IsTrue(noResultHasMultiplier);
            }
            else
            {
                SicBoSnapshot sicBo = (SicBoSnapshot)snapshot;
                Assert.AreEqual(sampleSize, sicBo.GetStatistic("Small") + sicBo.GetStatistic("Big") + sicBo.GetStatistic("Triple"));
                Assert.AreEqual(sampleSize, sicBo.GetStatistic("Odd") + sicBo.GetStatistic("Even") + sicBo.GetStatistic("Triple"));
                Assert.AreEqual(sampleSize * 3, Enumerable.Range(1, 6).Sum(face => sicBo.GetStatistic($"Face{face}")));
                Assert.AreEqual(8, sicBo.HotTotals.Count);
            }
        }

        [Test]
        public void SimulatedTable_NewGame_IsSeenAsANewResult()
        {
            SimulatedTable table = new SimulatedTable(1, "ROULETTE", "AR", "#102030", 1000, 1000000, seed: 3);
            TableRegistry registry = new TableRegistry(35);
            registry.Apply(SnapshotParser.Parse(table.BuildSummary(DateTimeOffset.UtcNow)), 0);

            Assert.AreEqual(TableChange.Heartbeat, registry.Apply(SnapshotParser.Parse(table.BuildSummary(DateTimeOffset.UtcNow.AddSeconds(10))), 10));

            table.PlayGame();

            Assert.AreEqual(TableChange.NewResult, registry.Apply(SnapshotParser.Parse(table.BuildSummary(DateTimeOffset.UtcNow.AddSeconds(20))), 20));
        }

        [Test]
        public void MainThreadQueue_DeliversEveryMessageInArrivalOrder()
        {
            MainThreadQueue queue = new MainThreadQueue();
            queue.PostMessage("table-1", new byte[] { 1 });
            queue.PostMessage("table-2", new byte[] { 2 });
            queue.PostMessage("table-1", new byte[] { 3 });

            List<string> received = new List<string>();
            queue.Drain((topic, payload) => received.Add($"{topic}:{payload[0]}"));

            CollectionAssert.AreEqual(new[] { "table-1:1", "table-2:2", "table-1:3" }, received);
            Assert.AreEqual(0, queue.DroppedMessages);

            int secondDrain = 0;
            queue.Drain((topic, payload) => secondDrain++);
            Assert.AreEqual(0, secondDrain, "Messages are delivered once.");
        }

        [Test]
        public void MainThreadQueue_NotDrained_LetsGoOfTheOldestArrivals()
        {
            MainThreadQueue queue = new MainThreadQueue(messageLimit: 3);
            for (byte i = 1; i <= 5; i++) queue.PostMessage("table-1", new[] { i });

            List<byte> received = new List<byte>();
            queue.Drain((topic, payload) => received.Add(payload[0]));

            CollectionAssert.AreEqual(new byte[] { 3, 4, 5 }, received);
            Assert.AreEqual(2, queue.DroppedMessages);
        }

        // The case the queue used to get wrong: it kept the last arrival per topic, so when a table's newer summary
        // was overtaken by an older one inside a single frame, the registry was only ever shown the older one.
        [Test]
        public void NewerSummaryOvertakenByAnOlderOneInOneFrame_StillWins()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);
            MtdTableSummaryMessage older = TestSamples.Roulette(1, "17", "4");
            older.EventTime = now;
            MtdTableSummaryMessage newer = TestSamples.Roulette(1, "32", "17", "4");
            newer.EventTime = now.AddSeconds(1);

            TableRegistry registry = new TableRegistry(35);
            MainThreadQueue queue = new MainThreadQueue();
            queue.PostMessage(TestSamples.Topic(1), TestSamples.Bytes(MtdJson.Serialize(older)));
            queue.Drain((topic, payload) => registry.Apply(SummaryReader.Read(topic, payload, out _, out _), 0));

            // One frame: the newer summary arrives first, then a late copy of the older one.
            queue.PostMessage(TestSamples.Topic(1), TestSamples.Bytes(MtdJson.Serialize(newer)));
            queue.PostMessage(TestSamples.Topic(1), TestSamples.Bytes(MtdJson.Serialize(older)));

            List<TableChange> changes = new List<TableChange>();
            queue.Drain((topic, payload) => changes.Add(registry.Apply(SummaryReader.Read(topic, payload, out _, out _), 1)));

            CollectionAssert.AreEqual(new[] { TableChange.NewResult, TableChange.None }, changes);
            Assert.IsTrue(registry.TryGet(TestSamples.TableId(1), out TableState state));
            CollectionAssert.AreEqual(new[] { "32", "17", "4" }, state.Snapshot.ResultKeys);
        }

        [Test]
        public void MainThreadQueue_RunsActionsInOrderBeforeMessages()
        {
            MainThreadQueue queue = new MainThreadQueue();
            List<string> log = new List<string>();
            queue.PostMessage("table-1", new byte[] { 1 });
            queue.Post(() => log.Add("first"));
            queue.Post(() => log.Add("second"));

            queue.Drain((key, payload) => log.Add(key));

            CollectionAssert.AreEqual(new[] { "first", "second", "table-1" }, log);
        }
    }
}
