using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Parameters;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI;

namespace UR.RTDE.Grasshopper
{
    public class UR_RobotiqCommandSelector : GH_Component
    {
        private int _selectedIndex = 1; // Default to "Open"

        private static readonly string[] Commands = { "Activate", "Open", "Close", "Move", "SetSpeed", "SetForce" };

        public UR_RobotiqCommandSelector()
          : base("Robotiq Command", "RobotiqCmd",
            "Select a Robotiq gripper command from the dropdown.",
            "UR", "RTDE")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            // No inputs
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddIntegerParameter("Command", "C", "Selected command index (0=Activate, 1=Open, 2=Close, 3=Move, 4=SetSpeed, 5=SetForce)", GH_ParamAccess.item);
            p.AddTextParameter("Name", "N", "Command name", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (_selectedIndex < 0 || _selectedIndex >= Commands.Length)
                _selectedIndex = 1; // Default to "Open"

            da.SetData(0, _selectedIndex);
            da.SetData(1, Commands[_selectedIndex]);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "UR.RTDE.Grasshopper.Resources.Icons.robot-duotone.png";
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                        return new System.Drawing.Bitmap(stream);
                }
                return null;
            }
        }

        public override Guid ComponentGuid => new Guid("9bc73e82-5417-48ea-92d2-83d41a7f9bce");

        public override void CreateAttributes()
        {
            m_attributes = new UR_RobotiqCommandSelectorAttributes(this);
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetInt32("selectedIndex", _selectedIndex);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("selectedIndex"))
                _selectedIndex = reader.GetInt32("selectedIndex");
            return base.Read(reader);
        }

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (value != _selectedIndex && value >= 0 && value < Commands.Length)
                {
                    _selectedIndex = value;
                    ExpireSolution(true);
                }
            }
        }

        public static string[] GetCommands() => Commands;
    }

    public class UR_RobotiqCommandSelectorAttributes : GH_ComponentAttributes
    {
        public UR_RobotiqCommandSelectorAttributes(UR_RobotiqCommandSelector owner) : base(owner)
        {
            _owner = owner;
        }

        private readonly UR_RobotiqCommandSelector _owner;
        private RectangleF _dropdownBounds;
        private bool _mouseOver;

        protected override void Layout()
        {
            base.Layout();

            var scale = GH_GraphicsUtil.UiScale <= 0 ? 1f : GH_GraphicsUtil.UiScale;
            var s = 4f / scale;
            var dropdownHeight = 24f / scale;

            var body = Bounds;
            var reservedHeight = dropdownHeight + 4f * s;
            Bounds = new RectangleF(body.X, body.Y, body.Width, body.Height + reservedHeight);
            body = Bounds;

            var dropdownTop = body.Bottom - reservedHeight;
            var dropdownWidth = Math.Max(120f / scale, body.Width - 4f * s);
            var dropdownX = body.X + (body.Width - dropdownWidth) * 0.5f;
            _dropdownBounds = new RectangleF(dropdownX, dropdownTop + 2f * s, dropdownWidth, dropdownHeight);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects) return;
            if (_owner.Locked || _owner.Hidden) return;

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var scale = GH_GraphicsUtil.UiScale <= 0 ? 1f : GH_GraphicsUtil.UiScale;

            // Draw dropdown background
            var bgColor = _mouseOver ? Color.FromArgb(240, 240, 240) : Color.FromArgb(250, 250, 250);
            var borderColor = Color.FromArgb(200, 200, 200);
            var corner = (int)Math.Max(2, Math.Round(4f / scale));

            using (var path = RoundedRect(_dropdownBounds, corner))
            {
                using (var brush = new SolidBrush(bgColor))
                    graphics.FillPath(brush, path);
                graphics.DrawPath(new Pen(borderColor, 1f), path);
            }

            // Draw dropdown arrow
            var arrowSize = 6f / scale;
            var arrowX = _dropdownBounds.Right - arrowSize - 4f / scale;
            var arrowY = _dropdownBounds.Y + _dropdownBounds.Height * 0.5f;
            var arrowPoints = new[]
            {
                new PointF(arrowX, arrowY - arrowSize * 0.5f),
                new PointF(arrowX + arrowSize, arrowY - arrowSize * 0.5f),
                new PointF(arrowX + arrowSize * 0.5f, arrowY + arrowSize * 0.5f)
            };
            graphics.FillPolygon(Brushes.Gray, arrowPoints);

            // Draw selected text
            var selectedText = UR_RobotiqCommandSelector.GetCommands()[_owner.SelectedIndex];
            var font = new Font(GH_FontServer.Standard.FontFamily, GH_FontServer.Standard.Size / scale);
            var textBounds = new RectangleF(
                _dropdownBounds.X + 6f / scale,
                _dropdownBounds.Y,
                arrowX - _dropdownBounds.X - 8f / scale,
                _dropdownBounds.Height);
            graphics.DrawString(selectedText, font, Brushes.Black, textBounds, new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            });
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_owner.Locked || _owner.Hidden) return base.RespondToMouseDown(sender, e);
            if (e.Button == MouseButtons.Left && _dropdownBounds.Contains(e.CanvasLocation))
            {
                ShowDropdownMenu();
                return GH_ObjectResponse.Handled;
            }
            return base.RespondToMouseDown(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_owner.Locked || _owner.Hidden) return base.RespondToMouseMove(sender, e);
            bool wasOver = _mouseOver;
            _mouseOver = _dropdownBounds.Contains(e.CanvasLocation);
            if (wasOver != _mouseOver)
            {
                Owner.OnDisplayExpired(false);
                sender.Cursor = _mouseOver ? Cursors.Hand : Cursors.Default;
            }
            return base.RespondToMouseMove(sender, e);
        }

        private void ShowDropdownMenu()
        {
            var menu = new ContextMenuStrip();
            var commands = UR_RobotiqCommandSelector.GetCommands();
            for (int i = 0; i < commands.Length; i++)
            {
                int index = i; // Capture for closure
                var item = menu.Items.Add(commands[i]) as ToolStripMenuItem;
                item.Click += (s, e) =>
                {
                    _owner.SelectedIndex = index;
                    Owner.OnDisplayExpired(false);
                };
                if (item != null && i == _owner.SelectedIndex)
                    item.Checked = true;
            }

            menu.Show(Instances.ActiveCanvas, new System.Drawing.Point(
                (int)(_dropdownBounds.X + _dropdownBounds.Width),
                (int)(_dropdownBounds.Y + _dropdownBounds.Height)));
        }

        private static GraphicsPath RoundedRect(RectangleF bounds, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            var size = new Size(diameter, diameter);
            var arc = new RectangleF(bounds.Location, size);

            if (radius == 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }
    }
}
