using System;
using System.Collections.Generic;
using System.Security.Authentication;
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
        const int retrySeconds = 5;
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
        bool started;

        public MqttMtdTransport(MtdConnectionSettings settings)
        {
            this.settings = settings;
        }

        public string Name => "MQTT";

        public bool Connected => connected && !disposed;

        public string LastError => lastError;

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
                    // The same trust policy as DDS: any server certificate is accepted.
                    builder.UseTls(settings.UseTls)
                        .WithSslProtocols(SslProtocols.Tls12)
                        .WithAllowUntrustedCertificates()
                        .WithIgnoreCertificateChainErrors()
                        .WithCertificateValidationHandler(args => true);
                })
                .Build();

            Debug.Log($"MTD: connecting to MQTT at {settings.Host}:{Port}");

            while (!disposed)
            {
                if (!client.IsConnected)
                {
                    connected = false;
                    try
                    {
                        MqttClientConnectResult result = await client.ConnectAsync(options, token).ConfigureAwait(false);
                        if (result.ResultCode == MqttClientConnectResultCode.Success)
                        {
                            await SubscribeAllAsync(token).ConfigureAwait(false);
                            connected = true;
                            lastError = null;
                            Debug.Log($"MTD: connected to MQTT at {settings.Host}:{Port}");
                        }
                        else
                        {
                            lastError = $"connection refused: {result.ResultCode}";
                            Debug.Log($"MTD: MQTT {lastError}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        lastError = exception.InnerException != null ? $"{exception.Message} ({exception.InnerException.Message})" : exception.Message;
                        Debug.Log($"MTD: could not connect to MQTT at {settings.Host}:{Port}: {lastError}");
                    }
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(connected ? connectionCheckSeconds : retrySeconds), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
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
                        await SubscribeAsync(mqttTopicFilter, lifetime.Token).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        Debug.Log($"MTD: could not subscribe to MQTT topic {mqttTopicFilter}: {exception.Message}");
                    }
                });
            }
        }

        async Task SubscribeAllAsync(CancellationToken token)
        {
            List<string> filters = new List<string>();
            lock (gate)
            {
                foreach ((string filter, MtdMessageHandler _) in subscriptions) filters.Add(filter);
            }

            foreach (string filter in filters)
            {
                await SubscribeAsync(filter, token).ConfigureAwait(false);
            }
        }

        async Task SubscribeAsync(string filter, CancellationToken token)
        {
            MqttClientSubscribeOptions subscribeOptions = factory.CreateSubscribeOptionsBuilder()
                .WithTopicFilter(topicFilter =>
                {
                    topicFilter.WithTopic(filter);
                    topicFilter.WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);
                })
                .Build();

            await client.SubscribeAsync(subscribeOptions, token).ConfigureAwait(false);
            Debug.Log($"MTD: subscribed to MQTT topic {filter}");
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
