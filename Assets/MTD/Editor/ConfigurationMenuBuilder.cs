using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MTD.Editor
{
    // Builds the Configuration Menu's shell prefab: everything except the option rows, which MtdConfigurationMenu
    // creates at start-up. Sizes and colours were read from DDS's landscape menu prefab
    // (Configuration_Panel (Landscape Mode).prefab), and the buttons are instances of DDS's own templates, so the
    // result matches DDS without being a copy of its 14 MB prefab.
    //
    // Run it again after changing anything here: MTD > Setup > 2. Build Configuration Menu Prefab.
    public static class ConfigurationMenuBuilder
    {
        public const string PrefabPath = "Assets/Configuration/MTD Configuration Menu.prefab";

        const string templateFolder = "Assets/Configuration/UI/";
        const string scrollviewPath = "Assets/Configuration/Scrollview.prefab";
        const string watermarkPath = "Assets/Configuration/TCS_1080p_Transparent_White.png";
        const float rowHeight = 64f;

        static readonly Color orange = new Color32(0xFF, 0xA0, 0x40, 0xFF);
        static readonly Color watermarkColour = new Color32(0xFF, 0xFF, 0xFF, 0x40);

        public static void Build()
        {
            GameObject menuButton = LoadTemplate("Menu Button.prefab");
            GameObject scrollview = AssetDatabase.LoadAssetAtPath<GameObject>(scrollviewPath);
            Sprite watermark = AssetDatabase.LoadAssetAtPath<Sprite>(watermarkPath);
            if (menuButton == null || scrollview == null || watermark == null)
            {
                Debug.LogError("MTD: the Configuration Menu cannot be built because a DDS template is missing from Assets/Configuration.");
                return;
            }

            GameObject root = new GameObject("MTD Configuration Menu", typeof(RectTransform));
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // Above any MTD UI.
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
            MtdConfigurationMenu menu = root.AddComponent<MtdConfigurationMenu>();

            RectTransform panel = CreateStretched("Configuration Canvas", root.transform);

            AddImage(CreateStretched("Background", panel), orange, null);

            Image watermarkImage = AddImage(CreateStretched("Background Image", panel), watermarkColour, watermark);
            watermarkImage.preserveAspect = true; // DDS stretches it over a fixed 16:9 panel; this one changes shape.

            // In DDS only the orange background and the watermark are drawn behind the controls: the panels below
            // have Image components, but they are switched off. So none of these panels gets one here.

            // DDS's top row switches between Display and Body configuration. MTD has one configuration, so the
            // row carries a single entry in the selected state.
            RectTransform titleRow = CreateTopRow("Primary Configuration Selection Buttons", panel);
            GameObject title = (GameObject)PrefabUtility.InstantiatePrefab(menuButton, titleRow);
            title.name = "MTD Configuration Button";
            title.GetComponent<Image>().color = new Color(orange.r, orange.g, orange.b, 0f);
            title.GetComponent<Image>().raycastTarget = false;
            title.GetComponent<Button>().interactable = false;
            ColorBlock titleColours = title.GetComponent<Button>().colors;
            titleColours.disabledColor = Color.white; // Not greyed: it is a heading, not a disabled button.
            title.GetComponent<Button>().colors = titleColours;
            title.GetComponentInChildren<TMP_Text>(true).text = "Multi-Table Display Configuration";

            RectTransform content = CreateStretched("Display Configuration Panel", panel);
            content.sizeDelta = new Vector2(0, -168);
            content.anchoredPosition = new Vector2(0, 12);

            RectTransform tabRow = CreateTopRow("Configuration Selection Buttons", content);

            RectTransform tabPanel = CreateStretched("Tab Panel Template", content);
            tabPanel.sizeDelta = new Vector2(0, -rowHeight);
            tabPanel.anchoredPosition = new Vector2(0, -rowHeight / 2);

            GameObject scroll = (GameObject)PrefabUtility.InstantiatePrefab(scrollview, tabPanel);
            Stretch((RectTransform)scroll.transform);

            // DDS's rows sit 12 px in from the panel edge and 2.5 px apart; its sub panels grow with their rows.
            RectTransform scrollContent = (RectTransform)scroll.transform.Find("Content");
            scrollContent.anchorMin = new Vector2(0, 1);
            scrollContent.anchorMax = new Vector2(1, 1);
            scrollContent.pivot = new Vector2(0.5f, 1);
            scrollContent.sizeDelta = Vector2.zero;
            scrollContent.anchoredPosition = Vector2.zero;
            VerticalLayoutGroup contentLayout = scrollContent.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(12, 12, 12, 12);
            contentLayout.spacing = 2.5f;
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            scrollContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            tabPanel.gameObject.SetActive(false);

            RectTransform buttons = CreateChild("Cancel Confirm Button Panel", panel);
            buttons.anchorMin = buttons.anchorMax = new Vector2(0.5f, 0);
            buttons.sizeDelta = new Vector2(1056, rowHeight);
            buttons.anchoredPosition = new Vector2(0, 48);
            HorizontalLayoutGroup buttonLayout = AddRowLayout(buttons.gameObject);
            buttonLayout.spacing = 12;
            buttonLayout.childAlignment = TextAnchor.UpperCenter;

            Button cancel = CreateActionButton(menuButton, buttons, "Cancel_Button", "Cancel", Color.red);
            Button confirm = CreateActionButton(menuButton, buttons, "Confirm_Button", "Confirm & Save", Color.green);

            panel.gameObject.SetActive(false);

            menu.panel = panel.gameObject;
            menu.canvasScaler = scaler;
            menu.tabButtonRow = tabRow;
            menu.tabPanelTemplate = tabPanel.gameObject;
            menu.cancelButton = cancel;
            menu.confirmButton = confirm;
            menu.menuButtonTemplate = menuButton;
            menu.subPanelTemplate = LoadTemplate("Sub Panel.prefab");
            menu.checkboxTemplate = LoadTemplate("Checkbox.prefab");
            menu.stringInputTemplate = LoadTemplate("String Input.prefab");
            menu.dropdownTemplate = LoadTemplate("Dropdown Menu.prefab");

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"MTD: built {PrefabPath}");
        }

        static GameObject LoadTemplate(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(templateFolder + fileName);
        }

        // White with coloured text, as DDS's Cancel and Confirm & Save are.
        static Button CreateActionButton(GameObject menuButton, Transform parent, string name, string label, Color textColour)
        {
            GameObject button = (GameObject)PrefabUtility.InstantiatePrefab(menuButton, parent);
            button.name = name;
            button.GetComponent<Image>().color = Color.white;
            button.AddComponent<LayoutElement>().minHeight = 48;

            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            text.text = label;
            text.color = textColour;
            text.enableAutoSizing = false;
            text.fontSize = 36;

            return button.GetComponent<Button>();
        }

        // A full-width, 64 px row pinned to the top of its parent, laying its children out side by side.
        static RectTransform CreateTopRow(string name, Transform parent)
        {
            RectTransform row = CreateChild(name, parent);
            row.anchorMin = new Vector2(0, 1);
            row.anchorMax = new Vector2(1, 1);
            row.pivot = new Vector2(0.5f, 1);
            row.sizeDelta = new Vector2(0, rowHeight);
            row.anchoredPosition = Vector2.zero;
            AddRowLayout(row.gameObject);
            return row;
        }

        static HorizontalLayoutGroup AddRowLayout(GameObject row)
        {
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return layout;
        }

        static Image AddImage(RectTransform rect, Color colour, Sprite sprite)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = colour;
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            return image;
        }

        static RectTransform CreateStretched(string name, Transform parent)
        {
            RectTransform rect = CreateChild(name, parent);
            Stretch(rect);
            return rect;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        static RectTransform CreateChild(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            return rect;
        }
    }
}
