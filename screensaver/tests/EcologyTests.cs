using System;
using System.Collections.Generic;
using System.Linq;

namespace Znake.Tests
{
    internal static class EcologyTests
    {
        public static void Run()
        {
            var world = new World(13, 9, 139, null);
            Snake donor = world.Snakes.Single();
            Rgb green = Rgb.StartingGreen, hostile = Rgb.FromBytes(225, 22, 170);
            var raw = ColorGrid.Uniform(13, 9, hostile);
            var corner = new Cell(5, 4);
            donor.Body = new List<Cell> { corner };
            donor.RecentPath = new List<Cell> { new Cell(3, 3), new Cell(4, 3), new Cell(5, 3), corner };
            donor.Genes[Gene.ColorDrift] = 0.8;
            donor.Stress = donor.Capacity(0);
            donor.SaturatedFor = donor.TailDwell(0) - 0.01;
            world.Step(raw, 0);
            TestRunner.Check(world.Snakes.Count == 1 && world.Snakes[0].IsResident &&
                world.Snakes[0].Id != donor.Id, "Resident death creates exactly one successor");
            Vine vine = world.Vines.Single();
            TestRunner.Check(vine.Squares.Count == 4 && vine.Squares.Any(square => square.Position.Equals(corner)),
                "The corpse follows the donor's curved path");

            vine.NextFruitIn = 0;
            Ecology.Advance(world, 0.1, 0);
            Fruit first = world.Fruits.Single();
            TestRunner.Near(first.Genes[Gene.ColorDrift], 0.8, 1e-9, "Fruit retains the producing genome");
            Snake eater = world.Snakes.Single();
            eater.Genes[Gene.ColorDrift] = 0;
            double energy = eater.Energy;
            Ecology.Eat(world, eater, first.Position);
            Ecology.Eat(world, eater, first.Position);
            TestRunner.Near(eater.Energy, energy + 1, 1e-9, "Fruit gives energy once");
            TestRunner.Check(eater.Genes[Gene.ColorDrift] > 0 && world.Fruits.Count == 0, "Fruit transfers donor traits");

            vine.NextFruitIn = 100;
            var location = new Cell(6, 5);
            world.Fruits.Add(new Fruit(world.NewId(), vine, location, 200));
            double before = vine.Nutrition;
            Ecology.Advance(world, 0.2, 0);
            double lossWithOne = before - vine.Nutrition;

            world.Fruits.Add(new Fruit(world.NewId(), vine, new Cell(7, 5), 200));
            before = vine.Nutrition;
            Ecology.Advance(world, 0.2, 0);
            TestRunner.Check(before - vine.Nutrition < lossWithOne, "Two fruits from the same vine slow decay");
            var color = new ColorField(ColorGrid.Uniform(13, 9, green), null, null);
            Ecology.TintField(world, color, 0);
            TestRunner.Near(color.DebugTint(location).Alpha, Ecology.LifeInfluence(first.Genes, 0), 1e-9,
                "Fruit contributes an individual environmental life unit");

            long retainedId = vine.Id;
            long retainedFruitId = world.Fruits[0].Id;
            Snake overlapping = SnakeTests.Create(new Genome());
            overlapping.Genes[Gene.ColorDrift] = 0.04;
            overlapping.RecentPath = new List<Cell> { new Cell(5, 2), new Cell(5, 3), corner };
            Ecology.Decompose(world, overlapping, corner, 0);
            TestRunner.Check(world.Vines.Count == 1 && vine.Id == retainedId && vine.Squares.Count >= 5 &&
                world.Fruits.Any(fruit => fruit.Id == retainedFruitId && fruit.VineId == vine.Id),
                "A merge adds path and preserves the original vine and its fruit relationship");
            TestRunner.Near(vine.Genes[Gene.ColorDrift], 0.04, 1e-9, "Survival can select the lower numeric allele");
            TestRunner.Near(world.Fruits[0].Genes[Gene.ColorDrift], 0.8, 1e-9,
                "An earlier fruit retains its original allele after merge");

            vine.Squares.Clear();
            Ecology.Advance(world, 0.2, 0);
            TestRunner.Check(world.Vines.Count == 1, "Remaining fruit keeps its vine alive");
            world.Fruits.Clear();
            Ecology.Advance(world, 0.2, 0);
            TestRunner.Check(world.Vines.Count == 0, "The vine disappears once its color and fruit are gone");

            var visitorWorld = new World(12, 9, 82, null);
            Snake visitor = visitorWorld.AddVisitor(new SaverRandom(52));
            visitor.Exiting = true;
            visitor.Heading = 2;
            visitor.Body = new List<Cell> { new Cell(-1, 2), new Cell(0, 2) };
            visitor.Speed = 1;
            visitor.Motion = 0;
            visitorWorld.Step(ColorGrid.Uniform(12, 9, green), 0);
            TestRunner.Check(visitorWorld.Snakes.Contains(visitor), "A partly visible visitor stays active");
            visitor.Motion = 0.99;
            visitorWorld.Step(ColorGrid.Uniform(12, 9, green), 0);
            TestRunner.Check(!visitorWorld.Snakes.Contains(visitor), "A wholly departed visitor leaves permanently");
            FruitRetryCadence();
            DonorTraitReachesFruit(Gene.Imprint, 33, 0.02, 0.17);
            DonorTraitReachesFruit(Gene.VisitorArrival, 20, 0.02, 0.17);
            DonorTraitReachesFruit(Gene.VisitorArrival, 7, 0.17, 0.02);
            RareBirthAllelesUseWeightedSurvival();
        }

        private static void RareBirthAllelesUseWeightedSurvival()
        {
            Rgb skin = Rgb.StartingGreen;
            var receiver = Genome.Roll(new SaverRandom(18), null);
            receiver[Gene.Length3] = 0.08;
            receiver[Gene.Length5] = 0.22;
            receiver[Gene.Length8] = 0.025;
            receiver[Gene.Length13] = 0.0003;
            var donor = receiver.Copy();
            donor[Gene.Length13] = 0.003;
            Genome selected = Ecology.ChooseSurvivingGenes(receiver, donor, skin, 0);
            TestRunner.Near(selected[Gene.Length13], 0.003, 1e-9,
                "A rare length-13 allele wins when it outlives the weighted shorter births");

            receiver[Gene.Length3] = donor[Gene.Length3] = 0;
            receiver[Gene.Length5] = donor[Gene.Length5] = 0;
            selected = Ecology.ChooseSurvivingGenes(receiver, donor, skin, 0);
            TestRunner.Near(selected[Gene.Length13], 0.0003, 1e-9,
                "A higher length-13 chance loses when the shorter birth survives longer");
            selected = Ecology.ChooseSurvivingGenes(donor, receiver, skin, 0);
            TestRunner.Near(selected[Gene.Length13], 0.0003, 1e-9,
                "A lower length-13 chance can win on survival");

            receiver[Gene.Length8] = donor[Gene.Length8] = 0;
            selected = Ecology.ChooseSurvivingGenes(receiver, donor, skin, 0);
            TestRunner.Near(selected[Gene.Length13], 0.0003, 1e-9,
                "Two guaranteed length-13 births tie and preserve the receiver");
            receiver[Gene.Length13] = 0;
            selected = Ecology.ChooseSurvivingGenes(receiver, donor, skin, 0);
            TestRunner.Near(selected[Gene.Length13], 0.003, 1e-9,
                "Zero total birth weight compares against its length-five fallback");
        }

        private static void DonorTraitReachesFruit(Gene trait, uint profile, double present, double incoming)
        {
            var receiver = Genome.Roll(new SaverRandom(profile), null);
            var donor = receiver.Copy();
            receiver[trait] = present;
            donor[trait] = incoming;
            var world = new World(13, 9, 139, null);
            var snake = SnakeTests.Create(donor);
            var death = snake.Body[0];
            var vine = new Vine(world.NewId(), receiver, snake.Skin) { NextFruitIn = 0 };
            vine.Squares.Add(new VineSquare(death, snake.Skin, 2));
            world.Vines.Add(vine);

            Ecology.Decompose(world, snake, death, 0);
            Ecology.Advance(world, World.StepSeconds, 0);

            TestRunner.Near(vine.Genes[trait], incoming, 1e-9, trait + " can win the survival comparison");
            TestRunner.Near(world.Fruits.Single().Genes[trait], incoming, 1e-9,
                "Fruit inherits the winning " + trait + " allele");
        }

        private static void FruitRetryCadence()
        {
            var world = new World(1, 1, 139, null);
            var center = new Cell(0, 0);
            var blocked = new Vine(world.NewId(), new Genome(), Rgb.StartingGreen) { NextFruitIn = 0 };
            blocked.Squares.Add(new VineSquare(center, Rgb.StartingGreen, 3));
            world.Vines.Add(blocked);
            var depleted = new Vine(world.NewId(), new Genome(), Rgb.StartingGreen) { NextFruitIn = 0 };
            depleted.Squares.Add(new VineSquare(center, Rgb.StartingGreen, 0.25));
            world.Vines.Add(depleted);

            Ecology.Advance(world, World.StepSeconds, 0);

            TestRunner.Check(world.Fruits.Count == 0 && blocked.NextFruitIn > World.StepSeconds,
                "A blocked fruit site waits for another production window");
            TestRunner.Check(depleted.NextFruitIn > World.StepSeconds,
                "A nutrient shortfall waits for another production window");
        }
    }
}
