using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MTD.Tests
{
    public class TableSelectionTests
    {
        static readonly Func<TableSnapshot, bool> showEverything = table => true;

        static TableRegistry RegistryWith(params MtdTableSummaryMessage[] messages)
        {
            TableRegistry registry = new TableRegistry(35);
            foreach (MtdTableSummaryMessage message in messages)
            {
                registry.Apply(SnapshotParser.Parse(message), 0);
            }

            return registry;
        }

        static MtdTableSummaryMessage Table(int tableNumber, string mtdNumber, string name = "AR", bool open = true)
        {
            MtdTableSummaryMessage message = TestSamples.Roulette(tableNumber, "17");
            message.MTDTableNumber = mtdNumber;
            message.TableName = name;
            message.TableOpen = open;
            return message;
        }

        static int[] TableNumbers(TableRegistry registry, IEnumerable<Guid> tableIds)
        {
            return tableIds.Select(id =>
            {
                registry.TryGet(id, out TableState state);
                return state.Snapshot.TableNumber;
            }).ToArray();
        }

        [Test]
        public void Order_IsNumericByMtdNumber_NotAlphabetical()
        {
            TableRegistry registry = RegistryWith(Table(1, "10"), Table(2, "9"), Table(3, "100"), Table(4, "1"));

            List<Guid> ordered = TableSelection.GetEligibleOrdered(registry.Tables, new DiscoveryTableListSource(), showEverything, true);

            CollectionAssert.AreEqual(new[] { 4, 2, 1, 3 }, TableNumbers(registry, ordered));
        }

        [Test]
        public void Order_PutsNonNumericMtdNumbersAfterNumbers()
        {
            TableRegistry registry = RegistryWith(Table(1, "B2"), Table(2, "7"), Table(3, "a1"), Table(4, ""));

            List<Guid> ordered = TableSelection.GetEligibleOrdered(registry.Tables, new DiscoveryTableListSource(), showEverything, true);

            CollectionAssert.AreEqual(new[] { 2, 4, 3, 1 }, TableNumbers(registry, ordered));
        }

        [Test]
        public void Order_BreaksTiesByNameThenTableNumber()
        {
            TableRegistry registry = RegistryWith(Table(9, "5", "SB"), Table(8, "5", "AR"), Table(7, "5", "AR"));

            List<Guid> ordered = TableSelection.GetEligibleOrdered(registry.Tables, new DiscoveryTableListSource(), showEverything, true);

            CollectionAssert.AreEqual(new[] { 7, 8, 9 }, TableNumbers(registry, ordered));
        }

        [Test]
        public void ClosedTables_AreSkippedOnlyWhenAsked()
        {
            TableRegistry registry = RegistryWith(Table(1, "1"), Table(2, "2", open: false), Table(3, "3"));

            List<Guid> skipping = TableSelection.GetEligibleOrdered(registry.Tables, new DiscoveryTableListSource(), showEverything, true);
            List<Guid> including = TableSelection.GetEligibleOrdered(registry.Tables, new DiscoveryTableListSource(), showEverything, false);

            CollectionAssert.AreEqual(new[] { 1, 3 }, TableNumbers(registry, skipping));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, TableNumbers(registry, including));
        }

        [Test]
        public void StaleTables_AreNotEligible()
        {
            TableRegistry registry = RegistryWith(Table(1, "1"), Table(2, "2"));
            registry.Apply(SnapshotParser.Parse(Table(2, "2")), 30);
            registry.Tick(40); // Table 1 was last heard at 0, table 2 at 30.

            List<Guid> ordered = TableSelection.GetEligibleOrdered(registry.Tables, new DiscoveryTableListSource(), showEverything, true);

            CollectionAssert.AreEqual(new[] { 2 }, TableNumbers(registry, ordered));
        }

        [Test]
        public void TablesTheUiCannotDraw_AreNotEligible()
        {
            MtdTableSummaryMessage blackjack = TestSamples.ReadMessage("blackjack.json");
            TableRegistry registry = RegistryWith(Table(1, "1"), blackjack);

            List<Guid> ordered = TableSelection.GetEligibleOrdered(registry.Tables, new DiscoveryTableListSource(), table => table.Family == GameFamily.Roulette, true);

            CollectionAssert.AreEqual(new[] { 1 }, TableNumbers(registry, ordered));
        }

        [Test]
        public void TableIdList_AdmitsOnlyListedTables()
        {
            TableRegistry registry = RegistryWith(Table(1, "1"), Table(2, "2"), Table(3, "3"));
            TableIdListSource source = new TableIdListSource();

            Assert.IsFalse(source.HasList);
            Assert.AreEqual(0, TableSelection.GetEligibleOrdered(registry.Tables, source, showEverything, true).Count, "No list yet means no tables.");

            int version = source.Version;
            source.SetTables(new[] { TestSamples.TableId(3), TestSamples.TableId(1), TestSamples.TableId(99), Guid.Empty });

            Assert.IsTrue(source.HasList);
            Assert.AreEqual(3, source.Count, "The empty ID is dropped.");
            Assert.Greater(source.Version, version);
            CollectionAssert.AreEqual(new[] { 1, 3 }, TableNumbers(registry, TableSelection.GetEligibleOrdered(registry.Tables, source, showEverything, true)));
        }

        [Test]
        public void DiscoveryWithFilter_AdmitsOnlyMatchingMtdNumbers()
        {
            TableRegistry registry = RegistryWith(Table(1, "1"), Table(2, "6"), Table(3, "7"), Table(4, "19"), Table(5, "A1"));
            DiscoveryTableListSource source = new DiscoveryTableListSource(MtdNumberFilter.Parse("1-6, 19"));

            CollectionAssert.AreEqual(new[] { 1, 2, 4 }, TableNumbers(registry, TableSelection.GetEligibleOrdered(registry.Tables, source, showEverything, true)));
        }

        [TestCase("", "5", true)]
        [TestCase("   ", "anything", true)]
        [TestCase(null, "", true)]
        [TestCase("1-6, 19", "1", true)]
        [TestCase("1-6, 19", "6", true)]
        [TestCase("1-6, 19", "7", false)]
        [TestCase("1-6, 19", "19", true)]
        [TestCase("1-6, 19", " 19 ", true)]
        [TestCase("1-6, 19", "", false)]
        [TestCase("6-1", "3", true)]
        [TestCase("3;5", "5", true)]
        [TestCase("3;5", "4", false)]
        [TestCase("A1, 7", "a1", true)]
        [TestCase("A1, 7", "A2", false)]
        [TestCase("VIP-1", "VIP-1", true)]
        [TestCase("1-6,,", "2", true)]
        public void MtdNumberFilter_Matches(string filterText, string mtdNumber, bool expected)
        {
            Assert.AreEqual(expected, MtdNumberFilter.Parse(filterText).Matches(mtdNumber));
        }
    }
}
