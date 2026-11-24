using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using Debug = UnityEngine.Debug;

namespace MTD
{
    // MQTT connection, adapted from DDS's MqttManagerLite. One loop connects, notices a lost connection, reconnects
    // and subscribes again, for as long as the transport lives.
    public sealed class MqttMtdTransport : IMtdTransport
    {
        const int connectionCheckSeconds = 2;

        readonly MtdConnectionSettings settings;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly object gate = new object();
        readonly List<(string filter, MtdMessageHandler handler)> subscriptions = new List<(string, MtdMessageHandler)>();

        MqttFactory factory;
        IMqttClient client;
        volatile bool connected;
        volatile bool disposed;
        volatile string lastError;
        volatile string certificateProblem;
        volatile bool certificateWasChecked;
        volatile string subscriptionProblem;
        volatile string lastLogged;
        bool started;

        public MqttMtdTransport(MtdConnectionSettings settings)
        {
            this.settings = settings;
        }

        // How long to wait before trying again after a failed connection or a refused subscription.
        internal TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

        public string Name => "MQTT";

        public bool Connected => connected && !disposed;

        public string LastError => subscriptionProblem ?? certificateProblem ?? lastError;

        int Port => settings.Port > 0 ? settings.Port : MtdTopics.DefaultMqttPort;

        public void Start()
        {
            lock (gate)
            {
                if (started || disposed) return;
                started = true;
            }

            Task.Run(RunAsync);
        }

        async Task RunAsync()
        {
            CancellationToken token = lifetime.Token;
            factory = new MqttFactory();
            client = factory.CreateMqttClient();
            client.ApplicationMessageReceivedAsync += OnMessageReceived;
            client.DisconnectedAsync += args =>
            {
                if (connected && !disposed)
                {
                    lastError = "connection lost";
                    Debug.Log("MTD: MQTT connection lost");
                }

                connected = false;
                return Task.CompletedTask;
            };

            MqttClientOptions options = new MqttClientOptionsBuilder()
                .WithTcpServer(settings.Host, Port)
                .WithCredentials(settings.Username, settings.Password)
                .WithProtocolVersion(MqttProtocolVersion.V500)
                .WithCleanSession(true)
                .WithTlsOptions(builder =>
                {
                    // Every certificate decision is MtdTlsTrust's; none of the client's own "allow" switches is set.
                    builder.UseTls(settings.UseTls)
                        .WithSslProtocols(SslProtocols.Tls12)
                        .WithCertificateValidationHandler(args => AcceptsCertificate(args.Certificate, args.SslPolicyErrors));
                })
                .Build();

            Debug.Log($"MTD: connecting to MQTT at {settings.Host}:{Port}");

            while (!disposed)
            {
                try
                {
                    if (!client.IsConnected)
                    {
                        connected = false;
                        subscriptionProblem = null;
                        await ConnectAsync(options, token).ConfigureAwait(false);
                    }
                    else if (subscriptionProblem != null)
                    {
                        // The broker may have been given the permission since the last attempt.
                        await SubscribeAllAsync(token).ConfigureAwait(false);
                    }

                    bool allIsWell = connected && subscriptionProblem == null;
                    await Task.Delay(allIsWell ? TimeSpan.FromSeconds(connectionCheckSeconds) : RetryInterval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        async Task ConnectAsync(MqttClientOptions options, CancellationToken token)
        {
            certificateWasChecked = false;
            try
            {
                MqttClientConnectResult result = await client.ConnectAsync(options, token).ConfigureAwait(false);
                if (!certificateWasChecked) certificateProblem = null;

                if (result.ResultCode != MqttClientConnectResultCode.Success)
                {
                    lastError = $"connection refused: {result.ResultCode}";
                    LogOnChange($"MTD: MQTT {lastError}");
                    return;
                }

                connected = true;
                lastError = null;
                LogOnChange($"MTD: connected to MQTT at {settings.Host}:{Port}");
                await SubscribeAllAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A certificate refusal from an earlier attempt must not hide a different failure in this one.
                if (!certificateWasChecked) certificateProblem = null;

                lastError = MtdTransportErrors.Describe(exception);
                LogOnChange($"MTD: could not connect to MQTT at {settings.Host}:{Port}: {LastError}");
            }
        }

        bool AcceptsCertificate(X509Certificate certificate, SslPolicyErrors errors)
        {
            bool accepted = MtdTlsTrust.Accepts(certificate, errors, settings.Host, settings.AllowUntrustedCertificate, settings.TrustedCertificateFolder, out string problem);
            certificateProblem = problem;
            certificateWasChecked = true;
            return accepted;
        }

        // The loop retries every few seconds for as long as something is wrong. Each new state is logged once.
        void LogOnChange(string message)
        {
            if (message == lastLogged) return;
            lastLogged = message;
            Debug.Log(message);
        }

        public void Subscribe(string mqttTopicFilter, MtdMessageHandler handler)
        {
            bool subscribeNow;
            lock (gate)
            {
                if (disposed) return;
                subscriptions.Add((mqttTopicFilter, handler));
                subscribeNow = connected;
            }

            if (subscribeNow)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        string problem = await SubscribeAsync(mqttTopicFilter, lifetime.Token).ConfigureAwait(false);
                        if (problem != null) subscriptionProblem = problem; // The connect loop keeps retrying it.
                    }
                    catch (OperationCanceledException)
                    {
                    }
                });
            }
        }

        // Subscribes to everything asked for and records the outcome: subscriptionProblem is null afterwards only if
        // the broker granted every one.
        async Task SubscribeAllAsync(CancellationToken token)
        {
            List<string> filters = new List<string>();
            lock (gate)
            {
                foreach ((string filter, MtdMessageHandler _) in subscriptions) filters.Add(filter);
            }

            string firstProblem = null;
            foreach (string filter in filters)
            {
                string problem = await SubscribeAsync(filter, token).ConfigureAwait(false);
                firstProblem ??= problem;
            }

            subscriptionProblem = firstProblem;
        }

        // Returns null when the broker granted the subscription, otherwise what went wrong. A broker answers a
        // subscription it will not serve with a refusal code and leaves the connection open, so a connection that is
        // up says nothing about whether any message can arrive.
        async Task<string> SubscribeAsync(string filter, CancellationToken token)
        {
            MqttClientSubscribeOptions subscribeOptions = factory.CreateSubscribeOptionsBuilder()
                .WithTopicFilter(topicFilter =>
                {
                    topicFilter.WithTopic(filter);
                    topicFilter.WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);
                })
                .Build();

            string problem = null;
            try
            {
                MqttClientSubscribeResult result = await client.SubscribeAsync(subscribeOptions, token).ConfigureAwait(false);
                if (result.Items.Count == 0) problem = $"the broker did not answer the subscription to {filter}";

                foreach (MqttClientSubscribeResultItem item in result.Items)
                {
                    if (item.ResultCode > MqttClientSubscribeResultCode.GrantedQoS2)
                    {
                        string reason = string.IsNullOrEmpty(result.ReasonString) ? "" : $", \"{result.ReasonString}\"";
                        problem = $"the broker refused the subscription to {filter} ({item.ResultCode}{reason})";
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                problem = $"could not subscribe to {filter}: {exception.Message}";
            }

            LogOnChange(problem == null ? $"MTD: subscribed to MQTT topic {filter}" : $"MTD: MQTT: {problem}");
            return problem;
        }

        Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs args)
        {
            if (disposed) return Task.CompletedTask;

            string topic = args.ApplicationMessage.Topic;
            ArraySegment<byte> segment = args.ApplicationMessage.PayloadSegment;
            byte[] payload = new byte[segment.Count];
            if (segment.Count > 0) Buffer.BlockCopy(segment.Array, segment.Offset, payload, 0, segment.Count);

            List<MtdMessageHandler> handlers = new List<MtdMessageHandler>();
            lock (gate)
            {
                foreach ((string filter, MtdMessageHandler handler) in subscriptions)
                {
                    if (MtdTopics.MqttFilterMatches(filter, topic)) handlers.Add(handler);
                }
            }

            foreach (MtdMessageHandler handler in handlers)
            {
                try
                {
                    handler(topic, payload);
                }
                catch (Exception exception)
                {
                    Debug.Log($"MTD: error handling MQTT message on {topic}: {exception}");
                }
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
            }

            connected = false;
            lifetime.Cancel(); // Ends the connect loop and any wait in it.

            IMqttClient toClose = client;
            if (toClose == null) return;

            // The disconnect is left to finish in the background so a slow broker cannot hold up the caller.
            Task.Run(async () =>
            {
                try
                {
                    if (toClose.IsConnected)
                    {
                        using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        MqttClientDisconnectOptions disconnectOptions = new MqttClientDisconnectOptionsBuilder()
                            .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                            .Build();
                        await toClose.DisconnectAsync(disconnectOptions, timeout.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    Debug.Log($"MTD: MQTT client did not disconnect cleanly: {exception.Message}");
                }

                toClose.Dispose();
            });
        }
    }
}
