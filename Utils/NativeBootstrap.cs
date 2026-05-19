using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using UR.RTDE;
using UR.RTDE.Native;

namespace UR.RTDE.Grasshopper
{
    /// <summary>
    /// Ensures UR.RTDE native libraries are loaded before Rhino/Grasshopper touches RTDE APIs.
    /// </summary>
    internal static class NativeBootstrap
    {
        private static readonly Lazy<bool> Initialized = new(EnsureLoadedCore, LazyThreadSafetyMode.ExecutionAndPublication);

        private static string _windowsLastError;
        private static IntPtr _windowsNativeHandle;

        public static void EnsureLoaded() => _ = Initialized.Value;

        public static string LastLoadError => MacOsNativeLibraryBootstrap.LastLoadError ?? _windowsLastError ?? string.Empty;

        private static bool EnsureLoadedCore()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                MacOsNativeLibraryBootstrap.EnsureInitialized();
                return true;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                EnsureWindowsLoaded();
                return true;
            }

            return true;
        }

        private static void EnsureWindowsLoaded()
        {
            if (_windowsNativeHandle != IntPtr.Zero)
                return;

            const string libraryFileName = "ur_rtde_c_api.dll";
            string libraryPath = null;
            string libraryDir = null;

            foreach (string dir in GetPluginDirectories())
            {
                AddDllSearchDirectory(dir);
                string candidate = ResolveWindowsLibraryPath(dir, libraryFileName);
                if (string.IsNullOrEmpty(candidate))
                    continue;

                libraryPath = candidate;
                libraryDir = Path.GetDirectoryName(candidate);
                break;
            }

            if (string.IsNullOrEmpty(libraryPath))
            {
                _windowsLastError =
                    $"Native dependency not found: {libraryFileName}. Searched plugin dirs: {string.Join(", ", GetPluginDirectories())}";
                throw new DllNotFoundException(_windowsLastError);
            }

            if (!string.IsNullOrEmpty(libraryDir))
            {
                AddDllSearchDirectory(libraryDir);
                PreloadWindowsDependencies(libraryDir);
            }

            _windowsNativeHandle = LoadLibrary(libraryPath);
            if (_windowsNativeHandle == IntPtr.Zero)
            {
                _windowsLastError =
                    $"Failed to load '{libraryFileName}' from '{libraryPath}'. Win32 error: {Marshal.GetLastWin32Error()}";
                throw new DllNotFoundException(_windowsLastError);
            }

#if NET5_0_OR_GREATER
            IntPtr coreClrHandle = NativeLibrary.Load(libraryPath);
            NativeLibrary.SetDllImportResolver(typeof(RTDEReceive).Assembly, ResolveWindowsNativeLibrary);
            if (coreClrHandle != IntPtr.Zero)
                _windowsNativeHandle = coreClrHandle;
#endif
        }

#if NET5_0_OR_GREATER
        private static IntPtr ResolveWindowsNativeLibrary(
            string libraryName,
            Assembly assembly,
            DllImportSearchPath? searchPath)
        {
            if (IsPrimaryNativeLibraryName(libraryName))
            {
                if (_windowsNativeHandle == IntPtr.Zero)
                    throw new DllNotFoundException(
                        "UR.RTDE Windows native library is not initialized. Call NativeBootstrap.EnsureLoaded() first.");

                return _windowsNativeHandle;
            }

            return IntPtr.Zero;
        }
#endif

        private static IEnumerable<string> GetPluginDirectories()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Type marker in new[] { typeof(URSession), typeof(RTDEReceive) })
            {
                string dir = Path.GetDirectoryName(marker.Assembly.Location);
                if (string.IsNullOrWhiteSpace(dir) || !seen.Add(dir))
                    continue;

                yield return dir;
            }
        }

        private static string ResolveWindowsLibraryPath(string assemblyDir, string fileName)
        {
            string rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
            string runtimes = Path.Combine(assemblyDir, "runtimes");

            string[] candidates =
            {
                Path.Combine(assemblyDir, fileName),
                Path.Combine(runtimes, rid, "native", fileName),
                Path.Combine(runtimes, "win-x64", "native", fileName),
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return string.Empty;
        }

        private static void PreloadWindowsDependencies(string directory)
        {
            string[] dependencyFiles =
            {
                "boost_thread-vc143-mt-x64-1_89.dll",
                "boost_thread-vc145-mt-x64-1_89.dll",
                "rtde.dll",
            };

            foreach (string fileName in dependencyFiles)
            {
                string path = Path.Combine(directory, fileName);
                if (File.Exists(path))
                    LoadLibrary(path);
            }
        }

        private static bool IsPrimaryNativeLibraryName(string libraryName)
        {
            return string.Equals(libraryName, "ur_rtde_c_api", StringComparison.OrdinalIgnoreCase)
                || string.Equals(libraryName, "ur_rtde_c_api.dll", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddDllSearchDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return;

            if (AddDllDirectory(directory) == IntPtr.Zero)
                SetDllDirectory(directory);
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr AddDllDirectory(string lpPath);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPath);
    }
}
