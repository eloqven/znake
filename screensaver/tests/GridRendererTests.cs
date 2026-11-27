using System;

namespace Znake.Tests
{
    internal static class GridRendererTests
    {
        public static void Run()
        {
            FactoryReturnsGdiWhenNoGpu();
            GdiRenderToBitmapNonNull();
            LayerOpacityCloning();
            GridSceneStateDefaults();
        }

        private static void FactoryReturnsGdiWhenNoGpu()
        {
            // provide a probe that always says no GPU
            var probe = new NoGpuProbe();
            var renderer = Znake.ControlPanel.GridRendererFactory.Create(true, probe);
            TestRunner.Check(renderer != null, "GridRendererFactory returns non-null");
            TestRunner.Check(!renderer.IsGpuAccelerated, "No-GPU probe produces GDI renderer");
            renderer.Dispose();
        }

        private static void GdiRenderToBitmapNonNull()
        {
            var renderer = new Znake.ControlPanel.GdiGridRenderer();
            var scene    = new Znake.ControlPanel.GridSceneState();
            var bitmap   = renderer.RenderToBitmap(scene, 64, 64);
            TestRunner.Check(bitmap != null, "GdiGridRenderer.RenderToBitmap returns bitmap");
            TestRunner.Check(bitmap.Width == 64,  "Bitmap width 64");
            TestRunner.Check(bitmap.Height == 64, "Bitmap height 64");
            bitmap.Dispose();
            renderer.Dispose();
        }

        private static void LayerOpacityCloning()
        {
            var orig = new Znake.ControlPanel.LayerSettings
            {
                ShowVines    = false,
                VinesOpacity = 0.3f,
                ShowSnakes   = true,
                SnakesOpacity = 0.9f
            };
            var clone = orig.Clone();
            TestRunner.Check(!clone.ShowVines,       "Layer clone ShowVines false");
            TestRunner.Check(clone.VinesOpacity == 0.3f,  "Layer clone VinesOpacity");
            TestRunner.Check(clone.ShowSnakes,       "Layer clone ShowSnakes true");
            TestRunner.Check(clone.SnakesOpacity == 0.9f, "Layer clone SnakesOpacity");

            // mutate original — clone must be independent
            orig.ShowVines = true;
            orig.VinesOpacity = 1.0f;
            TestRunner.Check(!clone.ShowVines,       "Clone independent: ShowVines");
            TestRunner.Check(clone.VinesOpacity == 0.3f, "Clone independent: VinesOpacity");
        }

        private static void GridSceneStateDefaults()
        {
            var scene = new Znake.ControlPanel.GridSceneState();
            TestRunner.Check(scene.Start    == null, "GridSceneState.Start null by default");
            TestRunner.Check(scene.Snapshot == null, "GridSceneState.Snapshot null by default");
            TestRunner.Check(scene.Layers   != null, "GridSceneState.Layers non-null by default");
        }

        private sealed class NoGpuProbe : Znake.ControlPanel.IGpuCapabilityProbe
        {
            public bool IsHardwareGpuAvailable() { return false; }
        }
    }
}
