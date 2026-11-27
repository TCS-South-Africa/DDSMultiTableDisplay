using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MTD.Tests
{
    public class SummaryParsingTests
    {
        [Test]
        public void LegacyNumberOnAV1SummaryCannotIdentifyOrLabelTheTable()
        {
            Guid id = TestSamples.TableId(1);
            string json = $"{{\"SchemaVersion\":1,\"TableId\":\"{id}\",\"GameType\":\"ROULETTE\",\"TableName\":\"AR\",\"TableNumber\":1,\"MTDTableNumber\":\"987\"}}";
            var table = SummaryReader.Read(MtdTopics.TableSummary("test", id), TestSamples.Bytes(json), out _, out string error);
            Assert.IsNull(error);
            Assert.AreEqual(id, table.TableId);
            Assert.IsEmpty(table.MtdTableUniqueName);
            Assert.AreEqual("AR 01", table.DisplayLabel(true));
        }

        [Test]
        public void StandardRoulette_ParsesIdentityLimitsAndResults()
        {
            RouletteSnapshot snapshot = (RouletteSnapshot)TestSamples.ReadSnapshot("roulette-standard.json");

            Assert.AreEqual(TestSamples.TableId(1), snapshot.TableId);
            Assert.AreEqual(new Guid("aaaaaaaa-0000-4000-8000-000000000001"), snapshot.GameId);
            Assert.AreEqual(GameFamily.Roulette, snapshot.Family);
            Assert.AreEqual(RouletteVariant.Standard, snapshot.Variant);
            Assert.AreEqual("AR", snapshot.TableName);
            Assert.AreEqual(1, snapshot.TableNumber);
            Assert.AreEqual("Salon 01", snapshot.MtdTableUniqueName);
            Assert.AreEqual(new Color32(0x0B, 0x4F, 0x8A, 255), snapshot.Colour.Value);
            Assert.IsTrue(snapshot.TableOpen);
            Assert.AreEqual(1000, snapshot.MinBet);
            Assert.AreEqual(1000000, snapshot.MaxBet);
            Assert.AreEqual("$10", snapshot.MinBetText);
            Assert.AreEqual("$10,000", snapshot.MaxBetText);
            Assert.AreEqual(new DateTimeOffset(2026, 11, 24, 10, 15, 30, 123, TimeSpan.Zero), snapshot.EventTime);

            Assert.AreEqual(12, snapshot.AllResults().Count);
            Assert.AreEqual("36", snapshot.AllResults()[0].Pocket.Label);
            Assert.AreEqual(RouletteColour.Red, snapshot.AllResults()[0].Pocket.Colour);
            Assert.AreEqual(RouletteColour.Black, snapshot.AllResults()[2].Pocket.Colour); // 35
            Assert.AreEqual(RouletteColour.Green, snapshot.AllResults()[8].Pocket.Colour); // 0
            Assert.IsFalse(snapshot.AllResults().Any(result => result.HasMultiplier));

            CollectionAssert.AreEqual(new[] { "35", "14", "8", "22" }, snapshot.HotNumbers.Select(pocket => pocket.Label));
            CollectionAssert.AreEqual(new[] { "2", "17", "29", "00" }, snapshot.ColdNumbers.Select(pocket => pocket.Label));
            Assert.AreEqual(RouletteColour.Green, snapshot.ColdNumbers[3].Colour);

            Assert.AreEqual(200, snapshot.GetStatistic("SampleSize"));
            Assert.AreEqual(92, snapshot.GetStatistic("Red"));
            Assert.AreEqual(-1, snapshot.GetStatistic("NotSent", -1));
        }

        [Test]
        public void RouletteXtra_CarriesMultiplierOnlyOnBonusResults()
        {
            RouletteSnapshot snapshot = (RouletteSnapshot)TestSamples.ReadSnapshot("roulette-xtra.json");

            Assert.AreEqual(RouletteVariant.Xtra, snapshot.Variant);
            Assert.AreEqual(2, snapshot.AllResults().Count(result => result.HasMultiplier));

            RouletteResult seven = snapshot.AllResults()[6];
            Assert.AreEqual("7", seven.Pocket.Label);
            Assert.AreEqual(100, seven.Multiplier);
            Assert.AreEqual(new Color32(0xC9, 0xA2, 0x27, 255), seven.MultiplierColour.Value);

            Assert.AreEqual("00", snapshot.AllResults()[8].Pocket.Label);
            Assert.IsTrue(snapshot.AllResults()[8].Pocket.IsZero);
        }

        [Test]
        public void PowerReel_CarriesMultiplierOnEveryResult()
        {
            RouletteSnapshot snapshot = (RouletteSnapshot)TestSamples.ReadSnapshot("roulette-power-reel.json");

            Assert.AreEqual(RouletteVariant.PowerReel, snapshot.Variant);
            Assert.AreEqual(5, snapshot.AllResults().Count);
            Assert.IsTrue(snapshot.AllResults().All(result => result.HasMultiplier));
            CollectionAssert.AreEqual(new[] { 50, 30, 20, 17, 60 }, snapshot.AllResults().Select(result => result.Multiplier));
        }

        [Test]
        public void SicBo_DerivesTotalAndOutcomeFromDice()
        {
            SicBoSnapshot snapshot = (SicBoSnapshot)TestSamples.ReadSnapshot("sicbo.json");

            Assert.AreEqual(GameFamily.SicBo, snapshot.Family);
            Assert.AreEqual("SB", snapshot.TableName);
            Assert.AreEqual(6, snapshot.AllResults().Count);

            Assert.AreEqual(15, snapshot.AllResults()[0].Total);
            Assert.AreEqual(SicBoOutcome.Big, snapshot.AllResults()[0].Outcome);
            Assert.AreEqual(9, snapshot.AllResults()[1].Total);
            Assert.AreEqual(SicBoOutcome.Small, snapshot.AllResults()[1].Outcome);
            Assert.AreEqual(SicBoOutcome.Triple, snapshot.AllResults()[2].Outcome);

            CollectionAssert.AreEqual(new[] { 10, 11, 9 }, snapshot.HotTotals);
            CollectionAssert.AreEqual(new[] { 4, 17, 5 }, snapshot.ColdTotals);
            Assert.AreEqual(98, snapshot.GetStatistic("Big"));
        }

        [Test]
        public void SicBo_SortsDiceThatArriveUnsorted()
        {
            Assert.IsTrue(SicBoResult.TryParse("6-1-4", out SicBoResult result));
            Assert.AreEqual("1-4-6", result.ToString());
        }

        [Test]
        public void SicBo_TotalOfTenIsSmallAndElevenIsBig()
        {
            Assert.IsTrue(SicBoResult.TryParse("1-3-6", out SicBoResult ten));
            Assert.IsTrue(SicBoResult.TryParse("1-4-6", out SicBoResult eleven));
            Assert.AreEqual(SicBoOutcome.Small, ten.Outcome);
            Assert.AreEqual(SicBoOutcome.Big, eleven.Outcome);
        }

        [Test]
        public void Blackjack_IsRecognisedButUnsupported()
        {
            TableSnapshot snapshot = TestSamples.ReadSnapshot("blackjack.json");

            Assert.IsInstanceOf<UnsupportedSnapshot>(snapshot);
            Assert.AreEqual(GameFamily.Blackjack, snapshot.Family);
            Assert.AreEqual("BJ", snapshot.TableName);
        }

        [Test]
        public void UnknownGameAndUnknownFields_ParseWithoutError()
        {
            TableSnapshot snapshot = TestSamples.ReadSnapshot("future-version.json");

            Assert.IsInstanceOf<UnsupportedSnapshot>(snapshot);
            Assert.AreEqual(GameFamily.Unsupported, snapshot.Family);
            Assert.AreEqual("SOMETHING_NEW", snapshot.GameType);
            Assert.IsNull(snapshot.Colour);
            Assert.IsTrue(snapshot.TableOpen, "A missing TableOpen means open.");
        }

        [TestCase("ROULETTE", GameFamily.Roulette, RouletteVariant.Standard)]
        [TestCase("ROULETTE_XTRA", GameFamily.Roulette, RouletteVariant.Xtra)]
        [TestCase("ROULETTE_POWER", GameFamily.Roulette, RouletteVariant.PowerReel)]
        [TestCase("ROULETTE_SPREAD", GameFamily.Roulette, RouletteVariant.Standard)]
        [TestCase("PARIMAX_ROULETTE", GameFamily.Roulette, RouletteVariant.Standard)]
        [TestCase("sicbo", GameFamily.SicBo, RouletteVariant.Standard)]
        [TestCase("BLACKJACK", GameFamily.Blackjack, RouletteVariant.Standard)]
        [TestCase("BACCARAT", GameFamily.Unsupported, RouletteVariant.Standard)]
        [TestCase("HEADCRACK", GameFamily.Unsupported, RouletteVariant.Standard)]
        [TestCase(null, GameFamily.Unsupported, RouletteVariant.Standard)]
        public void GameType_MapsToFamilyAndVariant(string gameType, GameFamily expectedFamily, RouletteVariant expectedVariant)
        {
            Assert.AreEqual(expectedFamily, GameTypes.GetFamily(gameType, out RouletteVariant variant));
            Assert.AreEqual(expectedVariant, variant);
        }

        [TestCase("0", RouletteColour.Green)]
        [TestCase("00", RouletteColour.Green)]
        [TestCase("000", RouletteColour.Green)]
        [TestCase("0000", RouletteColour.Green)]
        [TestCase("1", RouletteColour.Red)]
        [TestCase("2", RouletteColour.Black)]
        [TestCase("10", RouletteColour.Black)]
        [TestCase("11", RouletteColour.Black)]
        [TestCase("18", RouletteColour.Red)]
        [TestCase("19", RouletteColour.Red)]
        [TestCase("28", RouletteColour.Black)]
        [TestCase("36", RouletteColour.Red)]
        public void RoulettePocket_ParsesWithColour(string label, RouletteColour expected)
        {
            Assert.IsTrue(RoulettePocket.TryParse(label, out RoulettePocket pocket));
            Assert.AreEqual(label, pocket.Label);
            Assert.AreEqual(expected, pocket.Colour);
        }

        // 37 is DDS's internal number for "00" and 40 its dealer-change marker. Neither may reach a widget.
        [TestCase("37")]
        [TestCase("40")]
        [TestCase("-1")]
        [TestCase("07")]
        [TestCase("1.0")]
        [TestCase("")]
        [TestCase(null)]
        [TestCase("red")]
        public void RoulettePocket_RejectsAnythingThatIsNotAPocket(string label)
        {
            Assert.IsFalse(RoulettePocket.TryParse(label, out _));
        }

        [TestCase("7-7-7")] // DDS's "invalid result" value, written the way a careless publisher might send it.
        [TestCase("1-2")]
        [TestCase("1-2-3-4")]
        [TestCase("0-1-2")]
        [TestCase("144")]
        [TestCase("")]
        public void SicBoResult_RejectsAnythingThatIsNotThreeDice(string text)
        {
            Assert.IsFalse(SicBoResult.TryParse(text, out _));
        }

        [Test]
        public void BadEntries_AreDroppedWithoutLosingTheRest()
        {
            MtdTableSummaryMessage message = TestSamples.Roulette(3, "17", "40", "banana", "00", null);
            message.HotNumbers = new System.Collections.Generic.List<string> { "5", "99", "12" };
            message.MTDColourSetting = "#2704700"; // Seven digits, as in the original request's example.

            RouletteSnapshot snapshot = (RouletteSnapshot)SnapshotParser.Parse(message);

            CollectionAssert.AreEqual(new[] { "17", "00" }, snapshot.AllResults().Select(result => result.Pocket.Label));
            CollectionAssert.AreEqual(new[] { "5", "12" }, snapshot.HotNumbers.Select(pocket => pocket.Label));
            Assert.IsNull(snapshot.Colour);
        }

        [Test]
        public void MultiplierOfZeroOrLess_MeansNoMultiplier()
        {
            MtdTableSummaryMessage message = TestSamples.Roulette(3, "17", "18");
            message.Results[0].Multiplier = 0;
            message.Results[0].MultiplierColour = "#FFFFFF";
            message.Results[1].Multiplier = -5;

            RouletteSnapshot snapshot = (RouletteSnapshot)SnapshotParser.Parse(message);

            Assert.IsFalse(snapshot.AllResults()[0].HasMultiplier);
            Assert.IsNull(snapshot.AllResults()[0].MultiplierColour);
            Assert.IsFalse(snapshot.AllResults()[1].HasMultiplier);
        }

        [Test]
        public void MissingLists_GiveEmptySnapshotLists()
        {
            MtdTableSummaryMessage message = TestSamples.Roulette(3);
            message.Results = null;

            RouletteSnapshot snapshot = (RouletteSnapshot)SnapshotParser.Parse(message);

            Assert.AreEqual(0, snapshot.AllResults().Count);
            Assert.AreEqual(0, snapshot.HotNumbers.Count);
            Assert.AreEqual(0, snapshot.ColdNumbers.Count);
            Assert.AreEqual(0, snapshot.Statistics.Count);
        }

        [Test]
        public void UnreadableFirstRouletteRow_LeavesNoCurrentResult_InsteadOfPromotingAnOlderOne()
        {
            RouletteSnapshot snapshot = (RouletteSnapshot)SnapshotParser.Parse(TestSamples.Roulette(3, "bad", "24", "5"));

            Assert.IsNull(snapshot.Current, "24 is the result before the latest one. It must not be shown as the latest.");
            Assert.IsTrue(snapshot.CurrentResultUnreadable);
            Assert.AreEqual("bad", snapshot.UnreadableCurrentResult);
            CollectionAssert.AreEqual(new[] { "24", "5" }, snapshot.History.Select(result => result.Pocket.Label));
            CollectionAssert.AreEqual(new[] { "?bad", "24", "5" }, snapshot.ResultKeys);
        }

        [Test]
        public void UnreadableFirstSicBoRow_LeavesNoCurrentResult_InsteadOfPromotingAnOlderOne()
        {
            MtdTableSummaryMessage message = TestSamples.Roulette(3, "7-7-7", "1-2-3", "4-4-6");
            message.GameType = "SICBO";

            SicBoSnapshot snapshot = (SicBoSnapshot)SnapshotParser.Parse(message);

            Assert.IsNull(snapshot.Current);
            Assert.IsTrue(snapshot.CurrentResultUnreadable);
            CollectionAssert.AreEqual(new[] { 6, 14 }, snapshot.History.Select(result => result.Total));
        }

        // A widget does not draw a history row MTD cannot read, but the row is still one of the table's results. Its
        // key keeps its place, so that the registry compares each result with the right one in the next message.
        [Test]
        public void UnreadableHistoryRow_IsLeftOutOfTheHistory_ButKeepsItsPlaceAmongTheResultKeys()
        {
            MtdTableSummaryMessage message = TestSamples.Roulette(3, "17", "bad", "24", "5", "worse");
            message.Results[3] = null;

            RouletteSnapshot snapshot = (RouletteSnapshot)SnapshotParser.Parse(message);

            Assert.AreEqual("17", snapshot.Current.Value.Pocket.Label);
            Assert.IsFalse(snapshot.CurrentResultUnreadable);
            CollectionAssert.AreEqual(new[] { "24" }, snapshot.History.Select(result => result.Pocket.Label));
            CollectionAssert.AreEqual(new[] { "17", "?bad", "24", "?", "?worse" }, snapshot.ResultKeys);
        }

        [Test]
        public void UnreadableSicBoHistoryRow_IsLeftOutOfTheHistory_ButKeepsItsPlaceAmongTheResultKeys()
        {
            MtdTableSummaryMessage message = TestSamples.Roulette(3, "1-2-3", "7-7-7", "4-4-6");
            message.GameType = "SICBO";

            SicBoSnapshot snapshot = (SicBoSnapshot)SnapshotParser.Parse(message);

            Assert.AreEqual(6, snapshot.Current.Value.Total);
            CollectionAssert.AreEqual(new[] { 14 }, snapshot.History.Select(result => result.Total));
            Assert.AreEqual(3, snapshot.ResultKeys.Count);
            Assert.AreEqual("?7-7-7", snapshot.ResultKeys[1]);
        }

        [Test]
        public void UnreadableRows_AreComparedByTheirWholeText_NotTheShortenedOneShownOnScreen()
        {
            string longText = new string('x', 40);
            RouletteSnapshot first = (RouletteSnapshot)SnapshotParser.Parse(TestSamples.Roulette(3, longText + "1", "24"));
            RouletteSnapshot second = (RouletteSnapshot)SnapshotParser.Parse(TestSamples.Roulette(3, longText + "2", "24"));

            Assert.AreEqual(first.UnreadableCurrentResult, second.UnreadableCurrentResult, "What is shown is cut to a length that fits a widget.");
            Assert.AreNotEqual(first.ResultKeys[0], second.ResultKeys[0]);
        }

        [Test]
        public void MissingFirstRow_IsAnUnreadableCurrentResult()
        {
            MtdTableSummaryMessage message = TestSamples.Roulette(3, "17", "24");
            message.Results[0] = null;

            RouletteSnapshot snapshot = (RouletteSnapshot)SnapshotParser.Parse(message);

            Assert.IsNull(snapshot.Current);
            Assert.IsTrue(snapshot.CurrentResultUnreadable);
            Assert.AreEqual("", snapshot.UnreadableCurrentResult);
            Assert.AreEqual(1, snapshot.History.Count);
        }

        [Test]
        public void ReadableResults_SplitIntoCurrentAndHistory()
        {
            RouletteSnapshot snapshot = (RouletteSnapshot)SnapshotParser.Parse(TestSamples.Roulette(3, "17", "24", "5"));

            Assert.AreEqual("17", snapshot.Current.Value.Pocket.Label);
            Assert.IsFalse(snapshot.CurrentResultUnreadable);
            Assert.IsNull(snapshot.UnreadableCurrentResult);
            CollectionAssert.AreEqual(new[] { "24", "5" }, snapshot.History.Select(result => result.Pocket.Label));
            CollectionAssert.AreEqual(new[] { "17", "24", "5" }, snapshot.ResultKeys);
        }

        [Test]
        public void NoResultsAtAll_IsNotAnUnreadableResult()
        {
            RouletteSnapshot snapshot = (RouletteSnapshot)SnapshotParser.Parse(TestSamples.Roulette(3));

            Assert.IsNull(snapshot.Current);
            Assert.IsFalse(snapshot.CurrentResultUnreadable);
            Assert.AreEqual(0, snapshot.ResultKeys.Count);
        }

        [Test]
        public void Summary_OnItsOwnTablesTopic_IsRead_InBothTopicForms()
        {
            byte[] payload = TestSamples.Bytes(MtdJson.Serialize(TestSamples.Roulette(4, "17")));
            Guid tableId = TestSamples.TableId(4);

            TableSnapshot fromMqtt = SummaryReader.Read($"tcsjh/crown/table/{tableId}/mtdsummary", payload, out _, out string mqttProblem);
            TableSnapshot fromNats = SummaryReader.Read($"tcsjh.table.{tableId}.mtdsummary", payload, out _, out string natsProblem);

            Assert.IsNull(mqttProblem);
            Assert.IsNull(natsProblem);
            Assert.AreEqual(tableId, fromMqtt.TableId);
            Assert.AreEqual(tableId, fromNats.TableId);
        }

        [Test]
        public void Summary_NamingAnotherTable_IsIgnored()
        {
            // Sent on table 1's topic, but the payload claims to be table 2.
            byte[] payload = TestSamples.Bytes(MtdJson.Serialize(TestSamples.Roulette(2, "17")));

            TableSnapshot fromMqtt = SummaryReader.Read(TestSamples.Topic(1), payload, out _, out string mqttProblem);
            TableSnapshot fromNats = SummaryReader.Read(MtdTopics.ToNatsSubject(TestSamples.Topic(1)), payload, out _, out string natsProblem);

            Assert.IsNull(fromMqtt);
            Assert.IsNull(fromNats);
            StringAssert.Contains(TestSamples.TableId(2).ToString(), mqttProblem);
            StringAssert.Contains("not the table that topic belongs to", natsProblem);
        }

        [Test]
        public void Summary_NamingAnotherTable_CannotChangeThatTablesRegistryEntry()
        {
            TableRegistry registry = new TableRegistry(35);
            registry.Apply(SummaryReader.Read(TestSamples.Topic(2), TestSamples.Bytes(MtdJson.Serialize(TestSamples.Roulette(2, "17"))), out _, out _), 0);

            // Table 1's topic carries a message dressed up as table 2.
            TableSnapshot forged = SummaryReader.Read(TestSamples.Topic(1), TestSamples.Bytes(MtdJson.Serialize(TestSamples.Roulette(2, "36", "17"))), out _, out _);
            registry.Apply(forged, 1);

            Assert.IsTrue(registry.TryGet(TestSamples.TableId(2), out TableState state));
            CollectionAssert.AreEqual(new[] { "17" }, state.Snapshot.ResultKeys);
        }

        [TestCase("tcsjh/crown/table/11111111-0000-4000-8000-000000000001/gamesummary")]
        [TestCase("tcsjh/crown/table/not-a-guid/mtdsummary")]
        [TestCase("tcsjh/crown/table/00000000-0000-0000-0000-000000000000/mtdsummary")]
        [TestCase("other/crown/table/11111111-0000-4000-8000-000000000001/mtdsummary")]
        [TestCase("tcsjh/crown/11111111-0000-4000-8000-000000000001/mtdsummary")]
        [TestCase("mtdsummary")]
        [TestCase("")]
        [TestCase(null)]
        public void Message_NotOnASummaryTopic_IsIgnored(string topic)
        {
            byte[] payload = TestSamples.Bytes(MtdJson.Serialize(TestSamples.Roulette(1, "17")));

            Assert.IsFalse(MtdTopics.TryGetSummaryTableId(topic, out _));
            Assert.IsNull(SummaryReader.Read(topic, payload, out _, out string problem));
            Assert.IsNotEmpty(problem);
        }

        [Test]
        public void Summary_ThatCannotBeRead_IsIgnoredWithAReason()
        {
            Assert.IsNull(SummaryReader.Read(TestSamples.Topic(1), TestSamples.Bytes("{ not json"), out _, out string problem));
            StringAssert.Contains("unreadable", problem);
        }

        [Test]
        public void MessageWithoutTableId_IsRejected()
        {
            MtdTableSummaryMessage message = TestSamples.Roulette(3, "17");
            message.TableId = Guid.Empty;

            Assert.IsNull(SnapshotParser.Parse(message));
            Assert.IsNull(SnapshotParser.Parse(null));
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("null")]
        [TestCase("[1,2,3]")]
        [TestCase("{\"TableId\":\"not-a-guid\"}")]
        [TestCase("{\"TableNumber\":\"nineteen\"}")]
        public void UnusablePayloads_AreReportedNotThrown(string payload)
        {
            Assert.IsFalse(MtdJson.TryDeserialize(TestSamples.Bytes(payload), out MtdTableSummaryMessage message, out string error));
            Assert.IsNull(message);
            Assert.IsNotEmpty(error);
        }

        [TestCase("#102030", 0x10, 0x20, 0x30, 0xFF)]
        [TestCase("102030", 0x10, 0x20, 0x30, 0xFF)]
        [TestCase("#10203040", 0x10, 0x20, 0x30, 0x40)]
        [TestCase(" #abcdef ", 0xAB, 0xCD, 0xEF, 0xFF)]
        public void Colour_ParsesHex(string text, int r, int g, int b, int a)
        {
            Assert.IsTrue(MtdColour.TryParse(text, out Color32 colour));
            Assert.AreEqual(new Color32((byte)r, (byte)g, (byte)b, (byte)a), colour);
        }

        [TestCase("#12345")]
        [TestCase("#2704700")]
        [TestCase("#GGGGGG")]
        [TestCase("red")]
        [TestCase("")]
        [TestCase(null)]
        public void Colour_RejectsAnythingElse(string text)
        {
            Assert.IsFalse(MtdColour.TryParse(text, out _));
        }

        [Test]
        public void SerialisedMessage_RoundTrips()
        {
            MtdTableSummaryMessage original = TestSamples.ReadMessage("roulette-power-reel.json");

            Assert.IsTrue(MtdJson.TryDeserialize(TestSamples.Bytes(MtdJson.Serialize(original)), out MtdTableSummaryMessage copy, out string error), error);

            Assert.AreEqual(original.TableId, copy.TableId);
            Assert.AreEqual(original.EventTime, copy.EventTime);
            Assert.AreEqual(original.Results.Count, copy.Results.Count);
            Assert.AreEqual(original.Results[0].Multiplier, copy.Results[0].Multiplier);
            Assert.AreEqual(original.Statistics["Red"], copy.Statistics["Red"]);
        }
    }
}
