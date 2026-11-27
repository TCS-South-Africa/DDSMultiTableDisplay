using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using NUnit.Framework;

namespace MTD.Tests
{
    // Runs MTD's MQTT transport against a real MQTT 5 broker: the one built into the MQTT library, started inside
    // the test on a free port of this machine's loopback address. Nothing needs installing, and nothing outside
    // this machine can reach it.
    public class MqttTransportTests
    {
        const string host = "127.0.0.1";

        MqttServer broker;
        MqttMtdTransport transport;
        int port;
        volatile bool brokerRefusesSubscriptions;

        [SetUp]
        public void SetUp()
        {
            port = FreePort();
            brokerRefusesSubscriptions = false;

            MqttServerOptions options = new MqttServerOptionsBuilder()
                .WithDefaultEndpoint()
                .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
                .WithDefaultEndpointBoundIPV6Address(IPAddress.IPv6None)
                .WithDefaultEndpointPort(port)
                .Build();

            broker = new MqttFactory().CreateMqttServer(options);
            broker.InterceptingSubscriptionAsync += args =>
            {
                // What a broker does when its access rules do not allow the topic: it answers the subscription with
                // a refusal and keeps the connection open.
                if (brokerRefusesSubscriptions) args.Response.ReasonCode = MqttSubscribeReasonCode.NotAuthorized;
                return Task.CompletedTask;
            };
            Run(() => broker.StartAsync());

            transport = new MqttMtdTransport(new MtdConnectionSettings { Host = host, Port = port })
            {
                RetryInterval = TimeSpan.FromMilliseconds(200),
            };
        }

        [TearDown]
        public void TearDown()
        {
            transport?.Dispose();
            if (broker != null)
            {
                Run(() => broker.StopAsync());
                broker.Dispose();
            }
        }

        static int FreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int freePort = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return freePort;
        }

        // The broker's calls are run off the test's thread so nothing can wait on Unity's main thread.
        static void Run(Func<Task> action)
        {
            Assert.IsTrue(Task.Run(action).Wait(TimeSpan.FromSeconds(10)), "The test's MQTT broker did not respond.");
        }

        static bool WaitFor(Func<bool> condition, double seconds)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed.TotalSeconds < seconds)
            {
                if (condition()) return true;
                Thread.Sleep(20);
            }

            return condition();
        }

        void Publish(string topic, byte[] payload)
        {
            MqttApplicationMessage message = new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload).Build();
            Run(() => broker.InjectApplicationMessage(new InjectedMqttApplicationMessage(message) { SenderClientId = "test-dds" }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Summary_PublishedTheWayDdsWould_ReachesTheRegistry(bool manual)
        {
            MainThreadQueue queue = new MainThreadQueue();
            Guid tableId = TestSamples.ReadMessage("sicbo.json").TableId;
            string topicFilter = manual ? MtdTopics.TableSummary("Crown", tableId) : MtdTopics.TableSummaryFilter("Crown");
            transport.Subscribe(topicFilter, queue.PostMessage);
            transport.Start();
            Assert.IsTrue(WaitFor(() => transport.Connected, 5), $"Not connected: {transport.LastError}");
            Assert.IsNull(transport.LastError);

            byte[] payload = TestSamples.ReadBytes("sicbo.json");
            TableRegistry registry = new TableRegistry(35);
            int received = 0;

            // The subscription is acknowledged a moment after the connection is up, so publish until one arrives.
            Assert.IsTrue(WaitFor(() =>
            {
                Publish($"tcsjh/crown/table/{tableId}/mtdsummary", payload);
                if (manual) Publish(MtdTopics.TableSummary("Crown", TestSamples.TableId(99)), payload);
                Publish($"tcsjh/crown/table/{tableId}/gamesummary", payload); // A neighbouring topic MTD must not receive.
                Publish($"tcsjh/othersite/table/{tableId}/mtdsummary", payload); // Another site's table.
                Thread.Sleep(100);
                queue.Drain((topic, bytes) =>
                {
                    received++;
                    StringAssert.EndsWith("/mtdsummary", topic);
                    StringAssert.StartsWith("tcsjh/crown/", topic);
                    TableSnapshot snapshot = SummaryReader.Read(topic, bytes, out _, out string problem);
                    Assert.IsNull(problem);
                    registry.Apply(snapshot, 0);
                });
                return received > 0;
            }, 5), "No summary arrived.");

            Assert.IsTrue(registry.TryGet(tableId, out TableState state));
            Assert.AreEqual(GameFamily.SicBo, state.Snapshot.Family);
        }

        // The reported fault: the broker refused the subscription, and MTD showed a healthy connection with no error
        // while no table could ever arrive.
        [Test]
        public void RefusedSubscription_IsReported_AndRecoversOnceTheBrokerAllowsIt()
        {
            brokerRefusesSubscriptions = true;
            List<string> topics = new List<string>();
            string filter = MtdTopics.TableSummaryFilter("crown");
            transport.Subscribe(filter, (topic, payload) =>
            {
                lock (topics) topics.Add(topic);
            });
            transport.Start();

            Assert.IsTrue(WaitFor(() => transport.Connected && transport.LastError != null, 5), "The refusal was not reported.");
            StringAssert.Contains("refused the subscription", transport.LastError);
            StringAssert.Contains(filter, transport.LastError);
            StringAssert.Contains("NotAuthorized", transport.LastError);
            Assert.IsTrue(transport.Connected, "The connection itself is up; only the subscription was refused.");

            // Someone corrects the broker's access rules. MTD must pick that up without being restarted.
            brokerRefusesSubscriptions = false;
            Assert.IsTrue(WaitFor(() => transport.LastError == null, 5), $"Did not recover: {transport.LastError}");

            Guid tableId = TestSamples.TableId(1);
            Assert.IsTrue(WaitFor(() =>
            {
                Publish($"tcsjh/crown/table/{tableId}/mtdsummary", new byte[] { 1 });
                Thread.Sleep(100);
                lock (topics) return topics.Count > 0;
            }, 5), "No message arrived after the subscription was allowed.");
        }

        [Test]
        public void BrokerNotThere_IsReportedAsNotConnected()
        {
            Run(() => broker.StopAsync());
            transport.Start();

            Assert.IsTrue(WaitFor(() => transport.LastError != null, 10), "The failure was not reported.");
            Assert.IsFalse(transport.Connected);
        }
    }
}
