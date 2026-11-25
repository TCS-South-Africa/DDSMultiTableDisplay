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
        const double interval = 5;

        static RotationController Create(SwitchingMode mode, int slots)
        {
            return new RotationController(new RotationSettings
            {
                SlotCount = slots,
                Mode = mode,
                DwellSeconds = dwell,
                SlotSwapIntervalSeconds = interval,
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
        public void SlotBySlot_TwelveTables_SwapsOneSlotAtATimeThenHolds()
        {
            RotationController controller = Settled(SwitchingMode.SlotBySlot, 6, Range(1, 12));
            AssertShown(controller, 1, 2, 3, 4, 5, 6);

            double passStart = settle + dwell;
            controller.Tick(passStart - 0.1);
            AssertShown(controller, 1, 2, 3, 4, 5, 6);

            controller.Tick(passStart);
            AssertShown(controller, 7, 2, 3, 4, 5, 6);

            controller.Tick(passStart + interval - 0.1);
            AssertShown(controller, 7, 2, 3, 4, 5, 6);

            controller.Tick(passStart + interval);
            AssertShown(controller, 7, 8, 3, 4, 5, 6);

            for (int step = 2; step < 6; step++) controller.Tick(passStart + interval * step);
            AssertShown(controller, 7, 8, 9, 10, 11, 12);

            // Holds for a full dwell after the last slot, then the first six come back the same way.
            double secondPass = passStart + interval * 5 + dwell;
            controller.Tick(secondPass - 0.1);
            AssertShown(controller, 7, 8, 9, 10, 11, 12);
            controller.Tick(secondPass);
            AssertShown(controller, 1, 8, 9, 10, 11, 12);
        }

        [Test]
        public void SlotBySlot_EightTables_EveryTableKeepsTakingItsTurn()
        {
            RotationController controller = Settled(SwitchingMode.SlotBySlot, 6, Range(1, 8));

            double passStart = settle + dwell;
            for (int step = 0; step < 6; step++) controller.Tick(passStart + interval * step);

            // 7 and 8 came in, then 1 to 4 came back round as they reached the front of the queue.
            AssertShown(controller, 7, 8, 1, 2, 3, 4);
        }

        [Test]
        public void SlotBySlot_FewerTablesThanSlots_NeverSwap()
        {
            RotationController controller = Settled(SwitchingMode.SlotBySlot, 6, Range(1, 6));
            int changes = 0;
            controller.SlotChanged += (slot, table) => changes++;

            for (double time = settle; time < settle + dwell * 5; time += 1) controller.Tick(time);

            AssertShown(controller, 1, 2, 3, 4, 5, 6);
            Assert.AreEqual(0, changes);
        }

        [Test]
        public void SlotBySlot_TableLeaving_IsReplacedFromTheQueueAtOnce()
        {
            RotationController controller = Settled(SwitchingMode.SlotBySlot, 6, Range(1, 9));

            List<Guid> withoutThree = Range(1, 9);
            withoutThree.Remove(TestSamples.TableId(3));
            controller.SetEligible(withoutThree, 50);

            AssertShown(controller, 1, 2, 7, 4, 5, 6);
        }

        [Test]
        public void SlotBySlot_NewTable_JoinsTheBackOfTheQueue()
        {
            RotationController controller = Settled(SwitchingMode.SlotBySlot, 6, Range(1, 8));

            controller.SetEligible(Range(1, 9), 50);
            AssertShown(controller, 1, 2, 3, 4, 5, 6);

            double passStart = settle + dwell;
            for (int step = 0; step < 3; step++) controller.Tick(passStart + interval * step);
            AssertShown(controller, 7, 8, 9, 4, 5, 6);
        }

        [Test]
        public void SlotBySlot_NewTable_FillsAnEmptySlotAtOnce()
        {
            RotationController controller = Settled(SwitchingMode.SlotBySlot, 6, Range(1, 5));
            AssertShown(controller, 1, 2, 3, 4, 5, 0);

            controller.SetEligible(Range(1, 6), 50);

            AssertShown(controller, 1, 2, 3, 4, 5, 6);
        }

        [Test]
        public void SlotBySlot_NoTableIsEverInTwoSlots_AndScreenTimeIsShared()
        {
            RotationController controller = Settled(SwitchingMode.SlotBySlot, 6, Range(1, 9));
            Dictionary<int, int> secondsOnScreen = Enumerable.Range(1, 9).ToDictionary(table => table, table => 0);

            for (double time = settle; time < settle + 4 * 3600; time += 1)
            {
                controller.Tick(time);
                int[] shown = Shown(controller);
                Assert.AreEqual(6, shown.Distinct().Count(), $"A table is in two slots at {time}s.");
                foreach (int table in shown) secondsOnScreen[table]++;
            }

            // Nine tables share six slots, so each should be up about two thirds of the time.
            double average = secondsOnScreen.Values.Average();
            foreach (KeyValuePair<int, int> entry in secondsOnScreen)
            {
                Assert.AreEqual(average, entry.Value, average * 0.05, $"Table {entry.Key} got an unfair share.");
            }
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
