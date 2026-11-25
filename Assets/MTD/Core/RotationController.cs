using System;
using System.Collections.Generic;

namespace MTD
{
    public enum SwitchingMode
    {
        Pages, // Option 1: every slot changes together.
        SlotBySlot, // Option 2: one slot changes at a time.
    }

    public sealed class RotationSettings
    {
        public int SlotCount = 6;
        public SwitchingMode Mode = SwitchingMode.Pages;
        public double DwellSeconds = 120; // How long a page, or a full set of slots, stays up.
        public double SlotSwapIntervalSeconds = 5; // Slot by slot only: the gap between one slot's swap and the next.
        public double SettleSeconds = 12; // After the first table appears, slots simply follow the list for this long.
    }

    // Decides which table is in which slot. Feed it the ordered list of eligible tables whenever that changes and
    // call Tick regularly; it raises SlotChanged for every slot whose table changes. Time is passed in, in seconds.
    //
    // The rules, from Docs/MTD-Plan.md section 3.3:
    //  - While settling, the slots show the first tables of the list, in order.
    //  - Pages: every dwell, the next slice of the list is shown. A short last page leaves slots empty.
    //  - Slot by slot: after a dwell, each slot in turn takes the table that has waited longest off screen and the
    //    table it replaces joins the back of the queue. After the last slot, the display holds for another dwell.
    //  - A table that leaves the list vacates its slot at once. A table that joins fills an empty slot at once and
    //    otherwise waits for the next switch.
    public sealed class RotationController
    {
        enum Phase
        {
            Idle, // No eligible tables.
            Settling,
            Holding,
            Swapping, // Slot by slot only: part-way through a pass.
        }

        readonly RotationSettings settings;
        readonly Guid?[] slots;
        readonly List<Guid> waiting = new List<Guid>(); // Slot by slot: off-screen tables, longest wait first.

        List<Guid> eligible = new List<Guid>();
        HashSet<Guid> eligibleSet = new HashSet<Guid>();
        Phase phase = Phase.Idle;
        double phaseEndsAt;

        // Pages: where the next page starts. The IDs let the position survive a change to the list.
        int nextPageStart;
        Guid? pageLastTable;
        Guid? nextPageFirstTable;

        // Slot by slot.
        int nextSwapSlot;
        double nextSwapAt;

        public RotationController(RotationSettings settings)
        {
            this.settings = settings;
            slots = new Guid?[Math.Max(1, settings.SlotCount)];
        }

        public event Action<int, Guid?> SlotChanged;

        public IReadOnlyList<Guid?> Slots => slots;

        public void SetEligible(IReadOnlyList<Guid> orderedTables, double now)
        {
            HashSet<Guid> previousSet = eligibleSet;
            eligible = new List<Guid>(orderedTables);
            eligibleSet = new HashSet<Guid>(eligible);

            if (eligible.Count == 0)
            {
                for (int slot = 0; slot < slots.Length; slot++) SetSlot(slot, null);
                waiting.Clear();
                phase = Phase.Idle;
                return;
            }

            if (phase == Phase.Idle)
            {
                phase = Phase.Settling;
                phaseEndsAt = now + settings.SettleSeconds;
            }

            if (phase == Phase.Settling)
            {
                ShowInOrder(0);
                return;
            }

            for (int slot = 0; slot < slots.Length; slot++)
            {
                if (slots[slot].HasValue && !eligibleSet.Contains(slots[slot].Value)) SetSlot(slot, null);
            }

            List<Guid> newcomers = eligible.FindAll(table => !previousSet.Contains(table));

            if (settings.Mode == SwitchingMode.Pages)
            {
                foreach (Guid table in newcomers)
                {
                    int emptySlot = Array.IndexOf(slots, null);
                    if (emptySlot < 0) break;
                    SetSlot(emptySlot, table);
                }

                RelocateNextPage();
            }
            else
            {
                waiting.RemoveAll(table => !eligibleSet.Contains(table));
                waiting.AddRange(newcomers);
                FillEmptySlotsFromQueue();
            }
        }

        public void Tick(double now)
        {
            switch (phase)
            {
                case Phase.Settling:
                    if (now >= phaseEndsAt) BeginHolding(now);
                    break;

                case Phase.Holding:
                    if (now < phaseEndsAt) break;
                    if (settings.Mode == SwitchingMode.Pages)
                    {
                        TurnPage(now);
                    }
                    else if (waiting.Count == 0)
                    {
                        phaseEndsAt = now + settings.DwellSeconds; // Everything fits on screen: nothing to swap.
                    }
                    else
                    {
                        phase = Phase.Swapping;
                        nextSwapSlot = 0;
                        SwapNextSlot(now);
                    }
                    break;

                case Phase.Swapping:
                    if (now >= nextSwapAt) SwapNextSlot(now);
                    break;
            }
        }

        void BeginHolding(double now)
        {
            phase = Phase.Holding;
            phaseEndsAt = now + settings.DwellSeconds;

            if (settings.Mode == SwitchingMode.Pages)
            {
                RememberPage(0);
            }
            else
            {
                waiting.Clear();
                foreach (Guid table in eligible)
                {
                    if (Array.IndexOf(slots, table) < 0) waiting.Add(table);
                }
            }
        }

        void TurnPage(double now)
        {
            int start = nextPageStart;
            if (eligible.Count <= slots.Length || start >= eligible.Count) start = 0;

            ShowInOrder(start);
            RememberPage(start);
            phaseEndsAt = now + settings.DwellSeconds;
        }

        void RememberPage(int start)
        {
            int shown = Math.Min(slots.Length, eligible.Count - start);
            nextPageStart = start + shown;
            pageLastTable = shown > 0 ? eligible[nextPageStart - 1] : (Guid?)null;
            nextPageFirstTable = nextPageStart < eligible.Count ? eligible[nextPageStart] : (Guid?)null;
        }

        // The list changed under a page. Find where the next page now starts: just after the table that ended the
        // current page, or failing that at the table that was due to start the next one.
        void RelocateNextPage()
        {
            int index = pageLastTable.HasValue ? eligible.IndexOf(pageLastTable.Value) : -1;
            if (index >= 0)
            {
                nextPageStart = index + 1;
            }
            else
            {
                index = nextPageFirstTable.HasValue ? eligible.IndexOf(nextPageFirstTable.Value) : -1;
                nextPageStart = index >= 0 ? index : Math.Min(nextPageStart, eligible.Count);
            }

            nextPageFirstTable = nextPageStart < eligible.Count ? eligible[nextPageStart] : (Guid?)null;
        }

        void SwapNextSlot(double now)
        {
            if (waiting.Count == 0)
            {
                EndPass(now);
                return;
            }

            int slot = nextSwapSlot;
            Guid incoming = waiting[0];
            waiting.RemoveAt(0);
            Guid? outgoing = slots[slot];

            SetSlot(slot, incoming);
            if (outgoing.HasValue && eligibleSet.Contains(outgoing.Value)) waiting.Add(outgoing.Value);

            nextSwapSlot++;
            if (nextSwapSlot >= slots.Length) EndPass(now);
            else nextSwapAt = now + settings.SlotSwapIntervalSeconds;
        }

        void EndPass(double now)
        {
            phase = Phase.Holding;
            phaseEndsAt = now + settings.DwellSeconds;
        }

        void FillEmptySlotsFromQueue()
        {
            for (int slot = 0; slot < slots.Length && waiting.Count > 0; slot++)
            {
                if (slots[slot].HasValue) continue;
                SetSlot(slot, waiting[0]);
                waiting.RemoveAt(0);
            }
        }

        // Slot i shows the table at start + i, or nothing when the list runs out.
        void ShowInOrder(int start)
        {
            for (int slot = 0; slot < slots.Length; slot++)
            {
                int index = start + slot;
                SetSlot(slot, index < eligible.Count ? eligible[index] : (Guid?)null);
            }
        }

        void SetSlot(int slot, Guid? table)
        {
            if (slots[slot] == table) return;
            slots[slot] = table;
            SlotChanged?.Invoke(slot, table);
        }
    }
}
