using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Debug = UnityEngine.Debug;

namespace MTD
{
    // Demo Mode: pretend tables that publish summaries the way DDS tables would, with no broker involved.
    // Messages are delivered from a timer thread, like a real transport, so the same threading path is used.
    public sealed class SimulatedTransport : IMtdTransport
    {
        const double heartbeatSeconds = 10; // The contract's heartbeat.
        const double minSecondsBetweenGames = 12;
        const double maxSecondsBetweenGames = 30;
        const int tickMilliseconds = 250;

        // One screen of six, in the order of the first design: three standard roulette tables, Power Reel,
        // Roulette Xtra and SicBo.
        static readonly (string gameType, string tableName)[] pattern =
        {
            ("ROULETTE", "AR"), ("ROULETTE", "AR"), ("ROULETTE", "AR"), ("ROULETTE_POWER", "AR"), ("ROULETTE_XTRA", "AR"), ("SICBO", "SB"),
        };

        static readonly string[] colours = { "#3F31F3", "#097437", "#E60004", "#611F8C", "#DF4C28", "#0A75B5" };
        static readonly (long min, long max)[] limits = { (1000, 1000000), (2500, 250000), (5000, 500000), (2000, 50000) };

        readonly object gate = new object();
        readonly List<MtdMessageHandler> handlers = new List<MtdMessageHandler>();
        readonly List<TableSchedule> tables = new List<TableSchedule>();
        readonly Random random = new Random(12345);
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();

        Timer timer;
        volatile bool disposed;
        int ticking;

        sealed class TableSchedule
        {
            public SimulatedTable Table;
            public string Topic;
            public double NextGameAt;
            public double NextHeartbeatAt;
        }

        public SimulatedTransport(int tableCount)
        {
            for (int number = 1; number <= Math.Max(0, tableCount); number++)
            {
                (string gameType, string tableName) = pattern[(number - 1) % pattern.Length];
                (long min, long max) = limits[(number - 1) % limits.Length];
                SimulatedTable table = new SimulatedTable(number, gameType, tableName, colours[(number - 1) % colours.Length], min, max, seed: 1000 + number);

                tables.Add(new TableSchedule
                {
                    Table = table,
                    Topic = MtdTopics.TableSummary("demo", table.TableId),
                    // Tables report in over the first two seconds in no particular order, as real ones would.
                    NextHeartbeatAt = random.NextDouble() * 2,
                    NextGameAt = minSecondsBetweenGames * random.NextDouble() + 3,
                });
            }
        }

        public IEnumerable<Guid> TableIds
        {
            get { foreach (TableSchedule schedule in tables) yield return schedule.Table.TableId; }
        }

        public string Name => "Demo";

        public bool Connected => timer != null && !disposed;

        public string LastError => null;

        public void Start()
        {
            lock (gate)
            {
                if (timer != null || disposed) return;
                clock.Start();
                timer = new Timer(Tick, null, tickMilliseconds, tickMilliseconds);
            }

            Debug.Log($"MTD: Demo Mode started with {tables.Count} simulated tables");
        }

        public void Subscribe(string mqttTopicFilter, MtdMessageHandler handler)
        {
            lock (gate)
            {
                handlers.Add(handler); // Only table summaries are simulated, so every subscriber gets them all.
            }
        }

        void Tick(object state)
        {
            if (disposed || Interlocked.Exchange(ref ticking, 1) == 1) return; // Skip a tick that overlaps the last.

            try
            {
                double now = clock.Elapsed.TotalSeconds;
                foreach (TableSchedule schedule in tables)
                {
                    bool publish = false;

                    if (now >= schedule.NextGameAt)
                    {
                        schedule.Table.PlayGame();
                        schedule.NextGameAt = now + minSecondsBetweenGames + random.NextDouble() * (maxSecondsBetweenGames - minSecondsBetweenGames);
                        publish = true;
                    }

                    if (now >= schedule.NextHeartbeatAt) publish = true;
                    if (!publish) continue;

                    schedule.NextHeartbeatAt = now + heartbeatSeconds;
                    Publish(schedule);
                }
            }
            catch (Exception exception)
            {
                Debug.Log($"MTD: Demo Mode error: {exception}");
            }
            finally
            {
                Interlocked.Exchange(ref ticking, 0);
            }
        }

        void Publish(TableSchedule schedule)
        {
            byte[] payload = Encoding.UTF8.GetBytes(MtdJson.Serialize(schedule.Table.BuildSummary(DateTimeOffset.UtcNow)));

            MtdMessageHandler[] targets;
            lock (gate)
            {
                targets = handlers.ToArray();
            }

            foreach (MtdMessageHandler handler in targets)
            {
                if (disposed) return;
                handler(schedule.Topic, payload);
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                timer?.Dispose();
            }
        }
    }
}
