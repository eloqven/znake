using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace Znake.Tests
{
    internal static class WidgetTests
    {
        public static void Run()
        {
            ResizeKeepsOccupiedCellsOnTheSamePixels();
            FeedingOnlyUsesLiveSkinWhenEnabledAndOverlapping();
            ManualSteeringUsesOneMoveBypassAndAdditiveCost();
            JumpCostUsesTheRiseBetweenStacks();
            RadialVisionFindsFoodAndSteersTowardIt();
            SquaresRecoverAfterGeometricCooldowns();
            StationaryColorUsesOneDosePerPulse();
            MissedMovesRaiseStressAndResetOnMovement();
            ParkedColorScalesMissedMoveStress();
            StressJerkRespondsToSpikesAndChronicStress();
            AgentsChooseEveryMove();
            BerryVisionStacksAndExpiresPerMove();
            AbruptCalmDropsAMirroredAntifruit();
            TimeMultiplierChangesSimulationElapsed();
            PreferredLengthReducesEnergyDrainAndStress();
            FruitInheritanceUsesTheReceivingGeneAndPhase();
            BirthColorRemainsTheStrongestMatch();
            MutatedVisionIgnoresInsensitiveChannels();
            SavedSpeciesRestoresTheGenomeAtBirth();
            OriginalSkinKeepsItsShapeAndOpacity();
            RealClockDrivesTheWidgetDayAndNight();
            EggWaitsForItsMoonAndParent();
            TailBiteSplitsOnlyTheEncounteredTail();
            TrayBirthsStayAndManualHealthFollowsSelection();
            FineGeneEditsUsePercentagePoints();
            GeneEditsSaveImmediately();
            DeathMakesTwoSquaresAndTheLastSquareIsInert();
        }

        private static void ResizeKeepsOccupiedCellsOnTheSamePixels()
        {
            var screen = new Rectangle(0, 0, 1920, 1080);
            var work = new Rectangle(0, 0, 1920, 1050);
            WidgetViewport corner = WidgetViewport.Create(screen, work, 14, 7, 24, false);
            WidgetViewport wider = WidgetViewport.Create(screen, work, 15, 8, 24, false);
            WidgetViewport full = WidgetViewport.Create(screen, work, 15, 8, 24, true);
            TestRunner.Check(corner.Bounds == new Rectangle(1568, 866, 336, 168) &&
                wider.Bounds == new Rectangle(1544, 842, 360, 192), "The sandbox grows up and left");
            TestRunner.Check(full.Bounds == screen && full.FullScreen, "Maximized habitat covers the full monitor");

            World world = new World(corner.Columns, corner.Rows, 4, null);
            Snake snake = world.Snakes[0];
            Cell initialHead = snake.Body[0];
            Point initialPixel = DesktopCell(corner, initialHead);
            TestRunner.Check(world.TryReframe(wider.Columns, wider.Rows, corner.ShiftTo(wider)) &&
                DesktopCell(wider, snake.Body[0]) == initialPixel, "Expansion preserves the snake's desktop position");
            TestRunner.Check(world.TryReframe(full.Columns, full.Rows, wider.ShiftTo(full)) &&
                DesktopCell(full, snake.Body[0]) == initialPixel, "Borderless mode preserves the same grid lattice");
            snake.Body.Add(new Cell(0, 0));
            TestRunner.Check(!world.TryReframe(corner.Columns, corner.Rows, full.ShiftTo(corner)) &&
                world.Width == full.Columns, "A shrink cannot crop an occupied square");

            WidgetViewport moved = corner.Drag(new Point(-48, -72), work);
            TestRunner.Check(corner.ShiftTo(moved).Equals(new Cell(0, 0)) &&
                DesktopCell(moved, initialHead) == new Point(initialPixel.X - 48, initialPixel.Y - 72),
                "Dragging the habitat moves the grid and snake together");
        }

        private static Point DesktopCell(WidgetViewport view, Cell cell)
        {
            return new Point(view.Bounds.X + view.GridOffset.X + cell.X * view.Square,
                view.Bounds.Y + view.GridOffset.Y + cell.Y * view.Square);
        }

        private static void FeedingOnlyUsesLiveSkinWhenEnabledAndOverlapping()
        {
            var color = Rgb.FromBytes(68, 158, 93);
            ColorGrid grid = ColorGrid.Uniform(6, 6, color);
            var field = new ColorField(grid, null, new Dictionary<Cell, Tint>());
            var head = new Cell(2, 2);
            var body = new List<Cell> { head, new Cell(1, 2), head };
            Genome genes = Genome.Roll(new SaverRandom(8), null);
            TestRunner.Check(genes[Gene.SelfFeeding] == 0, "Self feeding starts at zero");
            var baseline = new Snake(1, true, genes, Rgb.StartingGreen, body, 0);
            baseline.Advance(field, 0, World.StepSeconds, new SaverRandom(10), null, null);
            var enabled = new Snake(2, true, genes.Copy(), Rgb.StartingGreen, new List<Cell>(body), 0);
            enabled.Genes[Gene.SelfFeeding] = 1;
            enabled.Advance(field, 0, World.StepSeconds, new SaverRandom(10), null, null);
            TestRunner.Check(enabled.Energy > baseline.Energy, "Enabling self feeding can increase energy on overlap");
            var separate = new Snake(3, true, enabled.Genes, Rgb.StartingGreen,
                new List<Cell> { head, new Cell(1, 2), new Cell(0, 2) }, 0);
            separate.Advance(field, 0, World.StepSeconds, new SaverRandom(10), null, null);
            TestRunner.Near(separate.Energy, baseline.Energy, 1e-9, "Without overlap the gene supplies no feeding");
        }

        private static void ManualSteeringUsesOneMoveBypassAndAdditiveCost()
        {
            ColorGrid grid = ColorGrid.Uniform(8, 8, Rgb.FromBytes(0, 0, 0));
            var world = new World(8, 8, 78, null) { WidgetSquare = 24 };
            Snake snake = world.Snakes[0];
            var head = new Cell(3, 2);
            var target = new Cell(3, 3);
            snake.Body = new List<Cell> { head, new Cell(2, 2), new Cell(2, 3), target };
            snake.RecentPath = new List<Cell>(snake.Body);
            snake.Heading = 0;
            snake.Genes[Gene.NightBoost] = 0;
            snake.Genes[Gene.ManeuverCost] = 0.05;
            snake.Genes[Gene.LengthComfort] = 0;
            snake.Genes[Gene.Imprint] = 0;
            snake.Energy = 0.8;
            snake.Motion = 0.99;
            world.Manual = new ManualSteering { Heading = 1 };
            world.Step(grid, 0);
            TestRunner.Check(snake.Body[0].Equals(head), "Click-to-pause blocks autonomous steps");
            world.Manual.Paused = false;
            world.Step(grid, 0);
            TestRunner.Check(snake.Body[0].Equals(head), "A normal manual turn is blocked by a body square");
            double before = snake.Energy;
            snake.Motion = 0.99;
            world.Manual.Boost = true;
            world.Step(grid, 0);
            TestRunner.Check(snake.Body[0].Equals(target) && !world.Manual.Boost,
                "Space permits the occupied move and clears the one-move bypass");
            TestRunner.Near(snake.Energy, before - 0.15, 1e-9, "Turning jump pays for the turn and the bypass");
        }

        private static void BirthColorRemainsTheStrongestMatch()
        {
            Rgb home = Rgb.StartingGreen;
            Rgb nearby = Rgb.FromBytes(57, 177, 80);
            Rgb drifted = Rgb.FromBytes(151, 33, 157);
            Snake exact = SampleColor(home, drifted, home, ColorChannels.All, 0.45);
            Snake ordinary = SampleColor(home, drifted, drifted, ColorChannels.All, 0.45);
            TestRunner.Check(exact.Energy > ordinary.Energy && exact.Stress < ordinary.Stress,
                "The exact birth color still gives the strongest feeding and calm after skin drifts");
            Snake soft = SampleColor(home, drifted, nearby, ColorChannels.All, 0.2);
            Snake sharp = SampleColor(home, drifted, nearby, ColorChannels.All, 0.9);
            TestRunner.Check(exact.Energy > soft.Energy && soft.Energy > sharp.Energy,
                "A sharper birth-color gradient reduces feeding on neighboring RGB values");
        }

        private static void RadialVisionFindsFoodAndSteersTowardIt()
        {
            var world = new World(14, 10, 12, null);
            world.WidgetSquare = 24;
            Snake snake = world.Snakes[0];
            var start = new Cell(4, 4);
            var berry = new Cell(4, 0);
            snake.Body = new List<Cell> { start };
            snake.RecentPath = new List<Cell>(snake.Body);
            snake.Heading = 0;
            snake.Speed = 1;
            snake.Motion = 0.99;
            var vine = new Vine(world.NewId(), snake.Genes.Copy(), snake.Skin);
            world.Vines.Add(vine);
            world.Fruits.Add(new Fruit(world.NewId(), vine, berry, 10));
            TestRunner.Check(snake.Genes[Gene.VisionRadius] == 5 &&
                !world.FindVisibleFruit(start, 0).HasValue &&
                !world.FindVisibleFruit(start, 3).HasValue &&
                world.FindVisibleFruit(start, 5).Value.Equals(berry) &&
                world.FindVisibleFruit(new Cell(13, 9), 100).Value.Equals(berry),
                "Vision radius zero disables food detection, five looks nearby and one hundred sees the whole map");
            world.Step(ColorGrid.Uniform(14, 10, Rgb.FromBytes(0, 0, 0)), 0);
            TestRunner.Check(snake.Body[0].Equals(new Cell(4, 3)), "The snake steers toward a berry in its radius");
        }

        private static void SquaresRecoverAfterGeometricCooldowns()
        {
            SquareUse fresh = default(SquareUse);
            SquareUse once = fresh.Consume(0, 0);
            SquareUse twice = once.Consume(1, 0);
            TestRunner.Near(fresh.Strength(0), 1, 0, "Fresh squares give their full color effect");
            TestRunner.Near(once.Strength(749), 0.5, 0, "Half-strength lasts for one minute");
            TestRunner.Near(once.Strength(750), 1, 0, "One minute restores a half-strength square");
            TestRunner.Near(twice.Strength(1500), 0.25, 0, "A second use halves the effect again");
            TestRunner.Near(twice.Strength(1501), 1, 0, "Two minutes restore a quarter-strength square");
            TestRunner.Near(fresh.Consume(0, 1).ReadyAt, 375, 0, "Positive recovery halves the cooldown");
            TestRunner.Near(fresh.Consume(0, -1).ReadyAt, 1500, 0, "Negative recovery doubles the cooldown");
        }

        private static void StationaryColorUsesOneDosePerPulse()
        {
            var world = new World(9, 8, 11, null, 24);
            Snake snake = world.Snakes[0];
            snake.Entering = false;
            var head = new Cell(4, 4);
            snake.Body = new List<Cell> { head };
            snake.Genes[Gene.VisitorArrival] = 0;
            snake.Genes[Gene.LengthComfort] = 0;
            snake.Genes[Gene.SquareRecovery] = 0;
            world.Manual = new ManualSteering { Heading = 0 };
            ColorGrid field = ColorGrid.Uniform(9, 8, snake.Skin);
            for (int i = 0; i < 29; i++) world.Step(field, 0);
            TestRunner.Check(snake.Energy == 0 && world.SquareUses == null,
                "A stopped snake gets no color dose before 2.33 seconds");
            world.Step(field, 0);
            double firstDose = snake.Energy;
            TestRunner.Check(firstDose > 0 && world.SquareUses[head].Uses == 1,
                "The first idle pulse applies and halves a fresh square once");
            for (int i = 0; i < 28; i++) world.Step(field, 0);
            TestRunner.Near(snake.Energy, firstDose, 0, "Stationary sampling waits between pulses");
            world.Step(field, 0);
            TestRunner.Near(snake.Energy - firstDose, firstDose * 0.5, 1e-9,
                "The second idle pulse gives half the first dose");
            SquareUse used = world.SquareUses[head];
            TestRunner.Check(used.Uses == 2 && used.ReadyAt == world.Ticks - 1 + 1500,
                "The half-strength pulse leaves a two-minute cooldown");
        }

        private static void MissedMovesRaiseStressAndResetOnMovement()
        {
            var world = new World(8, 8, 11, null, 24);
            Snake snake = world.Snakes[0];
            snake.Entering = false;
            snake.Body = new List<Cell> { new Cell(7, 4) };
            snake.Heading = 0;
            snake.Speed = 1;
            snake.MutatedChannels = ColorChannels.None;
            snake.Genes[Gene.NightBoost] = 0;
            snake.Genes[Gene.LengthComfort] = 0;
            snake.Genes[Gene.ManeuverCost] = 0;
            snake.Genes[Gene.StressCapacity] = 1;
            snake.Genes[Gene.VisitorArrival] = 0;
            world.Manual = new ManualSteering { Heading = 0 };
            ColorGrid field = ColorGrid.Uniform(8, 8, snake.Skin);
            world.Step(field, 0);
            TestRunner.Check(snake.ConsecutiveMissedMoves == 0 && snake.Stress == 0,
                "A tick before a turn comes due adds no missed-move penalty");
            snake.Motion = 0.99;
            world.Step(field, 0);
            TestRunner.Near(snake.Stress, 0.001, 1e-9, "The first missed move adds only a faint penalty");
            TestRunner.Check(snake.GeneActivity(Gene.MissedMoveStress) == 3,
                "The gene panel records an active missed-move penalty");
            world.Manual.Paused = false;
            snake.Motion = 0.99;
            world.Step(field, 0);
            TestRunner.Near(snake.Stress, 0.003, 1e-9, "The second miss adds twice the first faint penalty");
            TestRunner.Check(snake.ConsecutiveMissedMoves == 2, "Blocked and withheld moves share a streak");
            for (int i = 0; i < 4; i++)
            {
                snake.Motion = 0.99;
                world.Step(field, 0);
            }
            TestRunner.Near(snake.Stress, 0.61, 1e-9, "The fifth miss adds the base and the sixth adds twice the base");
            world.Manual.Heading = 1;
            snake.Motion = 0.99;
            world.Step(field, 0);
            TestRunner.Check(snake.Moves == 1 && snake.ConsecutiveMissedMoves == 0,
                "A successful move resets the streak");
            world.Manual.Paused = true;
            snake.Motion = 0.99;
            world.Step(field, 0);
            TestRunner.Near(snake.Stress, 0.611, 1e-9, "A new streak starts at the faint penalty");
        }

        private static void ParkedColorScalesMissedMoveStress()
        {
            Rgb calm = Rgb.StartingGreen;
            Rgb hostile = Rgb.FromBytes(247, 8, 241);
            TestRunner.Near(ParkedStress(calm, false), 0.1, 1e-9, "A fresh matching parked square halves the penalty");
            TestRunner.Near(ParkedStress(calm, true), 0.15, 1e-9, "Half-strength color reduces the parking modifier");
            TestRunner.Check(ParkedStress(hostile, false) > 0.2, "A hostile parked square raises the penalty");
        }

        private static double ParkedStress(Rgb color, bool worn)
        {
            var world = new World(12, 9, 54, null, 24);
            Snake snake = world.Snakes[0];
            var head = new Cell(6, 4);
            snake.Body = new List<Cell> { head };
            snake.Entering = false;
            snake.MutatedChannels = ColorChannels.All;
            snake.ConsecutiveMissedMoves = 4;
            snake.Motion = 0.99;
            world.Manual = new ManualSteering { Heading = 0 };
            if (worn) world.ConsumeSquares(snake.Body, 0);
            world.Step(ColorGrid.Uniform(12, 9, color), 0);
            return snake.Stress;
        }

        private static void StressJerkRespondsToSpikesAndChronicStress()
        {
            var world = new World(12, 9, 52, null, 24);
            Snake snake = world.Snakes[0];
            snake.Body = new List<Cell> { new Cell(6, 4) };
            snake.Heading = 0;
            snake.Entering = false;
            snake.MutatedChannels = ColorChannels.None;
            snake.Genes[Gene.LengthComfort] = 0;
            snake.Genes[Gene.ManeuverCost] = 0;
            snake.Genes[Gene.Sprint] = 0;
            snake.Genes[Gene.VisitorArrival] = 0;
            snake.Genes[Gene.StressJerk] = 100;
            ColorGrid field = ColorGrid.Uniform(12, 9, snake.Skin);
            snake.Motion = 0.99;
            world.Step(field, 0);
            TestRunner.Check(snake.GeneActivity(Gene.StressJerk) == 0 && snake.SprintRemaining == 0,
                "A calm snake does not roll the stress reaction");

            world.Manual = new ManualSteering { Heading = snake.Heading };
            snake.Genes[Gene.MissedMoveStress] = 0.5;
            snake.ConsecutiveMissedMoves = 4;
            snake.Motion = 0.99;
            world.Step(field, 0);
            TestRunner.Check(snake.RecentStressSpike > 0 && snake.GeneActivity(Gene.StressJerk) == 0,
                "A sharp rise leaves a signal without taking over manual control");
            world.Manual = null;
            snake.Motion = 0.99;
            int opposite = (snake.Heading + 2) % 4;
            world.Step(field, 0);
            TestRunner.Check(snake.GeneActivity(Gene.StressJerk) == 3 && snake.Heading != opposite,
                "A sharp rise can trigger a legal automatic turn or sprint");

            world.Manual = new ManualSteering { Heading = snake.Heading };
            snake.Genes[Gene.MissedMoveStress] = 0;
            snake.Stress = 0.85 * snake.Capacity(0, false);
            snake.RecentStressSpike = 0;
            for (int i = 0; i < 35; i++) world.Step(field, 0);
            world.Manual = null;
            snake.Motion = 0.99;
            world.Step(field, 0);
            TestRunner.Check(snake.HighStressSeconds > 2 && snake.RecentStressSpike == 0 &&
                snake.GeneActivity(Gene.StressJerk) == 3, "High stress sustained over time can trigger a jerk on its own");
        }

        private static void AgentsChooseEveryMove()
        {
            var world = new World(20, 12, 42, null, 24);
            ColorGrid grid = ColorGrid.Uniform(20, 12, Rgb.StartingGreen);
            world.Step(grid, 0);
            bool changed;
            string token = Guid.NewGuid().ToString("N");
            AgentReply birth = AgentControl.Apply(world, 0, false, token, new AgentRequest { Command = "spawn" }, out changed);
            TestRunner.Check(changed && birth.Token == token && !world.Contains(birth.View.Head),
                "An agent spawns its own snake outside the arena");
            TestRunner.Check(birth.View.Options[1].Legal && !birth.View.Options[0].Legal,
                "Only the first step into the arena is allowed while offscreen");
            for (int index = 0; index < 15; index++) world.Step(grid, 0);
            AgentReply sensed = AgentControl.Apply(world, 0, false, "sense",
                new AgentRequest { Command = "sense", Token = token }, out changed);
            TestRunner.Check(!changed && sensed.View.Head.Equals(birth.View.Head) && sensed.View.Moves == 0,
                "An agent snake waits for its command while the world advances");
            var order = new AgentRequest { Command = "move", Token = token, Direction = "forward", Bypass = true };
            AgentReply moved = AgentControl.Apply(world, 0, false, "move1", order, out changed);
            TestRunner.Check(changed && moved.View.Moves == 1 && world.Contains(moved.View.Head),
                "An accepted command moves the agent snake exactly once");
            AgentReply duplicate = AgentControl.Apply(world, 0, false, "move1", order, out changed);
            TestRunner.Check(!changed && duplicate.View.Moves == 1, "A repeated request does not repeat an agent move");
            string secondToken = Guid.NewGuid().ToString("N");
            AgentReply second = AgentControl.Apply(world, 0, false, secondToken,
                new AgentRequest { Command = "spawn" }, out changed);
            TestRunner.Check(changed && second.Token == secondToken && second.View.Snake != birth.View.Snake,
                "Each agent token links to its own snake");
            AgentReply released = AgentControl.Apply(world, 0, false, "release",
                new AgentRequest { Command = "release", Token = token }, out changed);
            TestRunner.Check(changed && released.Status == "released" &&
                world.Snakes.Find(snake => snake.Id == birth.View.Snake).AgentToken == null &&
                world.Snakes.Find(snake => snake.Id == second.View.Snake).AgentToken == secondToken,
                "Release restores autonomy only to the snake linked to the supplied token");
        }

        private static void BerryVisionStacksAndExpiresPerMove()
        {
            var world = new World(20, 12, 36, null);
            world.WidgetSquare = 24;
            Snake snake = world.Snakes[0];
            snake.Genes[Gene.NightBoost] = 0;
            snake.Genes[Gene.FruitSurge] = 55;
            snake.Genes[Gene.VisionRadius] = 5;
            snake.Stress = 0.7;
            snake.AgentToken = "controlled";
            var vine = new Vine(world.NewId(), snake.Genes.Copy(), snake.Skin);
            world.Vines.Add(vine);
            world.Fruits.Add(new Fruit(world.NewId(), vine, snake.Body[0], 10));
            Ecology.Eat(world, snake, snake.Body[0], 0);
            TestRunner.Check(snake.Stress == 0 && snake.Energy == 1 && snake.FruitPower == 2 &&
                snake.EffectiveGene(Gene.VisionRadius, 0) == 10, "A berry immediately feeds, calms and buffs the eater");
            snake.Moves = 3;
            world.Fruits.Add(new Fruit(world.NewId(), vine, snake.Body[0], 10));
            Ecology.Eat(world, snake, snake.Body[0], 0);
            TestRunner.Check(snake.FruitVisionUntil[0] == 55 && snake.FruitVisionUntil[1] == 58 &&
                snake.EffectiveGene(Gene.VisionRadius, 0) == 20 && snake.FruitPower == 4,
                "Each berry adds its own fifty-five-move power and vision timer");
            snake.Moves = 54;
            ColorGrid grid = ColorGrid.Uniform(20, 12, snake.Skin);
            world.Step(grid, 0);
            world.MoveAgent(snake, snake.Heading, false, 0);
            TestRunner.Check(snake.FruitPower == 2 &&
                snake.EffectiveGene(Gene.VisionRadius, 0) == 10, "The oldest berry expires first as the snake moves");
        }

        private static void TimeMultiplierChangesSimulationElapsed()
        {
            TestRunner.Near(ScreenOverlay.SimulationElapsed(0.08, 0.5), 0.04, 1e-9, "Half speed halves simulation time");
            TestRunner.Near(ScreenOverlay.SimulationElapsed(0.08, 10), 0.8, 1e-9, "The fastest preset scales frame time");
            TestRunner.Near(ScreenOverlay.SimulationElapsed(0.08, 0), 0, 0, "Zero pauses the simulation");
        }

        private static void AbruptCalmDropsAMirroredAntifruit()
        {
            var world = new World(12, 6, 45, null) { WidgetSquare = 24 };
            Snake maker = world.Snakes[0];
            maker.Body = new List<Cell> { new Cell(3, 2), new Cell(2, 2), new Cell(1, 2) };
            maker.RecentPath = new List<Cell>(maker.Body);
            maker.Heading = 0;
            maker.Speed = 2;
            maker.Motion = 0.99;
            maker.Mood = Affect.Stressed;
            maker.Stress = 0.8;
            maker.MutatedChannels = ColorChannels.All;
            maker.Genes[Gene.Speed] = 0.6;
            maker.Genes[Gene.VisionRadius] = 5;
            maker.Genes[Gene.LengthComfort] = 0;
            world.Manual = new ManualSteering { Heading = 0, Paused = false };
            var vine = new Vine(world.NewId(), maker.Genes.Copy(), maker.Skin);
            world.Vines.Add(vine);
            world.Fruits.Add(new Fruit(world.NewId(), vine, new Cell(4, 2), 10));
            world.Step(ColorGrid.Uniform(12, 6, maker.Skin), 0);
            TestRunner.Check(world.Fruits.Count == 1 && world.Fruits[0].Antifruit &&
                world.Fruits[0].Position.Equals(new Cell(3, 2)), "A single stressed-to-calm move drops an antifruit behind");

            Fruit trap = world.Fruits[0];
            TestRunner.Near(trap.Tone.R, 1 - maker.Skin.R, 1e-12, "Antifruit uses the maker's opposite color");
            var eater = new Snake(world.NewId(), false, maker.Genes.Copy(), trap.Tone,
                new List<Cell> { trap.Position }, 0)
            {
                Energy = 2, Stress = 0.1, MutatedChannels = ColorChannels.All
            };
            eater.Genes[Gene.StressCapacity] = 1;
            eater.Genes[Gene.Speed] = 0.3;
            eater.Genes[Gene.FruitInheritance] = 0.2;
            TestRunner.Check(!world.FindVisibleFruit(maker.Body[0], 5, maker).HasValue &&
                world.FindVisibleFruit(maker.Body[0], 5, maker, true).Value.Equals(trap.Position) &&
                world.FindVisibleFruit(eater.Body[0], 5, eater).Value.Equals(trap.Position),
                "The maker avoids the tint that lures a snake of the complementary color");
            Ecology.Eat(world, eater, trap.Position, 0);
            TestRunner.Check(eater.Energy == 1 && eater.Stress > 1 && eater.FruitPower == 0.5,
                "An antifruit drains a meal's energy, raises stress and halves power");
            TestRunner.Near(eater.Genes[Gene.Speed], 0.12, 1e-9, "The ingested donor allele has the opposite sign");
            eater.ApplyFruitTimer(0, false);
            TestRunner.Near(eater.FruitPower, 1, 0, "Equal positive and negative fruit stacks cancel");
        }

        private static void JumpCostUsesTheRiseBetweenStacks()
        {
            var world = new World(8, 8, 46, null) { WidgetSquare = 24 };
            ColorGrid grid = ColorGrid.Uniform(8, 8, Rgb.FromBytes(0, 0, 0));
            Snake resident = world.Snakes[0];
            var head = new Cell(3, 2);
            var twoLayers = new Cell(3, 3);
            var threeLayers = new Cell(4, 3);
            resident.Body = new List<Cell> { head, new Cell(2, 2), new Cell(1, 2) };
            resident.RecentPath = new List<Cell>(resident.Body);
            resident.Heading = 0;
            resident.Genes[Gene.NightBoost] = 0;
            resident.Genes[Gene.ManeuverCost] = 0.05;
            resident.Genes[Gene.LengthComfort] = 0;
            resident.Genes[Gene.Imprint] = 0;
            resident.Genes[Gene.BirthColorFeed] = 0;
            resident.Energy = 0.8;
            resident.Speed = 1;
            world.Snakes.Add(new Snake(world.NewId(), false, resident.Genes.Copy(), resident.Skin,
                new List<Cell> { twoLayers, twoLayers, threeLayers, threeLayers, threeLayers }, 0));
            world.Manual = new ManualSteering { Heading = 1 };
            world.Step(grid, 0);
            TestRunner.Check(world.JumpHeight(resident, twoLayers) == 2 &&
                world.JumpHeight(resident, threeLayers) == 3, "From ground level each target layer adds one jump unit");

            world.Manual.Paused = false;
            world.Manual.Boost = true;
            resident.Motion = 0.99;
            world.Step(grid, 0);
            TestRunner.Check(resident.Body[0].Equals(twoLayers), "The head lands on the two-layer stack");
            TestRunner.Near(world.JumpHeight(resident, threeLayers), 1, 0,
                "The next jump only pays for the rise from the current layer");
            TestRunner.Near(resident.Energy, 0.55, 1e-9, "Turning over two layers charges four base units plus the turn");

            world.Manual.Heading = 0;
            world.Manual.Boost = true;
            resident.Motion = 0.99;
            world.Step(grid, 0);
            TestRunner.Check(resident.Body[0].Equals(threeLayers), "The next jump lands on the higher stack");
            TestRunner.Near(resident.Energy, 0.40, 1e-9, "Rising one layer charges two base units plus the turn");
        }

        private static void MutatedVisionIgnoresInsensitiveChannels()
        {
            Rgb home = Rgb.StartingGreen;
            var alternate = Rgb.FromBytes(54, 177, 240);
            ColorChannels mask = ColorChannels.Red | ColorChannels.Green;
            Snake masked = SampleColor(home, home, alternate, mask, 0.45);
            Snake exact = SampleColor(home, home, home, mask, 0.45);
            TestRunner.Near(masked.Energy, exact.Energy, 1e-9, "A two-channel snake cannot sense the omitted channel");
            var hostile = Rgb.FromBytes(255, 0, 255);
            Snake blind = SampleColor(home, home, hostile, ColorChannels.None, 0.45);
            TestRunner.Near(blind.Energy, 0.2, 1e-9, "A fully color-blind snake gets no color feeding");
            TestRunner.Near(blind.Stress, 0.4, 1e-9, "A fully color-blind snake gets no direct color stress");
        }

        private static Snake SampleColor(Rgb home, Rgb skin, Rgb environment, ColorChannels mask, double gradient)
        {
            Genome genes = Genome.Roll(new SaverRandom(8), null);
            genes[Gene.BirthColorGradient] = gradient;
            genes[Gene.LengthComfort] = 0;
            var snake = new Snake(1, true, genes, home, new List<Cell> { new Cell(2, 2) }, 0)
            {
                Skin = skin, MutatedChannels = mask, Energy = 0.2, Stress = 0.4
            };
            var field = new ColorField(ColorGrid.Uniform(5, 5, environment), null, null);
            snake.Advance(field, 0, World.StepSeconds, new SaverRandom(10), null, null, new World(5, 5, 1, null));
            return snake;
        }

        private static void SavedSpeciesRestoresTheGenomeAtBirth()
        {
            string path = Path.GetTempFileName();
            try
            {
                var world = new World(14, 7, 55, null, WidgetViewport.DefaultSquare);
                var resident = world.Snakes[0];
                resident.Genes[Gene.Speed] = 0.739;
                resident.Genes[Gene.SelfFeeding] = 0.004;
                resident.MutatedChannels = ColorChannels.Blue;
                var library = new SpeciesLibrary(path);
                TestRunner.Check(library.List().Count == 0, "An empty species database has no named records");
                string name = "M\u0103r & O'Green";
                library.Save(name, SpeciesTemplate.FromSnake(resident));
                resident.Genes[Gene.Speed] = 0.215;

                var saved = new SpeciesLibrary(path).List();
                TestRunner.Check(saved.Count == 1 && saved[0].Key == name, "The SQLite library preserves the species name");
                var arrival = world.Birth(saved[0].Value);
                TestRunner.Check(!arrival.IsResident && world.Snakes.Contains(resident) &&
                    arrival.Entering && !world.Contains(arrival.Body[0]), "A named birth starts just outside the arena");
                TestRunner.Near(arrival.Genes[Gene.Speed], 0.739, 1e-12, "Named birth restores the saved speed");
                TestRunner.Near(arrival.Genes[Gene.SelfFeeding], 0.004, 1e-12, "Named birth restores the saved self feeding");
                TestRunner.Check(arrival.Sensitivity == ColorChannels.Blue &&
                    Rgb.Distance(arrival.BirthColor.Value, resident.BirthColor.Value) == 0,
                    "Named birth restores its color traits");
                TestRunner.Check(!world.Contains(world.Birth(null).Body[0]) && world.Snakes.Count == 3,
                    "Random birth works without a saved template");
            }
            finally { File.Delete(path); }
        }

        private static void OriginalSkinKeepsItsShapeAndOpacity()
        {
            var world = new World(5, 2, 5, null);
            Snake resident = world.Snakes[0];
            var body = new Cell(2, 0);
            resident.Body = new List<Cell> { new Cell(3, 0), body, new Cell(1, 0) };
            resident.SaturatedFor = resident.TailDwell(0, false);
            using (var pixels = new Bitmap(120, 48))
            using (Graphics graphics = Graphics.FromImage(pixels))
            {
                BrowserSnakeSkin.Draw(graphics, world, 0, 24, Point.Empty);
                int single = pixels.GetPixel(60, 12).A;
                TestRunner.Check(single >= 164 && single <= 167 && pixels.GetPixel(48, 0).A == 0,
                    "Game skin uses rounded, inset body segments at 65 percent opacity");
                TestRunner.Check(pixels.GetPixel(36, 12).A < single && pixels.GetPixel(84, 12).A == 255,
                    "The game tail fades while the head stays solid");
                world.Snakes.Add(new Snake(world.NewId(), false, resident.Genes.Copy(), Rgb.FromBytes(200, 94, 80),
                    new List<Cell> { new Cell(4, 0), body, new Cell(0, 0) }, 0));
                graphics.Clear(Color.Transparent);
                BrowserSnakeSkin.Draw(graphics, world, 0, 24, Point.Empty);
                int overlap = pixels.GetPixel(60, 12).A;
                TestRunner.Check(overlap > single && overlap < 255, "Game skin composites translucent overlaps");
            }
        }

        private static void RealClockDrivesTheWidgetDayAndNight()
        {
            var localOffset = TimeSpan.FromHours(2);
            var noon = new DateTimeOffset(2026, 3, 21, 12, 0, 0, localOffset);
            var midnight = noon.AddHours(-12);
            TestRunner.Near(Daylight.WidgetNightAt(noon, null, null), 0, 0, "Local noon has no night boost");
            TestRunner.Near(Daylight.WidgetNightAt(midnight, null, null), 1, 0, "Local midnight gives the strongest night boost");
            TestRunner.Near(Daylight.WidgetNightAt(noon, 47, 28), Daylight.NightAt(noon, 47, 28), 0,
                "Configured coordinates use actual solar darkness");
        }

        private static void FineGeneEditsUsePercentagePoints()
        {
            TestRunner.Near(Genome.Nudge(Gene.Speed, 0.2, 0.01), 0.2001, 1e-12, "Fine arrow edits use 0.01 percentage points");
            TestRunner.Near(Genome.Nudge(Gene.VisionRadius, 200, 0.01), 200.01, 1e-12, "Vision radius adjustments use its displayed units");
            TestRunner.Near(Genome.Nudge(Gene.LearningOffset, -0.8, -50), -1.3, 1e-12, "Signed edits extend below minus one");
        }

        private static void EggWaitsForItsMoonAndParent()
        {
            var midnight = new DateTime(2026, 3, 22, 0, 0, 0);
            var moon = new DateTimeOffset(midnight, TimeZoneInfo.Local.GetUtcOffset(midnight));
            TestRunner.Check(Daylight.NextWidgetFullMoon(moon.AddHours(-1), null, null) == moon,
                "Exactly one hour is enough to reach the upcoming peak");
            TestRunner.Check(Daylight.NextWidgetFullMoon(moon.AddMinutes(-59), null, null) == moon.AddDays(1),
                "A nest laid inside the last hour waits for the following peak");
            var world = new World(32, 8, 55, null, 24);
            Snake parent = world.Snakes[0];
            parent.Entering = false;
            parent.Genes[Gene.VisitorArrival] = 0;
            parent.Body.Clear();
            for (int x = 23; x >= 4; x--) parent.Body.Add(new Cell(x, 3));
            ColorGrid field = ColorGrid.Uniform(32, 8, parent.Skin);
            world.Step(field, 0, moon.AddHours(-2));
            TestRunner.Check(world.Eggs == null, "Twenty squares cannot lay an egg");
            parent.Body.Add(new Cell(3, 3));
            parent.LayerOrder.Add(world.NewLayerOrder());
            parent.LargestLength = 21;
            world.Step(field, 0, moon.AddHours(-2));
            Egg egg = world.Eggs[parent.Id];
            TestRunner.Check(egg.Position.Equals(new Cell(3, 3)) && egg.DueUtcTicks == moon.UtcDateTime.Ticks,
                "Twenty-one squares leave a nest with a fixed position and moon deadline");
            string profile = Path.Combine(Path.GetTempPath(), "Znake-egg-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var store = StateStore.Open(profile))
                {
                    store.Save(world, moon.UtcDateTime);
                    var savedEgg = store.Load(moon.UtcDateTime).Eggs[parent.Id];
                    TestRunner.Check(savedEgg.Position.Equals(egg.Position) && savedEgg.DueUtcTicks == egg.DueUtcTicks,
                        "Restart preserves the parent, nest coordinates, and real-time deadline");
                }
            }
            finally
            {
                File.Delete(Path.Combine(profile, "world.json"));
                File.Delete(Path.Combine(profile, "world.lock"));
                Directory.Delete(profile);
            }
            parent.Body = new List<Cell> { new Cell(5, 3) };
            parent.LayerOrder = new List<long> { world.NewLayerOrder() };
            parent.Heading = 0;
            parent.Motion = 0;
            parent.Speed = 0;
            TestRunner.Check(!world.TryReframe(27, 8, new Cell(-4, 0)),
                "Shrinking keeps the saved nest inside the arena");
            world.Step(field, 0, moon.AddSeconds(-1));
            TestRunner.Check(world.Snakes.Count == 1 && world.Eggs.Count == 1, "The baby waits for its moon");
            for (int i = 0; i < 8 && world.Eggs.Count > 0; i++)
            {
                parent.Motion = 0.99;
                parent.Speed = 1;
                world.Step(field, 1, moon);
            }
            TestRunner.Check(world.Eggs.Count == 0 && parent.Body[0].Equals(egg.Position) &&
                world.Snakes.Count == 2 && world.Snakes[1].Body[0].Equals(egg.Position),
                "The parent takes legal turns to retrieve its baby at the remembered nest");
        }

        private static void TailBiteSplitsOnlyTheEncounteredTail()
        {
            var world = new World(12, 9, 42, null, 24);
            Snake parent = world.Snakes[0];
            parent.Entering = false;
            parent.Body = new List<Cell> { new Cell(4, 4), new Cell(4, 5), new Cell(5, 5), new Cell(5, 4) };
            parent.Heading = 3;
            parent.Stress = 0.5;
            world.Manual = new ManualSteering { Heading = 0, SnakeId = parent.Id };
            TestRunner.Check(world.BiteTail(0, false) && world.Snakes.Count == 1 && parent.Body.Count == 3 &&
                parent.Stress < 0.5, "B eats the encountered tail tip for calm without making a clone");
            TestRunner.Check(!world.BiteTail(0, false), "B leaves a snake alone when its tail does not block the move");
            parent.Body.AddRange(new[] { new Cell(5, 4), new Cell(6, 4) });
            parent.SaturatedFor = parent.TailDwell(0, false) / 2;
            TestRunner.Check(world.BiteTail(0, false) && world.Snakes.Count == 2 && parent.Body.Count == 4 &&
                world.Snakes[1].Body[0].Equals(new Cell(6, 4)) &&
                world.Snakes[1].Body[1].Equals(parent.Body[3]) &&
                world.Snakes[1].Genes[Gene.Speed] == parent.Genes[Gene.Speed],
                "B shares the cut tile and turns the original tail into an exact clone's head");
            TestRunner.Near(world.Snakes[1].SplitHeadOpacity.Value, 0.63, 1e-9, "The old tip keeps its opacity as a head");
            TestRunner.Near(parent.SplitTailOpacity.Value, 0.5, 0, "The parent keeps half opacity at the cut");
            TestRunner.Near(world.Snakes[1].SplitTailOpacity.Value, 0.5, 0, "The clone gets the other half at the cut");

            parent.Body.AddRange(new[] { new Cell(6, 4), new Cell(7, 4), new Cell(7, 3), new Cell(6, 3),
                new Cell(5, 3), new Cell(4, 3), new Cell(3, 3) });
            parent.LayerOrder = null;
            double originalSpeed = parent.Genes[Gene.Speed];
            TestRunner.Check(world.BiteTail(1, false) && world.Snakes.Count == 5 &&
                world.Snakes[2].Body.Count == 3 && world.Snakes[3].Body.Count == 3 && world.Snakes[4].Body.Count == 2,
                "A long section becomes capped clones and keeps any shorter remainder");
            TestRunner.Check(world.Snakes[2].Body[0].Equals(new Cell(3, 3)) &&
                parent.Genes[Gene.Speed] == originalSpeed &&
                !System.Linq.Enumerable.SequenceEqual(world.Snakes[2].Genes.Values, parent.Genes.Values),
                "A multi-clone split mutates offspring and keeps the parent genome");
            parent.Motion = 0.99;
            parent.Speed = 1;
            world.Snakes[1].AgentToken = "fixture:unrelated-clone";
            world.Step(ColorGrid.Uniform(12, 9, parent.Skin), 1);
            TestRunner.Check(parent.Body[0].Equals(new Cell(4, 4)),
                "The cut pass still blocks unrelated snake segments on the same tile");
            world.Snakes[1].Body[1] = new Cell(6, 5);
            parent.Motion = 0.99;
            world.Step(ColorGrid.Uniform(12, 9, parent.Skin), 1);
            TestRunner.Check(parent.Body[0].Equals(new Cell(5, 4)) && !world.Manual.TailPass.HasValue,
                "The split clears a path for one move without a separate Space press");
        }

        private static void TrayBirthsStayAndManualHealthFollowsSelection()
        {
            var world = new World(12, 9, 55, null, 24);
            Snake resident = world.Snakes[0];
            Snake tray = world.Birth(null, true);
            Snake visitor = world.Birth(null);
            var target = new Cell(0, 4);
            TestRunner.Check(tray.Entering && !world.Contains(tray.Body[0]),
                "Keeping a tray birth inside still allows a normal offscreen entrance");
            tray.Entering = visitor.Entering = false;
            tray.Body = new List<Cell> { target };
            visitor.Body = new List<Cell> { new Cell(0, 5) };
            tray.Heading = visitor.Heading = 2;
            world.Manual = new ManualSteering { SnakeId = tray.Id, Heading = 2, Paused = false };
            tray.Speed = visitor.Speed = 1;
            tray.Motion = visitor.Motion = 0.99;
            ColorGrid field = ColorGrid.Uniform(12, 9, tray.Skin);
            world.Step(field, 0);
            TestRunner.Check(world.ManualSnake == tray && tray.Body[0].Equals(target) && !tray.Exiting,
                "Manual selection follows the chosen tray snake, which cannot cross the edge");
            world.Manual.SnakeId = visitor.Id;
            visitor.Body[0] = new Cell(0, 5);
            visitor.Heading = 2;
            visitor.Motion = 0.99;
            world.Step(field, 0);
            TestRunner.Check(!world.Snakes.Contains(visitor) && world.Snakes.Contains(tray),
                "An ordinary visitor can still leave, clearing its manual selection");

            var screen = new Rectangle(0, 0, 1200, 800);
            var sandbox = new Rectangle(560, 440, 624, 344);
            Rectangle badge = ManualHealthOverlay.Place(sandbox, screen, false);
            TestRunner.Check(badge.Bottom < sandbox.Top && badge.Left + badge.Width / 2 == sandbox.Left + sandbox.Width / 2,
                "The health bar is centered above the sandbox");
            TestRunner.Check(ManualHealthOverlay.Place(sandbox, screen, true).Top == screen.Top,
                "In fullscreen the health bar moves to the top of the screen");
            resident.Stress = 0;
            TestRunner.Near(ManualHealthOverlay.Calm(resident, 0), 1, 0, "Full calm maps to one");
            resident.Stress = resident.Capacity(0, false);
            TestRunner.Near(ManualHealthOverlay.Calm(resident, 0), 0, 0, "Maximum stress maps to zero");
        }

        private static void DeathMakesTwoSquaresAndTheLastSquareIsInert()
        {
            var world = new World(12, 6, 87, null, 24);
            Snake snake = world.Snakes[0];
            snake.Heading = 0;
            snake.LargestLength = 8;
            var head = new Cell(8, 2);
            var tail = new Cell(7, 2);
            snake.RecentPath = new List<Cell> { new Cell(3, 5), new Cell(4, 5), head };
            Ecology.Decompose(world, snake, head, 0);
            Vine vine = world.Vines[0];
            TestRunner.Check(vine.Squares.Count == 2 && vine.Squares.Exists(square => square.Position.Equals(head)) &&
                vine.Squares.Exists(square => square.Position.Equals(tail)),
                "Death leaves the head and its adjacent rear square");
            vine.NextFruitIn = 0;
            Ecology.Advance(world, World.StepSeconds, 0);
            TestRunner.Check(world.Fruits.Count == 1, "A two-square vine still produces fruit");

            vine.Squares.RemoveAt(1);
            vine.NextFruitIn = 0;
            double before = vine.Squares[0].Nutrients;
            Ecology.Advance(world, World.StepSeconds, 0);
            TestRunner.Check(vine.Inactive && vine.Squares[0].Nutrients < before && world.Fruits.Count == 1,
                "The last vine square fades without producing more fruit");
        }

        private static void GeneEditsSaveImmediately()
        {
            string path = Path.Combine(Path.GetTempPath(), "ZnakeGeneEdit-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (StateStore store = StateStore.Open(path))
                {
                    var world = new World(12, 6, 19, null, 24);
                    using (var overlay = new ScreenOverlay(store, new SaverSettings(), world))
                    {
                        var resident = world.Snakes[0];
                        TestRunner.Check(overlay.SetSnakeGene(resident.Id, Gene.Speed, 1.431, 2.5),
                            "A gene edit writes the checkpoint");
                        World saved = store.Load(DateTime.UtcNow);
                        TestRunner.Near(saved.Snakes[0].Genes[Gene.Speed], 1.431, 1e-12, "Values above one survive reopening");
                        TestRunner.Near(saved.GeneEditStep, 2.5, 0, "The chosen percentage step survives reopening");
                        TestRunner.Check(overlay.SetSnakeGene(resident.Id, Gene.SquareRecovery, -1.5) &&
                            store.Load(DateTime.UtcNow).Snakes[0].Genes[Gene.SquareRecovery] == -1.5,
                            "Negative cooldown genes are preserved in the checkpoint");
                        Snake visitor = world.Birth(null, true);
                        overlay.PauseSnake(visitor);
                        TestRunner.Check(overlay.InspectedSnake == visitor &&
                            !overlay.SetSnakeGene(resident.Id, Gene.Speed, 5),
                            "A stale resident edit cannot change the snake after selecting a visitor");
                        TestRunner.Check(overlay.SetSnakeGene(visitor.Id, Gene.Speed, 2.14),
                            "The panel can edit a manually selected visitor");
                        overlay.ResumeSnake();
                        saved = store.Load(DateTime.UtcNow);
                        TestRunner.Check(overlay.InspectedSnake == visitor && saved.Snakes[1].StaysInArena &&
                            saved.Snakes[1].Genes[Gene.Speed] == 2.14 && saved.Snakes[0].Genes[Gene.Speed] == 1.431,
                            "Autonomy keeps the selected genome visible and each snake saves its own genes");
                    }
                }
            }
            finally
            {
                foreach (string file in new[] { "world.json", "world.previous.json", "world.lock" })
                    File.Delete(Path.Combine(path, file));
                if (Directory.Exists(path)) Directory.Delete(path);
            }
        }

        private static void PreferredLengthReducesEnergyDrainAndStress()
        {
            Snake preferred = AtLength(5, 1);
            Snake close = AtLength(6, 1);
            Snake distant = AtLength(13, 1);
            Snake muted = AtLength(13, 0);
            TestRunner.Check(preferred.Energy > close.Energy && close.Energy > distant.Energy &&
                preferred.Stress < close.Stress && close.Stress < distant.Stress,
                "Moving away from the preferred length gradually increases energy drain and stress");
            TestRunner.Check(distant.Body.Count == 13, "Length pressure never resizes the snake directly");
            TestRunner.Near(muted.Energy, 0.8, 1e-9, "Zero influence disables length drain");
            TestRunner.Near(distant.Genes[Gene.Length13], 0.001, 1e-9, "Length pressure leaves the stored preference intact");
        }

        private static Snake AtLength(int length, double influence)
        {
            var world = new World(20, 3, 119, null, 24);
            Snake snake = world.Snakes[0];
            snake.Body.Clear();
            for (int index = 0; index < length; index++) snake.Body.Add(new Cell(15 - index, 1));
            snake.RecentPath = new List<Cell>(snake.Body);
            snake.LargestLength = length;
            snake.MutatedChannels = ColorChannels.None;
            snake.Energy = 0.8;
            snake.Genes[Gene.Length3] = 0.2;
            snake.Genes[Gene.Length5] = 1;
            snake.Genes[Gene.Length8] = 0.15;
            snake.Genes[Gene.Length13] = 0.001;
            snake.Genes[Gene.LengthComfort] = influence;
            snake.Genes[Gene.Sprint] = 0;
            world.Step(ColorGrid.Uniform(20, 3, Rgb.StartingGreen), 0);
            return snake;
        }

        private static void FruitInheritanceUsesTheReceivingGeneAndPhase()
        {
            TestRunner.Near(FruitLengthPreference(0.2, 0), 0.36, 1e-9, "A meal blends using the recipient's configured share");
            TestRunner.Near(FruitLengthPreference(0.2, 1), 0.44, 1e-9, "Full phase strengthens fruit inheritance via Night Boost");
            TestRunner.Near(FruitLengthPreference(0, 1), 0.2, 1e-9, "Zero inheritance leaves the recipient's preference intact");
        }

        private static double FruitLengthPreference(double share, double phase)
        {
            var world = new World(10, 4, 7, null);
            Snake snake = world.Snakes[0];
            snake.Genes[Gene.Length5] = 0.2;
            snake.Genes[Gene.FruitInheritance] = share;
            snake.Genes[Gene.NightBoost] = 0.5;
            Genome donor = snake.Genes.Copy();
            donor[Gene.Length5] = 1;
            var vine = new Vine(world.NewId(), donor, snake.Skin);
            Cell meal = snake.Body[0];
            world.Fruits.Add(new Fruit(world.NewId(), vine, meal, 9));
            Ecology.Eat(world, snake, meal, phase);
            return snake.Genes[Gene.Length5];
        }
    }
}
