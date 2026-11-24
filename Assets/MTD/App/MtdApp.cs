using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MTD
{
    // The application. Reads the configuration, then wires the pieces in the order data flows:
    // transport -> main-thread queue -> registry -> eligible tables -> rotation -> display.
    //
    // Nothing is applied piecemeal. Changing the configuration tears all of this down and builds it again.
    public sealed class MtdApp : MonoBehaviour
    {
        const float statusIntervalSeconds = 0.5f;
        const int maxReportedProblems = 50;

        readonly HashSet<string> reportedProblems = new HashSet<string>();
        readonly MtdStatus status = new MtdStatus();

        MainThreadQueue queue;
        IMtdTransport transport;
        TConnectMtdClient tConnectClient;
        TableRegistry registry;
        ITableListSource tableListSource;
        TableIdListSource tConnectTableList;
        RotationController rotation;
        MtdDisplayController display;
        MtdUiLoader.LoadedUi ui;
        GameObject uiRoot;

        int seenRegistryVersion = -1;
        int seenSourceVersion = -1;
        int eligibleCount;
        float nextStatusAt;
        string configurationNote;
        bool built;

        public MtdConfigurationData Configuration { get; private set; }

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Awake()
        {
            Application.runInBackground = true; // A display must keep updating when another window has focus.
            Cursor.visible = false;
        }

        void Start()
        {
            LoadConfiguration();
            Build();
        }

        void OnDestroy()
        {
            Teardown();
        }

        void LoadConfiguration()
        {
            Configuration = MtdConfigurationStore.Load(out MtdConfigurationStore.LoadSource source);
            configurationNote = source switch
            {
                MtdConfigurationStore.LoadSource.Backup => "The configuration file was damaged. The last known good copy was restored.",
                MtdConfigurationStore.LoadSource.Defaults => "No configuration was found. Defaults are in use.",
                _ => null,
            };
        }

        // Saves a new configuration and rebuilds everything from it. Called by the Configuration Menu.
        public void ApplyConfiguration(MtdConfigurationData newConfiguration)
        {
            MtdConfigurationStore.Save(newConfiguration);
            Teardown();
            LoadConfiguration();
            Build();
        }

        void Build()
        {
            MtdConfigurationData config = Configuration;
            queue = new MainThreadQueue();
            reportedProblems.Clear();

            // The MTD UI. As in DDS, orientation is whatever shape the screen is.
            MtdOrientation orientation = Screen.height > Screen.width ? MtdOrientation.Portrait : MtdOrientation.Landscape;
            uiRoot = new GameObject("MTD UI");
            uiRoot.transform.SetParent(transform, false);
            ui = MtdUiLoader.Load(config.display.mtdUi, orientation, config.display.numberOfTables, uiRoot.transform);
            if (ui.Warning != null) Debug.Log($"MTD: {ui.Warning}");

            registry = new TableRegistry(config.tables.tableTimeoutSeconds);
            display = gameObject.AddComponent<MtdDisplayController>();
            display.Initialise(ui.Layout, tableId => registry.TryGet(tableId, out TableState state) ? state.Snapshot : null);

            rotation = new RotationController(new RotationSettings
            {
                SlotCount = ui.Layout.SlotCount,
                Mode = config.switching.mode,
                DwellSeconds = config.switching.dwellTimeSeconds,
                SlotSwapIntervalSeconds = config.switching.slotSwapIntervalSeconds,
            });
            rotation.SlotChanged += OnSlotChanged;

            CreateTransport(config);

            seenRegistryVersion = -1;
            seenSourceVersion = -1;
            eligibleCount = 0;
            nextStatusAt = 0;
            built = true;

            if (transport != null)
            {
                transport.Subscribe(MtdTopics.TableSummaryFilter(config.tConnect.siteName), queue.PostMessage);
                transport.Start();
                tConnectClient?.Start();
            }
        }

        void CreateTransport(MtdConfigurationData config)
        {
            transport = null;
            tConnectClient = null;
            tConnectTableList = null;

            if (config.display.demoMode)
            {
                transport = new SimulatedTransport(config.display.demoTables);
                tableListSource = new DiscoveryTableListSource();
                return;
            }

            bool useTConnectList = config.EffectiveTableListSource == TableListSourceKind.TConnect;
            if (useTConnectList)
            {
                tConnectTableList = new TableIdListSource();
                tableListSource = tConnectTableList;
            }
            else
            {
                tableListSource = new DiscoveryTableListSource(MtdNumberFilter.Parse(config.tables.mtdTableNumbers));
            }

            if (!config.tConnect.connect || config.tConnect.url.Length == 0) return;

            MtdConnectionSettings connection = new MtdConnectionSettings
            {
                Host = config.tConnect.url,
                Port = config.tConnect.port,
                Username = config.tConnect.username,
                Password = config.tConnect.password,
                UseTls = config.tConnect.useTLS,
                AllowUntrustedCertificate = config.tConnect.allowUntrustedCertificate,
            };

            if (config.tConnect.useNats)
            {
                NatsMtdTransport nats = new NatsMtdTransport(connection);
                transport = nats;
                if (useTConnectList)
                {
                    tConnectClient = new TConnectMtdClient(nats, config.DeviceId, config.tConnect.mtdName, queue, tConnectTableList);
                }
            }
            else
            {
                transport = new MqttMtdTransport(connection);
            }
        }

        void Teardown()
        {
            if (!built) return;
            built = false;

            tConnectClient?.Dispose();
            tConnectClient = null;
            transport?.Dispose();
            transport = null;

            if (rotation != null) rotation.SlotChanged -= OnSlotChanged;
            rotation = null;

            if (display != null)
            {
                display.Clear();
                Destroy(display);
                display = null;
            }

            if (uiRoot != null) Destroy(uiRoot);
            uiRoot = null;
            ui = null;
        }

        void Update()
        {
            if (!built) return;

            double now = Now;
            queue.Drain(OnSummary);
            if (queue.DroppedMessages > 0)
            {
                ReportOnce($"MTD stopped processing messages for a while, and the oldest ones waiting were let go (limit {MainThreadQueue.DefaultMessageLimit}).");
            }

            registry.Tick(now);

            if (registry.MembershipVersion != seenRegistryVersion || tableListSource.Version != seenSourceVersion)
            {
                seenRegistryVersion = registry.MembershipVersion;
                seenSourceVersion = tableListSource.Version;

                List<Guid> eligible = TableSelection.GetEligibleOrdered(registry.Tables, tableListSource, display.CanShow, Configuration.tables.skipClosedTables);
                eligibleCount = eligible.Count;
                rotation.SetEligible(eligible, now);
            }

            rotation.Tick(now);

            if (Time.unscaledTime >= nextStatusAt)
            {
                nextStatusAt = Time.unscaledTime + statusIntervalSeconds;
                UpdateStatus();
                display.ShowStatus(new MtdStatus { Headline = status.Headline, Details = status.Details });
            }
        }

        // A table summary, now on the main thread.
        void OnSummary(string topic, byte[] payload)
        {
            TableSnapshot snapshot = SummaryReader.Read(topic, payload, out MtdTableSummaryMessage message, out string problem);
            if (snapshot == null)
            {
                ReportOnce(problem);
                return;
            }

            if (message.SchemaVersion > MtdTableSummaryMessage.CurrentSchemaVersion)
            {
                ReportOnce($"Table {snapshot.TableName}{snapshot.TableNumber} sends summary version {message.SchemaVersion}; this MTD understands version {MtdTableSummaryMessage.CurrentSchemaVersion} and reads the fields it knows.");
            }

            if (snapshot.Family == GameFamily.Unsupported || snapshot.Family == GameFamily.Blackjack)
            {
                ReportOnce($"Table {snapshot.TableName}{snapshot.TableNumber} is a {snapshot.GameType} table, which MTD does not show.");
            }

            if (snapshot.CurrentResultUnreadable)
            {
                ReportOnce($"Table {snapshot.TableName}{snapshot.TableNumber} sent a latest result MTD cannot read (\"{snapshot.UnreadableCurrentResult}\"). Its current result is shown as unavailable.");
            }

            TableChange change = registry.Apply(snapshot, Now);

            // A table that has just appeared is placed by the rotation; only tables already known need refreshing.
            if (change == TableChange.NewResult || change == TableChange.Corrected || change == TableChange.Updated)
            {
                display.RefreshTable(snapshot, change);
            }
        }

        void OnSlotChanged(int slot, Guid? tableId)
        {
            TableSnapshot snapshot = null;
            if (tableId.HasValue && registry.TryGet(tableId.Value, out TableState state)) snapshot = state.Snapshot;
            display.SetSlot(slot, snapshot);
        }

        // Each distinct problem is logged once, so a table that keeps sending the same bad message cannot flood the log.
        void ReportOnce(string problem)
        {
            if (reportedProblems.Count >= maxReportedProblems || !reportedProblems.Add(problem)) return;
            Debug.Log($"MTD: {problem}");
        }

        void UpdateStatus()
        {
            MtdConfigurationData config = Configuration;
            status.Headline = BuildHeadline(config);

            StringBuilder details = new StringBuilder(256);
            if (config.display.demoMode)
            {
                details.Append("Demo Mode: ").Append(config.display.demoTables).Append(" simulated tables");
            }
            else if (transport == null)
            {
                details.Append("Not connecting (T-Connect|Connect is off or no URL is set)");
            }
            else
            {
                details.Append(transport.Name).Append(' ').Append(config.tConnect.url).Append(':').Append(config.EffectivePort)
                    .Append(transport.Connected ? "  connected" : "  NOT connected");
                if (config.tConnect.useTLS) details.Append(config.tConnect.allowUntrustedCertificate ? ", TLS with the certificate NOT checked" : ", TLS");

                // Shown whether or not the connection is up: a broker can be reachable and still refuse the subscription.
                string transportProblem = transport.LastError;
                if (transportProblem != null) details.Append(" (").Append(transportProblem).Append(')');
            }

            if (tConnectClient != null)
            {
                details.Append("   |   T-Connect: ").Append(tConnectClient.State);
                if (tConnectClient.State == TConnectAssignmentState.Assigned) details.Append(" as \"").Append(tConnectClient.DisplayName ?? config.tConnect.mtdName).Append('"');
                if (!string.IsNullOrEmpty(tConnectClient.AssignmentCode)) details.Append("  code ").Append(tConnectClient.AssignmentCode);
                if (tConnectClient.TableListProblem != null) details.Append("  (").Append(tConnectClient.TableListProblem).Append(')');
            }

            details.Append('\n').Append("Tables: ").Append(tableListSource.Description)
                .Append("   |   reporting ").Append(registry.FreshCount)
                .Append("   |   showable ").Append(eligibleCount)
                .Append("   |   UI: ").Append(ui.DisplayName).Append(", ").Append(ui.Layout.SlotCount).Append(" slots, ")
                .Append(config.switching.mode == SwitchingMode.Pages ? "pages" : "slot by slot").Append(" every ").Append(config.switching.dwellTimeSeconds).Append(" s")
                .Append("   |   device ").Append(config.tConnect.deviceId);

            if (ui.Warning != null) details.Append('\n').Append(ui.Warning);
            if (configurationNote != null) details.Append('\n').Append(configurationNote);

            status.Details = details.ToString();
        }

        // The single most useful thing to tell someone looking at the screen, or nothing when tables are showing.
        string BuildHeadline(MtdConfigurationData config)
        {
            if (eligibleCount > 0) return "";

            if (transport == null)
            {
                return "Not connected. Press C to open the Configuration Menu.";
            }

            string transportProblem = transport.LastError;
            if (!transport.Connected)
            {
                string connecting = $"Connecting to {transport.Name} at {config.tConnect.url}:{config.EffectivePort}";
                return transportProblem == null ? connecting : $"{connecting}\n{Capitalise(transportProblem)}";
            }

            // Connected, but the broker will not let this display have what it asked for.
            if (transportProblem != null)
            {
                return $"Connected to {transport.Name}, but {transportProblem}";
            }

            if (tConnectClient != null)
            {
                switch (tConnectClient.State)
                {
                    case TConnectAssignmentState.Assigned:
                        if (tConnectTableList != null && !tConnectTableList.HasList) return "Waiting for the table list from T-Connect";
                        break;
                    case TConnectAssignmentState.Conflict:
                        return $"T-Connect refused the name \"{config.tConnect.mtdName}\": it belongs to another device"
                            + (string.IsNullOrEmpty(tConnectClient.AssignmentCode) ? "" : $" (code {tConnectClient.AssignmentCode})");
                    case TConnectAssignmentState.NoReply:
                        return "Waiting for T-Connect: the server is not answering";
                    default:
                        return $"Registering with T-Connect as \"{config.tConnect.mtdName}\"";
                }
            }

            return "Waiting for tables";
        }

        static string Capitalise(string text)
        {
            return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }
}
