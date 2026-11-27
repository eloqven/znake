using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Znake
{
    [DataContract]
    internal sealed class VineSquare
    {
        [DataMember] public Cell Position;
        [DataMember] public Rgb Tone;
        [DataMember] public double Nutrients;
        [DataMember] public double Peak;

        public VineSquare(Cell point, Rgb tone, double nutrients)
        {
            Position = point;
            Tone = tone;
            Nutrients = nutrients;
            Peak = nutrients;
        }

        public Tint ColorTrace()
        {
            return new Tint(Tone, 0.38 * Numbers.Unit(Nutrients / Peak));
        }
    }

    [DataContract]
    internal sealed class Vine
    {
        [DataMember] public long Id;
        [DataMember] public Genome Genes;
        [DataMember] public List<VineSquare> Squares = new List<VineSquare>();
        [DataMember] public Rgb FruitColor;
        [DataMember] public double NextFruitIn;
        [DataMember(EmitDefaultValue = false)] public bool Inactive;

        public Vine(long id, Genome genes, Rgb color)
        {
            Id = id;
            Genes = genes;
            FruitColor = color;
            NextFruitIn = 3;
        }

        public double Nutrition { get { return Squares.Sum(square => square.Nutrients); } }
    }

    [DataContract]
    internal sealed class Fruit
    {
        [DataMember] public long Id;
        [DataMember] public long VineId;
        [DataMember] public Cell Position;
        [DataMember] public Rgb Tone;
        [DataMember] public Genome Genes;
        [DataMember] public double Remaining;
        [DataMember(EmitDefaultValue = false)] public bool Antifruit;

        public Fruit(long id, Vine vine, Cell point, double remaining)
        {
            Id = id;
            VineId = vine.Id;
            Position = point;
            Tone = vine.FruitColor;
            Genes = vine.Genes.Copy();
            Remaining = remaining;
        }

        public Fruit(long id, Snake maker, Cell point, double remaining)
        {
            Id = id;
            Position = point;
            Tone = new Rgb(1 - maker.Skin.R, 1 - maker.Skin.G, 1 - maker.Skin.B);
            Genes = maker.Genes.Copy();
            Remaining = remaining;
            Antifruit = true;
        }
    }

    internal static class Ecology
    {
        private const double FruitNutrients = 1;
        private const double MinimumSquareNutrients = 0.00005;

        public static double LifeInfluence(Genome genome, double night)
        {
            return Numbers.Unit(0.015 + 0.027 * genome.Express(Gene.Imprint, 0, night));
        }

        public static void TintField(World world, ColorField field, double night)
        {
            foreach (Vine vine in world.Vines)
            {
                if (world.WidgetSquare != 0 && !IsActive(vine)) continue;
                foreach (VineSquare square in vine.Squares)
                    field.Add(square.Position, square.ColorTrace(), false);
            }
            foreach (Fruit fruit in world.Fruits)
                field.Add(fruit.Position, new Tint(fruit.Tone, LifeInfluence(fruit.Genes, night)), false);
        }

        public static bool IsActive(Vine vine)
        {
            if (vine.Inactive || vine.Squares.Count < 2) return false;
            var cells = new HashSet<Cell>(vine.Squares.Select(square => square.Position));
            return cells.Any(point => Cell.Directions.Any(direction => cells.Contains(point.Move(direction))));
        }

        public static bool Eat(World world, Snake snake, Cell point, double night = 0)
        {
            int index = world.Fruits.FindIndex(fruit => fruit.Position.Equals(point));
            if (index < 0) return false;
            Fruit meal = world.Fruits[index];
            world.Fruits.RemoveAt(index);
            snake.Energy = Math.Max(0, Numbers.Finite(snake.Energy + (meal.Antifruit ? -1 : 1)));
            if (world.WidgetSquare != 0)
            {
                snake.Stress = meal.Antifruit ? Math.Min(snake.Capacity(night), snake.Stress + 1) :
                    Math.Max(0, snake.Stress - 1);
                snake.LastFruitTick = world.Ticks;
                snake.LastFruitWasAnti = meal.Antifruit;
                snake.ApplyFruitTimer(night, meal.Antifruit);
            }
            snake.AbsorbFruit(meal.Genes, night, meal.Antifruit);
            return true;
        }

        internal static void DropAntifruit(World world, Snake maker, double night)
        {
            Cell head = maker.Body[0];
            Cell rear = head.Move(Cell.Directions[(maker.Heading + 2) % Cell.Directions.Length]);
            Func<Cell, bool> available = cell => world.Contains(cell) &&
                !world.Fruits.Any(fruit => fruit.Position.Equals(cell));
            Cell? point = available(rear) ? (Cell?)rear : FreeNeighbor(head, available);
            if (point.HasValue)
                world.Fruits.Add(new Fruit(world.NewId(), maker, point.Value,
                    62 + 24 * maker.EffectiveGene(Gene.StressCapacity, night)));
        }

        public static void Advance(World world, double seconds, double night)
        {
            foreach (Fruit fruit in world.Fruits) fruit.Remaining -= seconds;
            world.Fruits.RemoveAll(fruit => fruit.Remaining <= 0);

            foreach (Vine vine in world.Vines)
            {
                bool active = world.WidgetSquare == 0 || IsActive(vine);
                int fruits = world.Fruits.Count(fruit => fruit.VineId == vine.Id);
                double decay = (active ? DecayRate(vine.Genes, night, fruits) : 0.05) * seconds;
                foreach (VineSquare square in vine.Squares)
                    square.Nutrients = Math.Max(0, square.Nutrients - square.Peak * decay);
                vine.Squares.RemoveAll(square => square.Nutrients <= MinimumSquareNutrients);
                if (world.WidgetSquare != 0 && !IsActive(vine))
                {
                    vine.Inactive = true;
                    continue;
                }

                vine.NextFruitIn = Math.Max(0, vine.NextFruitIn - seconds);
                if (vine.NextFruitIn == 0)
                    MakeFruit(world, vine, night);
            }

            world.Vines.RemoveAll(vine => vine.Squares.Count == 0 &&
                !world.Fruits.Any(fruit => fruit.VineId == vine.Id));
        }

        private static double DecayRate(Genome genes, double night, int fruits)
        {
            double rate = 0.005 + 0.024 * genes.Express(Gene.ColorDrift, 0, night);
            double protection = fruits >= 2 ? 1.45 + 3.6 * genes.Express(Gene.StressCapacity, 0, night) : 1;
            return rate / protection;
        }

        private static bool FreeFruitCell(World world, Cell point)
        {
            return world.Contains(point) && !world.IsOccupied(point) &&
                !world.Fruits.Any(fruit => fruit.Position.Equals(point));
        }

        private static void MakeFruit(World world, Vine vine, double night)
        {
            double capacity = vine.Genes.Express(Gene.StressCapacity, 0, night);
            vine.NextFruitIn = 2 + 4 * (1 - Numbers.Unit(capacity));
            double stock = vine.Nutrition;
            if (stock < FruitNutrients) return;

            Cell? point = null;
            foreach (VineSquare square in vine.Squares)
            {
                if (FreeFruitCell(world, square.Position)) { point = square.Position; break; }
            }
            if (!point.HasValue)
            {
                foreach (VineSquare square in vine.Squares)
                {
                    point = FreeNeighbor(square.Position, next => FreeFruitCell(world, next));
                    if (point.HasValue) break;
                }
            }
            if (!point.HasValue) return;

            double remaining = (stock - FruitNutrients) / stock;
            foreach (VineSquare square in vine.Squares) square.Nutrients *= remaining;
            vine.Squares.RemoveAll(square => square.Nutrients <= MinimumSquareNutrients);
            world.Fruits.Add(new Fruit(world.NewId(), vine, point.Value, 62 + 24 * capacity));
        }

        public static void Decompose(World world, Snake donor, Cell death, double night)
        {
            Vine vine = world.Vines.Where(existing =>
                (world.WidgetSquare == 0 || IsActive(existing)) &&
                (existing.Squares.Any(square => square.Position.Equals(death)) ||
                world.Fruits.Any(fruit => fruit.VineId == existing.Id && fruit.Position.Equals(death))))
                .OrderBy(existing => existing.Id).FirstOrDefault();
            var path = world.WidgetSquare != 0 ? DeathFootprint(world, donor, death) :
                donor.RecentPath.Where(world.Contains).Distinct().ToList();
            if (!path.Contains(death)) path.Add(death);

            double nutrients = 0.8 * donor.LargestLength + Math.Min(donor.Energy, donor.LargestLength);
            if (vine == null)
            {
                vine = new Vine(world.NewId(), donor.Genes.Copy(), donor.Skin);
                world.Vines.Add(vine);
            }
            else
            {
                double prior = vine.Nutrition;
                vine.FruitColor = Rgb.Mix(vine.FruitColor, donor.Skin, nutrients / (prior + nutrients));
                vine.Genes = ChooseSurvivingGenes(vine.Genes, donor.Genes, donor.Skin, night);
                if (path.All(point => vine.Squares.Any(square => square.Position.Equals(point))))
                {
                    Cell? edge = FreeNeighbor(death, point => world.Contains(point) &&
                        !vine.Squares.Any(square => square.Position.Equals(point)));
                    if (edge.HasValue) path.Add(edge.Value);
                }
            }

            double portion = nutrients / path.Count;
            foreach (Cell point in path)
            {
                VineSquare square = vine.Squares.Find(cell => cell.Position.Equals(point));
                if (square == null)
                    vine.Squares.Add(new VineSquare(point, donor.Skin, portion));
                else
                {
                    square.Tone = Rgb.Mix(square.Tone, donor.Skin, portion / (portion + square.Nutrients));
                    square.Nutrients += portion;
                    square.Peak += portion;
                }
            }
        }

        private static List<Cell> DeathFootprint(World world, Snake donor, Cell death)
        {
            var footprint = new List<Cell> { death };
            Cell behind = death.Move(Cell.Directions[(donor.Heading + 2) % Cell.Directions.Length]);
            Cell? tail = world.Contains(behind) ? (Cell?)behind : FreeNeighbor(death, world.Contains);
            if (tail.HasValue) footprint.Add(tail.Value);
            return footprint;
        }

        private static Cell? FreeNeighbor(Cell origin, Func<Cell, bool> available)
        {
            foreach (Cell direction in Cell.Directions)
            {
                Cell point = origin.Move(direction);
                if (available(point)) return point;
            }
            return null;
        }

        internal static Genome ChooseSurvivingGenes(Genome receiver, Genome donor, Rgb skin, double night)
        {
            var selected = receiver.Copy();
            var alternate = receiver.Copy();
            var habitat = new SurvivalHabitat(skin);
            double? lifespan = null;

            for (int index = 0; index < selected.Values.Length; index++)
            {
                if (selected.Values[index] == donor.Values[index]) continue;
                alternate.Values[index] = donor.Values[index];
                Gene gene = (Gene)index;
                bool preferDonor;
                double? donorLifespan = null;
                if (gene == Gene.Length13)
                    preferDonor = habitat.PrefersLength13Weight(selected, donor[gene], night);
                else if (gene == Gene.ColorDrift || gene == Gene.StressCapacity)
                    preferDonor = DecayRate(alternate, night, 2) < DecayRate(selected, night, 2);
                else
                {
                    if (!lifespan.HasValue)
                        lifespan = habitat.Lifespan(selected, night);
                    donorLifespan = habitat.Lifespan(alternate, night);
                    preferDonor = donorLifespan.Value > lifespan.Value;
                }

                if (preferDonor)
                {
                    selected.Values[index] = donor.Values[index];
                    lifespan = donorLifespan;
                }
                else
                    alternate.Values[index] = selected.Values[index];
            }

            return selected;
        }

        private sealed class SurvivalHabitat
        {
            private const uint VisitorReplaySeed = 24;
            private readonly ColorGrid hostile;
            private readonly ColorGrid varied;
            private readonly Rgb skin;

            public SurvivalHabitat(Rgb skin)
            {
                this.skin = skin;
                Rgb black = Rgb.FromBytes(4, 4, 10), white = Rgb.FromBytes(244, 244, 250);
                Rgb opposite = Rgb.Distance(black, skin) > Rgb.Distance(white, skin) ? black : white;
                var uniform = ColorGrid.Uniform(18, 10, opposite);
                var patches = ColorGrid.Uniform(18, 10, opposite);
                for (int y = 0; y < patches.Height; y++)
                    for (int x = 0; x < patches.Width; x++)
                        if (((x + 2 * y) % 11) < 3) patches[new Cell(x, y)] = skin;
                hostile = uniform;
                varied = patches;
            }

            public int Lifespan(Genome genes, double night)
            {
                // This third seed exercises a rare visitor arrival at ordinary gene values.
                return Replay(genes, night, 381) + Replay(genes, night, 953) + Replay(genes, night, VisitorReplaySeed);
            }

            public bool PrefersLength13Weight(Genome genes, double donorWeight, double night)
            {
                double receiverWeight = genes[Gene.Length13];
                double shorterWeight = genes[Gene.Length3] + genes[Gene.Length5] + genes[Gene.Length8];
                if (shorterWeight == 0 && receiverWeight > 0 && donorWeight > 0)
                    return false;

                int longLife = Replay(genes, night, VisitorReplaySeed, 13);
                double longBirthAdvantage = shorterWeight == 0
                    ? longLife - Replay(genes, night, VisitorReplaySeed, 5)
                    : WeightedLifeDifference(genes, Gene.Length3, 3, night, longLife) +
                      WeightedLifeDifference(genes, Gene.Length5, 5, night, longLife) +
                      WeightedLifeDifference(genes, Gene.Length8, 8, night, longLife);
                return donorWeight > receiverWeight ? longBirthAdvantage > 0 : longBirthAdvantage < 0;
            }

            private double WeightedLifeDifference(Genome genes, Gene weightGene, int length, double night, int longLife)
            {
                double weight = genes[weightGene];
                return weight == 0 ? 0 : weight * (longLife - Replay(genes, night, VisitorReplaySeed, length));
            }

            private int Replay(Genome genes, double night, uint seed, int? birthLength = null)
            {
                World world = World.BeginSurvivalReplay(varied, genes, skin, seed, night, birthLength);
                Snake founder = world.Snakes[0];
                int step = 0;
                while (step < 180 && founder.Body.Count > 0)
                {
                    world.ReplayStep((step / 14) % 3 == 0 ? varied : hostile, night, 0.2);
                    step++;
                }
                return step;
            }
        }
    }
}
