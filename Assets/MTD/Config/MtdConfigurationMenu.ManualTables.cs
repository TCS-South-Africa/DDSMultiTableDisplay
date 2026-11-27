using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MTD
{
    public sealed partial class MtdConfigurationMenu
    {
        Transform manualPanel, manualRows, slotRow;
        TMP_InputField manualId;
        TMP_Dropdown manualSlot;
        Button storeButton, deleteButton, newButton;
        TMP_Text manualMessage;
        int editingIndex = -1;
        int editingSlot = 1;

        void BuildManualEditor()
        {
            const string group = "Manual Tables";
            Transform section = GetSection("Tables", group);
            manualPanel = section;
            Transform content = tabs.Find(tab => tab.Name == "Tables").Content;
            while (manualPanel.parent != content) manualPanel = manualPanel.parent;

            manualId = CreateInput("Tables", group + "|DDS Table ID", "Paste a table GUID", TMP_InputField.ContentType.Standard);
            ((RectTransform)manualId.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 620);
            manualId.textComponent.enableAutoSizing = true;
            manualId.textComponent.fontSizeMin = 14;
            manualId.textComponent.fontSizeMax = 30;
            SetUpTooltip(manualId, "Use the TableId from DDS. Each ID can appear once on this display.");

            GameObject slot = CreateRow(dropdownTemplate, "Tables", group + "|Assigned Slot", "Dropdown");
            slotRow = slot.transform;
            manualSlot = slot.GetComponentInChildren<TMP_Dropdown>(true);
            manualSlot.onValueChanged.AddListener(i => editingSlot = i + 1);

            var actions = new GameObject("Table actions", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<HorizontalLayoutGroup>();
            actions.transform.SetParent(section, false);
            actions.spacing = 8;
            storeButton = ManualButton(actions.transform, "Add table ID", () => StoreManualInput());
            newButton = ManualButton(actions.transform, "New entry", () => { ResetManualInput(); RefreshManualEditor(); });
            deleteButton = ManualButton(actions.transform, "Delete", () =>
            {
                draft.tables.manualTables.RemoveAt(editingIndex);
                ResetManualInput();
                RefreshManualEditor();
            });

            manualMessage = new GameObject("Assignment feedback", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            manualMessage.transform.SetParent(section, false);
            manualMessage.font = manualId.textComponent.font;
            manualMessage.fontSize = 26;
            manualMessage.color = Color.black;
            manualMessage.raycastTarget = false;
            manualMessage.richText = false;

            var rows = new GameObject("Assigned table IDs", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<VerticalLayoutGroup>();
            rows.transform.SetParent(section, false);
            rows.spacing = 3;
            rows.childForceExpandHeight = false;
            manualRows = rows.transform;
        }

        Button ManualButton(Transform parent, string label, Action click)
        {
            GameObject obj = Instantiate(menuButtonTemplate, parent, false);
            obj.name = label;
            var size = obj.GetComponent<LayoutElement>() ?? obj.AddComponent<LayoutElement>();
            size.preferredHeight = 48;
            TMP_Text caption = obj.GetComponentInChildren<TMP_Text>(true);
            caption.text = label;
            caption.richText = false;
            caption.enableAutoSizing = true;
            caption.fontSizeMin = 15;
            caption.fontSizeMax = 28;
            Button button = obj.GetComponent<Button>();
            button.image.color = Color.white;
            if (click != null) button.onClick.AddListener(() => click());
            else button.interactable = false;
            return button;
        }

        void ResetManualInput()
        {
            editingIndex = -1;
            editingSlot = draft.tables.manualTables.Count % draft.display.numberOfTables + 1;
            manualId.SetTextWithoutNotify("");
            manualMessage.text = "";
        }

        bool ManualInputPending()
        {
            if (editingIndex < 0) return !string.IsNullOrWhiteSpace(manualId.text);
            var entry = draft.tables.manualTables[editingIndex];
            return manualId.text.Trim() != entry.tableId || (draft.switching.mode == SwitchingMode.SlotBySlot
                && editingSlot != entry.SlotIndex(editingIndex, draft.display.numberOfTables) + 1);
        }

        bool StoreManualInput()
        {
            var entries = draft.tables.manualTables;
            if (!Guid.TryParse(manualId.text.Trim(), out Guid id) || id == Guid.Empty)
            {
                manualMessage.text = "Enter a valid, non-empty DDS table GUID.";
                return false;
            }
            string canonical = id.ToString();
            int duplicate = entries.FindIndex(entry => entry.tableId == canonical);
            if (duplicate >= 0 && duplicate != editingIndex)
            {
                manualMessage.text = "This table ID is already in the list. Select its row to edit it.";
                return false;
            }
            var entry = editingIndex >= 0 ? entries[editingIndex] : new MtdConfigurationData.ManualTable();
            entry.tableId = canonical;
            if (draft.switching.mode == SwitchingMode.SlotBySlot) entry.slot = editingSlot;
            if (editingIndex < 0) entries.Add(entry);
            ResetManualInput();
            RefreshManualEditor();
            return true;
        }

        void SelectManualEntry(int index)
        {
            editingIndex = index;
            var entry = draft.tables.manualTables[index];
            editingSlot = entry.SlotIndex(index, draft.display.numberOfTables) + 1;
            manualId.SetTextWithoutNotify(entry.tableId);
            manualMessage.text = "";
            RefreshManualEditor();
        }

        void RefreshManualEditor()
        {
            bool visible = draft.EffectiveTableListSource == TableListSourceKind.ManualEntry;
            manualPanel.gameObject.SetActive(visible);
            if (!visible) return;
            bool bySlot = draft.switching.mode == SwitchingMode.SlotBySlot;
            int count = draft.display.numberOfTables;
            slotRow.gameObject.SetActive(bySlot);
            var entries = draft.tables.manualTables;
            int choices = count;
            if (editingIndex >= 0) choices = Math.Max(choices, entries[editingIndex].slot);
            editingSlot = Mathf.Clamp(editingSlot, 1, choices);
            var labels = new List<string>(choices);
            for (int i = 1; i <= choices; i++) labels.Add(i > count ? $"Slot {i} (inactive)" : $"Slot {i}");
            manualSlot.ClearOptions();
            manualSlot.AddOptions(labels);
            manualSlot.SetValueWithoutNotify(editingSlot - 1);
            manualSlot.RefreshShownValue();
            storeButton.GetComponentInChildren<TMP_Text>().text = editingIndex < 0 ? "Add table ID" : "Save edit";
            deleteButton.gameObject.SetActive(editingIndex >= 0);
            newButton.gameObject.SetActive(editingIndex >= 0);

            foreach (Transform row in manualRows)
            {
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
            DrawAssignment("Assignment", "DDS table ID", -1, heading: true);
            if (bySlot)
            {
                for (int slot = 0; slot < 6; slot++)
                {
                    bool empty = true;
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (entries[i].SlotIndex(i, count) != slot) continue;
                        DrawAssignment($"Slot {slot + 1}" + (slot >= count ? " (inactive)" : ""), entries[i].tableId, i);
                        empty = false;
                    }
                    if (empty && slot < count) DrawAssignment($"Slot {slot + 1}", "Unassigned", -1);
                }
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                    DrawAssignment($"Page {1 + i / count}   Slot {1 + i % count}", entries[i].tableId, i);
                if (entries.Count == 0) DrawAssignment("Page 1", "No table IDs assigned", -1);
            }
        }

        void DrawAssignment(string position, string value, int index, bool heading = false)
        {
            Button row = ManualButton(manualRows, position + "      " + value,
                index >= 0 ? () => SelectManualEntry(index) : (Action)null);
            var left = row.GetComponentInChildren<TMP_Text>();
            var right = Instantiate(left, left.transform.parent, false);
            SetCell(left, position, 0, .3f);
            SetCell(right, value, .3f, 1);
            row.image.color = heading ? new Color32(255, 219, 177, 255)
                : index >= 0 && index == editingIndex ? new Color32(255, 213, 120, 255) : Color.white;
            if (heading) left.fontStyle = right.fontStyle = FontStyles.Bold;
            if (index < 0)
            {
                // A heading or an empty assignment is plain text and keeps full contrast.
                row.transition = Selectable.Transition.None;
                row.image.canvasRenderer.SetColor(Color.white);
            }
        }

        static void SetCell(TMP_Text cell, string text, float start, float end)
        {
            cell.text = text;
            cell.alignment = TextAlignmentOptions.MidlineLeft;
            cell.raycastTarget = false;
            var rect = cell.rectTransform;
            rect.anchorMin = new Vector2(start, 0);
            rect.anchorMax = new Vector2(end, 1);
            rect.offsetMin = new Vector2(14, 0);
            rect.offsetMax = new Vector2(-8, 0);
        }
    }
}
