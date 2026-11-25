using System;

namespace MTD
{
    // Turns one received message into a snapshot, or says why the message is to be ignored.
    public static class SummaryReader
    {
        // The topic is in the transport's own form (an MQTT topic or a NATS subject). Returns null, with the reason
        // in problem, for a message MTD must not act on.
        public static TableSnapshot Read(string topic, byte[] payload, out MtdTableSummaryMessage message, out string problem)
        {
            message = null;

            if (!MtdTopics.TryGetSummaryTableId(topic, out Guid topicTableId))
            {
                problem = $"Ignored a message on {topic}, which is not a table summary topic";
                return null;
            }

            if (!MtdJson.TryDeserialize(payload, out message, out string error))
            {
                problem = $"Ignored an unreadable summary on {topic}: {error}";
                return null;
            }

            TableSnapshot snapshot = SnapshotParser.Parse(message);
            if (snapshot == null)
            {
                problem = $"Ignored a summary with no table ID on {topic}";
                return null;
            }

            // A table may only describe itself. Without this check, whatever can publish on one table's topic
            // could redraw any other table's widget by putting that table's ID in the payload.
            if (snapshot.TableId != topicTableId)
            {
                problem = $"Ignored a summary on {topic}: it names table {snapshot.TableId}, which is not the table that topic belongs to";
                return null;
            }

            problem = null;
            return snapshot;
        }
    }
}
