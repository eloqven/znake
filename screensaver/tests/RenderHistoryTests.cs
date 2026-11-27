using System;
using System.Collections.Generic;

namespace Znake.Tests
{
    internal static class RenderHistoryTests
    {
        public static void Run()
        {
            RenderStepArgsBuilder();
            RenderJsonCreation();
            ReRenderWithoutRunner();
            HistoryStarPersistence();
            CompareViewSeriesParsing();
        }

        private static void RenderStepArgsBuilder()
        {
            double fps   = 30.0;
            double speed = 13.0;
            bool   vision = true;
            string enc   = "auto";
            string logPath = "/run/show.log.jsonl";
            string mp4   = "/run/output.mp4";

            string args = Znake.ControlPanel.RenderStep.BuildReplayArgs(logPath, mp4, fps, speed, enc, vision);

            TestRunner.Check(args.Contains("--fps 30"),    "RenderStep: fps 30");
            TestRunner.Check(args.Contains("--speed 13"),  "RenderStep: speed 13");
            TestRunner.Check(args.Contains("--vision"),    "RenderStep: vision flag");
            TestRunner.Check(args.Contains("--encoder auto"), "RenderStep: encoder auto");
            TestRunner.Check(args.Contains("--log \"/run/show.log.jsonl\""), "RenderStep: --log flag");
            TestRunner.Check(args.Contains("--out \"/run/output.mp4\""), "RenderStep: --out flag");
            TestRunner.Check(!args.Contains("--output"), "RenderStep: no unknown --output flag");
        }

        private static void RenderJsonCreation()
        {
            string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "znake-test-render-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            System.IO.Directory.CreateDirectory(tmpDir);
            try
            {
                var history = new Znake.ControlPanel.HistoryStore(tmpDir);
                string folder = System.IO.Path.Combine(tmpDir, "run001");
                System.IO.Directory.CreateDirectory(folder);

                var sidecar = new Znake.ControlPanel.RenderSidecar
                {
                    Fps           = 12.5,
                    Speed         = 1.0,
                    VisionOverlay = true,
                    Encoder       = "cpu",
                    Mp4File       = "output.mp4",
                    RenderedUtc   = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
                };
                history.RecordRender(folder, sidecar);

                string sidecarPath = System.IO.Path.Combine(folder, "render.json");
                TestRunner.Check(System.IO.File.Exists(sidecarPath), "render.json created");

                // reload history and verify entry has render info
                var list = history.List();
                TestRunner.Check(list.Count == 1, "History has one entry after RecordRender");
                TestRunner.Check(list[0].RenderInfo != null, "RenderInfo populated");
                TestRunner.Check(list[0].RenderInfo.Fps == 12.5, "RenderInfo Fps correct");
            }
            finally
            {
                try { System.IO.Directory.Delete(tmpDir, true); } catch (Exception) { }
            }
        }

        private static void ReRenderWithoutRunner()
        {
            // HistoryStore.RecordRender does not require runner to be running — it's standalone
            string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "znake-test-rerender-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            System.IO.Directory.CreateDirectory(tmpDir);
            try
            {
                var history = new Znake.ControlPanel.HistoryStore(tmpDir);
                string folder = System.IO.Path.Combine(tmpDir, "runX");
                System.IO.Directory.CreateDirectory(folder);

                // Record once
                history.RecordRender(folder, new Znake.ControlPanel.RenderSidecar { Fps = 12.5, Mp4File = "v1.mp4" });
                // Re-render (overwrite)
                history.RecordRender(folder, new Znake.ControlPanel.RenderSidecar { Fps = 30.0, Mp4File = "v2.mp4" });

                var list = history.List();
                TestRunner.Check(list.Count == 1, "Re-render doesn't duplicate history entry");
                TestRunner.Check(list[0].RenderInfo.Fps == 30.0, "Re-render updates Fps");
                TestRunner.Check(list[0].Mp4Path.Contains("v2.mp4"), "Re-render updates mp4 path");
            }
            finally
            {
                try { System.IO.Directory.Delete(tmpDir, true); } catch (Exception) { }
            }
        }

        private static void HistoryStarPersistence()
        {
            string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "znake-test-star-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            System.IO.Directory.CreateDirectory(tmpDir);
            try
            {
                var history = new Znake.ControlPanel.HistoryStore(tmpDir);
                string folder = System.IO.Path.Combine(tmpDir, "run001");
                System.IO.Directory.CreateDirectory(folder);
                history.RecordStopped(folder, 99u);

                bool starred = history.ToggleStar(folder);
                TestRunner.Check(starred, "ToggleStar returns true");

                // new instance reads from disk
                var history2 = new Znake.ControlPanel.HistoryStore(tmpDir);
                var list     = history2.List();
                TestRunner.Check(list.Count == 1, "History loaded from disk");
                TestRunner.Check(list[0].Starred, "Star persists across restart");

                // un-star
                bool unstarred = history2.ToggleStar(folder);
                TestRunner.Check(!unstarred, "ToggleStar returns false on second call");
            }
            finally
            {
                try { System.IO.Directory.Delete(tmpDir, true); } catch (Exception) { }
            }
        }

        private static void CompareViewSeriesParsing()
        {
            string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "znake-test-compare-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            System.IO.Directory.CreateDirectory(tmpDir);
            try
            {
                // write a simple series.jsonl
                string jsonl = System.IO.Path.Combine(tmpDir, "series.jsonl");
                var rows = new string[]
                {
                    "{\"gameSeconds\":1.0,\"population\":5,\"moves\":10,\"cast\":10}",
                    "{\"gameSeconds\":2.0,\"population\":4,\"moves\":20,\"cast\":10}",
                    "{\"gameSeconds\":3.0,\"population\":3,\"moves\":31,\"cast\":10}"
                };
                System.IO.File.WriteAllLines(jsonl, rows);

                var series = Znake.ControlPanel.HistoryStore.ReadSeries(tmpDir);
                TestRunner.Check(series.Count == 3, "ReadSeries returns 3 entries");
                TestRunner.Check(series[0].GameSeconds == 1.0, "ReadSeries row 0 GameSeconds");
                TestRunner.Check(series[2].Moves == 31, "ReadSeries row 2 Moves");
            }
            finally
            {
                try { System.IO.Directory.Delete(tmpDir, true); } catch (Exception) { }
            }
        }
    }
}
