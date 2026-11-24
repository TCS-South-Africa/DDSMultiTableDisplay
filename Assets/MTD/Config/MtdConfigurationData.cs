using System;
using UnityEngine;

namespace MTD
{
    public enum TableListSourceKind
    {
        TConnect, // The T-Connect server's table list for this display. NATS only.
        Discover, // Every table that publishes a summary, optionally narrowed by MTD table number.
    }

    // Everything the Configuration Menu edits. Saved as C:\tcs\mtd\MtdConfig.json with JsonUtility, the way DDS
    // saves its display configuration, so field names here are the JSON's property names.
    [Serializable]
    public class MtdConfigurationData
    {
        // Tables send a summary every 10 seconds (the contract's heartbeat). A timeout shorter than two of those
        // would drop healthy tables whenever a single heartbeat ran late.
        public const int MinTableTimeoutSeconds = 20;

        public TConnectSettings tConnect = new TConnectSettings();
        public TableSettings tables = new TableSettings();
        public DisplaySettings display = new DisplaySettings();
        public SwitchingSettings switching = new SwitchingSettings();

        [Serializable]
        public class TConnectSettings
        {
            public bool connect;
            public string url = "";
            public int port; // 0 means the protocol's default: 4222 for NATS, 1883 for MQTT.
            public string username = "";
            public string password = "";
            public string siteName = ""; // MQTT only. NATS subjects carry no site name.
            public bool useTLS;
            public bool allowUntrustedCertificate; // With TLS: accept the broker's certificate without checking it.
            public bool useNats = true;
            public string mtdName = "MTD 01"; // The name this display registers under with T-Connect.
            public string deviceId = ""; // Made once, on first run, and then kept.
        }

        [Serializable]
        public class TableSettings
        {
            public TableListSourceKind tableListSource = TableListSourceKind.TConnect;
            public string mtdTableNumbers = ""; // Discover only: "1-6, 19". Blank means every table.
            public int tableTimeoutSeconds = 35;
            public bool skipClosedTables = true;
        }

        [Serializable]
        public class DisplaySettings
        {
            public string mtdUi = ""; // An MTD UI id from the catalog. Blank means the first one.
            public int numberOfTables = 6;
            public bool demoMode;
            public int demoTables = 12;
        }

        [Serializable]
        public class SwitchingSettings
        {
            public SwitchingMode mode = SwitchingMode.Pages;
            public int dwellTimeSeconds = 120;
            public int slotSwapIntervalSeconds = 5;
        }

        // The server's table list travels over NATS Key-Value, which MQTT cannot read.
        public TableListSourceKind EffectiveTableListSource => tConnect.useNats ? tables.tableListSource : TableListSourceKind.Discover;

        public int EffectivePort => tConnect.port > 0 ? tConnect.port : (tConnect.useNats ? MtdTopics.DefaultNatsPort : MtdTopics.DefaultMqttPort);

        public Guid DeviceId => Guid.TryParse(tConnect.deviceId, out Guid id) ? id : Guid.Empty;

        // Puts every value back inside its legal range, and fills anything a hand-edited or older file left out.
        // Returns true when a device ID had to be created, which the caller should save.
        public bool Normalise()
        {
            tConnect ??= new TConnectSettings();
            tables ??= new TableSettings();
            display ??= new DisplaySettings();
            switching ??= new SwitchingSettings();

            tConnect.url = (tConnect.url ?? "").Trim();
            tConnect.username ??= "";
            tConnect.password ??= "";
            tConnect.siteName = (tConnect.siteName ?? "").Trim();
            tConnect.port = Mathf.Clamp(tConnect.port, 0, 65535);
            tConnect.mtdName = string.IsNullOrWhiteSpace(tConnect.mtdName) ? "MTD 01" : tConnect.mtdName.Trim();

            tables.mtdTableNumbers = (tables.mtdTableNumbers ?? "").Trim();
            tables.tableTimeoutSeconds = Mathf.Clamp(tables.tableTimeoutSeconds, MinTableTimeoutSeconds, 3600);
            if (!Enum.IsDefined(typeof(TableListSourceKind), tables.tableListSource)) tables.tableListSource = TableListSourceKind.TConnect;

            display.mtdUi = (display.mtdUi ?? "").Trim();
            if (display.numberOfTables != 4 && display.numberOfTables != 6) display.numberOfTables = 6;
            display.demoTables = Mathf.Clamp(display.demoTables, 1, 60);

            if (!Enum.IsDefined(typeof(SwitchingMode), switching.mode)) switching.mode = SwitchingMode.Pages;
            switching.dwellTimeSeconds = Mathf.Clamp(switching.dwellTimeSeconds, 5, 86400);
            switching.slotSwapIntervalSeconds = Mathf.Clamp(switching.slotSwapIntervalSeconds, 1, 86400);

            if (DeviceId == Guid.Empty)
            {
                tConnect.deviceId = Guid.NewGuid().ToString();
                return true;
            }

            return false;
        }

        public MtdConfigurationData Clone()
        {
            return JsonUtility.FromJson<MtdConfigurationData>(JsonUtility.ToJson(this));
        }
    }
}
