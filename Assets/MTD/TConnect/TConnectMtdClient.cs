using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

namespace MTD
{
    public enum TConnectAssignmentState
    {
        NotStarted,
        WaitingForConnection,
        Requesting,
        NoReply, // The T-Connect server did not answer.
        Conflict, // The server refused: this name belongs to another device.
        Error,
        Assigned,
    }

    // MTD's side of the T-Connect server's MTD model, NATS only:
    //  1. registers this display as a device of type MTD, which gives it a display ID;
    //  2. pings every 30 seconds so the server sees the display online;
    //  3. follows the display's entry in the "mtd-table-list" Key-Value bucket and feeds it to the table list.
    //
    // Everything runs on background threads. The table list itself is only touched on the main thread.
    public sealed class TConnectMtdClient : IDisposable
    {
        const string deviceType = "MTD";
        static readonly TimeSpan requestTimeout = TimeSpan.FromSeconds(3);
        static readonly TimeSpan assignmentRetryInterval = TimeSpan.FromSeconds(30);
        static readonly TimeSpan pingInterval = TimeSpan.FromSeconds(30);
        static readonly TimeSpan connectionPollInterval = TimeSpan.FromSeconds(1);

        readonly NatsMtdTransport transport;
        readonly Guid deviceId;
        readonly string mtdName;
        readonly MainThreadQueue mainThread;
        readonly TableIdListSource tableList;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly object revisionGate = new object();

        ulong appliedRevision;
        bool hasAppliedValue;
        IDisposable watch;
        bool started;

        public TConnectMtdClient(NatsMtdTransport transport, Guid deviceId, string mtdName, MainThreadQueue mainThread, TableIdListSource tableList)
        {
            this.transport = transport;
            this.deviceId = deviceId;
            this.mtdName = mtdName;
            this.mainThread = mainThread;
            this.tableList = tableList;
        }

        // Read from the main thread for the status display. Each is written whole from a background thread.
        public volatile TConnectAssignmentState State = TConnectAssignmentState.NotStarted;
        public volatile string AssignmentCode;
        public volatile string DisplayName;
        public volatile string TableListProblem; // Null when the table list is being followed normally.
        public Guid DisplayId { get; private set; }

        public void Start()
        {
            if (started) return;
            started = true;
            Task.Run(RunAsync);
        }

        async Task RunAsync()
        {
            CancellationToken token = lifetime.Token;

            try
            {
                await RegisterAsync(token).ConfigureAwait(false);

                while (!token.IsCancellationRequested)
                {
                    if (transport.Connected)
                    {
                        await PingAsync().ConfigureAwait(false);
                        FollowTableList();
                    }

                    await Task.Delay(pingInterval, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                State = TConnectAssignmentState.Error;
                Debug.Log($"MTD: the T-Connect client stopped unexpectedly: {exception}");
            }
        }

        // Asks the server for this display's ID until it gets one, like DDS does for its table ID.
        async Task RegisterAsync(CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();

                if (!transport.Connected)
                {
                    State = TConnectAssignmentState.WaitingForConnection;
                    await Task.Delay(connectionPollInterval, token).ConfigureAwait(false);
                    continue;
                }

                State = TConnectAssignmentState.Requesting;
                try
                {
                    DeviceAssignmentMessage request = new DeviceAssignmentMessage
                    {
                        RequestId = Guid.NewGuid(),
                        DeviceId = deviceId,
                        DeviceType = deviceType,
                        TableName = mtdName,
                    };

                    byte[] reply = await transport.RequestAsync(MtdTopics.AssignDeviceSubject, Encoding.UTF8.GetBytes(MtdJson.Serialize(request)), requestTimeout).ConfigureAwait(false);
                    if (ApplyAssignment(reply)) return;
                }
                catch (Exception exception)
                {
                    State = TConnectAssignmentState.Error;
                    Debug.Log($"MTD: T-Connect device assignment failed: {exception.Message}");
                }

                await Task.Delay(assignmentRetryInterval, token).ConfigureAwait(false);
            }
        }

        bool ApplyAssignment(byte[] reply)
        {
            if (reply == null)
            {
                State = TConnectAssignmentState.NoReply;
                Debug.Log("MTD: T-Connect device assignment got no reply");
                return false;
            }

            if (!MtdJson.TryDeserialize(reply, out DeviceAssignmentResponse response, out string error))
            {
                State = TConnectAssignmentState.Error;
                Debug.Log($"MTD: T-Connect device assignment reply could not be read: {error}");
                return false;
            }

            AssignmentCode = response.AssignmentCode;
            if (!string.IsNullOrWhiteSpace(response.TableName)) DisplayName = response.TableName;

            if (response.Status != AssignmentStatus.Assigned)
            {
                State = response.Status == AssignmentStatus.Conflict ? TConnectAssignmentState.Conflict : TConnectAssignmentState.Error;
                Debug.Log($"MTD: T-Connect device assignment status {response.Status}, assignment code {response.AssignmentCode}");
                return false;
            }

            if (!response.TableId.HasValue || response.TableId.Value == Guid.Empty)
            {
                State = TConnectAssignmentState.Error;
                Debug.Log("MTD: T-Connect device assignment reply did not include a display ID");
                return false;
            }

            DisplayId = response.TableId.Value;
            State = TConnectAssignmentState.Assigned;
            Debug.Log($"MTD: registered with T-Connect as display \"{DisplayName ?? mtdName}\" ({DisplayId})");
            return true;
        }

        async Task PingAsync()
        {
            try
            {
                PingRequest request = new PingRequest { DeviceId = deviceId, ClientUtcTime = DateTimeOffset.UtcNow };
                byte[] reply = await transport.RequestAsync(MtdTopics.PingSubject, Encoding.UTF8.GetBytes(MtdJson.Serialize(request)), requestTimeout).ConfigureAwait(false);
                if (reply == null) Debug.Log("MTD: T-Connect ping got no reply");
            }
            catch (Exception exception)
            {
                Debug.Log($"MTD: T-Connect ping failed: {exception.Message}");
            }
        }

        // Starts the watch if it is not running, then reads the entry directly as well. The direct read costs one
        // request every 30 seconds and means a watch that silently stopped cannot leave the display on an old list.
        void FollowTableList()
        {
            string key = MtdTopics.TableListKey(DisplayId);

            try
            {
                watch ??= transport.WatchKeyValue(MtdTopics.TableListBucket, key, OnTableListValue);

                if (transport.TryGetKeyValue(MtdTopics.TableListBucket, key, out byte[] value, out ulong revision))
                {
                    OnTableListValue(value, revision);
                    TableListProblem = null;
                }
                else
                {
                    TableListProblem = "T-Connect has not sent a table list for this display";
                }
            }
            catch (Exception exception)
            {
                // Most often the bucket does not exist yet: the server creates it the first time it sends a list.
                TableListProblem = $"table list not available ({exception.Message})";
                Debug.Log($"MTD: {TableListProblem}");
            }
        }

        // Runs on a NATS thread (watch) or the client's own task (direct read).
        void OnTableListValue(byte[] value, ulong revision)
        {
            lock (revisionGate)
            {
                if (hasAppliedValue && revision <= appliedRevision) return;
                appliedRevision = revision;
                hasAppliedValue = true;
            }

            List<Guid> tableIds = new List<Guid>();
            if (value != null)
            {
                if (!MtdJson.TryDeserialize(value, out MtdTableListMessage message, out string error))
                {
                    Debug.Log($"MTD: the T-Connect table list could not be read: {error}");
                    return;
                }

                if (message.AvailableTableIds != null) tableIds.AddRange(message.AvailableTableIds);
            }

            Debug.Log($"MTD: T-Connect table list revision {revision} has {tableIds.Count} tables");
            mainThread.Post(() => tableList.SetTables(tableIds));
        }

        public void Dispose()
        {
            lifetime.Cancel();

            IDisposable toStop = watch;
            watch = null;
            if (toStop != null)
            {
                // Unsubscribing talks to the server, so it is kept off the caller's thread.
                Task.Run(() =>
                {
                    try
                    {
                        toStop.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                });
            }
        }
    }
}
