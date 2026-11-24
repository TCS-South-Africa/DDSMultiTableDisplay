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

                bool everyResultHasMultiplier = roulette.Results.All(result => result.HasMultiplier);
                bool noResultHasMultiplier = roulette.Results.All(result => !result.HasMultiplier);
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
        public void MainThreadQueue_KeepsOnlyTheNewestMessagePerKey()
        {
            MainThreadQueue queue = new MainThreadQueue();
            queue.PostLatest("table-1", new byte[] { 1 });
            queue.PostLatest("table-2", new byte[] { 2 });
            queue.PostLatest("table-1", new byte[] { 3 });

            Dictionary<string, byte> received = new Dictionary<string, byte>();
            queue.Drain((key, payload) => received[key] = payload[0]);

            Assert.AreEqual(2, received.Count);
            Assert.AreEqual(3, received["table-1"]);
            Assert.AreEqual(2, received["table-2"]);

            int secondDrain = 0;
            queue.Drain((key, payload) => secondDrain++);
            Assert.AreEqual(0, secondDrain, "Messages are delivered once.");
        }

        [Test]
        public void MainThreadQueue_RunsActionsInOrderBeforeMessages()
        {
            MainThreadQueue queue = new MainThreadQueue();
            List<string> log = new List<string>();
            queue.PostLatest("table-1", new byte[] { 1 });
            queue.Post(() => log.Add("first"));
            queue.Post(() => log.Add("second"));

            queue.Drain((key, payload) => log.Add(key));

            CollectionAssert.AreEqual(new[] { "first", "second", "table-1" }, log);
        }
    }
}
