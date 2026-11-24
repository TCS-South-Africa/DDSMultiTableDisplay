using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace MTD
{
    // Carries work from transport threads to Unity's main thread. Messages are kept per key with the newest
    // replacing any still waiting, because only a table's latest summary matters. Actions run in the order posted.
    public sealed class MainThreadQueue
    {
        readonly object gate = new object();
        readonly ConcurrentQueue<Action> actions = new ConcurrentQueue<Action>();

        Dictionary<string, byte[]> pending = new Dictionary<string, byte[]>();
        Dictionary<string, byte[]> spare = new Dictionary<string, byte[]>();

        // Any thread.
        public void PostLatest(string key, byte[] payload)
        {
            lock (gate)
            {
                pending[key] = payload;
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

            Dictionary<string, byte[]> batch;
            lock (gate)
            {
                if (pending.Count == 0) return;
                batch = pending;
                pending = spare;
            }

            foreach (KeyValuePair<string, byte[]> message in batch) onMessage(message.Key, message.Value);

            batch.Clear();
            spare = batch;
        }
    }
}
