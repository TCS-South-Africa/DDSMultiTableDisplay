using System;

namespace MTD
{
    // Called on a transport thread, never on Unity's main thread. The topic is in the transport's own form.
    public delegate void MtdMessageHandler(string topic, byte[] payload);

    // The connection MTD receives table summaries through. MTD always names topics in MQTT form
    // (tcsjh/{site}/table/+/mtdsummary), as DDS does, and each transport puts them on the wire in its own form.
    //
    // Every member returns straight away; none waits on the network. Dispose ends the connection for good,
    // including any connecting or reconnecting still in progress.
    public interface IMtdTransport : IDisposable
    {
        string Name { get; }
        bool Connected { get; }
        // What is wrong, for the status display; null when nothing is. Not only "could not connect": it can be set
        // while Connected is true, when the broker is reachable but refused the subscription, so no table can arrive.
        string LastError { get; }

        // Starts connecting and keeps trying until disposed.
        void Start();

        // May be called before the connection is up. Subscriptions survive reconnects.
        void Subscribe(string mqttTopicFilter, MtdMessageHandler handler);
    }

    static class MtdTransportErrors
    {
        // A failure as one line for the status display: the message, and the cause behind it when there is one.
        public static string Describe(Exception exception)
        {
            string text = OneLine(exception.Message);
            if (exception.InnerException == null) return text;

            string cause = OneLine(exception.InnerException.Message);
            return cause.Length == 0 || cause == text ? text : $"{text} ({cause})";
        }

        static string OneLine(string text)
        {
            return string.Join(" ", (text ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
