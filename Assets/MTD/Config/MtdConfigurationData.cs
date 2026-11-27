using System;
using System.Collections.Generic;
using UnityEngine;

namespace MTD
{
    public enum TableListSourceKind
    {
        TConnect, // The T-Connect server's table list for this display. NATS only.
        ManualEntry = 1, // Former Discover configurations open with an empty manual list.
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
            public List<ManualTable> manualTables = new List<ManualTable>();
            public int tableTimeoutSeconds = 35;
            public bool skipClosedTables = true;
        }

        [Serializable]
        public class ManualTable
        {
            public string tableId;
            public int slot; // 1..6 when assigned. Page entries use 0 for automatic distribution in slot mode.

            public int SlotIndex(int listIndex, int slotCount) => slot == 0 ? listIndex % slotCount : slot - 1;
        }

        [Serializable]
        public class DisplaySettings
        {
            public string mtdUi = ""; // An MTD UI id from the catalog. Blank means the first one.
            public bool useMtdTableUniqueName;
            public int numberOfTables = 6;
            public bool demoMode;
            public int demoTables = 12;
        }

        [Serializable]
        public class SwitchingSettings
        {
            public SwitchingMode mode = SwitchingMode.Pages;
            public int dwellTimeSeconds = 120;
            public int[] slotIntervals = { 120, 120, 120, 120, 120, 120 };
        }

        // The server's table list travels over NATS Key-Value, which MQTT cannot read.
        public TableListSourceKind EffectiveTableListSource => tConnect.useNats ? tables.tableListSource : TableListSourceKind.ManualEntry;

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

            tables.manualTables ??= new List<ManualTable>();
            var seen = new HashSet<Guid>();
            tables.manualTables.RemoveAll(entry =>
            {
                if (entry == null || !Guid.TryParse(entry.tableId, out Guid id) || id == Guid.Empty || !seen.Add(id)) return true;
                entry.tableId = id.ToString();
                entry.slot = Mathf.Clamp(entry.slot, 0, 6);
                return false;
            });
            tables.tableTimeoutSeconds = Mathf.Clamp(tables.tableTimeoutSeconds, MinTableTimeoutSeconds, 3600);
            if (!Enum.IsDefined(typeof(TableListSourceKind), tables.tableListSource)) tables.tableListSource = TableListSourceKind.TConnect;

            display.mtdUi = (display.mtdUi ?? "").Trim();
            if (display.numberOfTables != 4 && display.numberOfTables != 6) display.numberOfTables = 6;
            display.demoTables = Mathf.Clamp(display.demoTables, 1, 60);

            if (!Enum.IsDefined(typeof(SwitchingMode), switching.mode)) switching.mode = SwitchingMode.Pages;
            switching.dwellTimeSeconds = Mathf.Clamp(switching.dwellTimeSeconds, 5, 86400);
            int[] intervals = switching.slotIntervals;
            Array.Resize(ref intervals, 6);
            for (int i = 0; i < intervals.Length; i++)
                intervals[i] = intervals[i] <= 0 ? switching.dwellTimeSeconds : Mathf.Clamp(intervals[i], 1, 86400);
            switching.slotIntervals = intervals;

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
