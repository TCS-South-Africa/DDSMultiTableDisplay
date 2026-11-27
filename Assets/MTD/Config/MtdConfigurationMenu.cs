using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MTD
{
    // The Configuration Menu. It looks like DDS's because it is made from DDS's own control templates, but it is
    // put together differently: every row is created here, at start-up, from the list in DefineOptions.
    //
    // To add an option:
    //  1. add the field to MtdConfigurationData (and a range check in Normalise if it needs one);
    //  2. add one line to DefineOptions.
    // No prefab needs editing. As in DDS, an option's key is "Group|Label" and the label shown is the text after
    // the last "|".
    //
    // C opens the menu. Controls edit a draft copy; Cancel throws it away, and Confirm & Save writes it and has
    // MtdApp rebuild everything from the new configuration.
    public sealed partial class MtdConfigurationMenu : MonoBehaviour
    {
        static readonly Color tabSelectedColour = new Color(1f, 0.627f, 0.25f, 0f); // The same pair DDS uses.
        static readonly Color tabUnselectedColour = new Color(1f, 0.627f, 0.25f, 1f);
        const int tooltipSortingOrder = 1000;

        [Header("Shell (set by the menu builder)")]
        public GameObject panel;
        public CanvasScaler canvasScaler;
        public RectTransform tabButtonRow;
        public GameObject tabPanelTemplate; // Inactive. Holds a Scrollview whose Content stacks sub panels.
        public Button cancelButton;
        public Button confirmButton;

        [Header("DDS control templates")]
        public GameObject menuButtonTemplate;
        public GameObject subPanelTemplate;
        public GameObject checkboxTemplate;
        public GameObject stringInputTemplate;
        public GameObject dropdownTemplate;

        readonly List<Option> options = new List<Option>();
        readonly List<Tab> tabs = new List<Tab>();

        MtdApp app;
        MtdConfigurationData draft;
        string originalJson;
        bool isPopulating;

        public bool IsOpen => panel != null && panel.activeSelf;

        sealed class Tab
        {
            public string Name;
            public Image Button;
            public GameObject Panel;
            public Transform Content;
            public readonly Dictionary<string, Transform> Sections = new Dictionary<string, Transform>();
        }

        // One row. The delegates move its value between the draft configuration and the control.
        sealed class Option
        {
            public string Key;
            public string Tab;
            public string Tooltip;
            public Selectable Control;
            public Action<MtdConfigurationData> Show; // Draft to control.
            public Func<MtdConfigurationData, bool> IsEnabled; // Null means always enabled.
        }

        void Awake()
        {
            app = FindAnyObjectByType<MtdApp>();
            panel.SetActive(false);
            tabPanelTemplate.SetActive(false);

            DefineOptions();
            cancelButton.onClick.AddListener(OnCancel);
            confirmButton.onClick.AddListener(OnConfirm);
        }

        void Update()
        {
            if (IsOpen || app == null || app.Configuration == null) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.cKey.wasPressedThisFrame) Open();
        }

        // ---------------------------------------------------------------------------------------------------
        // The options. Order here is the order on screen.
        // ---------------------------------------------------------------------------------------------------

        void DefineOptions()
        {
            const string tConnect = "T-Connect";
            AddCheckbox(tConnect, "T-Connect|Connect", "Connect to the DDS results broker. Manual Entry uses this connection without registering the display with T-Connect.",
                c => c.tConnect.connect, (c, v) => c.tConnect.connect = v);
            AddText(tConnect, "T-Connect|URL", "The broker's host name or IP address.", "none",
                c => c.tConnect.url, (c, v) => c.tConnect.url = v);
            AddNumber(tConnect, "T-Connect|Port", "Leave blank for the default: 4222 for NATS, 1883 for MQTT.", "default",
                c => c.tConnect.port, (c, v) => c.tConnect.port = v, blankWhenZero: true);
            AddText(tConnect, "T-Connect|Username", null, "none",
                c => c.tConnect.username, (c, v) => c.tConnect.username = v);
            AddText(tConnect, "T-Connect|Password", null, "none",
                c => c.tConnect.password, (c, v) => c.tConnect.password = v,
                isPassword: true);
            AddText(tConnect, "T-Connect|Site Name", "MQTT only. Must match the Site Name set on the DDS tables. Not used with NATS.", "testsite",
                c => c.tConnect.siteName, (c, v) => c.tConnect.siteName = v,
                isEnabled: c => !c.tConnect.useNats);
            AddCheckbox(tConnect, "T-Connect|TLS", "Encrypt the connection and check the broker's certificate. The certificate must be for the host in URL, and be trusted by Windows or issued by a certificate placed in " + MtdTlsTrust.DefaultTrustedFolder + ".",
                c => c.tConnect.useTLS, (c, v) => c.tConnect.useTLS = v);
            AddCheckbox(tConnect, "T-Connect|Allow Untrusted Certificate", "TLS only. Accept the broker's certificate without checking it. The connection is still encrypted, but MTD can no longer tell whether it reached the real broker. Leave unticked wherever the certificate can be trusted instead.",
                c => c.tConnect.allowUntrustedCertificate, (c, v) => c.tConnect.allowUntrustedCertificate = v,
                isEnabled: c => c.tConnect.useTLS);
            AddCheckbox(tConnect, "T-Connect|Use NATS", "Connect over NATS. Untick to use MQTT. The T-Connect table list needs NATS.",
                c => c.tConnect.useNats, (c, v) => c.tConnect.useNats = v);
            AddText(tConnect, "T-Connect|MTD Name", "The name this display registers under with T-Connect.", "MTD 01",
                c => c.tConnect.mtdName, (c, v) => c.tConnect.mtdName = v);
            AddText(tConnect, "T-Connect|Device ID", "This display's identity on T-Connect. Created on first run.", "",
                c => c.tConnect.deviceId, null,
                isEnabled: c => false, shrinkToFit: true);

            const string tables = "Tables";
            AddDropdown(tables, "Tables|Table List Source", "T-Connect uses the server list on NATS. Manual Entry uses the DDS table IDs entered below, over NATS or MQTT.",
                c => new List<Choice> { new Choice("T-Connect", TableListSourceKind.TConnect.ToString()), new Choice("Manual Entry", TableListSourceKind.ManualEntry.ToString()) },
                c => c.EffectiveTableListSource.ToString(),
                (c, v) => c.tables.tableListSource = (TableListSourceKind)Enum.Parse(typeof(TableListSourceKind), v),
                isEnabled: c => c.tConnect.useNats);
            AddNumber(tables, "Tables|Table Timeout (s)", "A table that has sent nothing for this long is dropped until it reports again. Tables report every 10 seconds, so the least this can be is 20.", "35",
                c => c.tables.tableTimeoutSeconds, (c, v) => c.tables.tableTimeoutSeconds = v);
            AddCheckbox(tables, "Tables|Skip Closed Tables", "Leave closed tables out of the rotation.",
                c => c.tables.skipClosedTables, (c, v) => c.tables.skipClosedTables = v);

            BuildManualEditor();

            const string displayTab = "Display";
            AddDropdown(displayTab, "Display|MTD UI", "The look of the display.",
                BuildUiChoices,
                c => c.display.mtdUi,
                (c, v) => c.display.mtdUi = v);
            AddDropdown(displayTab, "Display|Number Of Tables", "How many tables are on screen at once.",
                c => new List<Choice> { new Choice("4", "4"), new Choice("6", "6") },
                c => c.display.numberOfTables.ToString(CultureInfo.InvariantCulture),
                (c, v) => c.display.numberOfTables = int.Parse(v, CultureInfo.InvariantCulture));
            AddCheckbox(displayTab, "Display|Use MTD Table Unique Name", "Show the DDS display alias. An empty alias uses the ordinary table name and number, for example AR 01.",
                c => c.display.useMtdTableUniqueName, (c, v) => c.display.useMtdTableUniqueName = v);
            AddCheckbox(displayTab, "Display|Demo Mode", "Show simulated tables instead of connecting. Nothing on screen is real.",
                c => c.display.demoMode, (c, v) => c.display.demoMode = v);
            AddNumber(displayTab, "Display|Demo Tables", "How many simulated tables Demo Mode runs.", "12",
                c => c.display.demoTables, (c, v) => c.display.demoTables = v,
                isEnabled: c => c.display.demoMode);

            const string switching = "Switching";
            AddDropdown(switching, "Switching|Mode", "Pages: every table on screen changes together. Slot By Slot: each slot cycles through its assigned tables on its own timer.",
                c => new List<Choice> { new Choice("Pages", SwitchingMode.Pages.ToString()), new Choice("Slot By Slot", SwitchingMode.SlotBySlot.ToString()) },
                c => c.switching.mode.ToString(),
                (c, v) => c.switching.mode = (SwitchingMode)Enum.Parse(typeof(SwitchingMode), v));
            AddNumber(switching, "Switching|Page Dwell Time (s)", "How long each page stays on screen.", "120",
                c => c.switching.dwellTimeSeconds, (c, v) => c.switching.dwellTimeSeconds = v,
                isEnabled: c => c.switching.mode == SwitchingMode.Pages);
            for (int i = 0; i < 6; i++)
            {
                int slot = i;
                AddNumber(switching, $"Switching|Slot {i + 1} Interval (s)", "How long a table stays in this slot before the next assigned table appears.", "120",
                    c => c.switching.slotIntervals[slot], (c, v) => c.switching.slotIntervals[slot] = Mathf.Clamp(v, 1, 86400),
                    isEnabled: c => c.switching.mode == SwitchingMode.SlotBySlot && slot < c.display.numberOfTables);
            }
        }

        // Every MTD UI in this build. Blank means "the first one", so that stays selectable as what it is; a UI
        // named in the file but missing from the build is kept as a choice instead of being silently replaced.
        static List<Choice> BuildUiChoices(MtdConfigurationData configuration)
        {
            List<MtdUiLoader.UiChoice> installed = MtdUiLoader.ListUis();
            List<Choice> choices = new List<Choice>();

            string current = configuration.display.mtdUi;
            bool currentIsInstalled = current.Length == 0;
            choices.Add(new Choice($"Default ({installed[0].DisplayName})", ""));
            for (int i = 0; i < installed.Count; i++)
            {
                choices.Add(new Choice(installed[i].DisplayName, installed[i].Id));
                if (string.Equals(installed[i].Id, current, StringComparison.OrdinalIgnoreCase)) currentIsInstalled = true;
            }

            if (!currentIsInstalled) choices.Add(new Choice($"{current} (not installed)", current));
            return choices;
        }

        // ---------------------------------------------------------------------------------------------------
        // Opening, closing and saving.
        // ---------------------------------------------------------------------------------------------------

        public void Open()
        {
            if (IsOpen || app == null || app.Configuration == null) return;

            draft = app.Configuration.Clone();
            originalJson = JsonUtility.ToJson(draft);
            ResetManualInput();

            // Authored for a 16:9 screen either way up, like DDS's two menu prefabs.
            canvasScaler.referenceResolution = Screen.height > Screen.width ? new Vector2(1080, 1920) : new Vector2(1920, 1080);

            panel.SetActive(true);
            Cursor.visible = true;
            SelectTab(0);
            Populate();
        }

        void Close()
        {
            panel.SetActive(false);
            Cursor.visible = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            draft = null;
        }

        void OnCancel()
        {
            Close();
        }

        void OnConfirm()
        {
            if (draft.EffectiveTableListSource == TableListSourceKind.ManualEntry && ManualInputPending() && !StoreManualInput()) return;
            MtdConfigurationData toSave = draft;
            bool changed = JsonUtility.ToJson(toSave) != originalJson;
            Close();

            if (!changed) return;

            try
            {
                app.ApplyConfiguration(toSave);
            }
            catch (Exception exception)
            {
                Debug.LogError($"MTD: the configuration could not be saved: {exception.Message}");
            }
        }

        // Draft to controls.
        void Populate()
        {
            isPopulating = true;
            foreach (Option option in options)
            {
                option.Show(draft);
                option.Control.interactable = option.IsEnabled == null || option.IsEnabled(draft);
            }

            RefreshManualEditor();
            isPopulating = false;
        }

        // A control changed. The draft has already been updated; other rows may depend on the new value.
        void OnControlChanged()
        {
            if (!isPopulating) Populate();
        }

        void SelectTab(int index)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                tabs[i].Panel.SetActive(i == index);
                tabs[i].Button.color = i == index ? tabSelectedColour : tabUnselectedColour;
            }
        }

        // ---------------------------------------------------------------------------------------------------
        // Building rows from the DDS templates.
        // ---------------------------------------------------------------------------------------------------

        readonly struct Choice
        {
            public readonly string Label;
            public readonly string Value;

            public Choice(string label, string value)
            {
                Label = label;
                Value = value;
            }
        }

        void AddCheckbox(string tab, string key, string tooltip, Func<MtdConfigurationData, bool> get, Action<MtdConfigurationData, bool> set,
            Func<MtdConfigurationData, bool> isEnabled = null)
        {
            GameObject row = CreateRow(checkboxTemplate, tab, key, "Checkbox");
            Toggle toggle = row.GetComponentInChildren<Toggle>(true);

            toggle.onValueChanged.AddListener(value =>
            {
                if (isPopulating) return;
                set(draft, value);
                OnControlChanged();
            });

            Register(tab, key, tooltip, toggle, isEnabled, configuration => toggle.SetIsOnWithoutNotify(get(configuration)));
        }

        void AddText(string tab, string key, string tooltip, string placeholder, Func<MtdConfigurationData, string> get, Action<MtdConfigurationData, string> set,
            Func<MtdConfigurationData, bool> isEnabled = null, bool shrinkToFit = false, bool isPassword = false)
        {
            // A password is drawn as asterisks, while it is typed as well as when the menu opens: the display this
            // runs on may be in public view.
            TMP_InputField input = CreateInput(tab, key, placeholder, isPassword ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard);
            if (shrinkToFit)
            {
                // For a value that is read, not typed, and is longer than the field at the normal size.
                input.textComponent.enableAutoSizing = true;
                input.textComponent.fontSizeMin = 12;
                input.textComponent.fontSizeMax = 36;
            }

            input.onEndEdit.AddListener(value =>
            {
                if (isPopulating || set == null) return;
                set(draft, (value ?? "").Trim());
                OnControlChanged();
            });

            Register(tab, key, tooltip, input, isEnabled, configuration => input.SetTextWithoutNotify(get(configuration) ?? ""));
        }

        // A whole number. Anything that is not one leaves the value as it was. The saved configuration is put back
        // inside its legal range by MtdConfigurationData.Normalise.
        void AddNumber(string tab, string key, string tooltip, string placeholder, Func<MtdConfigurationData, int> get, Action<MtdConfigurationData, int> set,
            Func<MtdConfigurationData, bool> isEnabled = null, bool blankWhenZero = false)
        {
            TMP_InputField input = CreateInput(tab, key, placeholder, TMP_InputField.ContentType.IntegerNumber);

            input.onEndEdit.AddListener(value =>
            {
                if (isPopulating) return;

                string text = (value ?? "").Trim();
                if (text.Length == 0 && blankWhenZero) set(draft, 0);
                else if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)) set(draft, number);

                OnControlChanged();
            });

            Register(tab, key, tooltip, input, isEnabled, configuration =>
            {
                int value = get(configuration);
                input.SetTextWithoutNotify(value == 0 && blankWhenZero ? "" : value.ToString(CultureInfo.InvariantCulture));
            });
        }

        void AddDropdown(string tab, string key, string tooltip, Func<MtdConfigurationData, List<Choice>> getChoices,
            Func<MtdConfigurationData, string> get, Action<MtdConfigurationData, string> set, Func<MtdConfigurationData, bool> isEnabled = null)
        {
            GameObject row = CreateRow(dropdownTemplate, tab, key, "Dropdown Menu");
            TMP_Dropdown dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
            List<Choice> shown = new List<Choice>();

            dropdown.onValueChanged.AddListener(index =>
            {
                if (isPopulating || index < 0 || index >= shown.Count) return;
                set(draft, shown[index].Value);
                OnControlChanged();
            });

            Register(tab, key, tooltip, dropdown, isEnabled, configuration =>
            {
                shown.Clear();
                shown.AddRange(getChoices(configuration));

                dropdown.ClearOptions();
                List<string> labels = new List<string>();
                foreach (Choice choice in shown) labels.Add(choice.Label);
                dropdown.AddOptions(labels);

                string current = get(configuration);
                int selected = shown.FindIndex(choice => string.Equals(choice.Value, current, StringComparison.OrdinalIgnoreCase));
                dropdown.SetValueWithoutNotify(Mathf.Max(0, selected));
                dropdown.RefreshShownValue();
            });
        }

        TMP_InputField CreateInput(string tab, string key, string placeholder, TMP_InputField.ContentType contentType)
        {
            GameObject row = CreateRow(stringInputTemplate, tab, key, "String Input");
            TMP_InputField input = row.GetComponentInChildren<TMP_InputField>(true);
            input.contentType = contentType;
            input.lineType = TMP_InputField.LineType.SingleLine;
            if (input.placeholder is TMP_Text placeholderText) placeholderText.text = placeholder ?? "";
            return input;
        }

        void Register(string tab, string key, string tooltip, Selectable control, Func<MtdConfigurationData, bool> isEnabled, Action<MtdConfigurationData> show)
        {
            SetUpTooltip(control, tooltip);
            options.Add(new Option { Key = key, Tab = tab, Tooltip = tooltip, Control = control, IsEnabled = isEnabled, Show = show });
        }

        // Rows are named the way DDS names them ("T-Connect|URL String Input") and sit in a sub panel titled with
        // the key's group ("T-Connect:").
        GameObject CreateRow(GameObject template, string tabName, string key, string suffix)
        {
            int lastPipe = key.LastIndexOf('|');
            string group = lastPipe > 0 ? key.Substring(0, lastPipe) : tabName;
            string label = key.Substring(lastPipe + 1);

            GameObject row = Instantiate(template, GetSection(tabName, group), false);
            row.name = $"{key} {suffix}";
            row.GetComponentInChildren<TMP_Text>(true).text = label; // The label is the row's first text.
            return row;
        }

        Transform GetSection(string tabName, string group)
        {
            Tab tab = tabs.Find(candidate => candidate.Name == tabName) ?? CreateTab(tabName);
            if (tab.Sections.TryGetValue(group, out Transform rows)) return rows;

            GameObject subPanel = Instantiate(subPanelTemplate, tab.Content, false);
            subPanel.name = $"{group} Sub Panel";
            subPanel.GetComponentInChildren<TMP_Text>(true).text = group + ":";

            rows = FindDeep(subPanel.transform, "Sub Panel Configurations");
            tab.Sections[group] = rows;
            return rows;
        }

        Tab CreateTab(string tabName)
        {
            int index = tabs.Count;

            GameObject button = Instantiate(menuButtonTemplate, tabButtonRow, false);
            button.name = $"{tabName} Button";
            button.GetComponentInChildren<TMP_Text>(true).text = tabName;
            button.GetComponent<Button>().onClick.AddListener(() => SelectTab(index));

            GameObject tabPanel = Instantiate(tabPanelTemplate, tabPanelTemplate.transform.parent, false);
            tabPanel.name = $"{tabName} Configuration Panel";

            Tab tab = new Tab
            {
                Name = tabName,
                Button = button.GetComponent<Image>(),
                Panel = tabPanel,
                Content = FindDeep(tabPanel.transform, "Content"),
            };
            tabs.Add(tab);
            return tab;
        }

        // Each template carries an inactive Tooltip that its own EventTrigger shows on hover. As in DDS, the text is
        // set here, the tooltip is lifted above everything else, and a row with no tooltip has the trigger turned off.
        static void SetUpTooltip(Selectable control, string tooltip)
        {
            EventTrigger trigger = control.GetComponent<EventTrigger>();
            Transform tooltipObject = FindDeep(control.transform, "Tooltip");

            if (string.IsNullOrEmpty(tooltip) || tooltipObject == null)
            {
                if (trigger != null) trigger.enabled = false;
                return;
            }

            tooltipObject.GetComponentInChildren<TMP_Text>(true).text = tooltip;

            Canvas canvas = tooltipObject.GetComponent<Canvas>();
            if (canvas == null) canvas = tooltipObject.gameObject.AddComponent<Canvas>();

            // Unity ignores overrideSorting on a canvas that is not active, and the tooltip is inactive until the
            // pointer arrives. So it is set each time the template's own trigger has just shown the tooltip.
            if (trigger != null)
            {
                EventTrigger.Entry onEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                onEnter.callback.AddListener(eventData =>
                {
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = tooltipSortingOrder;
                });
                trigger.triggers.Add(onEnter);
            }
        }

        static Transform FindDeep(Transform root, string childName)
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate != root && candidate.name == childName) return candidate;
            }

            return null;
        }
    }
}
