using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Media;

namespace Znake.ControlPanel
{
    internal sealed class LayerSettings
    {
        public bool ShowGrid = true;
        public bool ShowVines = true;
        public bool ShowFruits = true;
        public bool ShowSnakes = true;
        public bool ShowVision = true;
        public float VinesOpacity = 0.7f;
        public float FruitsOpacity = 1.0f;
        public float SnakesOpacity = 1.0f;
        public float VisionOpacity = 0.65f;

        public LayerSettings Clone()
        {
            return new LayerSettings
            {
                ShowGrid = ShowGrid,
                ShowVines = ShowVines,
                ShowFruits = ShowFruits,
                ShowSnakes = ShowSnakes,
                ShowVision = ShowVision,
                VinesOpacity = VinesOpacity,
                FruitsOpacity = FruitsOpacity,
                SnakesOpacity = SnakesOpacity,
                VisionOpacity = VisionOpacity
            };
        }
    }

    internal sealed class GridSceneState
    {
        public string BackgroundPath;
        public Znake.Replay.Record Start;
        public Znake.Replay.Record Snapshot;
        public LayerSettings Layers = new LayerSettings();
        public System.Drawing.Color BackdropColor = System.Drawing.Color.FromArgb(14, 18, 20);
    }

    internal interface IGpuCapabilityProbe
    {
        bool IsHardwareGpuAvailable();
    }

    internal sealed class WpfGpuCapabilityProbe : IGpuCapabilityProbe
    {
        public bool IsHardwareGpuAvailable()
        {
            try
            {
                return (RenderCapability.Tier >> 16) >= 2;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    internal interface IGridRenderer : IDisposable
    {
        bool IsGpuAccelerated { get; }
        Control ViewControl { get; }
        void UpdateScene(GridSceneState scene);
        Bitmap RenderToBitmap(GridSceneState scene, int width, int height);
    }

    internal static class GridRendererFactory
    {
        public static IGridRenderer Create(bool preferGpu, IGpuCapabilityProbe probe = null)
        {
            IGpuCapabilityProbe effectiveProbe = probe ?? new WpfGpuCapabilityProbe();
            if (preferGpu && effectiveProbe.IsHardwareGpuAvailable())
            {
                try
                {
                    return new GpuGridRenderer();
                }
                catch (Exception)
                {
                    return new GdiGridRenderer();
                }
            }
            return new GdiGridRenderer();
        }
    }
}
