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
        static readonly TimeSpan connectionPollInterval = TimeSpan.FromSeconds(1);

        // How often the display pings the server and reads its table list directly.
        internal TimeSpan PingInterval = TimeSpan.FromSeconds(30);

        readonly NatsMtdTransport transport;
        readonly Guid deviceId;
        readonly string mtdName;
        readonly MainThreadQueue mainThread;
        readonly TableIdListSource tableList;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        // Main thread only: which table list is in force. See ApplyTableList.
        ulong appliedRevision;
        bool hasAppliedValue;
        ulong lowerRevisionReadBefore;

        long unreadableRevision = -1; // Background threads. Keeps an unreadable entry from being logged every 30 seconds.
        volatile bool watchIsOnAnOldBucket; // Set on the main thread, acted on by the client's own task.
        IDisposable watch;
        int watchGeneration; // The transport's connection generation the watch was made on.
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

                    await Task.Delay(PingInterval, token).ConfigureAwait(false);
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
                // A watch belongs to one bucket on one connection. If the server made the bucket again, or the
                // transport had to replace its connection, the watch in hand is dead and a new one is needed.
                int generation = transport.ConnectionGeneration;
                if (watchIsOnAnOldBucket || generation != watchGeneration)
                {
                    watchIsOnAnOldBucket = false;
                    StopWatch();
                }

                if (watch == null)
                {
                    watch = transport.WatchKeyValue(MtdTopics.TableListBucket, key, (watchedValue, watchedRevision) => OnTableListValue(watchedValue, watchedRevision, isDirectRead: false));
                    watchGeneration = generation;
                }

                if (transport.TryGetKeyValue(MtdTopics.TableListBucket, key, out byte[] value, out ulong revision))
                {
                    OnTableListValue(value, revision, isDirectRead: true);
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

        // Runs on a NATS thread (watch) or the client's own task (direct read). The two can deliver the same or
        // different revisions in either order, so nothing is decided here: the entry is only read and handed to the
        // main thread, where ApplyTableList decides in one place.
        internal void OnTableListValue(byte[] value, ulong revision, bool isDirectRead)
        {
            List<Guid> tableIds = new List<Guid>();
            if (value != null)
            {
                if (!MtdJson.TryDeserialize(value, out MtdTableListMessage message, out string error))
                {
                    if (Interlocked.Exchange(ref unreadableRevision, (long)revision) != (long)revision)
                    {
                        Debug.Log($"MTD: T-Connect table list revision {revision} could not be read, so the list in use stays: {error}");
                    }

                    return;
                }

                if (message.AvailableTableIds != null) tableIds.AddRange(message.AvailableTableIds);
            }

            mainThread.Post(() => ApplyTableList(tableIds, revision, isDirectRead));
        }

        // Main thread only. The revision is compared at the moment the list is put in force, so however the watch
        // and the direct read interleave, an older list can never replace a newer one.
        //
        // A revision lower than the one in force is normally just that: old. The exception is a bucket that was
        // deleted and made again on the server, whose revisions start over. A direct read reports what the server
        // holds now, so the same lower revision from two direct reads in a row (30 seconds apart) is taken as the
        // server's current list.
        internal bool ApplyTableList(List<Guid> tableIds, ulong revision, bool isDirectRead)
        {
            if (hasAppliedValue && revision <= appliedRevision)
            {
                if (!isDirectRead) return false;

                if (revision == appliedRevision)
                {
                    lowerRevisionReadBefore = 0;
                    return false;
                }

                if (revision != lowerRevisionReadBefore)
                {
                    lowerRevisionReadBefore = revision;
                    return false;
                }

                Debug.Log($"MTD: T-Connect's table list went back from revision {appliedRevision} to {revision} and stayed there; taking it as the current list");
                watchIsOnAnOldBucket = true;
            }

            appliedRevision = revision;
            hasAppliedValue = true;
            lowerRevisionReadBefore = 0;
            tableList.SetTables(tableIds);
            Debug.Log($"MTD: T-Connect table list revision {revision} has {tableIds.Count} tables");
            return true;
        }

        public void Dispose()
        {
            lifetime.Cancel();
            StopWatch();
        }

        void StopWatch()
        {
            IDisposable toStop = watch;
            watch = null;
            if (toStop == null) return;

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
