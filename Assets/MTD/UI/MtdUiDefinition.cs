using System;
using System.Collections.Generic;
using UnityEngine;

namespace MTD
{
    // One MTD UI: a look for the whole display. It is a list of layout prefabs, one per orientation and slot count
    // the UI supports. The asset lives in a Resources folder and is loaded by the path in the catalog, so only the
    // selected UI's prefabs and textures are ever loaded.
    [CreateAssetMenu(menuName = "MTD/MTD UI Definition", fileName = "MtdUiDefinition")]
    public sealed class MtdUiDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public List<MtdLayout> layouts = new List<MtdLayout>();

        // The layout for this orientation and slot count. When the UI has no exact match the nearest one is
        // returned and exact is false: same orientation first, then the closest slot count.
        public MtdLayout FindLayout(MtdOrientation orientation, int slotCount, out bool exact)
        {
            MtdLayout best = null;
            int bestScore = int.MaxValue;

            foreach (MtdLayout layout in layouts)
            {
                if (layout == null) continue;

                int score = Math.Abs(layout.SlotCount - slotCount) + (layout.orientation == orientation ? 0 : 1000);
                if (score < bestScore)
                {
                    best = layout;
                    bestScore = score;
                }
            }

            exact = bestScore == 0;
            return best;
        }
    }
}
