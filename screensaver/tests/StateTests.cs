using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Znake.Tests
{
    internal static class StateTests
    {
        public static void Run()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Znake-state-test-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "world.json");
            try
            {
                var settings = new SaverSettings();
                settings.Debug = false;
                settings.Latitude = 47;
                settings.Longitude = 28.8;
                settings.Genes["Length13"] = 1;
                settings.Genes["Length5"] = 0;
                settings.Save(folder);
                SaverSettings readSettings = SaverSettings.Load(folder);
                TestRunner.Check(!readSettings.Debug && readSettings.Latitude == 47 &&
                    readSettings.BirthOverrides()[Gene.Length13] == 1, "Settings and overrides round-trip");

                using (var store = StateStore.Open(folder))
                {
                    TestRunner.Check(store != null, "One writer can open the profile");
                    TestRunner.Check(StateStore.Open(folder) == null, "The running profile rejects a second writer");
                    var original = new World(13, 9, 662, null);
                    Snake snake = original.Snakes.Single();
                    snake.SprintRemaining = 0.7;
                    snake.Learned[(int)Gene.Sprint] = 0.025;
                    snake.Stress = snake.Capacity(0);
                    snake.SaturatedFor = snake.TailDwell(0) - 0.3;
                    snake.Saving = true;
                    snake.Energy = 1.3;

                    var vine = new Vine(original.NewId(), snake.Genes.Copy(), Rgb.StartingGreen);
                    vine.Squares.Add(new VineSquare(new Cell(1, 2), Rgb.StartingGreen, 2));
                    original.Vines.Add(vine);
                    original.Fruits.Add(new Fruit(original.NewId(), vine, new Cell(1, 3), 200));
                    var visitor = original.AddVisitor(new SaverRandom(7));
                    original.Imprints.Add(new Cell(8, 4), new Tint(snake.Skin, 0.019));

                    var clock = new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);
                    store.Save(original, clock);
                    World resumed = store.Load(clock);
                    TestRunner.Check(resumed.Snakes.Any(item => item.Id == visitor.Id) &&
                        resumed.Fruits.Single().VineId == resumed.Vines.Single().Id, "Resume keeps visible visitors and fruit ownership");
                    TestRunner.Near(resumed.Snakes[0].SprintRemaining, 0.7, 1e-9, "Resume retains sprint time");
                    TestRunner.Near(resumed.Snakes[0].Learned[(int)Gene.Sprint], 0.025, 1e-9, "Resume retains learning");
                    TestRunner.Near(resumed.Snakes[0].SaturatedFor, snake.SaturatedFor, 1e-9,
                        "Resume retains dwell near tail loss");
                    TestRunner.Check(resumed.Snakes[0].Saving, "Resume retains the spending choice");

                    var raw = ColorGrid.Uniform(13, 9, Rgb.FromBytes(225, 22, 170));
                    original.Step(raw, 0);
                    resumed.Step(raw, 0);
                    TestRunner.Check(resumed.RandomState == original.RandomState &&
                        resumed.Snakes[0].Body.SequenceEqual(original.Snakes[0].Body) &&
                        resumed.Snakes[0].Stress == original.Snakes[0].Stress &&
                        resumed.Snakes[0].Energy == original.Snakes[0].Energy, "The next active update replays after resume");

                    store.Save(resumed, clock);
                    File.WriteAllText(path, "{\"version\":");
                    World fallback = store.Load(clock);
                    TestRunner.Check(fallback.Ticks == 0 && fallback.Fruits.Count == 1,
                        "A damaged latest checkpoint falls back to the previous complete transaction");

                    fallback.Step(raw, 0);
                    store.Save(fallback, clock);
                    File.WriteAllText(path, "{\"version\":");
                    TestRunner.Check(store.Load(clock).Ticks == 0, "A recovery save preserves the valid backup");

                    World faded = store.Load(clock.AddSeconds(106));
                    TestRunner.Near(faded.Imprints[new Cell(8, 4)].Alpha,
                        0.0095, 1e-9, "Wall time fades imprints while the world is otherwise paused");
                    TestRunner.Check(faded.Ticks == 0 && faded.Fruits[0].Remaining == 200,
                        "While the saver is closed, active timers pause");
                }

                FruitCheckpoint(Path.Combine(folder, "fruit"));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        private static void FruitCheckpoint(string folder)
        {
            var world = new World(13, 9, 77, null);
            var stem = new Vine(world.NewId(), new Genome(), Rgb.StartingGreen) { NextFruitIn = 0 };
            stem.Squares.Add(new VineSquare(new Cell(1, 1), Rgb.StartingGreen, 3)
            {
                Nutrients = 1 + 3 * (0.005 * World.StepSeconds)
            });
            world.Vines.Add(stem);
            world.Step(ColorGrid.Uniform(13, 9, Rgb.StartingGreen), 0);

            using (var store = StateStore.Open(folder))
            {
                DateTime now = DateTime.UtcNow;
                store.Save(world, now);
                World resumed = store.Load(now);
                TestRunner.Check(resumed.Vines.Single().Squares.Count == 0 &&
                    resumed.Fruits.Single().VineId == stem.Id,
                    "Fruit can spend the last nutrient unit and resume with its source vine");
            }
        }
    }
}
