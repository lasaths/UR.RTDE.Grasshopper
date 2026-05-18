using UR.RTDE.Native;

namespace UR.RTDE.Grasshopper
{
    /// <summary>
    /// Ensures UR.RTDE native libraries are loaded before Rhino/Grasshopper touches RTDE APIs.
    /// </summary>
    internal static class NativeBootstrap
    {
        public static void EnsureLoaded() => MacOsNativeLibraryBootstrap.EnsureInitialized();

        public static string LastLoadError => MacOsNativeLibraryBootstrap.LastLoadError ?? string.Empty;
    }
}
