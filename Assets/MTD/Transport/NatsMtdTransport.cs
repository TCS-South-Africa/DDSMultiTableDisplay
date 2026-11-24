using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NATS.Client;
using NATS.Client.KeyValue;
using Debug = UnityEngine.Debug;

namespace MTD
{
    // Connection settings shared by both transports.
    public struct MtdConnectionSettings
    {
        public string Host;
        public int Port; // 0 means the transport's default port.
        public string Username;
        public string Password;
        public bool UseTls;
    }

    // NATS connection, adapted from DDS's NatsTConnect2Transport. MTD only subscribes, makes the occasional request
    // and reads Key-Value entries, so DDS's ordered publish queue and its socket deadline handling are not needed.
    public sealed class NatsMtdTransport : IMtdTransport
    {
        const int connectTimeoutMilliseconds = 5000;
        const int reconnectWaitMilliseconds = 2000;
        const int firstConnectionRetrySeconds = 5;

        readonly MtdConnectionSettings settings;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly object gate = new object();
        readonly List<(string subject, MtdMessageHandler handler)> subscriptions = new List<(string, MtdMessageHandler)>();

        IConnection connection; // Set once, when the first connection succeeds. Guarded by gate.
        volatile bool isUp;
        volatile bool disposed;
        volatile string lastError;
        bool started;

        public NatsMtdTransport(MtdConnectionSettings settings)
        {
            this.settings = settings;
        }

        public string Name => "NATS";

        public bool Connected => isUp && !disposed;

        public string LastError => lastError;

        string ServerUrl => $"{(settings.UseTls ? "tls" : "nats")}://{settings.Host}:{(settings.Port > 0 ? settings.Port : MtdTopics.DefaultNatsPort)}";

        public void Start()
        {
            lock (gate)
            {
                if (started || disposed) return;
                started = true;
            }

            Task.Run(ConnectUntilUp);
        }

        // The NATS client only reconnects by itself after a first successful connection, so the first one is
        // retried here.
        void ConnectUntilUp()
        {
            Debug.Log($"MTD: connecting to NATS at {ServerUrl}");

            while (!disposed)
            {
                try
                {
                    IConnection newConnection = new ConnectionFactory().CreateConnection(CreateOptions(), false);

                    lock (gate)
                    {
                        if (disposed)
                        {
                            Close(newConnection);
                            return;
                        }

                        connection = newConnection;
                        isUp = true;
                        lastError = null;
                        foreach ((string subject, MtdMessageHandler handler) in subscriptions)
                        {
                            SubscribeNow(newConnection, subject, handler);
                        }
                    }

                    Debug.Log($"MTD: connected to NATS at {ServerUrl}");
                    return;
                }
                catch (Exception exception)
                {
                    lastError = exception.InnerException != null ? $"{exception.Message} ({exception.InnerException.Message})" : exception.Message;
                    Debug.Log($"MTD: could not connect to NATS at {ServerUrl}: {lastError}");
                }

                if (lifetime.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(firstConnectionRetrySeconds))) return;
            }
        }

        Options CreateOptions()
        {
            Options options = ConnectionFactory.GetDefaultOptions();
            options.Url = ServerUrl;
            options.Name = "MTD";
            options.Secure = settings.UseTls;
            options.Timeout = connectTimeoutMilliseconds;
            options.AllowReconnect = true;
            options.MaxReconnect = Options.ReconnectForever;
            options.ReconnectWait = reconnectWaitMilliseconds;
            options.PingInterval = 5000;
            options.MaxPingsOut = 2;

            if (!string.IsNullOrEmpty(settings.Username))
            {
                options.User = settings.Username;
                options.Password = settings.Password;
            }

            if (settings.UseTls)
            {
                // The same trust policy as DDS: any server certificate is accepted.
                options.TLSRemoteCertificationValidationCallback = (sender, certificate, chain, errors) => true;
            }

            options.DisconnectedEventHandler = (sender, args) =>
            {
                isUp = false;
                lastError = "connection lost";
                Debug.Log("MTD: NATS connection lost");
            };
            options.ReconnectedEventHandler = (sender, args) =>
            {
                isUp = true;
                lastError = null;
                Debug.Log("MTD: NATS connection re-established");
            };
            options.ClosedEventHandler = (sender, args) =>
            {
                isUp = false;
                if (!disposed) Debug.Log("MTD: NATS connection closed");
            };

            return options;
        }

        public void Subscribe(string mqttTopicFilter, MtdMessageHandler handler)
        {
            string subject = MtdTopics.ToNatsSubject(mqttTopicFilter);

            lock (gate)
            {
                if (disposed) return;
                subscriptions.Add((subject, handler));
                if (connection != null) SubscribeNow(connection, subject, handler);
            }
        }

        // The NATS client restores its own subscriptions after a reconnect.
        void SubscribeNow(IConnection target, string subject, MtdMessageHandler handler)
        {
            Task.Run(() =>
            {
                try
                {
                    target.SubscribeAsync(subject, (sender, args) =>
                    {
                        if (disposed) return;
                        try
                        {
                            handler(args.Message.Subject, args.Message.Data);
                        }
                        catch (Exception exception)
                        {
                            Debug.Log($"MTD: error handling NATS message on {args.Message.Subject}: {exception}");
                        }
                    });
                    Debug.Log($"MTD: subscribed to NATS subject {subject}");
                }
                catch (Exception exception)
                {
                    Debug.Log($"MTD: could not subscribe to NATS subject {subject}: {exception.Message}");
                }
            });
        }

        // Request/reply to a NATS service. Returns null when there is no connection, nobody is listening, or no
        // reply arrives in time; other failures throw.
        public Task<byte[]> RequestAsync(string subject, byte[] payload, TimeSpan timeout)
        {
            IConnection target = CurrentConnection();
            if (target == null) return Task.FromResult<byte[]>(null);

            return Task.Run(() =>
            {
                try
                {
                    return target.Request(subject, payload, (int)timeout.TotalMilliseconds)?.Data;
                }
                catch (NATSTimeoutException)
                {
                    return null; // Also covers "no responders", which the client reports as a kind of timeout.
                }
            });
        }

        // Reads one Key-Value entry. Returns false when the key has no value. Throws when the bucket does not exist
        // or the server cannot be reached. Blocks, so call it off the main thread.
        public bool TryGetKeyValue(string bucket, string key, out byte[] value, out ulong revision)
        {
            value = null;
            revision = 0;

            IConnection target = CurrentConnection() ?? throw new InvalidOperationException("The NATS connection is not open.");
            KeyValueEntry entry = target.CreateKeyValueContext(bucket, null).Get(key);
            if (entry == null || entry.Value == null || entry.Value.Length == 0) return false;

            value = entry.Value;
            revision = entry.Revision;
            return true;
        }

        // Watches one Key-Value entry. The callback gets the current value first, then every change; a null value
        // means the entry was deleted. It runs on a NATS thread. Throws when the bucket does not exist. Blocks while
        // the watch is set up, so call it off the main thread. Dispose the result to stop watching.
        public IDisposable WatchKeyValue(string bucket, string key, Action<byte[], ulong> onValue)
        {
            IConnection target = CurrentConnection() ?? throw new InvalidOperationException("The NATS connection is not open.");
            return target.CreateKeyValueContext(bucket, null).Watch(key, new Watcher(this, onValue));
        }

        sealed class Watcher : IKeyValueWatcher
        {
            readonly NatsMtdTransport owner;
            readonly Action<byte[], ulong> onValue;

            public Watcher(NatsMtdTransport owner, Action<byte[], ulong> onValue)
            {
                this.owner = owner;
                this.onValue = onValue;
            }

            public void Watch(KeyValueEntry entry)
            {
                if (owner.disposed || entry == null) return;

                bool hasValue = entry.Value != null && entry.Value.Length > 0;
                try
                {
                    onValue(hasValue ? entry.Value : null, entry.Revision);
                }
                catch (Exception exception)
                {
                    Debug.Log($"MTD: error handling Key-Value entry {entry.Key}: {exception}");
                }
            }

            public void EndOfData()
            {
            }
        }

        IConnection CurrentConnection()
        {
            lock (gate)
            {
                return disposed || !isUp ? null : connection;
            }
        }

        public void Dispose()
        {
            IConnection toClose;
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                isUp = false;
                toClose = connection;
                connection = null;
            }

            lifetime.Cancel(); // Ends the wait between first-connection attempts.
            if (toClose != null) Task.Run(() => Close(toClose)); // Closing can block, so it is kept off the caller's thread.
        }

        static void Close(IConnection target)
        {
            try
            {
                target.Close();
            }
            catch (Exception exception)
            {
                Debug.Log($"MTD: error closing the NATS connection: {exception.Message}");
            }
        }
    }
}
