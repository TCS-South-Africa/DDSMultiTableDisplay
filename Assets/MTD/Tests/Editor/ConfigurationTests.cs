using System;
using System.IO;
using NUnit.Framework;

namespace MTD.Tests
{
    public class ConfigurationTests
    {
        string folder;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "mtd-config-tests-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        string MainFile => Path.Combine(folder, MtdConfigurationStore.FileName);
        string BackupFile => Path.Combine(folder, MtdConfigurationStore.BackupFileName);
        string CorruptFile => Path.Combine(folder, MtdConfigurationStore.CorruptFileName);

        [Test]
        public void MissingFile_GivesDefaults_AndWritesThem()
        {
            MtdConfigurationData data = MtdConfigurationStore.Load(out MtdConfigurationStore.LoadSource source, folder);

            Assert.AreEqual(MtdConfigurationStore.LoadSource.Defaults, source);
            Assert.IsFalse(data.tConnect.connect);
            Assert.IsTrue(data.tConnect.useNats);
            Assert.AreEqual("MTD 01", data.tConnect.mtdName);
            Assert.AreEqual(TableListSourceKind.TConnect, data.tables.tableListSource);
            Assert.AreEqual(35, data.tables.tableTimeoutSeconds);
            Assert.IsTrue(data.tables.skipClosedTables);
            Assert.AreEqual(6, data.display.numberOfTables);
            Assert.IsFalse(data.display.demoMode);
            Assert.AreEqual(SwitchingMode.Pages, data.switching.mode);
            Assert.AreEqual(120, data.switching.dwellTimeSeconds);
            CollectionAssert.AreEqual(new[] {120,120,120,120,120,120}, data.switching.slotIntervals);
            Assert.AreNotEqual(Guid.Empty, data.DeviceId);
            Assert.IsTrue(File.Exists(MainFile));
        }

        [Test]
        public void DeviceId_IsCreatedOnce_AndKept()
        {
            Guid first = MtdConfigurationStore.Load(out _, folder).DeviceId;
            Guid second = MtdConfigurationStore.Load(out MtdConfigurationStore.LoadSource source, folder).DeviceId;

            Assert.AreEqual(MtdConfigurationStore.LoadSource.File, source);
            Assert.AreEqual(first, second);
        }

        [Test]
        public void SavedValues_RoundTrip()
        {
            MtdConfigurationData data = MtdConfigurationStore.Load(out _, folder);
            data.tConnect.connect = true;
            data.tConnect.url = "floorserver01";
            data.tConnect.port = 4223;
            data.tConnect.username = "tconnect";
            data.tConnect.password = "secret";
            data.tConnect.siteName = "Crown";
            data.tConnect.useTLS = true;
            data.tConnect.allowUntrustedCertificate = true;
            data.tConnect.useNats = false;
            data.tConnect.mtdName = "Pit 3 MTD";
            data.tables.tableListSource = TableListSourceKind.ManualEntry;
            data.tables.manualTables.Add(new MtdConfigurationData.ManualTable { tableId = TestSamples.TableId(19).ToString("B"), slot = 6 });
            data.display.useMtdTableUniqueName = true;
            data.tables.tableTimeoutSeconds = 60;
            data.tables.skipClosedTables = false;
            data.display.mtdUi = "columns-4k";
            data.display.numberOfTables = 4;
            data.display.demoMode = true;
            data.display.demoTables = 14;
            data.switching.mode = SwitchingMode.SlotBySlot;
            data.switching.dwellTimeSeconds = 90;
            data.switching.slotIntervals[1] = 3;

            MtdConfigurationStore.Save(data, folder);
            MtdConfigurationData loaded = MtdConfigurationStore.Load(out MtdConfigurationStore.LoadSource source, folder);

            Assert.AreEqual(MtdConfigurationStore.LoadSource.File, source);
            Assert.IsTrue(loaded.tConnect.connect);
            Assert.AreEqual("floorserver01", loaded.tConnect.url);
            Assert.AreEqual(4223, loaded.tConnect.port);
            Assert.AreEqual("tconnect", loaded.tConnect.username);
            Assert.AreEqual("secret", loaded.tConnect.password);
            Assert.AreEqual("Crown", loaded.tConnect.siteName);
            Assert.IsTrue(loaded.tConnect.useTLS);
            Assert.IsTrue(loaded.tConnect.allowUntrustedCertificate);
            Assert.IsFalse(loaded.tConnect.useNats);
            Assert.AreEqual("Pit 3 MTD", loaded.tConnect.mtdName);
            Assert.AreEqual(data.DeviceId, loaded.DeviceId);
            Assert.AreEqual(TableListSourceKind.ManualEntry, loaded.tables.tableListSource);
            Assert.AreEqual(TestSamples.TableId(19).ToString(), loaded.tables.manualTables[0].tableId);
            Assert.AreEqual(6, loaded.tables.manualTables[0].slot);
            Assert.IsTrue(loaded.display.useMtdTableUniqueName);
            Assert.AreEqual(60, loaded.tables.tableTimeoutSeconds);
            Assert.IsFalse(loaded.tables.skipClosedTables);
            Assert.AreEqual("columns-4k", loaded.display.mtdUi);
            Assert.AreEqual(4, loaded.display.numberOfTables);
            Assert.IsTrue(loaded.display.demoMode);
            Assert.AreEqual(14, loaded.display.demoTables);
            Assert.AreEqual(SwitchingMode.SlotBySlot, loaded.switching.mode);
            Assert.AreEqual(90, loaded.switching.dwellTimeSeconds);
            Assert.AreEqual(3, loaded.switching.slotIntervals[1]);
        }

        [Test]
        public void SecondSave_KeepsThePreviousFileAsLastKnownGood()
        {
            MtdConfigurationData data = MtdConfigurationStore.Load(out _, folder);
            data.tConnect.mtdName = "First";
            MtdConfigurationStore.Save(data, folder);
            data.tConnect.mtdName = "Second";
            MtdConfigurationStore.Save(data, folder);

            StringAssert.Contains("Second", File.ReadAllText(MainFile));
            StringAssert.Contains("First", File.ReadAllText(BackupFile));
        }

        [Test]
        public void DamagedFile_FallsBackToLastKnownGood_AndRepairsTheMainFile()
        {
            MtdConfigurationData data = MtdConfigurationStore.Load(out _, folder);
            data.tConnect.mtdName = "Good";
            MtdConfigurationStore.Save(data, folder);
            data.tConnect.mtdName = "Newer";
            MtdConfigurationStore.Save(data, folder);

            File.WriteAllText(MainFile, "{ this is not json");

            MtdConfigurationData loaded = MtdConfigurationStore.Load(out MtdConfigurationStore.LoadSource source, folder);

            Assert.AreEqual(MtdConfigurationStore.LoadSource.Backup, source);
            Assert.AreEqual("Good", loaded.tConnect.mtdName);
            Assert.AreEqual(data.DeviceId, loaded.DeviceId, "The device ID must survive a damaged file.");
            StringAssert.Contains("not json", File.ReadAllText(CorruptFile));
            Assert.AreEqual("Good", MtdConfigurationStore.Load(out source, folder).tConnect.mtdName);
            Assert.AreEqual(MtdConfigurationStore.LoadSource.File, source);
        }

        // Repairing the main file used to move the damaged file into the backup's place, so the one good copy was
        // destroyed by the act of using it.
        [Test]
        public void RepairingADamagedFile_LeavesTheLastKnownGoodCopyIntact()
        {
            MtdConfigurationData data = MtdConfigurationStore.Load(out _, folder);
            data.tConnect.mtdName = "Good";
            MtdConfigurationStore.Save(data, folder);
            data.tConnect.mtdName = "Newer";
            MtdConfigurationStore.Save(data, folder);
            string backupBefore = File.ReadAllText(BackupFile);

            File.WriteAllText(MainFile, "{ this is not json");
            MtdConfigurationStore.Load(out _, folder);

            Assert.AreEqual(backupBefore, File.ReadAllText(BackupFile), "The backup must be the same good file as before the repair.");
            StringAssert.DoesNotContain("not json", File.ReadAllText(BackupFile));

            // A second failure before anything is saved again must still recover the device identity and settings.
            File.WriteAllText(MainFile, "");
            MtdConfigurationData recovered = MtdConfigurationStore.Load(out MtdConfigurationStore.LoadSource source, folder);

            Assert.AreEqual(MtdConfigurationStore.LoadSource.Backup, source);
            Assert.AreEqual("Good", recovered.tConnect.mtdName);
            Assert.AreEqual(data.DeviceId, recovered.DeviceId);
        }

        [Test]
        public void SavingOverADamagedFile_DoesNotKeepItAsLastKnownGood()
        {
            MtdConfigurationData data = MtdConfigurationStore.Load(out _, folder);
            data.tConnect.mtdName = "First";
            MtdConfigurationStore.Save(data, folder);
            data.tConnect.mtdName = "Second";
            MtdConfigurationStore.Save(data, folder);

            File.WriteAllText(MainFile, "\0\0\0\0");
            data.tConnect.mtdName = "Third";
            MtdConfigurationStore.Save(data, folder);

            StringAssert.Contains("Third", File.ReadAllText(MainFile));
            StringAssert.Contains("First", File.ReadAllText(BackupFile));
        }

        [Test]
        public void DamagedFileWithNoBackup_GivesDefaults()
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(MainFile, "");

            MtdConfigurationData loaded = MtdConfigurationStore.Load(out MtdConfigurationStore.LoadSource source, folder);

            Assert.AreEqual(MtdConfigurationStore.LoadSource.Defaults, source);
            Assert.AreEqual(6, loaded.display.numberOfTables);
        }

        [Test]
        public void Normalise_PutsOutOfRangeValuesBack()
        {
            MtdConfigurationData data = new MtdConfigurationData();
            data.tConnect.port = 99999;
            data.tConnect.mtdName = "   ";
            data.tConnect.url = null;
            data.tables.tableTimeoutSeconds = 0;
            data.display.numberOfTables = 5;
            data.display.demoTables = 0;
            data.switching.dwellTimeSeconds = -3;
            data.switching.slotIntervals[1] = 0;
            data.switching.mode = (SwitchingMode)42;

            data.Normalise();

            Assert.AreEqual(65535, data.tConnect.port);
            Assert.AreEqual("MTD 01", data.tConnect.mtdName);
            Assert.AreEqual("", data.tConnect.url);
            Assert.AreEqual(MtdConfigurationData.MinTableTimeoutSeconds, data.tables.tableTimeoutSeconds);
            Assert.AreEqual(6, data.display.numberOfTables);
            Assert.AreEqual(1, data.display.demoTables);
            Assert.AreEqual(5, data.switching.dwellTimeSeconds);
            Assert.AreEqual(5, data.switching.slotIntervals[1]);
            Assert.AreEqual(SwitchingMode.Pages, data.switching.mode);
        }

        [Test]
        public void ManualEntryNormalisesGuidSpellingsAndPreservesOrderAndInactiveSlots()
        {
            var data = new MtdConfigurationData();
            data.display.numberOfTables = 4;
            var first = TestSamples.TableId(1);
            var second = TestSamples.TableId(2);
            var entries = data.tables.manualTables;
            entries.Add(new MtdConfigurationData.ManualTable { tableId = second.ToString("B"), slot = 6 });
            entries.Add(new MtdConfigurationData.ManualTable { tableId = "VIP" });
            entries.Add(new MtdConfigurationData.ManualTable { tableId = first.ToString() });
            entries.Add(new MtdConfigurationData.ManualTable { tableId = second.ToString(), slot = 1 });
            data.Normalise();
            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual(second.ToString(), entries[0].tableId);
            Assert.AreEqual(5, entries[0].SlotIndex(0, 4));
            Assert.AreEqual(1, entries[1].SlotIndex(1, 4));
            var clone = data.Clone();
            clone.tables.manualTables.RemoveAt(0);
            Assert.AreEqual(2, entries.Count);
        }

        [Test]
        public void OldDiscoverySettingsOpenAnEmptyManualList()
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(MainFile, "{\"tables\":{\"tableListSource\":1,\"mtdTableNumbers\":\"1-6\"},\"switching\":{\"slotSwapIntervalSeconds\":3}}");
            var loaded = MtdConfigurationStore.Load(out _, folder);
            Assert.AreEqual(TableListSourceKind.ManualEntry, loaded.EffectiveTableListSource);
            Assert.IsEmpty(loaded.tables.manualTables);
            Assert.AreEqual(120, loaded.switching.slotIntervals[0]);
        }

        [Test]
        public void PartialFile_KeepsDefaultsForWhatItLeavesOut()
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(MainFile, "{ \"display\": { \"demoMode\": true } }");

            MtdConfigurationData loaded = MtdConfigurationStore.Load(out _, folder);

            Assert.IsTrue(loaded.display.demoMode);
            Assert.AreEqual(6, loaded.display.numberOfTables);
            Assert.AreEqual(120, loaded.switching.dwellTimeSeconds);
            Assert.IsTrue(loaded.tConnect.useNats);
        }

        [Test]
        public void MqttMode_UsesManualEntry()
        {
            MtdConfigurationData data = new MtdConfigurationData();
            data.tables.tableListSource = TableListSourceKind.TConnect;

            data.tConnect.useNats = true;
            Assert.AreEqual(TableListSourceKind.TConnect, data.EffectiveTableListSource);
            Assert.AreEqual(4222, data.EffectivePort);

            data.tConnect.useNats = false;
            Assert.AreEqual(TableListSourceKind.ManualEntry, data.EffectiveTableListSource);
            Assert.AreEqual(1883, data.EffectivePort);

            data.tConnect.port = 8883;
            Assert.AreEqual(8883, data.EffectivePort);
        }
    }
}
