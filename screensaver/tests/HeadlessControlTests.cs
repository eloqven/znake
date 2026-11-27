using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;

namespace Znake.Tests
{
    internal static class HeadlessControlTests
    {
        public static void Run()
        {
            RandomSpawnPlacesAllSnakesInsideArenaWithNoEntering();
            RandomSpawnRejectsCastLargerThanFreeCells();
            ValidateOnlyChecksCastWithoutCreatingFolder();
            StopFileEndsRunCleanlyAndResumeContinues();
            ResumeRejectsInvalidManualCheckpointEdit();
            ZeroResidentNoRespawnShowCheckpointsCleanly();
        }

        private static void RandomSpawnPlacesAllSnakesInsideArenaWithNoEntering()
        {
            var world = new World(14, 10, 20261127, null, 12);
            world.StartShow(12, true, true);
            TestRunner.Check(world.Snakes.Count == 12, "Random spawn creates the requested cast");
            TestRunner.Check(world.Snakes.Count(s => s.IsResident) == 1, "Random spawn preserves one resident");
            var heads = new HashSet<Cell>();
            foreach (Snake snake in world.Snakes)
            {
                TestRunner.Check(!snake.Entering, "Random spawn snake has Entering=false");
                if (!snake.IsResident)
                    TestRunner.Check(snake.StaysInArena, "Random spawn arrival stays in arena");
                TestRunner.Check(heads.Add(snake.Body[0]), "Random spawn gives each snake a distinct free head cell");
                TestRunner.Check(snake.Body.All(world.Contains), "Every random-spawn segment begins inside the arena");
            }
        }

        private static void RandomSpawnRejectsCastLargerThanFreeCells()
        {
            var world = new World(5, 4, 99, null, 12);
            bool threw = false;
            try
            {
                world.StartShow(21, true, true);
            }
            catch (ArgumentOutOfRangeException)
            {
                threw = true;
            }
            TestRunner.Check(threw, "Random spawn rejects cast larger than arena free-cell count");
        }

        private static void ValidateOnlyChecksCastWithoutCreatingFolder()
        {
            string root = Path.Combine(Path.GetTempPath(), "Znake-validate-" + Guid.NewGuid().ToString("N"));
            string bg = Path.Combine(root, "bg.png");
            string runFolder = Path.Combine(root, "should-not-exist");
            try
            {
                Directory.CreateDirectory(root);
                WriteSolidPng(bg, 64, 48, Color.FromArgb(40, 120, 70));
                // 64x48 at square 16 => 4x3 = 12 cells. Cast 13 must fail before creating runFolder.
                int failCode = Znake.Headless.Program.Run(new[]
                {
                    "--background", bg,
                    "--folder", runFolder,
                    "--square", "16",
                    "--cast", "13",
                    "--spawn", "random",
                    "--validate-only"
                });
                TestRunner.Check(failCode != 0 && !Directory.Exists(runFolder),
                    "Oversized random cast fails --validate-only without creating output folder");

                int okCode = Znake.Headless.Program.Run(new[]
                {
                    "--background", bg,
                    "--folder", runFolder,
                    "--square", "16",
                    "--cast", "12",
                    "--spawn", "random",
                    "--validate-only"
                });
                TestRunner.Check(okCode == 0 && !Directory.Exists(runFolder),
                    "Valid --validate-only succeeds without creating output folder");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void StopFileEndsRunCleanlyAndResumeContinues()
        {
            string root = Path.Combine(Path.GetTempPath(), "Znake-stop-resume-" + Guid.NewGuid().ToString("N"));
            string bg = Path.Combine(root, "bg.png");
            string folder = Path.Combine(root, "run");
            string stopFile = Path.Combine(root, "stop.flag");
            try
            {
                Directory.CreateDirectory(root);
                WriteSolidPng(bg, 160, 120, Color.FromArgb(54, 177, 80));

                var stopper = new Thread(() =>
                {
                    string progressPath = Path.Combine(folder, "progress.json");
                    for (int i = 0; i < 100 && !File.Exists(progressPath); i++) Thread.Sleep(25);
                    Thread.Sleep(50);
                    File.WriteAllText(stopFile, "stop");
                });
                stopper.Start();

                int stopExit = Znake.Headless.Program.Run(new[]
                {
                    "--background", bg,
                    "--folder", folder,
                    "--square", "16",
                    "--cast", "4",
                    "--seconds", "8",
                    "--pace", "1",
                    "--snapshot", "0.2",
                    "--progress", "0.1",
                    "--spawn", "random",
                    "--stop-file", stopFile,
                    "--no-replay"
                });
                stopper.Join();

                TestRunner.Check(stopExit == 0, "Stop-file run exits with code 0");
                string progressText = File.ReadAllText(Path.Combine(folder, "progress.json"));
                TestRunner.Check(progressText.Contains("\"status\":\"stopped\""),
                    "Stopped run records status=stopped in progress.json");
                TestRunner.Check(File.Exists(Path.Combine(folder, "world.json")),
                    "Stopped run saves world.json checkpoint");
                string[] logBefore = File.ReadAllLines(Path.Combine(folder, "show.log.jsonl"));
                TestRunner.Check(logBefore.Length >= 2 && logBefore.Count(l => l.Contains("\"type\":\"complete\"")) == 0,
                    "Stopped run leaves valid snapshot lines without premature complete record");
                string[] seriesBefore = File.ReadAllLines(Path.Combine(folder, "series.jsonl"));
                TestRunner.Check(seriesBefore.Length >= 1 && seriesBefore[0].Contains("\"savedSnakeAlive\":"),
                    "Series JSONL records progress rows with savedSnakeAlive");

                File.Delete(stopFile);
                int resumeExit = Znake.Headless.Program.Run(new[]
                {
                    "--folder", folder,
                    "--resume",
                    "--seconds", "8",
                    "--pace", "0",
                    "--snapshot", "0.2",
                    "--progress", "0.1",
                    "--no-replay"
                });
                TestRunner.Check(resumeExit == 0, "Resumed run finishes cleanly");
                string[] logAfter = File.ReadAllLines(Path.Combine(folder, "show.log.jsonl"));
                TestRunner.Check(logAfter.Length > logBefore.Length &&
                    logAfter.Count(l => l.Contains("\"type\":\"start\"")) == 1 &&
                    logAfter.Count(l => l.Contains("\"type\":\"complete\"")) == 1,
                    "Resume appends to existing show.log.jsonl and writes one complete record");
                string[] seriesAfter = File.ReadAllLines(Path.Combine(folder, "series.jsonl"));
                TestRunner.Check(seriesAfter.Length > seriesBefore.Length,
                    "Resume appends additional rows to series.jsonl");
                string finalProgress = File.ReadAllText(Path.Combine(folder, "progress.json"));
                TestRunner.Check(finalProgress.Contains("\"savedSnakeAlive\":"),
                    "Final progress record reports savedSnakeAlive");
                string runScript = File.ReadAllText(Path.Combine(folder, "run.ps1"));
                TestRunner.Check(runScript.Contains("--square 16") && runScript.Contains("--spawn random"),
                    "Generated run.ps1 preserves square and spawn arguments");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void ResumeRejectsInvalidManualCheckpointEdit()
        {
            string root = Path.Combine(Path.GetTempPath(), "Znake-bad-resume-" + Guid.NewGuid().ToString("N"));
            string bg = Path.Combine(root, "bg.png");
            string folder = Path.Combine(root, "run");
            try
            {
                Directory.CreateDirectory(root);
                WriteSolidPng(bg, 128, 96, Color.FromArgb(54, 177, 80));
                int ok = Znake.Headless.Program.Run(new[]
                {
                    "--background", bg,
                    "--folder", folder,
                    "--square", "16",
                    "--cast", "3",
                    "--seconds", "1",
                    "--snapshot", "0.2",
                    "--progress", "0.1",
                    "--no-replay"
                });
                TestRunner.Check(ok == 0, "Initial short run succeeds");

                // Save a second time so world.previous.json exists, then corrupt world.json.
                string worldPath = Path.Combine(folder, "world.json");
                string backupPath = Path.Combine(folder, "world.previous.json");
                File.Copy(worldPath, backupPath, true);
                File.WriteAllText(worldPath, "{\"version\":1,\"savedUtcTicks\":1,\"world\":{\"Ticks\":-5}}");

                int resumeFail = Znake.Headless.Program.Run(new[]
                {
                    "--folder", folder,
                    "--resume",
                    "--no-replay"
                });
                TestRunner.Check(resumeFail != 0,
                    "Resume rejects manually corrupted world.json even when world.previous.json exists");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void ZeroResidentNoRespawnShowCheckpointsCleanly()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Znake-zero-res-" + Guid.NewGuid().ToString("N"));
            try
            {
                var world = new World(10, 6, 17, null, 12);
                world.StartShow(1, true, true);
                world.Snakes.Clear();
                world.ShowComplete = true;
                using (StateStore store = StateStore.Open(folder))
                {
                    store.Save(world, DateTime.UtcNow);
                    World loaded = store.Load(DateTime.UtcNow, false);
                    TestRunner.Check(loaded != null && loaded.Snakes.Count == 0 && loaded.ShowNoRespawn,
                        "No-respawn show checkpoints cleanly with zero surviving residents");
                }
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        private static void WriteSolidPng(string path, int width, int height, Color color)
        {
            using (var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(color);
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }
}
