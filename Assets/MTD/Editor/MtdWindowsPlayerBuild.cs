using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MTD.Editor
{
    internal sealed class MtdWindowsPlayerBuild : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            string player = report.summary.outputPath;
            if (report.summary.platform != BuildTarget.StandaloneWindows64 ||
                !string.Equals(Path.GetExtension(player), ".exe", StringComparison.OrdinalIgnoreCase))
                return;

            using (var reader = new BinaryReader(File.OpenRead(player)))
            {
                reader.BaseStream.Position = 0x3c;
                int peOffset = reader.ReadInt32();
                reader.BaseStream.Position = peOffset + 4;
                if (reader.ReadUInt16() != 0x8664) return; // This entry point is for x64.
            }

            string template = Path.Combine(Application.dataPath, "../Tools/WindowsPlayer/WindowsPlayer-x64.bin");
            string staging = player + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(template, staging);
                CopyPlayerResources(player, staging);
                File.Replace(staging, player, null);
            }
            finally
            {
                File.Delete(staging);
            }
        }

        // Unity generates the project icons, manifest and version information on each build.
        // Transfer them to the new entry point before replacing the executable.
        private static void CopyPlayerResources(string player, string staging)
        {
            IntPtr source = LoadLibraryExW(player, IntPtr.Zero, 2); // LOAD_LIBRARY_AS_DATAFILE
            if (source == IntPtr.Zero) throw new Win32Exception();
            try
            {
                IntPtr update = BeginUpdateResourceW(staging, true);
                if (update == IntPtr.Zero) throw new Win32Exception();
                try
                {
                    int copyError = 0;
                    bool CopyLanguage(IntPtr module, IntPtr type, IntPtr name, ushort language, IntPtr unused)
                    {
                        IntPtr resource = FindResourceExW(module, type, name, language);
                        if (resource != IntPtr.Zero)
                        {
                            uint size = SizeofResource(module, resource);
                            IntPtr loaded = LoadResource(module, resource);
                            if (loaded != IntPtr.Zero)
                            {
                                IntPtr bytes = LockResource(loaded);
                                if (bytes != IntPtr.Zero && UpdateResourceW(update, type, name, language, bytes, size))
                                    return true;
                            }
                        }
                        copyError = Marshal.GetLastWin32Error();
                        return false;
                    }

                    bool CopyName(IntPtr module, IntPtr type, IntPtr name, IntPtr unused) =>
                        EnumResourceLanguagesW(module, type, name, CopyLanguage, IntPtr.Zero);
                    bool CopyType(IntPtr module, IntPtr type, IntPtr unused) =>
                        EnumResourceNamesW(module, type, CopyName, IntPtr.Zero);

                    if (!EnumResourceTypesW(source, CopyType, IntPtr.Zero))
                        throw new Win32Exception(copyError != 0 ? copyError : Marshal.GetLastWin32Error());
                    IntPtr completed = update;
                    update = IntPtr.Zero;
                    if (!EndUpdateResourceW(completed, false)) throw new Win32Exception();
                }
                finally
                {
                    if (update != IntPtr.Zero) EndUpdateResourceW(update, true);
                }
            }
            finally
            {
                FreeLibrary(source);
            }
        }

        private delegate bool ResourceType(IntPtr module, IntPtr type, IntPtr context);
        private delegate bool ResourceName(IntPtr module, IntPtr type, IntPtr name, IntPtr context);
        private delegate bool ResourceLanguage(IntPtr module, IntPtr type, IntPtr name, ushort language, IntPtr context);

        [DllImport("kernel32", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
        [DllImport("kernel32")]
        private static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr BeginUpdateResourceW(string path, bool deleteExisting);
        [DllImport("kernel32", SetLastError = true)]
        private static extern bool EnumResourceTypesW(IntPtr module, ResourceType callback, IntPtr context);
        [DllImport("kernel32", SetLastError = true)]
        private static extern bool EnumResourceNamesW(IntPtr module, IntPtr type, ResourceName callback, IntPtr context);
        [DllImport("kernel32", SetLastError = true)]
        private static extern bool EnumResourceLanguagesW(IntPtr module, IntPtr type, IntPtr name, ResourceLanguage callback, IntPtr context);
        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr FindResourceExW(IntPtr module, IntPtr type, IntPtr name, ushort language);
        [DllImport("kernel32", SetLastError = true)]
        private static extern uint SizeofResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr LockResource(IntPtr loaded);
        [DllImport("kernel32", SetLastError = true)]
        private static extern bool UpdateResourceW(IntPtr update, IntPtr type, IntPtr name, ushort language, IntPtr bytes, uint size);
        [DllImport("kernel32", SetLastError = true)]
        private static extern bool EndUpdateResourceW(IntPtr update, bool discard);
    }
}
