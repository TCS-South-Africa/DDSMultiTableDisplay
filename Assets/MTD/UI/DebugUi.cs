using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MTD
{
    // An MTD UI made entirely in code: a row (or grid) of slots with plain text widgets. It needs no prefabs or
    // artwork, so it works in an empty project, and it stands in whenever the configured MTD UI cannot be loaded.
    public static class DebugUi
    {
        const float statusStrip = 0.11f; // The share of the screen's height kept for the status details.
        const float gap = 6f;

        public static MtdLayout Build(MtdOrientation orientation, int slotCount, Transform parent)
        {
            bool portrait = orientation == MtdOrientation.Portrait;

            GameObject root = new GameObject("Debug UI", typeof(RectTransform));
            root.transform.SetParent(parent, false);

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = portrait ? new Vector2(1080, 1920) : new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            Image backdrop = CreateRect("Backdrop", root.transform, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            backdrop.color = new Color(0.04f, 0.045f, 0.06f);
            backdrop.raycastTarget = false;

            MtdLayout layout = root.AddComponent<MtdLayout>();
            layout.orientation = orientation;
            layout.slots = BuildSlots(root.transform, portrait, slotCount);

            DebugTableWidget template = BuildWidgetTemplate(root.transform);
            layout.widgets.Add(new MtdLayout.WidgetEntry { family = GameFamily.Roulette, anyVariant = true, prefab = template });
            layout.widgets.Add(new MtdLayout.WidgetEntry { family = GameFamily.SicBo, prefab = template });

            layout.statusView = BuildStatusView(root);
            return layout;
        }

        // Landscape: one row. Portrait: two columns.
        static RectTransform[] BuildSlots(Transform root, bool portrait, int slotCount)
        {
            int columns = portrait ? 2 : slotCount;
            int rows = Mathf.CeilToInt(slotCount / (float)columns);
            float top = 1f;
            float bottom = statusStrip;

            RectTransform[] slots = new RectTransform[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                int column = i % columns;
                int row = i / columns;
                float rowHeight = (top - bottom) / rows;

                Vector2 min = new Vector2(column / (float)columns, top - (row + 1) * rowHeight);
                Vector2 max = new Vector2((column + 1) / (float)columns, top - row * rowHeight);
                RectTransform slot = CreateRect($"Slot {i + 1}", root, min, max);
                slot.offsetMin = new Vector2(gap, gap);
                slot.offsetMax = new Vector2(-gap, -gap);
                slots[i] = slot;
            }

            return slots;
        }

        // Kept inactive under the layout and cloned per slot, which is what a widget prefab would be in a real UI.
        static DebugTableWidget BuildWidgetTemplate(Transform root)
        {
            RectTransform rect = CreateRect("Debug Table Widget", root, Vector2.zero, Vector2.one);
            rect.gameObject.SetActive(false);

            DebugTableWidget widget = rect.gameObject.AddComponent<DebugTableWidget>();
            widget.background = rect.gameObject.AddComponent<Image>();
            widget.background.raycastTarget = false;

            RectTransform textRect = CreateRect("Text", rect, Vector2.zero, Vector2.one);
            textRect.offsetMin = new Vector2(14, 10);
            textRect.offsetMax = new Vector2(-14, -10);
            widget.text = CreateText(textRect.gameObject, 20, TextAlignmentOptions.TopLeft);

            return widget;
        }

        static DebugStatusView BuildStatusView(GameObject root)
        {
            DebugStatusView view = root.AddComponent<DebugStatusView>();

            RectTransform detailsRect = CreateRect("Status Details", root.transform, Vector2.zero, new Vector2(1f, statusStrip));
            detailsRect.offsetMin = new Vector2(16, 6);
            detailsRect.offsetMax = new Vector2(-16, -6);
            view.details = CreateText(detailsRect.gameObject, 17, TextAlignmentOptions.BottomLeft);
            view.details.color = new Color(0.72f, 0.76f, 0.84f);

            RectTransform panel = CreateRect("Status Headline", root.transform, new Vector2(0.1f, 0.42f), new Vector2(0.9f, 0.62f));
            Image panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.82f);
            panelImage.raycastTarget = false;
            view.headlinePanel = panel.gameObject;

            RectTransform headlineRect = CreateRect("Text", panel, Vector2.zero, Vector2.one);
            headlineRect.offsetMin = new Vector2(24, 12);
            headlineRect.offsetMax = new Vector2(-24, -12);
            view.headline = CreateText(headlineRect.gameObject, 40, TextAlignmentOptions.Center);

            panel.gameObject.SetActive(false);
            return view;
        }

        static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        static TMP_Text CreateText(GameObject gameObject, float size, TextAlignmentOptions alignment)
        {
            TextMeshProUGUI text = gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = alignment;
            text.richText = true;
            text.raycastTarget = false;
            text.color = Color.white;
            return text;
        }
    }
}
