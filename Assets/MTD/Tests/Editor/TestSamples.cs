using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace MTD.Tests
{
    // Helpers shared by the test fixtures.
    static class TestSamples
    {
        public static byte[] ReadBytes(string fileName)
        {
            return File.ReadAllBytes(Path.Combine(Application.dataPath, "MTD", "Tests", "Editor", "Samples", fileName));
        }

        public static MtdTableSummaryMessage ReadMessage(string fileName)
        {
            Assert.IsTrue(MtdJson.TryDeserialize(ReadBytes(fileName), out MtdTableSummaryMessage message, out string error), error);
            return message;
        }

        public static TableSnapshot ReadSnapshot(string fileName)
        {
            return SnapshotParser.Parse(ReadMessage(fileName));
        }

        public static byte[] Bytes(string json)
        {
            return Encoding.UTF8.GetBytes(json);
        }

        public static Guid TableId(int number)
        {
            return new Guid($"11111111-0000-4000-8000-{number:D12}");
        }

        // A roulette summary with the given results, newest first. Enough for registry and rotation tests.
        public static MtdTableSummaryMessage Roulette(int tableNumber, params string[] results)
        {
            MtdTableSummaryMessage message = new MtdTableSummaryMessage
            {
                SchemaVersion = 1,
                TableId = TableId(tableNumber),
                GameId = Guid.NewGuid(),
                GameType = "ROULETTE",
                TableName = "AR",
                TableNumber = tableNumber,
                MTDTableNumber = tableNumber.ToString(),
                MTDColourSetting = "#102030",
                TableOpen = true,
                MinBetText = "$10",
                MaxBetText = "$1,000",
                Results = new System.Collections.Generic.List<MtdResultMessage>(),
            };

            foreach (string result in results)
            {
                message.Results.Add(new MtdResultMessage { Result = result });
            }

            return message;
        }

        public static TableSnapshot RouletteSnapshot(int tableNumber, params string[] results)
        {
            return SnapshotParser.Parse(Roulette(tableNumber, results));
        }
    }
}
