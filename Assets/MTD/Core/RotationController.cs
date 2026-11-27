using System;
using System.Collections.Generic;

namespace MTD
{
    public enum SwitchingMode
    {
        Pages, // Option 1: every slot changes together.
        SlotBySlot, // Each slot rotates its own list on an independent clock.
    }

    public sealed class RotationSettings
    {
        public int SlotCount = 6;
        public SwitchingMode Mode = SwitchingMode.Pages;
        public double DwellSeconds = 120; // How long each page stays up.
        public int[] SlotIntervals; // Per-slot seconds. Missing intervals use DwellSeconds.
        public double SettleSeconds = 12; // After the first table appears, slots simply follow the list for this long.
    }

    // Decides which table is in which slot. Feed it the ordered list of eligible tables whenever that changes and
    // call Tick regularly; it raises SlotChanged for every slot whose table changes. Time is passed in, in seconds.
    //
    // Pages consume consecutive slices. Each independent slot consumes only its own assigned IDs.
    public sealed class RotationController
    {
        enum Phase
        {
            Idle, // No eligible tables.
            Settling,
            Holding,
        }

        readonly RotationSettings settings;
        readonly Guid?[] slots;
        readonly List<Guid>[] slotTables;
        readonly double[] slotDeadlines;

        List<Guid> eligible = new List<Guid>();
        HashSet<Guid> eligibleSet = new HashSet<Guid>();
        Phase phase = Phase.Idle;
        double phaseEndsAt;

        // Pages: where the next page starts. The IDs let the position survive a change to the list.
        int nextPageStart;
        Guid? pageLastTable;
        Guid? nextPageFirstTable;

        public RotationController(RotationSettings settings)
        {
            this.settings = settings;
            slots = new Guid?[Math.Max(1, settings.SlotCount)];
            slotTables = new List<Guid>[slots.Length];
            slotDeadlines = new double[slots.Length];
            for (int i = 0; i < slots.Length; i++) slotTables[i] = new List<Guid>();
        }

        public event Action<int, Guid?> SlotChanged;
        // After all assignments for the first visible board or a different timed page are published.
        public event Action PagePresented;

        public IReadOnlyList<Guid?> Slots => slots;

        public void SetEligible(IReadOnlyList<Guid> orderedTables, double now, Func<Guid, int> slotFor = null)
        {
            if (settings.Mode == SwitchingMode.SlotBySlot)
            {
                SetSlotTables(orderedTables, now, slotFor);
                return;
            }

            HashSet<Guid> previousSet = eligibleSet;
            eligible = new List<Guid>(orderedTables);
            eligibleSet = new HashSet<Guid>(eligible);

            if (eligible.Count == 0)
            {
                for (int slot = 0; slot < slots.Length; slot++) SetSlot(slot, null);
                phase = Phase.Idle;
                return;
            }

            bool firstPage = phase == Phase.Idle;
            if (firstPage)
            {
                phase = Phase.Settling;
                phaseEndsAt = now + settings.SettleSeconds;
            }

            if (phase == Phase.Settling)
            {
                ShowInOrder(0);
                if (firstPage) PagePresented?.Invoke();
                return;
            }

            for (int slot = 0; slot < slots.Length; slot++)
            {
                if (slots[slot].HasValue && !eligibleSet.Contains(slots[slot].Value)) SetSlot(slot, null);
            }

            List<Guid> newcomers = eligible.FindAll(table => !previousSet.Contains(table));

            foreach (Guid table in newcomers)
            {
                int emptySlot = Array.IndexOf(slots, null);
                if (emptySlot < 0) break;
                SetSlot(emptySlot, table);
            }
            RelocateNextPage();
        }

        void SetSlotTables(IReadOnlyList<Guid> orderedTables, double now, Func<Guid, int> slotFor)
        {
            foreach (List<Guid> pool in slotTables) pool.Clear();
            for (int i = 0; i < orderedTables.Count; i++)
            {
                Guid id = orderedTables[i];
                int slot = slotFor == null ? i % slots.Length : slotFor(id);
                if (slot >= 0 && slot < slots.Length) slotTables[slot].Add(id);
            }

            bool wasEmpty = phase == Phase.Idle;
            bool any = false;
            for (int slot = 0; slot < slots.Length; slot++)
            {
                List<Guid> pool = slotTables[slot];
                if (pool.Count == 0) { SetSlot(slot, null); continue; }
                any = true;
                if (slots[slot].HasValue && pool.Contains(slots[slot].Value)) continue;
                SetSlot(slot, pool[0]);
                slotDeadlines[slot] = now + Interval(slot);
            }
            phase = any ? Phase.Holding : Phase.Idle;
            if (any && wasEmpty) PagePresented?.Invoke();
        }

        double Interval(int slot) => settings.SlotIntervals != null && slot < settings.SlotIntervals.Length
            ? settings.SlotIntervals[slot] : settings.DwellSeconds;

        public void Tick(double now)
        {
            if (phase == Phase.Idle) return;
            if (settings.Mode == SwitchingMode.SlotBySlot)
            {
                for (int slot = 0; slot < slots.Length; slot++)
                {
                    List<Guid> pool = slotTables[slot];
                    if (pool.Count < 2 || now < slotDeadlines[slot]) continue;
                    int next = (pool.IndexOf(slots[slot].Value) + 1) % pool.Count;
                    SetSlot(slot, pool[next]);
                    slotDeadlines[slot] = now + Interval(slot);
                }
                return;
            }

            if (now < phaseEndsAt) return;
            if (phase == Phase.Settling)
            {
                phase = Phase.Holding;
                phaseEndsAt = now + settings.DwellSeconds;
                RememberPage(0);
            }
            else TurnPage(now);
        }

        void TurnPage(double now)
        {
            int start = nextPageStart;
            if (eligible.Count <= slots.Length || start >= eligible.Count) start = 0;

            bool changed = ShowInOrder(start);
            RememberPage(start);
            phaseEndsAt = now + settings.DwellSeconds;
            if (changed) PagePresented?.Invoke();
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

        // Slot i shows the table at start + i, or nothing when the list runs out.
        bool ShowInOrder(int start)
        {
            bool changed = false;
            for (int slot = 0; slot < slots.Length; slot++)
            {
                int index = start + slot;
                Guid? table = index < eligible.Count ? eligible[index] : (Guid?)null;
                changed |= slots[slot] != table;
                SetSlot(slot, table);
            }
            return changed;
        }

        void SetSlot(int slot, Guid? table)
        {
            if (slots[slot] == table) return;
            slots[slot] = table;
            SlotChanged?.Invoke(slot, table);
        }
    }
}
