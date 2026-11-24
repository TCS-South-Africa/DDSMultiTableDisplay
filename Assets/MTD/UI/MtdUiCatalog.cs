using System;
using System.Collections.Generic;
using UnityEngine;

namespace MTD
{
    // The list of MTD UIs the Configuration Menu offers. It holds only names and Resources paths, so reading it
    // loads no artwork. Keep it at Resources/MtdUis/MtdUiCatalog.
    [CreateAssetMenu(menuName = "MTD/MTD UI Catalog", fileName = "MtdUiCatalog")]
    public sealed class MtdUiCatalog : ScriptableObject
    {
        public const string ResourcePath = "MtdUis/MtdUiCatalog";

        [Serializable]
        public class Entry
        {
            public string id; // What the configuration file stores.
            public string displayName; // What the Configuration Menu shows.
            public string resourcePath; // The MtdUiDefinition's path under a Resources folder, without extension.
        }

        public List<Entry> entries = new List<Entry>();
    }
}
