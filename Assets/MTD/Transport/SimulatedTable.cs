using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MTD
{
    // One pretend table for Demo Mode. It keeps a statistics window of results and builds the same summary message
    // a DDS table would send, so everything downstream of the transport is exercised exactly as it is live.
    public sealed class SimulatedTable
    {
        const int statisticsWindow = 200;
        const int resultsToSend = 20;
        const int hotColdToSend = 8;

        static readonly int[] xtraMultipliers = { 50, 100, 150, 200, 250, 500 };
        static readonly int[] powerReelMultipliers = { 17, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
        static readonly string[] chipColours = { "#B71C1C", "#1565C0", "#2E7D32", "#6A1B9A", "#C9A227", "#AD1457", "#D2691E", "#37474F", "#00838F", "#4E342E" };

        readonly Random random;
        readonly string gameType;
        readonly GameFamily family;
        readonly RouletteVariant variant;
        readonly bool doubleZeroWheel;
        readonly List<Entry> history = new List<Entry>(); // Oldest first, like DDS's own log.

        struct Entry
        {
            public string Result;
            public int Multiplier;
            public string MultiplierColour;
        }

        public SimulatedTable(int number, string gameType, string tableName, string colour, long minBet, long maxBet, int seed)
        {
            random = new Random(seed);
            this.gameType = gameType;
            family = GameTypes.GetFamily(gameType, out variant);
            doubleZeroWheel = number % 2 == 0;

            TableId = new Guid($"dddddddd-0000-4000-8000-{number:D12}");
            TableNumber = number;
            TableName = tableName;
            Colour = colour;
            MinBet = minBet;
            MaxBet = maxBet;
            GameId = Guid.NewGuid();

            for (int i = 0; i < statisticsWindow; i++) history.Add(NextEntry());
        }

        public Guid TableId { get; }
        public int TableNumber { get; }
        public string TableName { get; }
        public string Colour { get; }
        public long MinBet { get; }
        public long MaxBet { get; }
        public Guid GameId { get; private set; }
        public bool TableOpen { get; set; } = true;

        public void PlayGame()
        {
            history.Add(NextEntry());
            if (history.Count > statisticsWindow) history.RemoveAt(0);
            GameId = Guid.NewGuid();
        }

        Entry NextEntry()
        {
            if (family == GameFamily.SicBo)
            {
                int[] dice = { random.Next(1, 7), random.Next(1, 7), random.Next(1, 7) };
                Array.Sort(dice);
                return new Entry { Result = $"{dice[0]}-{dice[1]}-{dice[2]}" };
            }

            int pocketCount = doubleZeroWheel ? 38 : 37;
            int pocket = random.Next(pocketCount);
            Entry entry = new Entry { Result = pocket == 37 ? "00" : pocket.ToString(CultureInfo.InvariantCulture) };

            if (variant == RouletteVariant.PowerReel)
            {
                int index = random.Next(powerReelMultipliers.Length);
                entry.Multiplier = powerReelMultipliers[index];
                entry.MultiplierColour = chipColours[index];
            }
            else if (variant == RouletteVariant.Xtra && random.Next(pocketCount) < 4)
            {
                // Four bonus numbers are drawn each game, so about four results in thirty-seven carry a multiplier.
                int index = random.Next(xtraMultipliers.Length);
                entry.Multiplier = xtraMultipliers[index];
                entry.MultiplierColour = chipColours[index];
            }

            return entry;
        }

        public MtdTableSummaryMessage BuildSummary(DateTimeOffset now)
        {
            MtdTableSummaryMessage message = new MtdTableSummaryMessage
            {
                SchemaVersion = MtdTableSummaryMessage.CurrentSchemaVersion,
                TableId = TableId,
                GameId = GameId,
                GameType = gameType,
                TableName = TableName,
                TableNumber = TableNumber,
                MTDTableUniqueName = "Demo " + TableNumber.ToString("D2", CultureInfo.InvariantCulture),
                MTDColourSetting = Colour,
                TableOpen = TableOpen,
                MinBet = MinBet,
                MaxBet = MaxBet,
                MinBetText = FormatAmount(MinBet),
                MaxBetText = FormatAmount(MaxBet),
                Results = new List<MtdResultMessage>(),
                Statistics = new Dictionary<string, string>(),
                EventTime = now,
            };

            for (int i = history.Count - 1; i >= 0 && message.Results.Count < resultsToSend; i--)
            {
                Entry entry = history[i];
                message.Results.Add(new MtdResultMessage
                {
                    Result = entry.Result,
                    Multiplier = entry.Multiplier > 0 ? entry.Multiplier : (int?)null,
                    MultiplierColour = entry.Multiplier > 0 ? entry.MultiplierColour : null,
                });
            }

            if (family == GameFamily.SicBo) AddSicBoStatistics(message);
            else AddRouletteStatistics(message);

            return message;
        }

        void AddRouletteStatistics(MtdTableSummaryMessage message)
        {
            List<string> pockets = Enumerable.Range(0, 37).Select(number => number.ToString(CultureInfo.InvariantCulture)).ToList();
            if (doubleZeroWheel) pockets.Add("00");

            Dictionary<string, int> hits = pockets.ToDictionary(pocket => pocket, pocket => 0);
            int red = 0, black = 0, zero = 0, odd = 0, even = 0, low = 0, high = 0;
            int[] dozens = new int[3];
            int[] columns = new int[3];

            foreach (Entry entry in history)
            {
                hits[entry.Result]++;
                RoulettePocket.TryParse(entry.Result, out RoulettePocket pocket);
                if (pocket.IsZero)
                {
                    zero++;
                    continue;
                }

                int number = int.Parse(entry.Result, CultureInfo.InvariantCulture);
                if (pocket.Colour == RouletteColour.Red) red++; else black++;
                if (number % 2 == 1) odd++; else even++;
                if (number <= 18) low++; else high++;
                dozens[(number - 1) / 12]++;
                columns[(number - 1) % 3]++;
            }

            // The pocket order breaks ties, so the lists do not shuffle between identical messages.
            message.HotNumbers = pockets.OrderByDescending(pocket => hits[pocket]).Take(hotColdToSend).ToList();
            message.ColdNumbers = pockets.OrderBy(pocket => hits[pocket]).Take(hotColdToSend).ToList();

            Set(message, "SampleSize", history.Count);
            Set(message, "Red", red);
            Set(message, "Black", black);
            Set(message, "Zero", zero);
            Set(message, "Odd", odd);
            Set(message, "Even", even);
            Set(message, "Low", low);
            Set(message, "High", high);
            for (int i = 0; i < 3; i++)
            {
                Set(message, $"Dozen{i + 1}", dozens[i]);
                Set(message, $"Column{i + 1}", columns[i]);
            }
        }

        void AddSicBoStatistics(MtdTableSummaryMessage message)
        {
            int small = 0, big = 0, triple = 0, odd = 0, even = 0;
            int[] faces = new int[7];
            Dictionary<int, int> totals = Enumerable.Range(4, 14).ToDictionary(total => total, total => 0); // 4 to 17.

            foreach (Entry entry in history)
            {
                SicBoResult.TryParse(entry.Result, out SicBoResult result);
                faces[result.Die1]++;
                faces[result.Die2]++;
                faces[result.Die3]++;

                if (result.IsTriple)
                {
                    triple++;
                    continue;
                }

                if (result.Outcome == SicBoOutcome.Big) big++; else small++;
                if (result.Total % 2 == 1) odd++; else even++;
                totals[result.Total]++;
            }

            message.HotNumbers = totals.Keys.OrderByDescending(total => totals[total]).Take(hotColdToSend)
                .Select(total => total.ToString(CultureInfo.InvariantCulture)).ToList();
            message.ColdNumbers = totals.Keys.OrderBy(total => totals[total]).Take(hotColdToSend)
                .Select(total => total.ToString(CultureInfo.InvariantCulture)).ToList();

            Set(message, "SampleSize", history.Count);
            Set(message, "Small", small);
            Set(message, "Big", big);
            Set(message, "Triple", triple);
            Set(message, "Odd", odd);
            Set(message, "Even", even);
            for (int face = 1; face <= 6; face++) Set(message, $"Face{face}", faces[face]);
        }

        static void Set(MtdTableSummaryMessage message, string key, int value)
        {
            message.Statistics[key] = value.ToString(CultureInfo.InvariantCulture);
        }

        // DDS stores limits in cents and formats them itself; this is a plain stand-in.
        static string FormatAmount(long cents)
        {
            return "$" + (cents / 100).ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
