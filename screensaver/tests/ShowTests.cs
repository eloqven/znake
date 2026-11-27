using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace Znake.Tests
{
    internal static class ShowTests
    {
        public static void Run()
        {
            OpeningCastUsesDistinctOffscreenEntries();
            ShowCountsAcceptedMovesOnly();
            ShowCountsMovesOfSnakesRemovedInTheSameUpdate();
            ResumeDoesNotRepeatTheOpeningCast();
            ShowCompletesOnlyAfterCrossingTheTarget();
            ShowRateBlendsSamples();
            ShowClockIgnoresStalledFrames();
            ShowAndArenaCheckpointsStayIndependent();
            ShowLogWritesStartSnapshotsAndCompletion();
            ShowLogKeepsOneCompleteRecordOnResume();
        }

        private static void OpeningCastUsesDistinctOffscreenEntries()
        {
            var world = new World(40, 20, 314, null, 24);
            world.StartShow(World.OpeningCast);
            TestRunner.Check(world.ShowActive && !world.ShowComplete && world.ShowMoves == 0 &&
                world.ShowCastSize == World.OpeningCast && world.ShowSeed != 0,
                "The show opens with its own cast, counter and seed");
            TestRunner.Check(world.Snakes.Count == World.OpeningCast &&
                world.Snakes.Count(snake => snake.IsResident) == 1,
                "The opening cast is one resident plus fifty-four arrivals");

            var slots = new HashSet<int>();
            bool distinct = true, outside = true, boundary = true;
            foreach (Snake snake in world.Snakes)
            {
                distinct &= slots.Add(world.EntrySlotOf(snake));
                outside &= !world.Contains(snake.Body[0]);
                if (!snake.IsResident) boundary &= snake.StaysInArena;
            }
            TestRunner.Check(distinct && slots.Count == World.OpeningCast && outside,
                "Every opening snake enters from its own offscreen boundary position");
            TestRunner.Check(boundary, "Opening arrivals use the tray-birth boundary rule");
        }

        private static void ShowCountsAcceptedMovesOnly()
        {
            var world = new World(30, 16, 88, null, 24);
            world.StartShow(6);
            ColorGrid grid = ColorGrid.Uniform(30, 16, Rgb.StartingGreen);
            foreach (Snake snake in world.Snakes)
            {
                snake.Motion = 0.99;
                snake.Speed = 1;
            }
            long before = world.ShowMoves;
            world.Step(grid, 0);
            long expected = world.Snakes.Sum(snake => snake.Moves);
            TestRunner.Check(before == 0 && world.ShowMoves == expected && expected > 0,
                "One world update adds exactly one count per accepted move, including arrivals");

            var blocked = new World(10, 6, 33, null, 24);
            blocked.StartShow(1);
            Snake stuck = blocked.Snakes[0];
            stuck.Entering = false;
            stuck.Body = new List<Cell> { new Cell(9, 3) };
            stuck.RecentPath = new List<Cell> { new Cell(9, 3) };
            stuck.Heading = 0;
            stuck.Motion = 0.99;
            stuck.Speed = 1;
            blocked.Manual = new ManualSteering { Heading = 0 };
            blocked.Step(ColorGrid.Uniform(10, 6, stuck.Skin), 0);
            TestRunner.Check(blocked.ShowMoves == 0, "Blocked attempts add nothing to the show counter");
        }

        private static void ShowCountsMovesOfSnakesRemovedInTheSameUpdate()
        {
            var world = new World(12, 8, 17, null, 24);
            world.StartShow(1);
            Snake snake = world.Snakes[0];
            snake.Entering = false;
            snake.Body = new List<Cell> { new Cell(5, 4) };
            snake.RecentPath = new List<Cell> { new Cell(5, 4) };
            snake.Heading = 0;
            snake.Motion = 0.99;
            snake.Speed = 1;
            snake.Stress = snake.Capacity(0);
            snake.SaturatedFor = snake.TailDwell(0);
            world.Step(ColorGrid.Uniform(12, 8, Rgb.FromBytes(255, 0, 255)), 0);
            TestRunner.Check(!world.Snakes.Contains(snake), "The single snake dies during the update");
            TestRunner.Check(world.ShowMoves == 1, "Its accepted move still counts even though it dies in that update");
        }

        private static void ResumeDoesNotRepeatTheOpeningCast()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Znake-show-resume-" + Guid.NewGuid().ToString("N"));
            try
            {
                var world = new World(40, 20, 314, null, 24);
                world.StartShow(World.OpeningCast);
                var ids = world.Snakes.Select(snake => snake.Id).ToList();
                var opening = new HashSet<long>(ids);
                using (StateStore store = StateStore.Open(folder))
                {
                    store.Save(world, DateTime.UtcNow);
                    World resumed = store.Load(DateTime.UtcNow);
                    TestRunner.Check(resumed.ShowActive && resumed.ShowCastSize == World.OpeningCast &&
                        resumed.Snakes.Select(snake => snake.Id).SequenceEqual(ids),
                        "The show checkpoint restores the same opening cast");
                    foreach (Snake snake in resumed.Snakes) snake.Genes[Gene.VisitorArrival] = 0;
                    resumed.Step(ColorGrid.Uniform(40, 20, Rgb.StartingGreen), 0);
                    TestRunner.Check(resumed.Snakes.Where(snake => !snake.IsResident)
                        .All(snake => opening.Contains(snake.Id)),
                        "Resuming never spawns a second opening cast");
                }
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        private static void ShowCompletesOnlyAfterCrossingTheTarget()
        {
            var world = new World(12, 8, 29, null, 24);
            world.StartShow(1);
            Snake snake = world.Snakes[0];
            snake.Entering = false;
            snake.Body = new List<Cell> { new Cell(4, 4) };
            snake.RecentPath = new List<Cell> { new Cell(4, 4) };
            snake.Heading = 0;
            snake.Motion = 0.99;
            snake.Speed = 1;
            world.ShowMoves = World.ShowTarget - 1;
            TestRunner.Check(!world.ShowComplete, "One move short of the target is not complete");
            world.Step(ColorGrid.Uniform(12, 8, Rgb.StartingGreen), 0);
            TestRunner.Check(world.ShowMoves == World.ShowTarget && world.ShowComplete,
                "The update that crosses the target finishes and marks the show complete");
        }

        private static void ShowRateBlendsSamples()
        {
            TestRunner.Near(ScreenOverlay.BlendRate(0, 120), 120, 0, "The first sample sets the measured rate");
            TestRunner.Near(ScreenOverlay.BlendRate(100, 200), 130, 1e-9, "Later samples move the rate by thirty percent");
        }

        private static void ShowClockIgnoresStalledFrames()
        {
            TestRunner.Near(ScreenOverlay.VisibleElapsed(0.08), 0.08, 1e-9, "A normal frame advances the show clock");
            TestRunner.Near(ScreenOverlay.VisibleElapsed(3600), 0.32, 1e-9,
                "A stalled or sleeping frame cannot inflate the show clock");
            TestRunner.Near(ScreenOverlay.VisibleElapsed(-1), 0, 1e-9, "The show clock never runs backwards");
        }

        private static void ShowAndArenaCheckpointsStayIndependent()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Znake-show-stores-" + Guid.NewGuid().ToString("N"));
            try
            {
                string arenaRoot = Path.Combine(folder, "arena");
                string showRoot = Path.Combine(folder, "Show");
                using (StateStore arenaStore = StateStore.Open(arenaRoot))
                using (StateStore showStore = StateStore.Open(showRoot))
                {
                    var arena = new World(10, 6, 3, null, 24);
                    var show = new World(10, 6, 4, null, 24);
                    show.StartShow(2);
                    arenaStore.Save(arena, DateTime.UtcNow);
                    showStore.Save(show, DateTime.UtcNow);
                    World keptArena = arenaStore.Load(DateTime.UtcNow);
                    World keptShow = showStore.Load(DateTime.UtcNow);
                    TestRunner.Check(!keptArena.ShowActive && keptArena.Snakes.Count == 1,
                        "The regular arena keeps its own checkpoint and population");
                    TestRunner.Check(keptShow.ShowActive && keptShow.Snakes.Count == 2,
                        "The show keeps its checkpoint beside the regular arena");
                }
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        private static void ShowLogWritesStartSnapshotsAndCompletion()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Znake-show-log-" + Guid.NewGuid().ToString("N"));
            try
            {
                ShowLog.Clear(folder);
                var world = new World(20, 12, 71, null, 24);
                world.StartShow(4);
                using (ShowLog log = ShowLog.Open(folder))
                {
                    TestRunner.Check(log.StartedNew, "A fresh show starts a new log file");
                    log.Start(world, new SaverSettings(), new Rectangle(0, 0, 1920, 1080), null,
                        new Point(3, 4));
                    log.Record(world, 0, true);
                    world.ShowSeconds = 61;
                    log.Record(world, 12.5);
                    world.ShowMoves = World.ShowTarget;
                    world.ShowComplete = true;
                    log.Record(world, 9.25);
                }

                string[] lines = File.ReadAllLines(LogPath(folder));
                TestRunner.Check(lines.Length == 4 && lines[0].Contains("\"type\":\"start\"") &&
                    lines[0].Contains("\"gridOffsetX\":3") && lines[0].Contains("\"gridOffsetY\":4") &&
                    lines[1].Contains("\"type\":\"snapshot\"") && lines[1].Contains("\"visibleSeconds\":0") &&
                    lines[2].Contains("\"type\":\"snapshot\"") && lines[2].Contains("\"visibleSeconds\":61") &&
                    lines[3].Contains("\"type\":\"complete\"") &&
                    lines[3].Contains("\"population\":4") && lines[3].Contains("\"moves\":" + World.ShowTarget),
                    "The log records the start, the opening scene, one snapshot per minute and the completion metrics");
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        private static void ShowLogKeepsOneCompleteRecordOnResume()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Znake-show-complete-" + Guid.NewGuid().ToString("N"));
            try
            {
                var world = new World(20, 12, 71, null, 24);
                world.StartShow(4);
                using (ShowLog log = ShowLog.Open(folder))
                {
                    log.Start(world, new SaverSettings(), new Rectangle(0, 0, 1920, 1080), null, Point.Empty);
                    world.ShowMoves = World.ShowTarget;
                    world.ShowComplete = true;
                    log.Record(world, 9.25);
                }

                using (ShowLog resumed = ShowLog.Open(folder))
                {
                    resumed.Record(world, 0);
                }

                string[] lines = File.ReadAllLines(LogPath(folder));
                TestRunner.Check(lines.Count(line => line.Contains("\"type\":\"complete\"")) == 1,
                    "Resuming a completed show keeps exactly one complete record");
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        private static string LogPath(string folder)
        {
            return Path.Combine(folder, "show.log.jsonl");
        }
    }
}
