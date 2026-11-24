using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NATS.Client;
using NATS.Client.JetStream;
using NATS.Client.KeyValue;
using NUnit.Framework;

namespace MTD.Tests
{
    // Runs MTD's NATS code against a real nats-server, with this test playing the parts of DDS and the T-Connect
    // server. Not part of a normal test run: start a JetStream-enabled server first, then run the "Live" tests.
    //
    //     nats-server -js -p 14222
    //
    // The port is deliberately not the default, so these tests can never talk to a real T-Connect broker.
    //
    // The two "restricted" tests need a second server that denies the summary subscription. They are skipped when it
    // is not running:
    //
    //     nats-server -c Assets/MTD/Tests/Editor/Samples/nats-live-restricted.conf
    [Explicit("Needs a local nats-server: nats-server -js -p 14222")]
    [Category("Live")]
    public class NatsLiveTests
    {
        const string host = "127.0.0.1";
        const int port = 14222;
        const int restrictedPort = 14223;
        const string restrictedLogin = "mtd-live-test"; // User and password, from Samples/nats-live-restricted.conf.

        IConnection serverSide; // Plays DDS and the T-Connect server.
        NatsMtdTransport transport;

        [SetUp]
        public void SetUp()
        {
            Options options = ConnectionFactory.GetDefaultOptions();
            options.Url = $"nats://{host}:{port}";
            options.Timeout = 2000;
            serverSide = new ConnectionFactory().CreateConnection(options);

            transport = new NatsMtdTransport(new MtdConnectionSettings { Host = host, Port = port });
            transport.Start();
            Assert.IsTrue(WaitFor(() => transport.Connected, 5), "MTD's transport did not connect.");
        }

        [TearDown]
        public void TearDown()
        {
            transport?.Dispose();
            serverSide?.Close();
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

        [Test]
        public void Summary_PublishedTheWayDdsWould_ReachesTheRegistry()
        {
            MainThreadQueue queue = new MainThreadQueue();
            transport.Subscribe(MtdTopics.TableSummaryFilter("anysite"), queue.PostMessage);
            Thread.Sleep(300); // Let the subscription reach the server.

            byte[] payload = TestSamples.ReadBytes("roulette-power-reel.json");
            Guid tableId = TestSamples.TableId(4);

            // DDS builds the MQTT-form topic and its NATS transport maps it to this subject.
            serverSide.Publish($"tcsjh.table.{tableId}.mtdsummary", payload);
            // A neighbouring subject MTD must not receive.
            serverSide.Publish($"tcsjh.table.{tableId}.gamesummary", payload);
            serverSide.Flush(2000);

            TableRegistry registry = new TableRegistry(35);
            int received = 0;
            Assert.IsTrue(WaitFor(() =>
            {
                queue.Drain((topic, bytes) =>
                {
                    received++;
                    TableSnapshot snapshot = SummaryReader.Read(topic, bytes, out _, out string problem);
                    Assert.IsNull(problem);
                    registry.Apply(snapshot, 0);
                });
                return received > 0;
            }, 3), "No summary arrived.");

            Thread.Sleep(200);
            queue.Drain((topic, bytes) => received++);
            Assert.AreEqual(1, received, "Only the mtdsummary subject should be delivered.");

            Assert.IsTrue(registry.TryGet(tableId, out TableState state));
            Assert.AreEqual(RouletteVariant.PowerReel, ((RouletteSnapshot)state.Snapshot).Variant);
        }

        [Test]
        public void Request_ToAServiceNobodyAnswers_ReturnsNull()
        {
            byte[] reply = transport.RequestAsync("mtd.test.nobody-home", new byte[] { 1 }, TimeSpan.FromSeconds(1)).Result;

            Assert.IsNull(reply);
        }

        [Test]
        public void KeyValue_GetAndWatch_DeliverTheValueAndLaterChanges()
        {
            const string bucket = "mtd-live-test";
            string key = MtdTopics.TableListKey(Guid.NewGuid());
            IKeyValue serverBucket = RecreateBucket(bucket);

            Assert.IsFalse(transport.TryGetKeyValue(bucket, key, out _, out _), "A key that was never written has no value.");

            ulong firstRevision = serverBucket.Put(key, Encoding.UTF8.GetBytes("one"));

            Assert.IsTrue(transport.TryGetKeyValue(bucket, key, out byte[] value, out ulong revision));
            Assert.AreEqual("one", Encoding.UTF8.GetString(value));
            Assert.AreEqual(firstRevision, revision);

            List<string> seen = new List<string>();
            using (transport.WatchKeyValue(bucket, key, (bytes, watchedRevision) =>
            {
                lock (seen) seen.Add(bytes == null ? "<deleted>" : Encoding.UTF8.GetString(bytes));
            }))
            {
                Assert.IsTrue(WaitFor(() => { lock (seen) return seen.Count == 1; }, 3), "The watch did not deliver the current value.");

                serverBucket.Put(key, Encoding.UTF8.GetBytes("two"));
                serverBucket.Delete(key);

                Assert.IsTrue(WaitFor(() => { lock (seen) return seen.Count == 3; }, 3), "The watch did not deliver later changes.");
            }

            lock (seen) CollectionAssert.AreEqual(new[] { "one", "two", "<deleted>" }, seen);
        }

        [Test]
        public void KeyValue_MissingBucket_Throws()
        {
            Assert.Catch(() => transport.TryGetKeyValue("mtd-bucket-that-does-not-exist", "some.key", out _, out _));
        }

        [Test]
        public void TConnectClient_RegistersAsAnMtd_ThenFollowsItsTableList()
        {
            Guid deviceId = Guid.NewGuid();
            Guid displayId = Guid.NewGuid();
            IKeyValue tableListBucket = RecreateBucket(MtdTopics.TableListBucket);
            DeviceAssignmentMessage seenRequest = null;
            int pings = 0;

            // The T-Connect server's two services.
            serverSide.SubscribeAsync(MtdTopics.AssignDeviceSubject, (sender, args) =>
            {
                MtdJson.TryDeserialize(args.Message.Data, out seenRequest, out _);
                DeviceAssignmentResponse response = new DeviceAssignmentResponse
                {
                    TableId = displayId,
                    TableName = "MGM MTD 01",
                    AssignmentCode = "OK",
                    Status = AssignmentStatus.Assigned,
                };
                serverSide.Publish(args.Message.Reply, Encoding.UTF8.GetBytes(MtdJson.Serialize(response)));
            });
            serverSide.SubscribeAsync(MtdTopics.PingSubject, (sender, args) =>
            {
                Interlocked.Increment(ref pings);
                MtdJson.TryDeserialize(args.Message.Data, out PingRequest ping, out _);
                serverSide.Publish(args.Message.Reply, Encoding.UTF8.GetBytes(MtdJson.Serialize(new PingResponse { DeviceId = ping.DeviceId, ServerUtcTime = DateTimeOffset.UtcNow })));
            });
            serverSide.Flush(2000);

            // The server has already published this display's list before the display starts.
            Guid tableA = TestSamples.TableId(1);
            Guid tableB = TestSamples.TableId(2);
            Guid tableC = TestSamples.TableId(3);
            PutTableList(tableListBucket, displayId, tableA, tableB);

            MainThreadQueue queue = new MainThreadQueue();
            TableIdListSource tableList = new TableIdListSource();
            using TConnectMtdClient client = new TConnectMtdClient(transport, deviceId, "MGM MTD 01", queue, tableList);
            client.Start();

            Assert.IsTrue(WaitFor(() => client.State == TConnectAssignmentState.Assigned, 5), $"Not assigned: {client.State}");
            Assert.AreEqual(displayId, client.DisplayId);
            Assert.AreEqual("MGM MTD 01", client.DisplayName);
            Assert.AreEqual("MTD", seenRequest.DeviceType);
            Assert.AreEqual("MGM MTD 01", seenRequest.TableName);
            Assert.AreEqual(deviceId, seenRequest.DeviceId);

            Assert.IsTrue(WaitFor(() => { queue.Drain((k, v) => { }); return tableList.HasList; }, 5), "The table list never arrived.");
            Assert.AreEqual(2, tableList.Count);
            Assert.IsTrue(WaitFor(() => pings > 0, 3), "The display never pinged.");
            Assert.IsNull(client.TableListProblem);

            // An operator changes the list while the display is running.
            PutTableList(tableListBucket, displayId, tableA, tableB, tableC);

            Assert.IsTrue(WaitFor(() => { queue.Drain((k, v) => { }); return tableList.Count == 3; }, 5), "The changed table list never arrived.");
        }

        // The server's bucket is deleted and made again, so its revisions start over at 1 while the display holds a
        // list with a higher revision. The display must end up on the new bucket's list and follow it from there.
        [Test]
        public void TConnectClient_FollowsItsTableList_AfterTheServerRecreatesTheBucket()
        {
            Guid displayId = Guid.NewGuid();
            Guid tableA = TestSamples.TableId(1);
            Guid tableB = TestSamples.TableId(2);
            Guid tableC = TestSamples.TableId(3);

            serverSide.SubscribeAsync(MtdTopics.AssignDeviceSubject, (sender, args) =>
            {
                DeviceAssignmentResponse response = new DeviceAssignmentResponse { TableId = displayId, TableName = "MTD 01", AssignmentCode = "OK", Status = AssignmentStatus.Assigned };
                serverSide.Publish(args.Message.Reply, Encoding.UTF8.GetBytes(MtdJson.Serialize(response)));
            });
            serverSide.Flush(2000);

            IKeyValue bucket = RecreateBucket(MtdTopics.TableListBucket);
            PutTableList(bucket, displayId, tableA);
            PutTableList(bucket, displayId, tableA, tableB);
            PutTableList(bucket, displayId, tableA, tableB, tableC); // Revision 3.

            MainThreadQueue queue = new MainThreadQueue();
            TableIdListSource tableList = new TableIdListSource();
            using TConnectMtdClient client = new TConnectMtdClient(transport, Guid.NewGuid(), "MTD 01", queue, tableList) { PingInterval = TimeSpan.FromMilliseconds(400) };
            client.Start();
            Assert.IsTrue(WaitFor(() => { queue.Drain((k, v) => { }); return tableList.Count == 3; }, 5), "The first list never arrived.");

            bucket = RecreateBucket(MtdTopics.TableListBucket);
            PutTableList(bucket, displayId, tableC); // Revision 1 of the new bucket.

            Assert.IsTrue(WaitFor(() => { queue.Drain((k, v) => { }); return tableList.Count == 1; }, 8), "The display stayed on the old bucket's list.");

            PutTableList(bucket, displayId, tableB, tableC);
            Assert.IsTrue(WaitFor(() => { queue.Drain((k, v) => { }); return tableList.Count == 2; }, 8), "The display did not follow the new bucket.");
        }

        NatsMtdTransport RestrictedTransport()
        {
            try
            {
                using TcpClient probe = new TcpClient();
                probe.Connect(host, restrictedPort);
            }
            catch (SocketException)
            {
                Assert.Ignore("The restricted nats-server is not running (see the top of this file).");
            }

            return new NatsMtdTransport(new MtdConnectionSettings { Host = host, Port = restrictedPort, Username = restrictedLogin, Password = restrictedLogin })
            {
                RetryInterval = TimeSpan.FromMilliseconds(500),
                StableAfter = TimeSpan.FromSeconds(2),
            };
        }

        // When the server denies a subscription, this NATS client's connection is closed for good. MTD must say why
        // on the status display and keep connecting, so that granting the permission is all it takes to recover.
        [Test]
        public void SubscriptionTheServerDenies_IsReported_AndTheTransportKeepsTrying()
        {
            using NatsMtdTransport restricted = RestrictedTransport();
            restricted.Subscribe(MtdTopics.TableSummaryFilter("anysite"), (topic, payload) => { });
            restricted.Start();

            Assert.IsTrue(WaitFor(() => (restricted.LastError ?? "").Contains("permissions violation"), 10), $"The denial was not reported: {restricted.LastError}");
            StringAssert.Contains("tcsjh.table.*.mtdsummary", restricted.LastError);
            int generation = restricted.ConnectionGeneration;

            Assert.IsTrue(WaitFor(() => restricted.ConnectionGeneration >= generation + 3, 10), "It stopped trying to connect.");
            StringAssert.Contains("permissions violation", restricted.LastError ?? "", "The reason must stay on show between attempts.");
        }

        [Test]
        public void SubscriptionTheServerAllows_OnTheSameLogin_ReportsNothing()
        {
            using NatsMtdTransport restricted = RestrictedTransport();
            restricted.Subscribe("tcsjh/anysite/table/+/somethingelse", (topic, payload) => { });
            restricted.Start();

            Assert.IsTrue(WaitFor(() => restricted.Connected, 10), restricted.LastError);
            Thread.Sleep(1500);

            Assert.IsTrue(restricted.Connected);
            Assert.IsNull(restricted.LastError);
            Assert.AreEqual(1, restricted.ConnectionGeneration, "The connection must not have been replaced.");
        }

        IKeyValue RecreateBucket(string bucket)
        {
            IKeyValueManagement management = serverSide.CreateKeyValueManagementContext(null);
            try
            {
                management.Delete(bucket);
            }
            catch (NATSJetStreamException)
            {
                // It did not exist.
            }

            management.Create(KeyValueConfiguration.Builder().WithName(bucket).WithStorageType(StorageType.Memory).Build());
            return serverSide.CreateKeyValueContext(bucket, null);
        }

        static void PutTableList(IKeyValue bucket, Guid displayId, params Guid[] tableIds)
        {
            MtdTableListMessage message = new MtdTableListMessage { DisplayId = displayId, AvailableTableIds = new List<Guid>(tableIds) };
            bucket.Put(MtdTopics.TableListKey(displayId), Encoding.UTF8.GetBytes(MtdJson.Serialize(message)));
        }
    }
}
