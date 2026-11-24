using System;
using System.IO;
using UnityEngine;

namespace MTD
{
    // Reads and writes MtdConfig.json. Follows DDS's approach: write a temporary file, check it reads back, swap it
    // in while keeping the previous file as "last known good", and fall back to that file if the main one is ever
    // unreadable. Only a file that reads back correctly is ever kept as "last known good".
    public static class MtdConfigurationStore
    {
        public const string DefaultFolder = @"C:\tcs\mtd";
        public const string FileName = "MtdConfig.json";
        public const string BackupFileName = "MtdConfig.last-known-good.json";
        public const string CorruptFileName = "MtdConfig.corrupt.json";

        public enum LoadSource
        {
            File,
            Backup, // The main file was unreadable; the last known good copy was used and restored.
            Defaults, // No usable file. A fresh configuration was written.
        }

        // Always returns a usable configuration. Never throws for a missing or damaged file.
        public static MtdConfigurationData Load(out LoadSource source, string folder = DefaultFolder)
        {
            string path = Path.Combine(folder, FileName);
            string backupPath = Path.Combine(folder, BackupFileName);

            MtdConfigurationData data = TryRead(path);
            source = LoadSource.File;

            if (data == null)
            {
                if (File.Exists(path))
                {
                    Debug.Log($"MTD: {path} could not be read. Keeping it as {CorruptFileName}.");
                    TryCopy(path, Path.Combine(folder, CorruptFileName));
                }

                data = TryRead(backupPath);
                source = LoadSource.Backup;

                if (data == null)
                {
                    data = new MtdConfigurationData();
                    source = LoadSource.Defaults;
                }
            }

            bool createdDeviceId = data.Normalise();
            if (source != LoadSource.File || createdDeviceId)
            {
                try
                {
                    Save(data, folder);
                }
                catch (Exception exception)
                {
                    Debug.Log($"MTD: the configuration could not be written to {folder}: {exception.Message}");
                }
            }

            return data;
        }

        public static void Save(MtdConfigurationData data, string folder = DefaultFolder)
        {
            data.Normalise();
            Directory.CreateDirectory(folder);

            string path = Path.Combine(folder, FileName);
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(data, true));

            if (TryRead(temporaryPath) == null)
            {
                File.Delete(temporaryPath);
                throw new IOException("The configuration did not read back correctly after writing, so the existing file was kept.");
            }

            if (!File.Exists(path))
            {
                File.Move(temporaryPath, path);
                return;
            }

            // The file being replaced becomes "last known good" only if it is good. When it is damaged (which is
            // when Load is repairing it from that very backup), it is simply replaced and the backup is left alone.
            bool existingIsGood = TryRead(path) != null;
            File.Replace(temporaryPath, path, existingIsGood ? Path.Combine(folder, BackupFileName) : null);
        }

        static MtdConfigurationData TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;

                return JsonUtility.FromJson<MtdConfigurationData>(json);
            }
            catch (Exception)
            {
                return null; // Not JSON, or not readable. The caller falls back.
            }
        }

        static void TryCopy(string from, string to)
        {
            try
            {
                File.Copy(from, to, true);
            }
            catch (Exception)
            {
                // Keeping a copy of the damaged file is a convenience, not a requirement.
            }
        }
    }
}
