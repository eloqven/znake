using System;
using System.Collections.Generic;
using System.Drawing;

namespace Znake.Tests
{
    internal static class SnakeTests
    {
        public static void Run()
        {
            var random = new SaverRandom(338);
            Rgb green = Rgb.StartingGreen;
            Rgb purple = Rgb.FromBytes(225, 22, 170);

            var genome = new Genome();
            genome[Gene.ShedShift] = 1;
            var snake = Create(genome);
            snake.Energy = 1.5;
            snake.Spend(green, 0, 0.1, random);
            TestRunner.Near(snake.Energy, 0.5, 1e-9, "Growth buys exactly one segment");
            TestRunner.Check(snake.PendingGrowth == 1, "Growth stays queued until the tail can advance");
            snake.Motion = 0.95;
            snake.Speed = 0.60;
            snake.Advance(new ColorField(ColorGrid.Uniform(9, 7, green), null, null), 0, 0.2, random, null, null);
            TestRunner.Check(snake.Body.Count == 4 && snake.PendingGrowth == 0, "Crossing realizes growth once");

            snake.Saving = true;
            snake.Energy = 4;
            snake.StressColor = purple;
            snake.HasStressColor = true;
            snake.Spend(green, 0, 0.1, random);
            TestRunner.Near(snake.Energy, 4, 1e-9, "A shed that worsens the local match keeps its energy");
            snake.Spend(purple, 0, 0.1, random);
            TestRunner.Near(snake.Energy, 0, 1e-9, "A qualifying shed pays the full current length");
            TestRunner.Near(Rgb.Distance(snake.Skin, purple), 0, 1e-8, "A shed approaches the remembered color");
            TestRunner.Check(Snake.SaveChance(0.8, 0.3) > Snake.SaveChance(0.1, 0.3),
                "Stress increases the chance of saving");

            var nightGenes = new Genome();
            nightGenes[Gene.NightBoost] = 0.8;
            nightGenes[Gene.Sprint] = 0.4;
            nightGenes[Gene.SprintDuration] = 0.6;
            nightGenes[Gene.SprintCost] = 0.6;
            nightGenes[Gene.SprintFrequency] = 0.6;
            var runner = Create(nightGenes);
            TestRunner.Check(runner.SprintDuration(1) > runner.SprintDuration(0) &&
                runner.SprintCost(1) > runner.SprintCost(0) && runner.SprintRate(1) > runner.SprintRate(0),
                "Deep night amplifies the sprint parent and its three subgenes");
            runner.SprintRemaining = 0.3;
            runner.Advance(new ColorField(ColorGrid.Uniform(9, 7, purple), null, null), 0, 0.15, random, null, null);
            TestRunner.Check(runner.Stress > 0 && runner.SprintRemaining < 0.3 && runner.Speed > 0,
                "Sprinting spends calm over active time and accelerates motion");
            runner.Stress = runner.Capacity(0);
            double dwell = runner.TailDwell(0);
            runner.SaturatedFor = dwell - 0.2;
            runner.Advance(new ColorField(ColorGrid.Uniform(9, 7, purple), null, null), 0, 0.1, random, null, null);
            TestRunner.Check(runner.Body.Count == 3 && runner.SaturatedFor > 0, "Short peak dwell keeps the fading square");
            runner.Advance(new ColorField(ColorGrid.Uniform(9, 7, purple), null, null), 0, 0.14, random, null, null);
            TestRunner.Check(runner.Body.Count == 2 && runner.Stress < runner.Capacity(0),
                "A sustained peak costs one square and relieves stress");

            TestRunner.Near(Snake.LearningDelta(true, 0.02, 0.25), 0.025, 1e-10, "Calm adds the offset");
            TestRunner.Near(Snake.LearningDelta(false, 0.02, 0.25), -0.015, 1e-10, "Stress adds the same offset");
            TestRunner.Near(Snake.LearningDelta(true, 0.02, -0.25), 0.015, 1e-10, "Negative offset reduces calm");
            TestRunner.Near(Snake.LearningDelta(false, 0.02, -0.25), -0.025, 1e-10, "Negative offset increases stress");
            TestRunner.Near(Snake.LearningDelta(false, 0.02, 0), -0.02, 1e-10, "Zero offset is balanced");

            var learner = Create(new Genome());
            learner.Genes[Gene.Learning] = 0.25;
            learner.Genes[Gene.LearningOffset] = 0.25;
            learner.RecordAffect(0.85, null, Gene.FlickerAttraction, 0);
            double penalty = learner.Learned[(int)Gene.FlickerAttraction];
            learner.RecordAffect(0.9, null, Gene.FlickerAttraction, 0);
            TestRunner.Near(penalty, -0.015, 1e-10, "Credited stress reduces future expression");
            TestRunner.Near(learner.Learned[(int)Gene.FlickerAttraction], penalty, 1e-10,
                "Remaining stressed applies one adjustment");
            learner.RecordAffect(0.1, Gene.FlickerAttraction, null, 0);
            TestRunner.Near(learner.Learned[(int)Gene.FlickerAttraction], 0.01, 1e-10,
                "Calm can reinforce the same credited gene on a new transition");

            LearningFollowsExposure();
            LearningCreditsTheBeneficialCrossing();
            RelativeSteering(true, 0, new Cell(1, 2), new Cell(2, 1));
            RelativeSteering(false, 3, new Cell(2, 3), new Cell(3, 2));
            BodyCrossings();
            CrossingOpacity();
            StressStripe();
            SandboxBorder();
        }

        private static void RelativeSteering(bool resident, int heading, Cell behind, Cell turn)
        {
            var skin = new Rgb(1, 1, 1);
            var grid = ColorGrid.Uniform(5, 5, new Rgb(0, 0, 0));
            grid[behind] = skin;
            grid[turn] = Rgb.FromBytes(128, 128, 128);
            var snake = new Snake(1, resident, new Genome(), skin, new List<Cell> { new Cell(2, 2) }, heading);
            snake.Motion = 0.99;
            snake.Speed = 1;

            snake.Advance(new ColorField(grid, null, null), 0, World.StepSeconds,
                new SaverRandom(338), null, null);
            TestRunner.Check(snake.Body[0].Equals(turn),
                (resident ? "A resident" : "A visitor") + " turns when the strongest color match is directly behind");
        }

        private static void BodyCrossings()
        {
            Rgb green = Rgb.StartingGreen;
            var world = new World(3, 3, 17, null);
            Cell center = new Cell(1, 1);
            Snake resident = new Snake(world.Snakes[0].Id, true, new Genome(), green,
                new List<Cell> { center, new Cell(2, 1), new Cell(1, 2), new Cell(0, 1),
                    new Cell(1, 0), new Cell(0, 0) }, 0);
            resident.Motion = 0.99;
            resident.Speed = 1;
            world.Snakes[0] = resident;
            world.Step(ColorGrid.Uniform(3, 3, green), 0);
            TestRunner.Check(!resident.Body[0].Equals(center) && resident.Body.IndexOf(resident.Body[0], 1) >= 1,
                "A surrounded resident crosses one of its own body cells");

            var narrow = new World(1, 2, 31, null);
            Cell entry = new Cell(0, 0), target = new Cell(0, 1);
            resident = new Snake(narrow.Snakes[0].Id, true, new Genome(), green, new List<Cell> { entry }, 1);
            resident.Motion = 0.99;
            resident.Speed = 1;
            var visitor = new Snake(narrow.NewId(), false, new Genome(), green, new List<Cell> { target }, 0);
            narrow.Snakes[0] = resident;
            narrow.Snakes.Add(visitor);
            narrow.Step(ColorGrid.Uniform(1, 2, green), 0);
            TestRunner.Check(resident.Body[0].Equals(target) && visitor.Body[0].Equals(target),
                "A resident crosses a cell occupied by another snake");
        }

        private static void CrossingOpacity()
        {
            var world = new World(3, 1, 31, null);
            var self = new Cell(0, 0);
            var shared = new Cell(1, 0);
            var single = new Cell(2, 0);
            world.Snakes[0] = new Snake(world.Snakes[0].Id, true, new Genome(), Rgb.StartingGreen,
                new List<Cell> { self, shared, self, single }, 0);
            world.Snakes.Add(new Snake(world.NewId(), false, new Genome(), Rgb.FromBytes(255, 0, 0),
                new List<Cell> { shared }, 0));

            using (var image = new Bitmap(72, 24))
            {
                using (Graphics graphics = Graphics.FromImage(image))
                    ScreenOverlay.DrawBodies(graphics, world, 0);
                Color selfPixel = image.GetPixel(12, 12);
                Color sharedPixel = image.GetPixel(36, 12);
                TestRunner.Check(selfPixel.A > 0 && selfPixel.A < 255,
                    "Self-crossings remain translucent");
                TestRunner.Check(sharedPixel.A > 0 && sharedPixel.A < 255 && sharedPixel.R > 60 && sharedPixel.G > 40,
                    "Different snakes remain visible where their bodies overlap");
                TestRunner.Check(image.GetPixel(60, 12).A == 255,
                    "A single segment stays fully visible");
            }
        }

        private static void StressStripe()
        {
            var world = new World(1, 1, 31, null);
            Snake snake = world.Snakes[0];
            snake.Heading = 0;
            using (var image = new Bitmap(24, 24))
            using (Graphics graphics = Graphics.FromImage(image))
            {
                ScreenOverlay.DrawBodies(graphics, world, 0);
                TestRunner.Check(image.GetPixel(23, 12).ToArgb() == Color.White.ToArgb() &&
                    image.GetPixel(22, 12).ToArgb() == snake.Skin.ToColor(1).ToArgb(),
                    "A calm heading marker is a single white pixel on the front edge");
                snake.Stress = snake.Capacity(0) / 2;
                ScreenOverlay.DrawBodies(graphics, world, 0);
                Color middle = image.GetPixel(23, 12);
                TestRunner.Check(middle.R == 128 && middle.G == 128 && middle.B == 128,
                    "The head edge darkens with increasing stress");
                snake.Heading = 1;
                snake.Stress = snake.Capacity(0);
                ScreenOverlay.DrawBodies(graphics, world, 0);
                TestRunner.Check(image.GetPixel(12, 23).ToArgb() == Color.Black.ToArgb() &&
                    image.GetPixel(23, 12).ToArgb() == snake.Skin.ToColor(1).ToArgb(),
                    "A fully stressed heading marker follows the head turn");
            }
        }

        private static void SandboxBorder()
        {
            using (var surface = new DibSurface(new Size(3, 3)))
            {
                var underlying = new[] { 0x123456, 0x19AF80, 0, 0, 0, 0, 0, 0, 0xFFFFFF };
                surface.DrawInvertedBorder(underlying);
                TestRunner.Check(surface.RgbAt(new Point(0, 0)) == 0xEDCBA9 &&
                    surface.RgbAt(new Point(1, 0)) == 0xE6507F &&
                    surface.RgbAt(new Point(2, 2)) == 0 && surface.AlphaAt(new Point(2, 2)) == 255,
                    "Sandbox border inverts each underlying pixel");
                TestRunner.Check(surface.AlphaAt(new Point(1, 1)) == 0, "Sandbox border is one pixel wide");
            }
        }

        private static void LearningCreditsTheBeneficialCrossing()
        {
            Rgb green = Rgb.StartingGreen;
            Rgb hostile = Rgb.FromBytes(225, 22, 170);
            var start = new Cell(2, 3);
            var first = new Cell(3, 3);
            var second = new Cell(3, 2);
            var steady = new Cell(4, 3);
            var genome = new Genome();
            genome[Gene.Learning] = 0.25;
            genome[Gene.FlickerAttraction] = 1;
            var snake = new Snake(1, true, genome, green, new List<Cell> { start }, 0);
            snake.Mood = Affect.Neutral;
            snake.Stress = 0.221 * snake.Capacity(0);
            snake.Motion = 0.99;
            snake.Speed = 15;
            var current = ColorGrid.Uniform(9, 7, hostile);
            var previous = ColorGrid.Uniform(9, 7, hostile);
            current[first] = previous[first] = green;
            current[steady] = previous[steady] = green;
            current[second] = Rgb.Mix(green, hostile, 0.2);
            previous[second] = new Rgb(0, 0, 0);

            snake.Advance(new ColorField(current, previous, null), 0, World.StepSeconds,
                new SaverRandom(338), null, null);
            TestRunner.Check(snake.Body[0].Equals(second) && snake.Mood == Affect.Calm &&
                snake.LastMovingGene == Gene.FlickerAttraction, "A later flicker crossing follows the feeding gain");
            TestRunner.Near(snake.Learned[(int)Gene.Speed], 0.02, 1e-10,
                "The crossing that improved feeding gets credit when a later crossing reduced it");
            TestRunner.Near(snake.Learned[(int)Gene.FlickerAttraction], 0, 1e-10,
                "A later crossing that reduced feeding does not earn calm credit");
        }

        private static void LearningFollowsExposure()
        {
            Rgb green = Rgb.StartingGreen;
            Rgb hostile = Rgb.FromBytes(225, 22, 170);
            var genome = new Genome();
            genome[Gene.Learning] = 0.25;
            var prior = ColorGrid.Uniform(9, 7, hostile);
            var matching = ColorGrid.Uniform(9, 7, green);
            var ambient = Create(genome);
            ambient.Mood = Affect.Neutral;
            ambient.Stress = 0.23 * ambient.Capacity(0);
            ambient.LastMovingGene = Gene.FlickerAttraction;
            Cell start = ambient.Body[0];

            ambient.Advance(new ColorField(matching, prior, null), 0, World.StepSeconds,
                new SaverRandom(338), null, null);
            TestRunner.Check(ambient.Body[0].Equals(start) && ambient.Mood == Affect.Calm,
                "Matching light calms a stationary snake");
            TestRunner.Near(ambient.Learned[(int)Gene.FlickerAttraction], 0, 1e-10,
                "Ambient calm does not reinforce stale movement");

            var traveler = new Snake(1, true, genome, green,
                new List<Cell> { new Cell(0, 0), new Cell(0, 1), new Cell(0, 2) }, 1);
            var corridor = ColorGrid.Uniform(1, 3, green);
            traveler.Mood = Affect.Neutral;
            traveler.Stress = 0.23 * traveler.Capacity(0);
            traveler.Motion = 0.99;
            traveler.Speed = 1;
            Cell destination = traveler.Body[0].Move(Cell.Directions[1]);
            corridor[traveler.Body[traveler.Body.Count - 1]] = hostile;
            traveler.Advance(new ColorField(corridor, null, null), 0, World.StepSeconds,
                new SaverRandom(338), null, null);
            TestRunner.Check(traveler.Body[0].Equals(destination) && traveler.Mood == Affect.Calm,
                "Crossing out of unsuitable light calms the snake");
            TestRunner.Near(traveler.Learned[(int)Gene.Speed], 0.02, 1e-10,
                "A crossing that improves feeding earns calm credit");

            var saturated = Create(genome);
            saturated.Stress = saturated.Capacity(0);
            var pressure = new ColorField(prior, null, null);
            var random = new SaverRandom(338);
            for (int tick = 0; tick < 24 && saturated.Body.Count == 3; tick++)
                saturated.Advance(pressure, 0, World.StepSeconds, random, null, null);
            TestRunner.Check(saturated.Body.Count == 2 && saturated.Mood == Affect.Stressed,
                "Tail loss reduces stress without always leaving the stressed mood");
            TestRunner.Near(saturated.Learned[(int)Gene.TailRelief], 0, 1e-10,
                "Tail relief does not earn a stress penalty");
        }

        internal static Snake Create(Genome genes)
        {
            return new Snake(1, true, genes, Rgb.StartingGreen,
                new List<Cell> { new Cell(4, 3), new Cell(3, 3), new Cell(2, 3) }, 0);
        }
    }
}
