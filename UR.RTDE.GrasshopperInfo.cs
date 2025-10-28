using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace UR.RTDE.Grasshopper
{
    public class UR_RTDE_GrasshopperInfo : GH_AssemblyInfo
    {
        public override string Name => "UR.RTDE.Grasshopper";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => IconProvider.Read;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "Minimal Grasshopper components for UR RTDE: session, read, and command (MoveJ/MoveL/Stop/SetDO). Test with URSim first.";

        public override Guid Id => new Guid("6d2ecd23-5f02-4314-9c8a-e5a5dc7a1c53");

        //Return a string identifying you or your company.
        public override string AuthorName => "UR.RTDE.Grasshopper Authors";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "https://github.com/lasaths/UR.RTDE.Grasshopper";

        //Return a string representing the version.  This returns the same version as the assembly.
        public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
    }
}
