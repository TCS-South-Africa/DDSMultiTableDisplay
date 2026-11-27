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
            message.MTDTableUniqueName = mtdNumber;
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

        static TableIdListSource Roster(params int[] ids)
        {
            var source = new TableIdListSource();
            source.SetTables(ids.Select(TestSamples.TableId));
            return source;
        }

        [Test]
        public void BothListSourcesUseGuidOrderEvenWhenDisplayNamesMatch()
        {
            var registry = RegistryWith(Table(1, "VIP"), Table(2, "VIP"), Table(3, ""));
            var source = Roster(2, 3, 2, 1);
            var eligible = TableSelection.GetEligibleOrdered(registry.Tables, source, showEverything, true);
            CollectionAssert.AreEqual(new[] { 2, 3, 1 }, TableNumbers(registry, eligible));
        }

        [Test]
        public void ClosedTables_AreSkippedOnlyWhenAsked()
        {
            TableRegistry registry = RegistryWith(Table(1, "1"), Table(2, "2", open: false), Table(3, "3"));

            List<Guid> skipping = TableSelection.GetEligibleOrdered(registry.Tables, Roster(1, 2, 3, 30), showEverything, true);
            List<Guid> including = TableSelection.GetEligibleOrdered(registry.Tables, Roster(1, 2, 3, 30), showEverything, false);

            CollectionAssert.AreEqual(new[] { 1, 3 }, TableNumbers(registry, skipping));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, TableNumbers(registry, including));
        }

        [Test]
        public void StaleTables_AreNotEligible()
        {
            TableRegistry registry = RegistryWith(Table(1, "1"), Table(2, "2"));
            registry.Apply(SnapshotParser.Parse(Table(2, "2")), 30);
            registry.Tick(40); // Table 1 was last heard at 0, table 2 at 30.

            List<Guid> ordered = TableSelection.GetEligibleOrdered(registry.Tables, Roster(1, 2, 3, 30), showEverything, true);

            CollectionAssert.AreEqual(new[] { 2 }, TableNumbers(registry, ordered));
        }

        [Test]
        public void TablesTheUiCannotDraw_AreNotEligible()
        {
            MtdTableSummaryMessage blackjack = TestSamples.ReadMessage("blackjack.json");
            TableRegistry registry = RegistryWith(Table(1, "1"), blackjack);

            List<Guid> ordered = TableSelection.GetEligibleOrdered(registry.Tables, Roster(1, 2, 3, 30), table => table.Family == GameFamily.Roulette, true);

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
            CollectionAssert.AreEqual(new[] { 3, 1 }, TableNumbers(registry, TableSelection.GetEligibleOrdered(registry.Tables, source, showEverything, true)));
        }

    }
}
