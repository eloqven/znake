using System;
using System.Drawing;
using System.Windows.Forms;

namespace Znake.ControlPanel
{
    /// <summary>
    /// Hosts an IGridRenderer and exposes layer toggle/opacity controls.
    /// </summary>
    internal sealed class GridPreview : UserControl
    {
        private IGridRenderer renderer;
        private readonly LayerSettings layers = new LayerSettings();

        // Layer toggle check-boxes
        private readonly CheckBox chkSnakes = new CheckBox { Text = "Snakes", Checked = true, AutoSize = true };
        private readonly CheckBox chkFruits = new CheckBox { Text = "Fruits", Checked = true, AutoSize = true };
        private readonly CheckBox chkVines  = new CheckBox { Text = "Vines",  Checked = true, AutoSize = true };
        private readonly CheckBox chkVision = new CheckBox { Text = "Vision", Checked = true, AutoSize = true };

        // Opacity track-bars (0–100)
        private readonly TrackBar trkSnakes = MakeTrack(100);
        private readonly TrackBar trkFruits = MakeTrack(100);
        private readonly TrackBar trkVines  = MakeTrack(70);
        private readonly TrackBar trkVision = MakeTrack(65);

        private readonly Panel hostPanel;
        private GridSceneState currentScene;

        public event EventHandler LayerChanged;

        public LayerSettings Layers { get { return layers; } }

        public GridPreview()
        {
            hostPanel = new Panel { Dock = DockStyle.Fill, Tag = "grid" };

            var toolbar = BuildToolbar();
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.Controls.Add(toolbar, 0, 0);
            layout.Controls.Add(hostPanel, 0, 1);

            Controls.Add(layout);

            WireEvents();
            SyncLayersFromControls();
        }

        // ── Public API ───────────────────────────────────────────────────────

        public void SetRenderer(IGridRenderer newRenderer)
        {
            if (renderer != null)
            {
                hostPanel.Controls.Remove(renderer.ViewControl);
                renderer.Dispose();
            }
            renderer = newRenderer;
            if (renderer != null)
            {
                renderer.ViewControl.Dock = DockStyle.Fill;
                hostPanel.Controls.Add(renderer.ViewControl);
                if (currentScene != null)
                    renderer.UpdateScene(currentScene);
            }
        }

        public void UpdateScene(GridSceneState scene)
        {
            currentScene = scene;
            if (scene != null) scene.Layers = layers.Clone();
            if (renderer != null) renderer.UpdateScene(currentScene);
        }

        public void ApplySettings(AppSettings s)
        {
            if (s == null) return;
            chkSnakes.Checked = s.ShowSnakes;
            chkFruits.Checked = s.ShowFruits;
            chkVines.Checked  = s.ShowVines;
            chkVision.Checked = s.ShowVisionOverlay;
            trkSnakes.Value   = Clamp100((int)Math.Round(s.SnakesOpacity * 100));
            trkFruits.Value   = Clamp100((int)Math.Round(s.FruitsOpacity * 100));
            trkVines.Value    = Clamp100((int)Math.Round(s.VinesOpacity  * 100));
            trkVision.Value   = Clamp100((int)Math.Round(s.VisionOpacity * 100));
            SyncLayersFromControls();
        }

        public void PersistToSettings(AppSettings s)
        {
            if (s == null) return;
            s.ShowSnakes       = chkSnakes.Checked;
            s.ShowFruits       = chkFruits.Checked;
            s.ShowVines        = chkVines.Checked;
            s.ShowVisionOverlay = chkVision.Checked;
            s.SnakesOpacity    = trkSnakes.Value / 100.0;
            s.FruitsOpacity    = trkFruits.Value / 100.0;
            s.VinesOpacity     = trkVines.Value  / 100.0;
            s.VisionOpacity    = trkVision.Value / 100.0;
        }

        // ── Internals ────────────────────────────────────────────────────────

        private FlowLayoutPanel BuildToolbar()
        {
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(2)
            };

            bar.Controls.Add(chkSnakes);
            bar.Controls.Add(trkSnakes);
            bar.Controls.Add(chkFruits);
            bar.Controls.Add(trkFruits);
            bar.Controls.Add(chkVines);
            bar.Controls.Add(trkVines);
            bar.Controls.Add(chkVision);
            bar.Controls.Add(trkVision);
            return bar;
        }

        private void WireEvents()
        {
            chkSnakes.CheckedChanged += OnLayerControlChanged;
            chkFruits.CheckedChanged += OnLayerControlChanged;
            chkVines.CheckedChanged  += OnLayerControlChanged;
            chkVision.CheckedChanged += OnLayerControlChanged;
            trkSnakes.ValueChanged   += OnLayerControlChanged;
            trkFruits.ValueChanged   += OnLayerControlChanged;
            trkVines.ValueChanged    += OnLayerControlChanged;
            trkVision.ValueChanged   += OnLayerControlChanged;
        }

        private void OnLayerControlChanged(object sender, EventArgs e)
        {
            SyncLayersFromControls();
            if (currentScene != null) currentScene.Layers = layers.Clone();
            if (renderer != null) renderer.UpdateScene(currentScene);
            if (LayerChanged != null) LayerChanged(this, EventArgs.Empty);
        }

        private void SyncLayersFromControls()
        {
            layers.ShowSnakes   = chkSnakes.Checked;
            layers.ShowFruits   = chkFruits.Checked;
            layers.ShowVines    = chkVines.Checked;
            layers.ShowVision   = chkVision.Checked;
            layers.SnakesOpacity = trkSnakes.Value / 100f;
            layers.FruitsOpacity = trkFruits.Value / 100f;
            layers.VinesOpacity  = trkVines.Value  / 100f;
            layers.VisionOpacity = trkVision.Value / 100f;
        }

        private static TrackBar MakeTrack(int value)
        {
            return new TrackBar
            {
                Minimum = 0, Maximum = 100, Value = Clamp100(value),
                Width = 70, TickFrequency = 25, SmallChange = 5, LargeChange = 25
            };
        }

        private static int Clamp100(int v) { return v < 0 ? 0 : v > 100 ? 100 : v; }

        protected override void Dispose(bool disposing)
        {
            if (disposing && renderer != null)
            {
                renderer.Dispose();
                renderer = null;
            }
            base.Dispose(disposing);
        }
    }
}
