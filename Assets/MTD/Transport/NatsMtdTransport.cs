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
        public bool AllowUntrustedCertificate; // Accept the broker's TLS certificate without checking it. See MtdTlsTrust.
        public string TrustedCertificateFolder; // Null means MtdTlsTrust.DefaultTrustedFolder.
    }

    // NATS connection, adapted from DDS's NatsTConnect2Transport. MTD only subscribes, makes the occasional request
    // and reads Key-Value entries, so DDS's ordered publish queue and its socket deadline handling are not needed.
    //
    // The NATS client reconnects by itself after a network fault, but in two cases it does not, and both are
    // handled here by connecting again from scratch every few seconds:
    //  - the first connection has never succeeded;
    //  - the server closed the connection. This client treats an error from the server as final, and the usual one
    //    is the server refusing a subscription this display's login is not allowed.
    public sealed class NatsMtdTransport : IMtdTransport
    {
        const int connectTimeoutMilliseconds = 5000;
        const int reconnectWaitMilliseconds = 2000;

        readonly MtdConnectionSettings settings;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly object gate = new object();
        readonly List<(string subject, MtdMessageHandler handler)> subscriptions = new List<(string, MtdMessageHandler)>();

        IConnection connection; // The connection in use, replaced when the server closes it. Guarded by gate.
        bool connecting; // True while ConnectUntilUp is running or about to. Guarded by gate.
        int connectionGeneration; // Guarded by gate.
        volatile bool isUp;
        volatile bool disposed;
        volatile string lastError;
        volatile string certificateProblem;
        volatile bool certificateWasChecked;
        volatile string closeProblem;
        readonly HashSet<string> loggedWhileUnsettled = new HashSet<string>(); // Its own lock.
        bool started;

        public NatsMtdTransport(MtdConnectionSettings settings)
        {
            this.settings = settings;
        }

        // How long to wait before connecting again after a failed attempt or after the server closed the connection.
        internal TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

        // A connection has to stay up this long before the reason the server closed the last one is forgotten.
        // A refused subscription closes the new connection within moments, every time, so until then the reason
        // stays on the status display instead of flickering away at each attempt.
        internal TimeSpan StableAfter = TimeSpan.FromSeconds(4);

        public string Name => "NATS";

        public bool Connected => isUp && !disposed;

        public string LastError => closeProblem ?? certificateProblem ?? lastError;

        // Goes up each time a new connection replaces a closed one. Anything tied to the old connection, such as a
        // Key-Value watch, is gone when this changes.
        public int ConnectionGeneration
        {
            get
            {
                lock (gate) return connectionGeneration;
            }
        }

        string ServerUrl => $"{(settings.UseTls ? "tls" : "nats")}://{settings.Host}:{(settings.Port > 0 ? settings.Port : MtdTopics.DefaultNatsPort)}";

        public void Start()
        {
            lock (gate)
            {
                if (started || disposed) return;
                started = true;
                connecting = true;
            }

            Task.Run(ConnectUntilUp);
        }

        void ConnectUntilUp()
        {
            Log($"MTD: connecting to NATS at {ServerUrl}");

            while (!disposed)
            {
                certificateWasChecked = false;
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
                        connectionGeneration++;
                        connecting = false;
                        isUp = true;
                        lastError = null;
                        foreach ((string subject, MtdMessageHandler handler) in subscriptions)
                        {
                            SubscribeNow(newConnection, subject, handler);
                        }
                    }

                    Log($"MTD: connected to NATS at {ServerUrl}");
                    ForgetCloseProblemOnceStable(newConnection);

                    // If the server closed it in the instant before it became the connection in use, that
                    // event was not acted on. Act on it now.
                    if (newConnection.State == ConnState.CLOSED) OnClosed(newConnection, newConnection.LastError);
                    return;
                }
                catch (Exception exception)
                {
                    // A certificate refusal from an earlier attempt must not hide a different failure in this one.
                    if (!certificateWasChecked) certificateProblem = null;

                    lastError = MtdTransportErrors.Describe(exception);
                    Log($"MTD: could not connect to NATS at {ServerUrl}: {certificateProblem ?? lastError}");
                }

                if (lifetime.Token.WaitHandle.WaitOne(RetryInterval)) return;
            }
        }

        // Connecting can go round every few seconds for as long as something is wrong, through the same few states
        // each time. Each message is logged once until a connection has settled.
        void Log(string message)
        {
            lock (loggedWhileUnsettled)
            {
                if (!loggedWhileUnsettled.Add(message)) return;
            }

            Debug.Log(message);
        }

        void ForgetCloseProblemOnceStable(IConnection established)
        {
            if (closeProblem == null)
            {
                lock (loggedWhileUnsettled) loggedWhileUnsettled.Clear();
                return;
            }

            Task.Run(() =>
            {
                if (lifetime.Token.WaitHandle.WaitOne(StableAfter)) return;

                lock (gate)
                {
                    if (!ReferenceEquals(connection, established) || !isUp) return;
                    closeProblem = null;
                }

                lock (loggedWhileUnsettled) loggedWhileUnsettled.Clear();
                Debug.Log($"MTD: the NATS connection to {ServerUrl} is staying up again");
            });
        }

        // The server closed the connection and this client will not reopen it. Wait, then start again from scratch,
        // which also puts every subscription back. If the cause is still there the server closes the new connection
        // too, and this goes round again until someone corrects it on the server.
        void OnClosed(IConnection closed, Exception error)
        {
            lock (gate)
            {
                // A connection that fails while it is being set up reports "closed" as well. Only the one in use counts.
                if (disposed || connecting || !ReferenceEquals(connection, closed)) return;
                connection = null;
                connecting = true;
                isUp = false;
            }

            string reason = DescribeCloseReason(error);
            if (reason != null) closeProblem = reason;
            Log($"MTD: NATS connection closed: {reason ?? "no reason given"}. Connecting again every {RetryInterval.TotalSeconds:0.#} s.");

            Task.Run(() =>
            {
                if (lifetime.Token.WaitHandle.WaitOne(RetryInterval))
                {
                    lock (gate) connecting = false;
                    return;
                }

                ConnectUntilUp();
            });
        }

        // What the server said when it closed the connection, in words for the status display.
        static string DescribeCloseReason(Exception error)
        {
            string text = (error?.Message ?? "").Trim();
            if (text.Length == 0) return null;

            const string clientPrefix = "Error from processErr():";
            if (text.StartsWith(clientPrefix, StringComparison.OrdinalIgnoreCase)) text = text.Substring(clientPrefix.Length).Trim();

            // The client cuts the closing quote off a quoted subject.
            int quotes = 0;
            foreach (char character in text)
            {
                if (character == '"') quotes++;
            }

            if (quotes % 2 == 1) text += "\"";

            return text.IndexOf("permissions violation", StringComparison.OrdinalIgnoreCase) >= 0
                ? $"the server refused this display's login ({text})"
                : $"the server closed the connection ({text})";
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
                // Called for the first connection and for every reconnection.
                options.TLSRemoteCertificationValidationCallback = (sender, certificate, chain, errors) =>
                {
                    bool accepted = MtdTlsTrust.Accepts(certificate, errors, settings.Host, settings.AllowUntrustedCertificate, settings.TrustedCertificateFolder, out string problem);
                    certificateProblem = problem;
                    certificateWasChecked = true;
                    return accepted;
                };
            }

            options.DisconnectedEventHandler = (sender, args) =>
            {
                lock (gate)
                {
                    if (disposed || !ReferenceEquals(connection, args.Conn)) return;
                    isUp = false;
                    lastError = "connection lost";
                }

                Log("MTD: NATS connection lost");
            };
            options.ReconnectedEventHandler = (sender, args) =>
            {
                lock (gate)
                {
                    if (disposed || !ReferenceEquals(connection, args.Conn)) return;
                    isUp = true;
                    lastError = null;
                }

                lock (loggedWhileUnsettled) loggedWhileUnsettled.Clear();
                Debug.Log("MTD: NATS connection re-established");
            };
            options.AsyncErrorEventHandler = (sender, args) =>
            {
                Debug.Log($"MTD: NATS reported an error: {args.Error}");
            };
            options.ClosedEventHandler = (sender, args) => OnClosed(args.Conn, args.Error);

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

        // The NATS client restores its own subscriptions after it reconnects. After the server closes a connection,
        // ConnectUntilUp makes them again on the new one.
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
                    Log($"MTD: subscribed to NATS subject {subject}");
                }
                catch (Exception exception)
                {
                    Log($"MTD: could not subscribe to NATS subject {subject}: {exception.Message}");
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
