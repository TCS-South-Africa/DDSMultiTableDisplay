using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace MTD
{
    // Carries work from transport threads to Unity's main thread. Messages and actions are each handed over in the
    // order they arrived.
    //
    // The queue does not decide which of two messages for a table is the newer one: arrival order is not sending
    // order, so every message goes to the registry, which compares their event times. Only when the main thread has
    // stopped draining for a long time does the queue let go of anything, and then it is the oldest arrivals.
    public sealed class MainThreadQueue
    {
        public const int DefaultMessageLimit = 4096;

        readonly object gate = new object();
        readonly ConcurrentQueue<Action> actions = new ConcurrentQueue<Action>();
        readonly int messageLimit;

        Queue<(string topic, byte[] payload)> pending = new Queue<(string, byte[])>();
        Queue<(string topic, byte[] payload)> spare = new Queue<(string, byte[])>();
        long droppedMessages;

        public MainThreadQueue(int messageLimit = DefaultMessageLimit)
        {
            this.messageLimit = Math.Max(1, messageLimit);
        }

        // How many messages were let go because the main thread was not draining. Any thread.
        public long DroppedMessages
        {
            get
            {
                lock (gate) return droppedMessages;
            }
        }

        // Any thread.
        public void PostMessage(string topic, byte[] payload)
        {
            lock (gate)
            {
                if (pending.Count >= messageLimit)
                {
                    pending.Dequeue();
                    droppedMessages++;
                }

                pending.Enqueue((topic, payload));
            }
        }

        // Any thread.
        public void Post(Action action)
        {
            actions.Enqueue(action);
        }

        // Main thread, once a frame.
        public void Drain(Action<string, byte[]> onMessage)
        {
            while (actions.TryDequeue(out Action action)) action();

            Queue<(string topic, byte[] payload)> batch;
            lock (gate)
            {
                if (pending.Count == 0) return;
                batch = pending;
                pending = spare;
            }

            try
            {
                while (batch.Count > 0)
                {
                    (string topic, byte[] payload) = batch.Dequeue();
                    onMessage(topic, payload);
                }
            }
            finally
            {
                batch.Clear();
                lock (gate) spare = batch;
            }
        }
    }
}
