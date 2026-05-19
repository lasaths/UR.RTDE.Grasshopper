using Grasshopper.Kernel;

namespace UR.RTDE.Grasshopper
{
    /// <summary>
    /// Runs native bootstrap as early as Grasshopper loads this assembly (before components solve).
    /// </summary>
    public sealed class UR_PluginPriority : GH_AssemblyPriority
    {
        public override GH_LoadingInstruction PriorityLoad()
        {
            URSession.EnsurePluginInitialized();
            return GH_LoadingInstruction.Proceed;
        }
    }
}
