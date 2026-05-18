using System;
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

        public static void EnsureLoaded() => _ = Initialized.Value;

        public static string LastLoadError => MacOsNativeLibraryBootstrap.LastLoadError ?? _windowsLastError ?? string.Empty;

        private static string _windowsLastError;
#if NET8_0_OR_GREATER
        private static IntPtr _windowsNativeHandle;
#endif

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
            const string libraryFileName = "ur_rtde_c_api.dll";
            string assemblyDir = Path.GetDirectoryName(typeof(RTDEReceive).Assembly.Location);
            if (string.IsNullOrWhiteSpace(assemblyDir))
            {
                _windowsLastError = "Could not determine UR.RTDE assembly directory for native libraries.";
                throw new DllNotFoundException(_windowsLastError);
            }

            string libraryPath = ResolveWindowsLibraryPath(assemblyDir, libraryFileName);
            if (string.IsNullOrEmpty(libraryPath))
            {
                _windowsLastError =
                    $"Native dependency not found: {libraryFileName}. assemblyDir={assemblyDir}";
                throw new DllNotFoundException(_windowsLastError);
            }

#if NET8_0_OR_GREATER
            if (_windowsNativeHandle != IntPtr.Zero)
                return;

            _windowsNativeHandle = NativeLibrary.Load(libraryPath);
            NativeLibrary.SetDllImportResolver(typeof(RTDEReceive).Assembly, ResolveWindowsNativeLibrary);
#else
            if (WindowsLoadLibrary(libraryPath) == IntPtr.Zero)
            {
                _windowsLastError =
                    $"Failed to load '{libraryFileName}' from '{libraryPath}'. Win32 error: {Marshal.GetLastWin32Error()}";
                throw new DllNotFoundException(_windowsLastError);
            }
#endif
        }

#if NET8_0_OR_GREATER
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

        private static bool IsPrimaryNativeLibraryName(string libraryName)
        {
            return string.Equals(libraryName, "ur_rtde_c_api", StringComparison.OrdinalIgnoreCase)
                || string.Equals(libraryName, "ur_rtde_c_api.dll", StringComparison.OrdinalIgnoreCase);
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

#if !NET8_0_OR_GREATER
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        private static IntPtr WindowsLoadLibrary(string path) => LoadLibrary(path);
#endif
    }
}
