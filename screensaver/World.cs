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
        public const long ShowTarget = 10000001;
        public const int OpeningCast = 55;
        internal const double OverlapOpacity = 0.88;
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
        [DataMember(EmitDefaultValue = false)] public int WidgetSquare;
        [DataMember(EmitDefaultValue = false)] public bool WidgetFullScreen;
        [DataMember(EmitDefaultValue = false)] public Cell? WidgetAnchor;
        [DataMember(EmitDefaultValue = false)] public long NextLayerOrder;
        [DataMember(EmitDefaultValue = false)] public double GeneEditStep;
        [DataMember(EmitDefaultValue = false)] public Dictionary<Cell, SquareUse> SquareUses;
        [DataMember(EmitDefaultValue = false)] public Dictionary<long, Egg> Eggs;
        [DataMember(EmitDefaultValue = false)] public bool ShowActive;
        [DataMember(EmitDefaultValue = false)] public bool ShowComplete;
        [DataMember(EmitDefaultValue = false)] public long ShowMoves;
        [DataMember(EmitDefaultValue = false)] public int ShowCastSize;
        [DataMember(EmitDefaultValue = false)] public long ShowGoal;
        [DataMember(EmitDefaultValue = false)] public uint ShowSeed;
        [DataMember(EmitDefaultValue = false)] public double ShowSeconds;
        [DataMember(EmitDefaultValue = false)] public bool ShowNoRespawn;

        private ColorGrid previousDesktop;
        private DateTimeOffset? eggClock;
        private double? eggLatitude, eggLongitude;
        public ColorField Field { get; private set; }
        internal ManualSteering Manual { get; set; }
        internal Snake ManualSnake
        {
            get
            {
                if (Manual == null) return null;
                return Snakes.Find(snake => Manual.SnakeId.HasValue ? snake.Id == Manual.SnakeId : snake.IsResident);
            }
        }

        private World(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public World(int width, int height, uint seed, IDictionary<Gene, double> firstBirth, int widgetSquare = 0)
            : this(width, height)
        {
            var random = new SaverRandom(seed);
            WidgetSquare = widgetSquare;
            ShowSeed = seed;
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

        public void Step(ColorGrid desktop, double night, DateTimeOffset? now = null,
            double? latitude = null, double? longitude = null)
        {
            eggClock = WidgetSquare == 0 ? null : now;
            eggLatitude = latitude;
            eggLongitude = longitude;
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
            SampleField(desktop, night);
            var random = new SaverRandom(RandomState);
            World habitat = WidgetSquare == 0 ? null : this;
            if (habitat != null && NextLayerOrder == 0)
            {
                NextLayerOrder = 1;
                foreach (Snake snake in Snakes) PlaceInitialLayers(snake);
            }

            Snake[] active = Snakes.ToArray();
            CollectReadyEggs();
            Snake selected = ManualSnake;
            foreach (Snake snake in active)
            {
                if (snake.AgentToken != null) continue;
                ManualSteering steering = snake == selected ? Manual : null;
                long moves = snake.Moves;
                snake.Advance(Field, night, seconds, random,
                    point => Stamp(snake, point, night, random),
                    point => Arrive(snake, point, random, night), habitat, steering);
                if (ShowActive) ShowMoves += snake.Moves - moves;
                if (steering == null || !steering.Paused) LayEgg(snake);

                if (snake.Body.Count == 0)
                {
                    Cell death = snake.RecentPath[snake.RecentPath.Count - 1];
                    if (decompose && Contains(death))
                        Ecology.Decompose(this, snake, death, night);
                    Snakes.Remove(snake);
                    if (decompose && snake.IsResident && !ShowNoRespawn)
                    {
                        AddResident(Genome.Roll(random, null), Rgb.StartingGreen, random);
                    }
                }
                else if (!snake.IsResident && !snake.Entering && !snake.VisibleOn(Field))
                    Snakes.Remove(snake);
            }
            if (selected != null && !Snakes.Contains(selected)) Manual = null;

            if (decompose)
                Ecology.Advance(this, seconds, night);
            TryVisitor(random, night, seconds);
            RandomState = random.State;
            Ticks++;
            CheckShowComplete();
        }

        internal void MoveAgent(Snake snake, int heading, bool bypass, double night)
        {
            var random = new SaverRandom(RandomState);
            var control = new ManualSteering { Heading = heading, Paused = false, Boost = bypass, SingleMove = true };
            long moves = snake.Moves;
            snake.Advance(Field, night, StepSeconds, random,
                point => Stamp(snake, point, night, random),
                point => Arrive(snake, point, random, night), this, control);
            if (ShowActive) ShowMoves += snake.Moves - moves;
            LayEgg(snake);
            RandomState = random.State;
            if (snake.Body.Count == 0)
            {
                Cell death = snake.RecentPath[snake.RecentPath.Count - 1];
                if (Contains(death)) Ecology.Decompose(this, snake, death, night);
                Snakes.Remove(snake);
            }
            else if (!snake.Entering && !snake.VisibleOn(Field)) Snakes.Remove(snake);
            CheckShowComplete();
        }

        private void CheckShowComplete()
        {
            if (!ShowActive) return;
            if (ShowMoves >= ShowGoal || (ShowNoRespawn && Snakes.Count == 0)) ShowComplete = true;
        }

        internal void StartShow(int cast, bool noRespawn = false)
        {
            if (WidgetSquare == 0)
                throw new InvalidOperationException("The endurance show needs the widget habitat.");
            if (cast < 1 || cast > ShowTarget)
                throw new ArgumentOutOfRangeException("cast");
            if (Snakes.Count != 1 || Vines.Count != 0 || Fruits.Count != 0)
                throw new InvalidOperationException("The endurance show starts from a fresh world.");

            ShowActive = true;
            ShowComplete = false;
            ShowNoRespawn = noRespawn;
            ShowMoves = 0;
            ShowCastSize = cast;
            ShowGoal = ShowTarget;
#if ZNAKE_SMOKE
            long goal;
            if (Int64.TryParse(Environment.GetEnvironmentVariable("ZNAKE_SHOW_TARGET"), out goal) && goal > 0)
                ShowGoal = goal;
#endif
            var random = new SaverRandom(RandomState);
            var used = new HashSet<int>();
            int slot = EntrySlotOf(Snakes[0]);
            if (slot >= 0) used.Add(slot);
            while (Snakes.Count < cast) AddOpeningSnake(random, used);
            RandomState = random.State;
        }

        internal int EntrySlotOf(Snake snake)
        {
            if (!snake.Entering || snake.Body.Count == 0) return -1;
            Cell head = snake.Body[0];
            if (head.X < 0) return EntrySlot(0, head.Y);
            if (head.Y < 0) return EntrySlot(1, head.X);
            if (head.X >= Width) return EntrySlot(2, head.Y);
            if (head.Y >= Height) return EntrySlot(3, head.X);
            return -1;
        }

        private int EntrySlot(int heading, int along)
        {
            return heading * Math.Max(Width, Height) + along;
        }

        private Snake AddOpeningSnake(SaverRandom random, HashSet<int> used)
        {
            Genome genes = Genome.Roll(random, null);
            Rgb skin = Rgb.Mix(Rgb.StartingGreen, new Rgb(random.Next(), random.Next(), random.Next()), 0.55);
            for (int attempt = 0; attempt < 256; attempt++)
            {
                int heading = random.Index(Cell.Directions.Length);
                int along = random.Index(heading % 2 == 0 ? Height : Width);
                if (!used.Add(EntrySlot(heading, along))) continue;
                Snake snake = AddEnteringSnake(random, false, genes, skin, null, heading, along);
                snake.StaysInArena = true;
                return snake;
            }
            throw new InvalidOperationException("The show cannot find another distinct entry position.");
        }

        private void Arrive(Snake snake, Cell point, SaverRandom random, double night)
        {
            EatFruit(snake, point, random, night);
            Egg egg;
            if (Eggs != null && Eggs.TryGetValue(snake.Id, out egg) && EggReady(egg) && point.Equals(egg.Position))
                Hatch(snake.Id, egg, snake.Heading);
        }

        internal bool BiteTail(double night, bool browserSkin)
        {
            Snake parent = ManualSnake;
            if (WidgetSquare == 0 || parent == null || parent.Body.Count < 2)
                return false;
            Cell target = parent.Body[0].Move(Cell.Directions[parent.ManualHeading(Manual)]);
            if (!Contains(target)) return false;
            int cut = parent.Body.FindLastIndex(point => point.Equals(target));
            if (cut < 1) return false;
            if (cut == parent.Body.Count - 1)
            {
                parent.EatTail(night);
                Manual.TailPass = null;
                Manual.TailPassClone = null;
            }
            else
            {
                Manual.TailPassClone = SplitTail(parent, cut, night, browserSkin);
                Manual.TailPass = target;
            }
            Manual.Boost = false;
            Manual.Paused = false;
            return true;
        }

        private Snake SplitTail(Snake parent, int cut, double night, bool browserSkin)
        {
            int remaining = cut + 1;
            Cell tip = parent.Body[parent.Body.Count - 1];
            int layers = browserSkin || parent.SplitTailOpacity.HasValue ? 1 : SegmentCount(tip);
            double oldTip = parent.SplitTailOpacity ?? (browserSkin ? parent.BrowserTailOpacity(night) : parent.TailOpacity(night));
            if (layers > 1) oldTip *= OverlapOpacity / layers;
            double half = browserSkin ? 0.325 : 0.5;
            List<Cell> detached = parent.Body.GetRange(cut, parent.Body.Count - cut);
            detached.Reverse();
            parent.Body.RemoveRange(remaining, parent.Body.Count - remaining);
            if (parent.LayerOrder != null)
                parent.LayerOrder.RemoveRange(remaining, parent.LayerOrder.Count - remaining);
            parent.SplitTailOpacity = half;

            int limit = remaining - 1;
            bool multiple = detached.Count > limit;
            var random = new SaverRandom(RandomState);
            Snake clone = null;
            for (int start = 0; start < detached.Count; start += limit)
            {
                List<Cell> body = detached.GetRange(start, Math.Min(limit, detached.Count - start));
                Cell from = start + 1 < detached.Count ? detached[start + 1] : detached[start];
                Cell to = start + 1 < detached.Count ? detached[start] : detached[start - 1];
                Cell towardHead = new Cell(to.X - from.X, to.Y - from.Y);
                int heading = Array.IndexOf(Cell.Directions, towardHead);
                Genome genes = parent.Genes.Copy();
                if (multiple) genes.MutateSplit(random, night);
                clone = new Snake(NewId(), false, genes, parent.Skin, body, heading < 0 ? parent.Heading : heading)
                {
                    BirthColor = parent.BirthColor,
                    MutatedChannels = parent.MutatedChannels,
                    Speed = parent.Speed,
                    SplitHeadOpacity = start == 0 ? (double?)oldTip : null,
                    SplitTailOpacity = start + body.Count == detached.Count ? (double?)half : null
                };
                if (NextLayerOrder > 0) PlaceInitialLayers(clone);
                Snakes.Add(clone);
            }
            RandomState = random.State;
            return clone;
        }

        private void LayEgg(Snake snake)
        {
            if (!eggClock.HasValue) return;
            if (snake.Body.Count < 21)
                snake.LaidEggAtLength = false;
            if (snake.Body.Count < 21 || snake.LaidEggAtLength ||
                snake.Entering || snake.Exiting || HasEgg(snake.Id))
                return;
            Cell nest = snake.Body[snake.Body.Count - 1];
            if (!Contains(nest) || (Eggs != null && Eggs.Values.Any(egg => egg.Position.Equals(nest))))
                return;
            if (Eggs == null) Eggs = new Dictionary<long, Egg>();
            Eggs.Add(snake.Id, new Egg
            {
                Position = nest,
                DueUtcTicks = Daylight.NextWidgetFullMoon(eggClock.Value, eggLatitude, eggLongitude).UtcDateTime.Ticks,
                Parent = SpeciesTemplate.FromSnake(snake)
            });
            snake.LaidEggAtLength = true;
        }

        internal bool HasEgg(long parent) { return Eggs != null && Eggs.ContainsKey(parent); }

        private bool EggReady(Egg egg)
        {
            return eggClock.HasValue && eggClock.Value.UtcDateTime.Ticks >= egg.DueUtcTicks && Contains(egg.Position);
        }

        internal int? NestHeading(Snake parent)
        {
            Egg egg;
            return Eggs != null && Eggs.TryGetValue(parent.Id, out egg) && EggReady(egg)
                ? egg.ReturnHeading(this, parent.Body[0], parent.Heading) : null;
        }

        private void CollectReadyEggs()
        {
            if (Eggs == null || Eggs.Count == 0) return;
            foreach (var entry in Eggs.Where(item => EggReady(item.Value)).ToArray())
            {
                Egg egg = entry.Value;
                Snake parent = Snakes.Find(snake => snake.Id == entry.Key);
                if (parent == null || parent.Body[0].Equals(egg.Position))
                    Hatch(entry.Key, egg, parent == null ? 0 : parent.Heading);
            }
        }

        private void Hatch(long parentId, Egg egg, int heading)
        {
            var baby = new Snake(NewId(), false, egg.Parent.Genes.Copy(), egg.Parent.BirthColor,
                new List<Cell> { egg.Position }, heading) { MutatedChannels = egg.Parent.Channels };
            PlaceInitialLayers(baby);
            Snakes.Add(baby);
            Eggs.Remove(parentId);
        }

        private void EatFruit(Snake snake, Cell point, SaverRandom random, double night)
        {
            if (!Ecology.Eat(this, snake, point, night) || WidgetSquare == 0 ||
                snake.LastFruitWasAnti || random.Next() >= 0.001) return;
            snake.Genes[Gene.SelfFeeding] = Numbers.Finite(snake.Genes[Gene.SelfFeeding] + random.Between(-0.001, 0.001));
        }

        internal void SampleField(ColorGrid desktop, double night)
        {
            Field = new ColorField(desktop, previousDesktop, Imprints);
            Ecology.TintField(this, Field, night);
            previousDesktop = desktop;
        }

        internal double SquareStrength(IList<Cell> body)
        {
            if (SquareUses == null || SquareUses.Count == 0) return 1;
            double total = 0;
            int count = 0;
            foreach (Cell cell in body)
            {
                if (!Contains(cell)) continue;
                total += SquareStrengthAt(cell);
                count++;
            }
            return count == 0 ? 0 : total / count;
        }

        internal double SquareStrengthAt(Cell cell)
        {
            SquareUse use;
            return SquareUses != null && SquareUses.TryGetValue(cell, out use) ? use.Strength(Ticks) : 1;
        }

        internal void ConsumeSquares(IList<Cell> body, double recovery)
        {
            if (SquareUses == null) SquareUses = new Dictionary<Cell, SquareUse>();
            foreach (Cell cell in body.Distinct())
            {
                if (!Contains(cell)) continue;
                SquareUse use;
                SquareUses.TryGetValue(cell, out use);
                SquareUses[cell] = use.Consume(Ticks, recovery);
            }
        }

        internal bool TryReframe(int width, int height, Cell shift)
        {
            if (width == 1 && height == 1) return false;
            if (width == Width && height == Height && shift.X == 0 && shift.Y == 0) return true;

            Func<Cell, bool> fits = point => point.X >= 0 && point.X < width && point.Y >= 0 && point.Y < height;
            if (Snakes.Any(snake => snake.Body.Any(point => Contains(point) && !fits(point.Move(shift)))))
                return false;
            if (Eggs != null && Eggs.Values.Any(egg => Contains(egg.Position) && !fits(egg.Position.Move(shift))))
                return false;

            Width = width;
            Height = height;
            previousDesktop = null;
            Field = null;
            foreach (Snake snake in Snakes)
            {
                for (int index = 0; index < snake.Body.Count; index++)
                    snake.Body[index] = snake.Body[index].Move(shift);
                for (int index = 0; index < snake.RecentPath.Count; index++)
                    snake.RecentPath[index] = snake.RecentPath[index].Move(shift);
            }
            foreach (Vine vine in Vines)
            {
                foreach (VineSquare square in vine.Squares)
                    square.Position = square.Position.Move(shift);
                vine.Squares.RemoveAll(square => !Contains(square.Position));
                if (WidgetSquare != 0 && !Ecology.IsActive(vine)) vine.Inactive = true;
            }
            foreach (Fruit fruit in Fruits)
                fruit.Position = fruit.Position.Move(shift);
            Fruits.RemoveAll(fruit => !Contains(fruit.Position));
            if (Eggs != null)
                foreach (Egg egg in Eggs.Values) egg.Position = egg.Position.Move(shift);
            if (Manual != null && Manual.TailPass.HasValue)
                Manual.TailPass = Manual.TailPass.Value.Move(shift);
            Imprints = Imprints.Where(entry => Contains(entry.Key.Move(shift)))
                .ToDictionary(entry => entry.Key.Move(shift), entry => entry.Value);
            if (SquareUses != null)
                SquareUses = SquareUses.Where(entry => Contains(entry.Key.Move(shift)))
                    .ToDictionary(entry => entry.Key.Move(shift), entry => entry.Value);
            return true;
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
            double weight = snake.ImprintWeight(night);
            Rgb tone = snake.Skin;
            if (WidgetSquare != 0)
            {
                Rgb background = Field.At(point);
                int roll = random.Index(111);
                int channels = roll < 100 ? 1 : roll < 110 ? 2 : 3;
                int first = random.Index(3);
                double step = 1 / (255 * weight);
                tone = new Rgb(
                    MarkChannel(background.R, tone.R, step, (3 - first) % 3 < channels),
                    MarkChannel(background.G, tone.G, step, (4 - first) % 3 < channels),
                    MarkChannel(background.B, tone.B, step, (5 - first) % 3 < channels));
                if (tone.R == background.R && tone.G == background.G && tone.B == background.B) return;
            }
            snake.ActivateGene(Gene.Imprint);
            Imprints[point] = new Tint(tone, weight).Over(existing);
        }

        private static double MarkChannel(double background, double skin, double step, bool selected)
        {
            return selected ? Numbers.Unit(background + Math.Sign(skin - background) * step) : background;
        }

        public bool IsOccupied(Cell point)
        {
            foreach (Snake other in Snakes)
                if (other.Body.Contains(point)) return true;
            return false;
        }

        internal long NewLayerOrder() { return NextLayerOrder++; }

        private void PlaceInitialLayers(Snake snake)
        {
            snake.LayerOrder = new List<long>(snake.Body.Count);
            for (int index = 0; index < snake.Body.Count; index++) snake.LayerOrder.Add(0);
            for (int index = snake.Body.Count - 1; index >= 0; index--)
                snake.LayerOrder[index] = NewLayerOrder();
        }

        private int SegmentCount(Cell position)
        {
            int count = 0;
            foreach (Snake other in Snakes)
                foreach (Cell segment in other.Body)
                    if (segment.Equals(position)) count++;
            return count;
        }

        internal int JumpHeight(Snake snake, Cell destination, int ignoredLayers = 0)
        {
            int landingLayer = SegmentCount(destination) - ignoredLayers;
            if (landingLayer <= 0) return 0;
            return Math.Max(1, landingLayer - HeadLayer(snake));
        }

        internal int HeadLayer(Snake snake)
        {
            Cell origin = snake.Body[0];
            long headOrder = snake.LayerOrder[0];
            int launchLayer = 0;
            foreach (Snake other in Snakes)
                for (int index = 0; index < other.Body.Count; index++)
                    if (other.Body[index].Equals(origin) && other.LayerOrder[index] < headOrder)
                        launchLayer++;
            return launchLayer;
        }

        private Snake AddResident(Genome genes, Rgb skin, SaverRandom random, int? birthLength = null)
        {
            if (WidgetSquare != 0) return AddEnteringSnake(random, true, genes, skin, null);
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
            if (WidgetSquare != 0) resident.RollColorMutation(random);
            if (NextLayerOrder > 0) PlaceInitialLayers(resident);
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

        internal Snake Birth(SpeciesTemplate template, bool staysInArena = false)
        {
            var random = new SaverRandom(RandomState);
            Snake snake = AddVisitor(random, template);
            snake.StaysInArena = staysInArena;
            RandomState = random.State;
            return snake;
        }

        internal Snake AddVisitor(SaverRandom random, SpeciesTemplate template = null)
        {
            if (WidgetSquare != 0)
            {
                Genome inherited = template == null ? Genome.Roll(random, null) : template.Genes.Copy();
                Rgb color = template == null
                    ? Rgb.Mix(Rgb.StartingGreen, new Rgb(random.Next(), random.Next(), random.Next()), 0.55)
                    : template.BirthColor;
                return AddEnteringSnake(random, false, inherited, color, template == null ? null : (ColorChannels?)template.Channels);
            }
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

            Genome genes = template == null ? Genome.Roll(random, null) : template.Genes.Copy();
            int length = genes.BirthLength(random);
            Cell forward = Cell.Directions[side];
            var body = new List<Cell>(length);
            for (int index = 0; index < length; index++)
                body.Add(new Cell(head.X - forward.X * index, head.Y - forward.Y * index));
            Rgb skin = template == null ?
                Rgb.Mix(Rgb.StartingGreen, new Rgb(random.Next(), random.Next(), random.Next()), 0.55) :
                template.BirthColor;
            var visitor = new Snake(NewId(), false, genes, skin, body, side);
            if (template != null) visitor.MutatedChannels = template.Channels;
            else if (WidgetSquare != 0) visitor.RollColorMutation(random);
            if (NextLayerOrder > 0) PlaceInitialLayers(visitor);
            Snakes.Add(visitor);
            return visitor;
        }

        private Snake AddEnteringSnake(SaverRandom random, bool resident, Genome genes, Rgb skin,
            ColorChannels? channels, int heading = -1, int along = -1)
        {
            if (heading < 0) heading = random.Index(Cell.Directions.Length);
            if (along < 0) along = random.Index(heading % 2 == 0 ? Height : Width);
            Cell head = heading == 0 ? new Cell(-1, along) : heading == 1 ? new Cell(along, -1) :
                heading == 2 ? new Cell(Width, along) : new Cell(along, Height);
            Cell direction = Cell.Directions[heading];
            int length = genes.BirthLength(random);
            var body = new List<Cell>(length);
            for (int index = 0; index < length; index++)
                body.Add(new Cell(head.X - index * direction.X, head.Y - index * direction.Y));
            var snake = new Snake(NewId(), resident, genes, skin, body, heading)
            {
                Entering = true, Speed = 1, MutatedChannels = channels
            };
            if (!channels.HasValue) snake.RollColorMutation(random);
            if (NextLayerOrder > 0) PlaceInitialLayers(snake);
            Snakes.Add(snake);
            return snake;
        }

        internal Cell? FindVisibleFruit(Cell head, double radius, Snake observer = null, bool repel = false)
        {
            if (radius <= 0) return null;
            double nearest = Double.PositiveInfinity;
            Cell? result = null;
            foreach (Fruit fruit in Fruits)
            {
                bool repellent = observer != null && fruit.Antifruit &&
                    LightResponse.Sense(observer.Skin, fruit.Tone.SensitiveChannels(observer.Skin, observer.Sensitivity)).Feed == 0;
                if (repellent != repel) continue;
                Cell point = fruit.Position;
                int distance = FoodDistance(head, point);
                if (distance < nearest && (radius >= 100 || distance <= radius))
                {
                    nearest = distance;
                    result = point;
                }
            }
            return result;
        }

        internal static int FoodDistance(Cell first, Cell second)
        {
            return Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y);
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
            if (Eggs != null)
                foreach (Egg egg in Eggs.Values) egg.Position = Map(egg.Position, oldWidth, oldHeight);

            var resized = new Dictionary<Cell, Tint>();
            foreach (var imprint in Imprints)
            {
                Cell next = Map(imprint.Key, oldWidth, oldHeight);
                Tint prior;
                resized.TryGetValue(next, out prior);
                resized[next] = imprint.Value.Over(prior);
            }
            Imprints = resized;
            if (SquareUses != null)
                foreach (Cell cell in SquareUses.Keys.Where(cell => !Contains(cell)).ToArray()) SquareUses.Remove(cell);
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
