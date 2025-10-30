using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

namespace UR.RTDE.Grasshopper
{
    public class URSessionParam : GH_PersistentParam<URSessionGoo>
    {
        public URSessionParam()
          : base("UR Session", "URSession", "UR RTDE session handle.", "UR", "RTDE")
        {
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "UR.RTDE.Grasshopper.Resources.Icons.plugs-connected-duotone.png";
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                        return new System.Drawing.Bitmap(stream);
                }
                return null;
            }
        }

        public override Guid ComponentGuid => new Guid("d4282434-580b-4a8d-a306-7fa14c4a0955");

        protected override GH_GetterResult Prompt_Singular(ref URSessionGoo value)
        {
            return GH_GetterResult.cancel;
        }

        protected override GH_GetterResult Prompt_Plural(ref List<URSessionGoo> values)
        {
            return GH_GetterResult.cancel;
        }
    }
}


