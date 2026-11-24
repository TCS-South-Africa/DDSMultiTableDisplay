using System;
using System.Collections.Generic;
using UnityEngine;

namespace MTD
{
    public enum MtdOrientation
    {
        Landscape,
        Portrait,
    }

    // Sits on the root of a layout prefab, which is a Canvas with its own CanvasScaler. A layout is one arrangement
    // of slots for one orientation, together with the widget prefabs that fit those slots. An MTD UI has one layout
    // per orientation and slot count it supports.
    public sealed class MtdLayout : MonoBehaviour
    {
        [Serializable]
        public class WidgetEntry
        {
            public GameFamily family;
            public bool anyVariant = true; // Roulette only: untick to give one variant its own prefab.
            public RouletteVariant variant;
            public TableWidget prefab;
        }

        public MtdOrientation orientation;
        public RectTransform[] slots = new RectTransform[0]; // In display order. The length is the slot count.
        public List<WidgetEntry> widgets = new List<WidgetEntry>();
        public GameObject emptySlotPrefab; // Optional. Shown in a slot that has no table.
        public Color defaultColour = new Color(0.16f, 0.18f, 0.24f); // For a table that sent no usable colour.
        public MtdStatusView statusView; // Optional. Where "connecting", "waiting for tables" and so on are shown.

        public int SlotCount => slots.Length;

        // The prefab that draws this table, or null when this layout has none for its game. An entry for the
        // table's exact roulette variant wins over an any-variant entry.
        public TableWidget FindWidgetPrefab(TableSnapshot table)
        {
            RouletteVariant variant = table is RouletteSnapshot roulette ? roulette.Variant : RouletteVariant.Standard;

            TableWidget fallback = null;
            foreach (WidgetEntry entry in widgets)
            {
                if (entry.prefab == null || entry.family != table.Family) continue;
                if (table.Family == GameFamily.Roulette && !entry.anyVariant)
                {
                    if (entry.variant == variant) return entry.prefab;
                    continue;
                }

                fallback ??= entry.prefab;
            }

            return fallback;
        }

        public bool CanShow(TableSnapshot table)
        {
            return FindWidgetPrefab(table) != null;
        }
    }
}
