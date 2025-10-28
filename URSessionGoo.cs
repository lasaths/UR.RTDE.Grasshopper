using Grasshopper.Kernel.Types;
using GH_IO.Serialization;

namespace UR.RTDE.Grasshopper
{
    // Type wrapper for passing URSession around in Grasshopper.
    public class URSessionGoo : GH_Goo<URSession>
    {
        public URSessionGoo() { }
        public URSessionGoo(URSession value) : base(value) { }

        public override bool IsValid => Value != null;
        public override string TypeName => "URSession";
        public override string TypeDescription => "UR RTDE session handle";

        public override IGH_Goo Duplicate()
        {
            // Shallow duplicate; the underlying session is shared by reference.
            return new URSessionGoo(Value);
        }

        public override string ToString()
        {
            if (Value == null) return "Null URSession";
            return $"URSession[{Value.Ip}] Connected={Value.IsConnected}";
        }

        // Do not serialize the live session; only store IP metadata.
        private string _savedIp;

        public override bool Write(GH_IWriter writer)
        {
            writer.SetString("ip", Value?.Ip ?? _savedIp ?? string.Empty);
            return true;
        }

        public override bool Read(GH_IReader reader)
        {
            _savedIp = reader.GetString("ip");
            Value = null; // live connections are not serialized
            return true;
        }
    }
}

