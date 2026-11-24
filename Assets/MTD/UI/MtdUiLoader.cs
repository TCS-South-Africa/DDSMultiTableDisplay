using System;
using System.Collections.Generic;
using UnityEngine;

namespace MTD
{
    // Finds and loads MTD UIs. Everything goes through the catalog and Resources.Load, so moving to Addressables
    // later means replacing this one class.
    public static class MtdUiLoader
    {
        public const string DebugUiId = "debug";
        public const string DebugUiName = "Debug (plain text)";

        public readonly struct UiChoice
        {
            public readonly string Id;
            public readonly string DisplayName;

            public UiChoice(string id, string displayName)
            {
                Id = id;
                DisplayName = displayName;
            }
        }

        public sealed class LoadedUi
        {
            public MtdLayout Layout; // An instance in the scene, under the parent that was given.
            public string UiId;
            public string DisplayName;
            public string Warning; // Null unless something other than what was asked for had to be shown.
        }

        // The UIs the Configuration Menu offers: everything in the catalog, then the built-in debug UI.
        public static List<UiChoice> ListUis()
        {
            List<UiChoice> choices = new List<UiChoice>();

            MtdUiCatalog catalog = Resources.Load<MtdUiCatalog>(MtdUiCatalog.ResourcePath);
            if (catalog != null)
            {
                foreach (MtdUiCatalog.Entry entry in catalog.entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.id)) continue;
                    choices.Add(new UiChoice(entry.id.Trim(), string.IsNullOrWhiteSpace(entry.displayName) ? entry.id.Trim() : entry.displayName.Trim()));
                }
            }

            choices.Add(new UiChoice(DebugUiId, DebugUiName));
            return choices;
        }

        // Loads the UI and creates its layout for this orientation and slot count. A blank id means the first UI in
        // the catalog. Whatever goes wrong, a layout is always returned: the debug UI stands in, with a warning.
        public static LoadedUi Load(string uiId, MtdOrientation orientation, int slotCount, Transform parent)
        {
            string wanted = (uiId ?? "").Trim();
            if (string.Equals(wanted, DebugUiId, StringComparison.OrdinalIgnoreCase))
            {
                return LoadDebugUi(orientation, slotCount, parent, null);
            }

            MtdUiCatalog catalog = Resources.Load<MtdUiCatalog>(MtdUiCatalog.ResourcePath);
            MtdUiCatalog.Entry entry = null;
            if (catalog != null)
            {
                foreach (MtdUiCatalog.Entry candidate in catalog.entries)
                {
                    if (candidate == null || string.IsNullOrWhiteSpace(candidate.id)) continue;
                    if (wanted.Length == 0 || string.Equals(candidate.id.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        entry = candidate;
                        break;
                    }
                }
            }

            if (entry == null)
            {
                // With nothing asked for and nothing installed, the debug UI is simply the UI. No warning.
                return LoadDebugUi(orientation, slotCount, parent, wanted.Length == 0 ? null : $"MTD UI \"{wanted}\" is not in this build.");
            }

            MtdUiDefinition definition = Resources.Load<MtdUiDefinition>(entry.resourcePath);
            if (definition == null)
            {
                return LoadDebugUi(orientation, slotCount, parent, $"MTD UI \"{entry.id}\" could not be loaded from Resources/{entry.resourcePath}.");
            }

            MtdLayout layoutPrefab = definition.FindLayout(orientation, slotCount, out bool exact);
            if (layoutPrefab == null)
            {
                return LoadDebugUi(orientation, slotCount, parent, $"MTD UI \"{entry.id}\" has no layouts.");
            }

            return new LoadedUi
            {
                Layout = UnityEngine.Object.Instantiate(layoutPrefab, parent),
                UiId = entry.id.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(definition.displayName) ? entry.id.Trim() : definition.displayName,
                Warning = exact ? null : $"MTD UI \"{entry.id}\" has no {orientation.ToString().ToLowerInvariant()} layout for {slotCount} tables. Showing its {layoutPrefab.orientation.ToString().ToLowerInvariant()} layout for {layoutPrefab.SlotCount}.",
            };
        }

        static LoadedUi LoadDebugUi(MtdOrientation orientation, int slotCount, Transform parent, string warning)
        {
            return new LoadedUi
            {
                Layout = DebugUi.Build(orientation, slotCount, parent),
                UiId = DebugUiId,
                DisplayName = DebugUiName,
                Warning = warning == null ? null : warning + " Showing the debug UI.",
            };
        }
    }
}
