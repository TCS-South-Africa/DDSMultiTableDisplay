using System;

namespace MTD
{
    // Topic and subject names. The table summary subject is defined in Docs/MTD-Table-Summary-Contract.md; the
    // T-Connect names were read from the server (TopicNameFactory, TableService, TConnectClient).
    public static class MtdTopics
    {
        public const string DefaultSiteName = "testsite";
        public const int DefaultNatsPort = 4222;
        public const int DefaultMqttPort = 1883;

        // T-Connect services, NATS only.
        public const string AssignDeviceSubject = "provisioning.assign-device";
        public const string PingSubject = "ping.ping-server";
        public const string TableListBucket = "mtd-table-list";

        const string root = "tcsjh";
        const string tableSegment = "table";
        const string summarySubtopic = "mtdsummary";

        // DDS lower-cases the site name and falls back to "testsite"; MTD must build the same topic.
        public static string NormaliseSiteName(string siteName)
        {
            string trimmed = (siteName ?? "").Trim().ToLowerInvariant();
            return trimmed.Length == 0 ? DefaultSiteName : trimmed;
        }

        public static string TableSummary(string siteName, Guid tableId)
        {
            return $"{root}/{NormaliseSiteName(siteName)}/{tableSegment}/{tableId}/{summarySubtopic}";
        }

        // Every table's summary.
        public static string TableSummaryFilter(string siteName)
        {
            return $"{root}/{NormaliseSiteName(siteName)}/{tableSegment}/+/{summarySubtopic}";
        }

        // The table a summary was published for, read from where it arrived. Takes either form a transport hands
        // over: tcsjh/{site}/table/{TableId}/mtdsummary from MQTT, tcsjh.table.{TableId}.mtdsummary from NATS.
        public static bool TryGetSummaryTableId(string topic, out Guid tableId)
        {
            tableId = Guid.Empty;
            if (string.IsNullOrEmpty(topic)) return false;

            string[] segments = topic.Split('/', '.');
            int last = segments.Length - 1;
            return last >= 3
                && segments[0] == root
                && segments[last] == summarySubtopic
                && segments[last - 2] == tableSegment
                && Guid.TryParseExact(segments[last - 1], "D", out tableId)
                && tableId != Guid.Empty;
        }

        public static string TableListKey(Guid displayId)
        {
            return $"{root}.services.tconnect.display.{displayId}.table-list";
        }

        // The same rule as DDS's TConnect2NatsSubjects: NATS subjects carry no site name, so the second segment is
        // dropped. MQTT wildcards become their NATS equivalents.
        public static string ToNatsSubject(string mqttTopic)
        {
            string[] segments = (mqttTopic ?? "").Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == "+") segments[i] = "*";
                else if (segments[i] == "#") segments[i] = ">";
            }

            if (segments.Length < 3 || segments[0] != root)
            {
                return string.Join(".", segments);
            }

            return root + "." + string.Join(".", segments, 2, segments.Length - 2);
        }

        // Standard MQTT filter matching: "+" is one level, "#" is everything below.
        public static bool MqttFilterMatches(string filter, string topic)
        {
            if (filter == null || topic == null) return false;

            string[] filterLevels = filter.Split('/');
            string[] topicLevels = topic.Split('/');

            for (int i = 0; i < filterLevels.Length; i++)
            {
                if (filterLevels[i] == "#") return i == filterLevels.Length - 1;
                if (i >= topicLevels.Length) return false;
                if (filterLevels[i] != "+" && filterLevels[i] != topicLevels[i]) return false;
            }

            return filterLevels.Length == topicLevels.Length;
        }
    }
}
