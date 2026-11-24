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
        string LastError { get; } // The most recent connection failure, for the status display. Null when there is none.

        // Starts connecting and keeps trying until disposed.
        void Start();

        // May be called before the connection is up. Subscriptions survive reconnects.
        void Subscribe(string mqttTopicFilter, MtdMessageHandler handler);
    }
}
