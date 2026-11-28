using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Znake.Tests
{
    internal static class ReplayOverlayTests
    {
        public static void Run()
        {
            SnapshotCarriesEffectiveVisionRadiusIncludingZero();
            VisionOverlayDrawsOnlyAroundLivingSnakes();
            SpeedAndDoubleFpsFormatCorrectly();
            CpuEncoderFallbackProducesValidMp4();
        }

        private static void SnapshotCarriesEffectiveVisionRadiusIncludingZero()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Znake-vision-log-" + Guid.NewGuid().ToString("N"));
            try
            {
                var world = new World(12, 8, 21, null, 12);
                world.StartShow(2, true, true);
                world.Snakes[0].Genes[Gene.VisionRadius] = 5;
                world.Snakes[0].ApplyFruitTimer(0, false);
                world.Snakes[1].Genes[Gene.VisionRadius] = 0;

                using (ShowLog log = ShowLog.Open(folder, 1))
                {
                    log.Start(world, new SaverSettings(), new Rectangle(0, 0, 144, 96), null, Point.Empty, 10);
                    log.Record(world, 2.5, true, 0.5);
                }

                string[] lines = File.ReadAllLines(Path.Combine(folder, "show.log.jsonl"));
                TestRunner.Check(lines.Length == 2, "Snapshot log has start and snapshot lines");
                string snap = lines[1];
                TestRunner.Check(snap.Contains("\"night\":0.5"), "Snapshot records night context");
                TestRunner.Check(snap.Contains("\"visionRadius\":10") && snap.Contains("\"visionRadius\":0"),
                    "Snapshot records effective visionRadius including 0 disable case");
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        private static void VisionOverlayDrawsOnlyAroundLivingSnakes()
        {
            var start = new Znake.Replay.Record
            {
                Type = "start",
                Width = 20,
                Height = 14,
                Square = 10,
                Cast = 2,
                Display = new Znake.Replay.ShowRect { X = 0, Y = 0, Width = 200, Height = 140 }
            };
            var scene = new Znake.Replay.Record
            {
                Type = "snapshot",
                VisibleSeconds = 1,
                Population = 1,
                Bodies = new List<Znake.Replay.Body>
                {
                    new Znake.Replay.Body
                    {
                        Id = 1,
                        Heading = 0,
                        VisionRadius = 4,
                        Red = 54,
                        Green = 177,
                        Blue = 80,
                        Points = new List<Znake.Replay.Mark> { new Znake.Replay.Mark { X = 10, Y = 7 } }
                    },
                    new Znake.Replay.Body
                    {
                        Id = 2,
                        Heading = 0,
                        VisionRadius = 4,
                        Red = 200,
                        Green = 80,
                        Blue = 80,
                        Points = new List<Znake.Replay.Mark>() // dead / empty body
                    }
                },
                Fruits = new List<Znake.Replay.Mark>(),
                Eggs = new List<Znake.Replay.Mark>(),
                Vines = new List<Znake.Replay.Mark>()
            };

            using (var withoutOverlay = new Bitmap(200, 140, PixelFormat.Format32bppArgb))
            using (var withOverlay = new Bitmap(200, 140, PixelFormat.Format32bppArgb))
            {
                using (Graphics g1 = Graphics.FromImage(withoutOverlay))
                {
                    g1.Clear(Color.Black);
                    Znake.Replay.Program.DrawScene(g1, start, scene, false);
                }
                using (Graphics g2 = Graphics.FromImage(withOverlay))
                {
                    g2.Clear(Color.Black);
                    Znake.Replay.Program.DrawScene(g2, start, scene, true);
                }

                // Pixel 25px right of head (within 40px radius, outside body and HUD) should differ when overlay is on.
                Color cOff = withoutOverlay.GetPixel(130, 75);
                Color cOn = withOverlay.GetPixel(130, 75);
                TestRunner.Check(cOff.ToArgb() != cOn.ToArgb() && cOn.G > cOff.G,
                    "Vision overlay draws translucent circle around living snake");

                // Pixel far outside radius (bottom-right corner) stays unchanged.
                TestRunner.Check(withoutOverlay.GetPixel(195, 135).ToArgb() == withOverlay.GetPixel(195, 135).ToArgb(),
                    "Vision overlay leaves pixels outside radius untouched");
            }
        }

        private static void SpeedAndDoubleFpsFormatCorrectly()
        {
            TestRunner.Near(Znake.Replay.Program.ComputeHold(1.3, 13.0), 0.1, 1e-9,
                "Speed divides concat hold duration");
            TestRunner.Check(Znake.Replay.Program.FormatFilter(12.5) == "fps=12.5,format=yuv420p",
                "12.5 default fps formats cleanly into ffmpeg filter");
            string[] cpuArgs = Znake.Replay.Program.BuildEncoderArgs("libx264");
            TestRunner.Check(cpuArgs.Contains("libx264"), "CPU fallback builds libx264 encoder arguments");
        }

        private static void CpuEncoderFallbackProducesValidMp4()
        {
            if (Znake.Replay.Program.FindFfmpeg() == null) return;

            string folder = Path.Combine(Path.GetTempPath(), "Znake-replay-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var world = new World(16, 10, 77, null, 10);
                world.StartShow(2, true, true);
                using (ShowLog log = ShowLog.Open(folder, 1))
                {
                    log.Start(world, new SaverSettings(), new Rectangle(0, 0, 160, 100), null, Point.Empty, 4);
                    log.Record(world, 0, true, 0);
                    world.ShowSeconds = 2;
                    log.Record(world, 1.0, true, 0);
                    world.ShowSeconds = 4;
                    world.ShowComplete = true;
                    log.Record(world, 1.0, true, 0);
                }

                string framesRoot = Path.Combine(folder, "frames");
                Directory.CreateDirectory(framesRoot);
                string existingFile = Path.Combine(framesRoot, "keep.txt");
                File.WriteAllText(existingFile, "keep");
                string mp41x = Path.Combine(framesRoot, "out-1x.mp4");
                string mp413x = Path.Combine(folder, "out-13x.mp4");
                int code1 = Znake.Replay.Program.Run(new[]
                {
                    "--log", Path.Combine(folder, "show.log.jsonl"),
                    "--out", mp41x,
                    "--frames", framesRoot,
                    "--hold", "2.6",
                    "--fps", "12.5",
                    "--speed", "1",
                    "--vision",
                    "--encoder", "cpu"
                });
                int code2 = Znake.Replay.Program.Run(new[]
                {
                    "--log", Path.Combine(folder, "show.log.jsonl"),
                    "--out", mp413x,
                    "--hold", "2.6",
                    "--fps", "30",
                    "--speed", "13",
                    "--vision",
                    "--encoder", "cpu"
                });
                TestRunner.Check(code1 == 0 && File.Exists(mp41x) && new FileInfo(mp41x).Length > 0,
                    "12.5 fps CPU render produces non-empty mp4");
                TestRunner.Check(File.Exists(existingFile) && File.ReadAllText(existingFile) == "keep" &&
                    Directory.GetDirectories(framesRoot).Length == 0,
                    "Successful replay cleans up its frames without modifying other files");
                TestRunner.Check(code2 == 0 && File.Exists(mp413x) && new FileInfo(mp413x).Length > 0,
                    "30 fps 13x speed CPU render produces non-empty mp4");
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
