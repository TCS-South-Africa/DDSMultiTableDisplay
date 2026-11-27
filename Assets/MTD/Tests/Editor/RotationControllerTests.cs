using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MTD.Tests
{
    public class RotationControllerTests
    {
        const double settle = 12;
        const double dwell = 120;

        static RotationController Create(SwitchingMode mode, int slots)
        {
            return new RotationController(new RotationSettings
            {
                SlotCount = slots,
                Mode = mode,
                DwellSeconds = dwell,
                SettleSeconds = settle,
            });
        }

        static List<Guid> Tables(params int[] numbers)
        {
            return numbers.Select(TestSamples.TableId).ToList();
        }

        static List<Guid> Range(int first, int last)
        {
            return Enumerable.Range(first, last - first + 1).Select(TestSamples.TableId).ToList();
        }

        // The table number in each slot, 0 for an empty slot.
        static int[] Shown(RotationController controller)
        {
            return controller.Slots.Select(table => table.HasValue ? int.Parse(table.Value.ToString().Substring(24)) : 0).ToArray();
        }

        static void AssertShown(RotationController controller, params int[] expected)
        {
            CollectionAssert.AreEqual(expected, Shown(controller));
        }

        // Starts with the given tables and runs to the end of the settle window.
        static RotationController Settled(SwitchingMode mode, int slots, List<Guid> tables)
        {
            RotationController controller = Create(mode, slots);
            controller.SetEligible(tables, 0);
            controller.Tick(settle);
            return controller;
        }

        [Test]
        public void NoTables_ShowsNothing()
        {
            RotationController controller = Create(SwitchingMode.Pages, 6);
            controller.SetEligible(new List<Guid>(), 0);
            controller.Tick(1000);

            AssertShown(controller, 0, 0, 0, 0, 0, 0);
        }

        [TestCase(SwitchingMode.Pages)]
        [TestCase(SwitchingMode.SlotBySlot)]
        public void PresentationIsRaisedAfterFirstAssignmentsAndOnlyOnDifferentWholePages(SwitchingMode mode)
        {
            var rotation = Create(mode, 4);
            var pages = new List<int[]>();
            rotation.PagePresented += () => pages.Add(Shown(rotation));
            rotation.SetEligible(Tables(), 0);
            rotation.SetEligible(Tables(1), 1);
            rotation.SetEligible(Range(1, 4), 2);
            rotation.Tick(1 + settle);
            rotation.Tick(1 + settle + dwell);
            Assert.That(pages.Count, Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { 1,0,0,0 }, pages[0]);
            rotation.SetEligible(Range(1, 7), 2 + settle + dwell);
            rotation.Tick(1 + settle + 2*dwell);
            Assert.That(pages.Count, Is.EqualTo(mode == SwitchingMode.Pages ? 2 : 1));
            if (mode == SwitchingMode.Pages)
                CollectionAssert.AreEqual(new[] { 5,6,7,0 }, pages[1]);
        }

        [Test]
        public void WhileSettling_SlotsFollowTheListAsTablesReportIn()
        {
            RotationController controller = Create(SwitchingMode.Pages, 6);

            controller.SetEligible(Tables(4), 0);
            AssertShown(controller, 4, 0, 0, 0, 0, 0);

            controller.SetEligible(Tables(2, 4), 3);
            AssertShown(controller, 2, 4, 0, 0, 0, 0);

            controller.SetEligible(Range(1, 8), 6);
            AssertShown(controller, 1, 2, 3, 4, 5, 6);
        }

        [Test]
        public void Pages_TwelveTablesSixSlots_AlternateEveryDwell()
        {
            RotationController controller = Settled(SwitchingMode.Pages, 6, Range(1, 12));
            AssertShown(controller, 1, 2, 3, 4, 5, 6);

            controller.Tick(settle + dwell - 0.1);
            AssertShown(controller, 1, 2, 3, 4, 5, 6);

            controller.Tick(settle + dwell);
            AssertShown(controller, 7, 8, 9, 10, 11, 12);

            controller.Tick(settle + dwell * 2);
            AssertShown(controller, 1, 2, 3, 4, 5, 6);
        }

        [Test]
        public void Pages_FourteenTables_LastPageIsShortThenWraps()
        {
            RotationController controller = Settled(SwitchingMode.Pages, 6, Range(1, 14));

            controller.Tick(settle + dwell);
            AssertShown(controller, 7, 8, 9, 10, 11, 12);

            controller.Tick(settle + dwell * 2);
            AssertShown(controller, 13, 14, 0, 0, 0, 0);

            controller.Tick(settle + dwell * 3);
            AssertShown(controller, 1, 2, 3, 4, 5, 6);
        }

        [Test]
        public void Pages_TwelveTablesFourSlots_ThreePages()
        {
            RotationController controller = Settled(SwitchingMode.Pages, 4, Range(1, 12));
            AssertShown(controller, 1, 2, 3, 4);

            controller.Tick(settle + dwell);
            AssertShown(controller, 5, 6, 7, 8);

            controller.Tick(settle + dwell * 2);
            AssertShown(controller, 9, 10, 11, 12);

            controller.Tick(settle + dwell * 3);
            AssertShown(controller, 1, 2, 3, 4);
        }

        [Test]
        public void Pages_FewerTablesThanSlots_NeverChange()
        {
            RotationController controller = Settled(SwitchingMode.Pages, 6, Range(1, 5));
            int changes = 0;
            controller.SlotChanged += (slot, table) => changes++;

            for (double time = settle; time < settle + dwell * 5; time += 1) controller.Tick(time);

            AssertShown(controller, 1, 2, 3, 4, 5, 0);
            Assert.AreEqual(0, changes);
        }

        [Test]
        public void Pages_TableLeaving_EmptiesItsSlotUntilTheNextPage()
        {
            RotationController controller = Settled(SwitchingMode.Pages, 6, Range(1, 12));

            List<Guid> withoutThree = Range(1, 12);
            withoutThree.Remove(TestSamples.TableId(3));
            controller.SetEligible(withoutThree, 50);
            AssertShown(controller, 1, 2, 0, 4, 5, 6);

            controller.Tick(settle + dwell);
            AssertShown(controller, 7, 8, 9, 10, 11, 12);

            controller.Tick(settle + dwell * 2);
            AssertShown(controller, 1, 2, 4, 5, 6, 7);
        }

        [Test]
        public void Pages_TableReturning_TakesTheEmptySlotAtOnce()
        {
            RotationController controller = Settled(SwitchingMode.Pages, 6, Range(1, 12));
            List<Guid> withoutThree = Range(1, 12);
            withoutThree.Remove(TestSamples.TableId(3));
            controller.SetEligible(withoutThree, 50);

            controller.SetEligible(Range(1, 12), 60);

            AssertShown(controller, 1, 2, 3, 4, 5, 6);
            controller.Tick(settle + dwell);
            AssertShown(controller, 7, 8, 9, 10, 11, 12);
        }

        [Test]
        public void Pages_NewTable_WaitsForItsPageWhenNoSlotIsEmpty()
        {
            RotationController controller = Settled(SwitchingMode.Pages, 6, Range(1, 12));

            controller.SetEligible(Range(1, 13), 50);
            AssertShown(controller, 1, 2, 3, 4, 5, 6);

            controller.Tick(settle + dwell);
            AssertShown(controller, 7, 8, 9, 10, 11, 12);
            controller.Tick(settle + dwell * 2);
            AssertShown(controller, 13, 0, 0, 0, 0, 0);
        }

        [Test]
        public void Pages_SixthTableArriving_FillsTheSpareSlotAtOnce()
        {
            RotationController controller = Settled(SwitchingMode.Pages, 6, Tables(1, 2, 4, 5, 6));
            AssertShown(controller, 1, 2, 4, 5, 6, 0);

            controller.SetEligible(Range(1, 6), 50);
            AssertShown(controller, 1, 2, 4, 5, 6, 3);

            // The next switch puts everything back in order.
            controller.Tick(settle + dwell);
            AssertShown(controller, 1, 2, 3, 4, 5, 6);
        }

        [Test]
        public void Pages_ShortLastPage_IsNotRefilledByAnUnrelatedListChange()
        {
            RotationController controller = Settled(SwitchingMode.Pages, 6, Range(1, 14));
            controller.Tick(settle + dwell);
            controller.Tick(settle + dwell * 2);
            AssertShown(controller, 13, 14, 0, 0, 0, 0);

            // Table 5, which is not on this page, leaves. The empty slots must stay empty.
            List<Guid> withoutFive = Range(1, 14);
            withoutFive.Remove(TestSamples.TableId(5));
            controller.SetEligible(withoutFive, settle + dwell * 2 + 10);

            AssertShown(controller, 13, 14, 0, 0, 0, 0);
        }

        [Test]
        public void IndependentSlotsCycleOnlyAssignedIdsAtTheirOwnIntervals()
        {
            var controller = new RotationController(new RotationSettings {
                Mode = SwitchingMode.SlotBySlot, SlotCount = 4, SlotIntervals = new[] { 5, 12, 20, 30 }
            });
            var ids = Range(1, 6);
            int[] assignments = { 0, 0, 1, 1, 3, 5 };
            int Slot(Guid id) => assignments[ids.IndexOf(id)];
            controller.SetEligible(ids, 0, Slot);
            AssertShown(controller, 1, 3, 0, 5);
            controller.Tick(5);
            AssertShown(controller, 2, 3, 0, 5);
            controller.SetEligible(Tables(1, 2, 4, 5, 6), 6, Slot);
            AssertShown(controller, 2, 4, 0, 5);
            controller.SetEligible(ids, 7, Slot);
            controller.Tick(12);
            AssertShown(controller, 1, 4, 0, 5);
            controller.Tick(18);
            AssertShown(controller, 2, 3, 0, 5);
        }

        [Test]
        public void QuietServerIdsStillOccupyTheirRosterPositions()
        {
            var ids = Range(1, 9);
            var controller = Create(SwitchingMode.SlotBySlot, 4);
            int Slot(Guid id) => ids.IndexOf(id) % 4;
            controller.SetEligible(Tables(2, 4, 5, 7, 9), 0, Slot);
            AssertShown(controller, 5, 2, 7, 4);
            controller.Tick(dwell);
            AssertShown(controller, 9, 2, 7, 4);
            controller.SetEligible(Tables(2, 4, 7), dwell + 1, Slot);
            AssertShown(controller, 0, 2, 7, 4);
        }

        [Test]
        public void AllTablesGoing_ClearsTheSlots_AndTheNextArrivalsSettleAgain()
        {
            RotationController controller = Settled(SwitchingMode.Pages, 6, Range(1, 12));
            controller.Tick(settle + dwell);
            AssertShown(controller, 7, 8, 9, 10, 11, 12);

            controller.SetEligible(new List<Guid>(), 200);
            AssertShown(controller, 0, 0, 0, 0, 0, 0);

            // Tables come back in arrival order; the page must still form in list order.
            controller.SetEligible(Tables(9), 300);
            controller.SetEligible(Tables(2, 9), 301);
            controller.SetEligible(Range(1, 12), 302);
            AssertShown(controller, 1, 2, 3, 4, 5, 6);

            controller.Tick(300 + settle);
            controller.Tick(300 + settle + dwell - 0.1);
            AssertShown(controller, 1, 2, 3, 4, 5, 6);
            controller.Tick(300 + settle + dwell);
            AssertShown(controller, 7, 8, 9, 10, 11, 12);
        }

        [Test]
        public void SlotChanged_ReportsEachSlotThatChanges()
        {
            RotationController controller = Create(SwitchingMode.Pages, 4);
            List<string> events = new List<string>();
            controller.SlotChanged += (slot, table) => events.Add($"{slot}:{(table.HasValue ? int.Parse(table.Value.ToString().Substring(24)) : 0)}");

            controller.SetEligible(Range(1, 6), 0);
            CollectionAssert.AreEqual(new[] { "0:1", "1:2", "2:3", "3:4" }, events);

            events.Clear();
            controller.Tick(settle);
            controller.Tick(settle + dwell);
            CollectionAssert.AreEqual(new[] { "0:5", "1:6", "2:0", "3:0" }, events);
        }

        [Test]
        public void LongStall_DoesNotReplayEverySwitchThatWasMissed()
        {
            RotationController controller = Settled(SwitchingMode.Pages, 6, Range(1, 18));
            int changes = 0;
            controller.SlotChanged += (slot, table) => changes++;

            controller.Tick(settle + dwell * 50); // The app was frozen for a long time.

            Assert.AreEqual(6, changes, "One page turn, not fifty.");
            AssertShown(controller, 7, 8, 9, 10, 11, 12);
        }
    }
}
