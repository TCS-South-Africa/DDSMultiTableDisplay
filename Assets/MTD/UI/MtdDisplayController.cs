using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MTD
{
    // Puts table widgets into a layout's slots. It knows nothing about connections or switching: it is told which
    // table belongs in which slot and when a shown table has new data.
    public sealed class MtdDisplayController : MonoBehaviour
    {
        sealed class Slot
        {
            public RectTransform Anchor;
            public readonly Dictionary<TableWidget, TableWidget> Instances = new Dictionary<TableWidget, TableWidget>(); // Prefab to its instance in this slot.
            public TableWidget Active;
            public TableWidget ActivePrefab;
            public Guid? TableId;
            public GameObject Empty;
            public Coroutine Transition;
        }

        MtdLayout layout;
        Slot[] slots = new Slot[0];
        Func<Guid, TableSnapshot> findLatest;
        MtdStatus shownStatus;
        bool useUniqueName;

        public MtdLayout Layout => layout;

        public int SlotCount => slots.Length;

        // findLatest returns the newest snapshot for a table, or null when the table is no longer known.
        public void Initialise(MtdLayout layoutInstance, Func<Guid, TableSnapshot> findLatest, bool useUniqueName = false)
        {
            layout = layoutInstance;
            this.findLatest = findLatest;
            this.useUniqueName = useUniqueName;
            shownStatus = null;

            slots = new Slot[layout.slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                Slot slot = new Slot { Anchor = layout.slots[i] };
                if (layout.emptySlotPrefab != null)
                {
                    slot.Empty = Instantiate(layout.emptySlotPrefab, slot.Anchor, false);
                    FillSlot((RectTransform)slot.Empty.transform);
                    slot.Empty.SetActive(true);
                }

                slots[i] = slot;
            }
        }

        // Stops everything in progress. The layout object itself belongs to whoever created it.
        public void Clear()
        {
            StopAllCoroutines();
            slots = new Slot[0];
            layout = null;
            shownStatus = null;
        }

        public bool CanShow(TableSnapshot table)
        {
            return layout != null && layout.CanShow(table);
        }

        // Shows the table in the slot, or empties the slot when the table is null.
        public void SetSlot(int index, TableSnapshot table)
        {
            if (index < 0 || index >= slots.Length) return;

            Slot slot = slots[index];
            if (slot.Transition != null) StopCoroutine(slot.Transition);
            slot.TableId = table?.TableId;
            slot.Transition = StartCoroutine(ChangeSlot(slot, table));
        }

        // A table that may be on screen has new data.
        public void RefreshTable(TableSnapshot table, TableChange change)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                Slot slot = slots[i];
                // Only a widget that is already showing this table is refreshed. A slot that is part-way through
                // changing to it still has the previous table's widget, and binds the latest data when it finishes.
                if (slot.TableId != table.TableId || slot.Active == null || slot.Active.Table == null || slot.Active.Table.TableId != table.TableId) continue;

                if (layout.FindWidgetPrefab(table) != slot.ActivePrefab)
                {
                    SetSlot(i, table); // Its game changed, so it needs a different widget.
                }
                else
                {
                    slot.Active.Refresh(table, change, ColourOf(table));
                }
            }
        }

        public void ShowStatus(MtdStatus status)
        {
            if (layout == null || layout.statusView == null || status.SameAs(shownStatus)) return;

            shownStatus = status;
            layout.statusView.Show(status);
        }

        IEnumerator ChangeSlot(Slot slot, TableSnapshot table)
        {
            if (slot.Active != null)
            {
                yield return slot.Active.PlayExit();
            }

            // Hide every instance, not only the active one: a change interrupted part-way may have left one showing.
            foreach (TableWidget instance in slot.Instances.Values)
            {
                if (instance != null) instance.gameObject.SetActive(false);
            }

            slot.Active = null;
            slot.ActivePrefab = null;

            // The table may have sent new data, or changed game, while the old widget was leaving.
            TableSnapshot latest = table != null ? findLatest(table.TableId) ?? table : null;
            TableWidget prefab = latest != null ? layout.FindWidgetPrefab(latest) : null;

            if (prefab == null)
            {
                if (slot.Empty != null) slot.Empty.SetActive(true);
                slot.Transition = null;
                yield break;
            }

            if (slot.Empty != null) slot.Empty.SetActive(false);

            if (!slot.Instances.TryGetValue(prefab, out TableWidget widget) || widget == null)
            {
                widget = Instantiate(prefab, slot.Anchor, false);
                widget.name = prefab.name;
                widget.UseUniqueName = useUniqueName;
                FillSlot((RectTransform)widget.transform);
                slot.Instances[prefab] = widget;
            }

            widget.gameObject.SetActive(true);
            widget.Bind(latest, ColourOf(latest));
            slot.Active = widget;
            slot.ActivePrefab = prefab;

            yield return widget.PlayEnter();
            slot.Transition = null;
        }

        Color ColourOf(TableSnapshot table)
        {
            return table.Colour.HasValue ? (Color)table.Colour.Value : layout.defaultColour;
        }

        // A widget's root always fills its slot. How the widget arranges itself inside is up to its prefab.
        static void FillSlot(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }
    }
}
