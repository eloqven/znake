using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Znake
{
    [DataContract]
    internal sealed class World
    {
        public const double StepSeconds = 0.08;
        private const double ImprintHalfLife = 106;
        [DataMember] public int Width;
        [DataMember] public int Height;
        [DataMember] public uint RandomState;
        [DataMember] public long Ticks;
        [DataMember] public long NextId = 1;
        [DataMember] public List<Snake> Snakes = new List<Snake>();
        [DataMember] public List<Vine> Vines = new List<Vine>();
        [DataMember] public List<Fruit> Fruits = new List<Fruit>();
        [DataMember] public Dictionary<Cell, Tint> Imprints = new Dictionary<Cell, Tint>();

        private ColorGrid previousDesktop;
        public ColorField Field { get; private set; }

        private World(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public World(int width, int height, uint seed, IDictionary<Gene, double> firstBirth)
            : this(width, height)
        {
            var random = new SaverRandom(seed);
            AddResident(Genome.Roll(random, firstBirth), Rgb.StartingGreen, random);
            RandomState = random.State;
        }

        internal static World BeginSurvivalReplay(ColorGrid habitat, Genome genes, Rgb skin, uint seed,
            double night, int? birthLength = null)
        {
            var world = new World(habitat.Width, habitat.Height);
            var random = new SaverRandom(seed);
            if (birthLength.HasValue)
                random.Next(); // Keep the continuation aligned when the birth draw is forced.
            Snake resident = world.AddResident(genes, skin, random, birthLength);
            resident.Stress = 0.75 * resident.Capacity(night);
            world.RandomState = random.State;
            return world;
        }

        public long NewId() { return NextId++; }

        public bool Contains(Cell point) { return point.X >= 0 && point.X < Width && point.Y >= 0 && point.Y < Height; }

        public void Step(ColorGrid desktop, double night)
        {
            Advance(desktop, night, StepSeconds, true);
        }

        internal void ReplayStep(ColorGrid desktop, double night, double seconds)
        {
            // Score how long the founder survives without starting a new vine merge inside the replay.
            Advance(desktop, night, seconds, false);
        }

        private void Advance(ColorGrid desktop, double night, double seconds, bool decompose)
        {
            Resize(desktop.Width, desktop.Height);
            FadeImprints(seconds);
            Field = new ColorField(desktop, previousDesktop, Imprints);
            Ecology.TintField(this, Field, night);
            previousDesktop = desktop;
            var random = new SaverRandom(RandomState);

            Snake[] active = Snakes.ToArray();
            foreach (Snake snake in active)
            {
                snake.Advance(Field, night, seconds, random,
                    point => Stamp(snake, point, night, random),
                    point => Ecology.Eat(this, snake, point));

                if (snake.Body.Count == 0)
                {
                    Cell death = snake.RecentPath[snake.RecentPath.Count - 1];
                    if (decompose && Contains(death))
                        Ecology.Decompose(this, snake, death, night);
                    Snakes.Remove(snake);
                    if (decompose && snake.IsResident)
                        AddResident(Genome.Roll(random, null), Rgb.StartingGreen, random);
                }
                else if (!snake.IsResident && !snake.VisibleOn(Field))
                    Snakes.Remove(snake);
            }

            if (decompose)
                Ecology.Advance(this, seconds, night);
            TryVisitor(random, night, seconds);
            RandomState = random.State;
            Ticks++;
        }

        public void FadeImprints(double seconds)
        {
            if (seconds <= 0 || Imprints.Count == 0) return;
            double fade = Math.Pow(0.5, seconds / ImprintHalfLife);
            foreach (Cell point in Imprints.Keys.ToArray())
            {
                Tint fading = Imprints[point];
                fading.Alpha *= fade;
                if (fading.Alpha < 0.001)
                    Imprints.Remove(point);
                else
                    Imprints[point] = fading;
            }
        }

        internal void Stamp(Snake snake, Cell point, double night, SaverRandom random)
        {
            if (!Contains(point) || random.Next() >= 0.09 * snake.Express(Gene.Imprint, night))
                return;
            Tint existing;
            Imprints.TryGetValue(point, out existing);
            Imprints[point] = new Tint(snake.Skin, snake.ImprintWeight(night)).Over(existing);
        }

        public bool IsOccupied(Cell point)
        {
            foreach (Snake other in Snakes)
                if (other.Body.Contains(point)) return true;
            return false;
        }

        private Snake AddResident(Genome genes, Rgb skin, SaverRandom random, int? birthLength = null)
        {
            int length = Math.Min(Width * Height, birthLength ?? genes.BirthLength(random));
            var body = new List<Cell>(length);
            if (Width >= length)
            {
                int left = (Width - length) / 2;
                int row = Height / 2;
                for (int index = length - 1; index >= 0; index--)
                    body.Add(new Cell(left + index, row));
            }
            else
            {
                for (int index = length - 1; index >= 0; index--)
                {
                    int row = index / Width, column = index % Width;
                    body.Add(new Cell(row % 2 == 0 ? column : Width - 1 - column, row));
                }
            }
            var resident = new Snake(NewId(), true, genes, skin, body, 0);
            Snakes.Add(resident);
            return resident;
        }

        public static double VisitorRate(Snake resident, double night)
        {
            return resident.Express(Gene.VisitorArrival, night) * 0.0015;
        }

        internal void TryVisitor(SaverRandom random, double night, double seconds)
        {
            Snake resident = Snakes.Find(snake => snake.IsResident);
            if (resident != null && random.Next() < 1 - Math.Exp(-seconds * VisitorRate(resident, night)))
                AddVisitor(random);
        }

        internal Snake AddVisitor(SaverRandom random)
        {
            int side = random.Index(4);
            int along = random.Index(side % 2 == 0 ? Height : Width);
            Cell head;
            switch (side)
            {
                case 0: head = new Cell(0, along); break;
                case 1: head = new Cell(along, 0); break;
                case 2: head = new Cell(Width - 1, along); break;
                default: head = new Cell(along, Height - 1); break;
            }

            Genome genes = Genome.Roll(random, null);
            int length = genes.BirthLength(random);
            Cell forward = Cell.Directions[side];
            var body = new List<Cell>(length);
            for (int index = 0; index < length; index++)
                body.Add(new Cell(head.X - forward.X * index, head.Y - forward.Y * index));
            var skin = Rgb.Mix(Rgb.StartingGreen, new Rgb(random.Next(), random.Next(), random.Next()), 0.55);
            var visitor = new Snake(NewId(), false, genes, skin, body, side);
            Snakes.Add(visitor);
            return visitor;
        }

        private void Resize(int width, int height)
        {
            if (Width == width && Height == height) return;
            int oldWidth = Width, oldHeight = Height;
            Width = width;
            Height = height;
            previousDesktop = null;
            foreach (Snake snake in Snakes)
            {
                Cell anchor = snake.IsResident ? snake.Body[0] : snake.Body.Find(point =>
                    point.X >= 0 && point.X < oldWidth && point.Y >= 0 && point.Y < oldHeight);
                Cell target = Map(anchor, oldWidth, oldHeight);
                int dx = target.X - anchor.X, dy = target.Y - anchor.Y;
                if (snake.IsResident)
                {
                    dx = FitShift(snake.Body.Min(point => point.X), snake.Body.Max(point => point.X), width, dx);
                    dy = FitShift(snake.Body.Min(point => point.Y), snake.Body.Max(point => point.Y), height, dy);
                }
                var shift = new Cell(dx, dy);
                for (int index = 0; index < snake.Body.Count; index++)
                {
                    Cell moved = snake.Body[index].Move(shift);
                    snake.Body[index] = snake.IsResident ? Clip(moved) : moved;
                }
                for (int index = 0; index < snake.RecentPath.Count; index++)
                    snake.RecentPath[index] = Clip(snake.RecentPath[index].Move(shift));
            }
            foreach (Vine vine in Vines)
            {
                foreach (VineSquare square in vine.Squares)
                    square.Position = Map(square.Position, oldWidth, oldHeight);
            }
            foreach (Fruit fruit in Fruits)
                fruit.Position = Map(fruit.Position, oldWidth, oldHeight);

            var resized = new Dictionary<Cell, Tint>();
            foreach (var imprint in Imprints)
            {
                Cell next = Map(imprint.Key, oldWidth, oldHeight);
                Tint prior;
                resized.TryGetValue(next, out prior);
                resized[next] = imprint.Value.Over(prior);
            }
            Imprints = resized;
        }

        private Cell Map(Cell point, int oldWidth, int oldHeight)
        {
            return Clip(new Cell((int)((long)point.X * Width / oldWidth),
                (int)((long)point.Y * Height / oldHeight)));
        }

        private static int FitShift(int min, int max, int size, int target)
        {
            return max - min < size ? Math.Max(-min, Math.Min(target, size - 1 - max)) : target;
        }

        private Cell Clip(Cell point)
        {
            return new Cell(Math.Max(0, Math.Min(Width - 1, point.X)),
                Math.Max(0, Math.Min(Height - 1, point.Y)));
        }
    }
}
