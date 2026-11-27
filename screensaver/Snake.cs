using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Znake
{
    internal enum Affect { Neutral, Calm, Stressed }

    internal sealed class ManualSteering
    {
        public long? SnakeId;
        public int Heading;
        public bool Paused = true;
        public bool Boost;
        public bool SingleMove;
        public Cell? TailPass;
        public Snake TailPassClone;
    }

    [DataContract]
    internal sealed class Snake
    {
        private ulong usedGenes, activeGenes, inhibitedGenes;
        private const double ResponseTolerance = 1e-9;
        internal const double IdleColorInterval = 2.33;
        internal const double ChronicStressLimit = 12;
        private const double ChronicStressStart = 2;
        private const long VisibleStressMiss = 5;

        [DataMember] public long Id;
        [DataMember] public bool IsResident;
        [DataMember] public Genome Genes;
        [DataMember] public double[] Learned = new double[(int)Gene.Count];

        [OnDeserialized]
        private void RestoreNewGenes(StreamingContext context)
        {
            if (Learned != null && Learned.Length >= (int)Gene.SelfFeeding && Learned.Length < (int)Gene.Count)
                Array.Resize(ref Learned, (int)Gene.Count);
            if (!BirthColor.HasValue) BirthColor = IsResident ? Rgb.StartingGreen : Skin;
            if (FruitPowerUntil == null && previousFruitSurgeUntil > Moves)
                FruitPowerUntil = new List<long> { previousFruitSurgeUntil };
            previousFruitSurgeUntil = 0;
        }
        [DataMember] public List<Cell> Body;
        [DataMember(EmitDefaultValue = false)] public List<long> LayerOrder;
        [DataMember] public List<Cell> RecentPath;
        [DataMember] public int LargestLength;
        [DataMember] public int Heading;
        [DataMember] public Rgb Skin;
        [DataMember(EmitDefaultValue = false)] public Rgb? BirthColor;
        [DataMember(EmitDefaultValue = false)] public ColorChannels? MutatedChannels;
        [DataMember] public double Energy;
        [DataMember] public bool Saving;
        [DataMember] public int PendingGrowth;
        [DataMember] public double Stress;
        [DataMember(EmitDefaultValue = false)] public double? SmoothedStress;
        [DataMember] public double SaturatedFor;
        [DataMember] public double Assimilation;
        [DataMember] public Rgb StressColor;
        [DataMember] public bool HasStressColor;
        [DataMember] public double DifferentFor;
        [DataMember] public Rgb DriftColor;
        [DataMember] public double Speed;
        [DataMember] public double Motion;
        [DataMember] public double SprintRemaining;
        [DataMember] public bool Exiting;
        [DataMember(EmitDefaultValue = false)] public bool Entering;
        [DataMember(EmitDefaultValue = false)] public bool StaysInArena;
        [DataMember(EmitDefaultValue = false)] public bool LaidEggAtLength;
        [DataMember(EmitDefaultValue = false)] public double? SplitHeadOpacity;
        [DataMember(EmitDefaultValue = false)] public double? SplitTailOpacity;
        [DataMember(EmitDefaultValue = false)] public double? IdleColorSeconds;
        [DataMember(EmitDefaultValue = false)] public long ConsecutiveMissedMoves;
        [DataMember(EmitDefaultValue = false)] public double HighStressSeconds;
        [DataMember(EmitDefaultValue = false)] public double RecentStressSpike;
        [DataMember(EmitDefaultValue = false)] public long? LastFruitTick;
        [DataMember(EmitDefaultValue = false)] public bool LastFruitWasAnti;
        [DataMember(EmitDefaultValue = false)] public long Moves;
        [DataMember(Name = "FruitSurgeUntil", EmitDefaultValue = false)] private long previousFruitSurgeUntil;
        [DataMember(EmitDefaultValue = false)] public List<long> FruitPowerUntil;
        [DataMember(EmitDefaultValue = false)] public List<long> FruitVisionUntil;
        [DataMember(EmitDefaultValue = false)] public List<long> AntiPowerUntil;
        [DataMember(EmitDefaultValue = false)] public List<long> AntiVisionUntil;
        [DataMember(EmitDefaultValue = false)] public string AgentToken;
        [DataMember(EmitDefaultValue = false)] public string LastAgentMove;
        [DataMember] public Affect Mood = Affect.Calm;
        [DataMember] public Gene LastMovingGene = Gene.Speed;

        public Snake(long id, bool resident, Genome genes, Rgb skin, List<Cell> body, int heading)
        {
            Id = id;
            IsResident = resident;
            Genes = genes;
            Skin = skin;
            BirthColor = skin;
            Body = body;
            Heading = heading;
            LargestLength = body.Count;
            RecentPath = new List<Cell>(body);
            RecentPath.Reverse();
        }

        internal ColorChannels Sensitivity
        {
            get { return MutatedChannels ?? ColorChannels.All; }
        }

        internal void RollColorMutation(SaverRandom random)
        {
            double roll = random.Next();
            if (roll < 0.000000001)
                MutatedChannels = ColorChannels.None;
            else if (roll < 0.000001001)
                MutatedChannels = (ColorChannels)(1 << random.Index(3));
            else if (roll < 0.001001001)
                MutatedChannels = ColorChannels.All ^ (ColorChannels)(1 << random.Index(3));
        }

        public double Express(Gene gene, double night)
        {
            usedGenes |= 1UL << (int)gene;
            double value = EffectiveGene(gene, night);
            if (gene > Gene.Length13 && gene != Gene.LearningOffset &&
                gene != Gene.NightBoost && gene != Gene.VisionRadius && gene != Gene.FruitSurge)
            {
                usedGenes |= 1UL << (int)Gene.NightBoost;
                if (night > 0 && value > 0 && Genes[Gene.NightBoost] > 0)
                    ActivateGene(Gene.NightBoost);
            }
            return value;
        }

        internal double EffectiveGene(Gene gene, double night)
        {
            if (gene == Gene.VisionRadius)
            {
                double radius = Math.Max(0, Genes[gene]);
                return radius == 0 ? 0 : Math.Min(100, radius * FruitMultiplier(FruitVisionUntil, AntiVisionUntil));
            }
            if (gene <= Gene.Length13 || gene == Gene.LearningOffset ||
                gene == Gene.NightBoost || gene == Gene.FruitSurge)
                return Genes[gene];
            return Genes.Express(gene, Learned[(int)gene], night);
        }

        internal void ActivateGene(Gene gene) { activeGenes |= 1UL << (int)gene; }
        private void InhibitGene(Gene gene) { inhibitedGenes |= 1UL << (int)gene; }

        internal int GeneActivity(Gene gene)
        {
            ulong bit = 1UL << (int)gene;
            if ((activeGenes & bit) != 0) return 3;
            if ((inhibitedGenes & bit) != 0) return 2;
            return (usedGenes & bit) != 0 ? 1 : 0;
        }

        public double Capacity(double night, bool track = true)
        {
            return 0.8 + 0.8 * (track ? Express(Gene.StressCapacity, night) : EffectiveGene(Gene.StressCapacity, night));
        }

        public double TailDwell(double night, bool track = true)
        {
            return 1.25 + 9 * (track ? Express(Gene.TailDwell, night) : EffectiveGene(Gene.TailDwell, night));
        }
        public double SprintDuration(double night) { return 0.25 + 2.6 * Express(Gene.SprintDuration, night); }
        public double SprintCost(double night) { return 0.14 + 0.46 * Express(Gene.SprintCost, night); }
        public double SprintRate(double night)
        {
            return Express(Gene.Sprint, night) * (0.023 + 0.32 * Express(Gene.SprintFrequency, night));
        }

        public double TailOpacity(double night)
        {
            return 1 - 0.74 * Numbers.Unit(SaturatedFor / TailDwell(night, false));
        }

        internal double BrowserTailOpacity(double night)
        {
            return Math.Max(0.12, 1 - SaturatedFor / TailDwell(night, false));
        }

        internal double? SplitOpacity(int index)
        {
            if (index == 0 && SplitHeadOpacity.HasValue) return SplitHeadOpacity;
            return index == Body.Count - 1 ? SplitTailOpacity : null;
        }

        internal void EatTail(double night)
        {
            DropTail();
            SaturatedFor = 0;
            double relief = (0.017 + 0.24 * Express(Gene.TailRelief, night)) * (1 + 0.25 * Assimilation);
            Stress = Math.Max(0, Stress - relief);
        }

        public double ImprintWeight(double night) { return Numbers.Unit(0.015 + 0.027 * Express(Gene.Imprint, night)); }

        internal double FruitPower
        {
            get { return FruitMultiplier(FruitPowerUntil, AntiPowerUntil); }
        }

        private static double FruitMultiplier(List<long> positive, List<long> negative)
        {
            int stacks = (positive == null ? 0 : positive.Count) - (negative == null ? 0 : negative.Count);
            return Math.Pow(2, Math.Max(-1022, Math.Min(1023, stacks)));
        }

        internal void ApplyFruitTimer(double night, bool mirrored)
        {
            int moves = (int)Math.Max(0, Math.Min(Int32.MaxValue, Math.Round(Express(Gene.FruitSurge, night))));
            if (mirrored)
            {
                if (moves > 0) AddTimer(ref AntiPowerUntil, Moves + moves);
                AddTimer(ref AntiVisionUntil, Moves + Math.Max(8, moves));
            }
            else
            {
                if (moves > 0) AddTimer(ref FruitPowerUntil, Moves + moves);
                AddTimer(ref FruitVisionUntil, Moves + Math.Max(8, moves));
            }
            if (moves > 0) ActivateGene(Gene.FruitSurge);
        }

        private static void AddTimer(ref List<long> timers, long expiry)
        {
            if (timers == null) timers = new List<long>();
            timers.Add(expiry);
        }

        internal void AbsorbFruit(Genome donor, double night, bool mirrored = false)
        {
            double share = Numbers.Unit(Express(Gene.FruitInheritance, night));
            if (share == 0)
            {
                InhibitGene(Gene.FruitInheritance);
                return;
            }
            Genes.BlendFrom(donor, share, mirrored);
            ActivateGene(Gene.FruitInheritance);
        }

        private double LengthLoad(int length, double night, bool track)
        {
            double influence = Numbers.Unit(track ? Express(Gene.LengthComfort, night) :
                EffectiveGene(Gene.LengthComfort, night));
            if (influence == 0) return 0;
            double at3 = Math.Max(0, track ? Express(Gene.Length3, night) : Genes[Gene.Length3]);
            double at5 = Math.Max(0, track ? Express(Gene.Length5, night) : Genes[Gene.Length5]);
            double at8 = Math.Max(0, track ? Express(Gene.Length8, night) : Genes[Gene.Length8]);
            double at13 = Math.Max(0, track ? Express(Gene.Length13, night) : Genes[Gene.Length13]);
            double peak = Math.Max(Math.Max(at3, at5), Math.Max(at8, at13));
            if (peak == 0) return 0;
            at3 /= peak;
            at5 /= peak;
            at8 /= peak;
            at13 /= peak;
            double preference;
            if (length <= 3) preference = at3 * length / 3;
            else if (length <= 5) preference = at3 + (at5 - at3) * (length - 3) / 2;
            else if (length <= 8) preference = at5 + (at8 - at5) * (length - 5) / 3;
            else if (length <= 13) preference = at8 + (at13 - at8) * (length - 8) / 5;
            else preference = at13 * 13 / length;
            double rate = 0.3 * influence * Numbers.Unit(1 - preference);
            if (track && rate > 0) ActivateGene(Gene.LengthComfort);
            return rate;
        }

        public void Advance(ColorField field, double night, double seconds, SaverRandom random,
            Action<Cell> crossing, Action<Cell> arrived, World habitat = null,
            ManualSteering manual = null)
        {
            usedGenes = activeGenes = inhibitedGenes = 0;
            double previousRatio = Numbers.Unit(Stress / Capacity(night));
            if (manual == null && SprintRemaining <= 0 && random.Next() < 1 - Math.Exp(-seconds *
                SprintRate(night) * (1 - previousRatio)))
                SprintRemaining = SprintDuration(night);

            double sprintTime = manual == null ? Math.Min(SprintRemaining, seconds) : 0;
            if (sprintTime > 0) ActivateGene(Gene.Sprint);
            SprintRemaining -= sprintTime;
            MovementExposure exposure = MoveAndSample(field, night, seconds, sprintTime / seconds, previousRatio,
                random, crossing, arrived, habitat, manual);
            bool paused = manual != null && manual.Paused;
            if ((Entering || !IsResident) && !VisibleOn(field))
                return;
            if (paused && !exposure.SampledColor && exposure.MissedStress == 0)
            {
                if (habitat != null) TrackStress(previousRatio, previousRatio, seconds);
                return;
            }

            Rgb environment = exposure.Color;
            LightResponse response = exposure.Light;
            if (habitat != null && (response.Feed > 0 || response.Pressure > 0))
            {
                double strength = habitat.SquareStrength(Body);
                response.Feed *= strength;
                response.Pressure *= strength;
                if (strength > 0) habitat.ConsumeSquares(Body, Express(Gene.SquareRecovery, night));
            }
            double lengthLoad = habitat == null || paused ? 0 : LengthLoad(Body.Count, night, true) * seconds;
            Energy = Math.Max(0, Numbers.Finite(Energy + 0.72 * response.Feed * seconds * FruitPower -
                exposure.ManeuverCost - lengthLoad));
            if (exposure.SampledColor)
                Assimilation += (Numbers.Unit(response.Feed) - Assimilation) * (1 - Math.Exp(-seconds / 12));

            double stressFromLight = exposure.SampledColor
                ? response.Pressure * (0.16 + 0.95 * Express(Gene.StressBuildup, night)) * seconds / FruitPower : 0;
            if (stressFromLight > 0) ActivateGene(Gene.StressBuildup);
            double stressFromSprint = SprintCost(night) * sprintTime;
            double capacity = Capacity(night);
            double calm = response.Feed * 0.52 * seconds;
            Stress = Math.Max(0, Math.Min(capacity, Stress + stressFromLight + stressFromSprint +
                exposure.ManeuverCost + exposure.MissedStress + lengthLoad - calm));
            if (stressFromLight > 0)
                RememberStress(environment, night, seconds);
            if (exposure.SampledColor) ObserveColor(environment, night, seconds);

            Gene? calmCause = exposure.FeedingCause;
            Gene? stressCause = null;
            if (exposure.MissedStress > 0 && exposure.MissedStress >= Math.Max(lengthLoad, Math.Max(stressFromSprint, stressFromLight)))
                stressCause = Gene.MissedMoveStress;
            else if (lengthLoad > 0 && lengthLoad >= Math.Max(stressFromSprint, stressFromLight) && lengthLoad > calm)
                stressCause = Gene.LengthComfort;
            else if (stressFromSprint > stressFromLight && stressFromSprint > calm)
                stressCause = Gene.SprintCost;
            else if (stressFromLight > 0)
                stressCause = exposure.FlickerRaisedPressure ? Gene.FlickerAttraction : Gene.StressBuildup;

            if (!paused && Stress >= capacity)
            {
                SaturatedFor += seconds;
                if (SaturatedFor >= TailDwell(night))
                {
                    EatTail(night);
                    calmCause = Gene.TailRelief;
                }
            }
            else if (Stress < capacity)
                SaturatedFor = 0;

            Affect before = Mood;
            RecordAffect(Stress / capacity, calmCause, stressCause, night);
            if (habitat != null && Body.Count > 0 && before == Affect.Stressed && Mood == Affect.Calm)
                Ecology.DropAntifruit(habitat, this, night);
            if (Body.Count > 0 && exposure.SampledColor && !paused)
                Spend(environment, night, seconds, random);
            if (habitat != null)
            {
                TrackStress(previousRatio, Stress / capacity, seconds);
                double retention = Numbers.Unit(Express(Gene.StressSmoothing, night)) * 0.98;
                SmoothedStress = Numbers.Unit((SmoothedStress ?? previousRatio) * retention +
                    Numbers.Unit(Stress / capacity) * (1 - retention));
                if (retention > 0) ActivateGene(Gene.StressSmoothing);
            }
        }

        private void TrackStress(double before, double after, double seconds)
        {
            HighStressSeconds = after >= 0.72 ? Math.Min(ChronicStressLimit, HighStressSeconds + seconds) : 0;
            double spike = Numbers.Unit((after - before - 0.08) / 0.32);
            RecentStressSpike = Math.Max(spike, RecentStressSpike * Math.Exp(-seconds));
        }

        public bool VisibleOn(ColorField field)
        {
            foreach (Cell point in Body)
                if (field.Contains(point)) return true;
            return false;
        }

        private void RememberStress(Rgb color, double night, double seconds)
        {
            double horizon = 6 + 64 * Express(Gene.StressMemory, night);
            StressColor = HasStressColor ? Rgb.Mix(StressColor, color, 1 - Math.Exp(-seconds / horizon)) : color;
            HasStressColor = true;
        }

        private void ObserveColor(Rgb environment, double night, double seconds)
        {
            if (Rgb.Distance(Skin, environment) < 0.045)
            {
                DifferentFor = 0;
                return;
            }
            if (DifferentFor == 0 || Rgb.Distance(DriftColor, environment) > 0.17)
            {
                DifferentFor = 0;
                DriftColor = environment;
            }
            else
                DriftColor = Rgb.Mix(DriftColor, environment, 1 - Math.Exp(-seconds / 7));

            DifferentFor += seconds;
            double rate = 0.016 * Express(Gene.ColorDrift, night);
            if (DifferentFor >= 6 && rate > 0)
                Skin = Rgb.Mix(Skin, DriftColor, 1 - Math.Exp(-seconds * rate));
        }

        public static double SaveChance(double stressFraction, double gene)
        {
            return Numbers.Unit(0.64 * gene + 0.81 * stressFraction);
        }

        public void Spend(Rgb environment, double night, double seconds, SaverRandom random)
        {
            if (PendingGrowth > 0 || Energy < 1)
                return;

            double pressure = Stress / Capacity(night);
            if (!Saving)
                Saving = random.Next() < SaveChance(pressure, Express(Gene.GrowSave, night));
            if (!Saving)
            {
                Energy -= 1;
                PendingGrowth = 1;
                return;
            }
            if (Energy < Body.Count)
                return;

            Rgb shade = Rgb.Mix(Skin, StressColor, Numbers.Unit(Express(Gene.ShedShift, night)));
            if (HasStressColor && Rgb.Distance(shade, environment) < Rgb.Distance(Skin, environment))
            {
                Skin = shade;
                Energy -= Body.Count;
                Saving = false;
            }
            else if (pressure < 0.20 && random.Next() < 1 - Math.Exp(-seconds * 0.12))
                Saving = false;
        }

        public static double LearningDelta(bool calm, double magnitude, double offsetFraction)
        {
            return (calm ? magnitude : -magnitude) + magnitude * Numbers.Signed(offsetFraction);
        }

        public void RecordAffect(double stressFraction, Gene? calmCause, Gene? stressCause, double night)
        {
            Affect next = Affect.Neutral;
            if (stressFraction >= (Mood == Affect.Stressed ? 0.65 : 0.72))
                next = Affect.Stressed;
            else if (stressFraction <= (Mood == Affect.Calm ? 0.29 : 0.22))
                next = Affect.Calm;

            Gene? cause = next == Affect.Calm ? calmCause : stressCause;
            if (next != Mood && next != Affect.Neutral && cause.HasValue)
            {
                double magnitude = 0.08 * Express(Gene.Learning, night);
                double delta = LearningDelta(next == Affect.Calm, magnitude, Genes[Gene.LearningOffset]);
                int index = (int)cause.Value;
                Learned[index] = Numbers.Signed(Learned[index] + delta);
            }
            Mood = next;
        }

        private struct MovementExposure
        {
            public Rgb Color;
            public LightResponse Light;
            public bool SampledColor;
            public Gene? FeedingCause;
            public bool FlickerRaisedPressure;
            public double ManeuverCost;
            public double MissedStress;
        }

        private MovementExposure MoveAndSample(ColorField field, double night, double seconds, double burstFraction,
            double stressFraction, SaverRandom random, Action<Cell> crossing, Action<Cell> arrived,
            World habitat, ManualSteering manual)
        {
            var exposure = new MovementExposure();
            bool paused = manual != null && manual.Paused;
            LightResponse? initialLight = null;
            int missed = 0;
            double cruise = Math.Min(50, (1.15 + 3.6 * Express(Gene.Speed, night)) * FruitPower);
            double variation = random.Between(-1, 1) * Express(Gene.SpeedVariability, night) * stressFraction;
            double target = cruise * Math.Max(0.28, 1 + variation) + burstFraction * (2.8 + cruise);
            double acceleration = (0.58 + 2.5 * Express(Gene.Acceleration, night)) *
                (1 + 0.06 * Body.Count + 0.88 * stressFraction) * FruitPower;
            Speed = Math.Min(50, Numbers.Approach(Speed, target, acceleration * seconds));
            Motion += Speed * seconds;
            if (manual != null && manual.Boost && !paused)
            {
                Cell destination = Body[0].Move(Cell.Directions[ManualHeading(manual)]);
                if (field.Contains(destination) && !habitat.IsOccupied(destination))
                    Motion += (2.8 + cruise) * Numbers.Unit(Express(Gene.Sprint, night)) * seconds;
            }
            if (manual != null && manual.SingleMove) Motion = 1;

            while (Motion >= 1)
            {
                Motion -= 1;
                if (paused)
                {
                    missed++;
                    continue;
                }
                bool choosing = !Exiting && manual == null && (!Entering || field.Contains(Body[0]));
                bool flickerWon = false;
                bool jerkTurn = false;
                int next = manual != null ? ManualHeading(manual) : choosing
                    ? ChooseHeading(field, night, stressFraction, random, habitat, out flickerWon, out jerkTurn) : Heading;
                if (next < 0)
                {
                    missed++;
                    continue;
                }
                Cell head = Body[0].Move(Cell.Directions[next]);
                if ((IsResident || StaysInArena) && !field.Contains(head))
                {
                    missed++;
                    continue;
                }
                int cutLayers = 0;
                if (manual != null && manual.TailPass.HasValue && manual.TailPass.Value.Equals(head))
                {
                    if (HasSplitTailAt(head)) cutLayers++;
                    if (manual.TailPassClone != null && manual.TailPassClone.HasSplitTailAt(head)) cutLayers++;
                }
                int jumpHeight = habitat == null ? 0 : habitat.JumpHeight(this, head, cutLayers);
                bool boosted = manual != null && manual.Boost;
                if (manual != null && jumpHeight > 0 && !boosted)
                {
                    missed++;
                    continue;
                }
                if (manual != null)
                {
                    manual.TailPass = null;
                    manual.TailPassClone = null;
                }
                if (boosted)
                {
                    manual.Boost = false;
                    if (jumpHeight == 0) ActivateGene(Gene.Sprint);
                }
                int jumpUnits = Math.Max(jumpHeight, boosted ? 1 : 0);
                int maneuver = (next != Heading ? 1 : 0) + 2 * jumpUnits;
                if (habitat != null && maneuver != 0)
                {
                    exposure.ManeuverCost += maneuver * Express(Gene.ManeuverCost, night);
                    ActivateGene(Gene.ManeuverCost);
                }

                if (!initialLight.HasValue)
                {
                    exposure.Light = SenseBody(field, night, random, habitat != null, out exposure.Color);
                    exposure.SampledColor = true;
                    initialLight = exposure.Light;
                }
                LightResponse priorLight = exposure.Light;
                if (choosing)
                    LastMovingGene = jerkTurn ? Gene.StressJerk : flickerWon ? Gene.FlickerAttraction :
                        (burstFraction > 0 ? Gene.Sprint : Gene.Speed);

                Heading = next;
                if (habitat != null) exposure.MissedStress += ChargeMissedMoves(missed, night, field, habitat);
                missed = 0;
                ConsecutiveMissedMoves = 0;
                IdleColorSeconds = null;
                SplitHeadOpacity = null;
                ActivateGene(Gene.Speed);
                if (flickerWon) ActivateGene(Gene.FlickerAttraction);
                if (!IsResident && !field.Contains(head))
                    Exiting = true;
                Body.Insert(0, head);
                if (habitat != null) LayerOrder.Insert(0, habitat.NewLayerOrder());
                if (PendingGrowth > 0)
                    PendingGrowth--;
                else
                    DropTail();
                if (Entering && (!IsResident && field.Contains(head) || Body.TrueForAll(field.Contains))) Entering = false;
                Moves++;
                if (FruitPowerUntil != null) FruitPowerUntil.RemoveAll(expiry => expiry <= Moves);
                if (FruitVisionUntil != null) FruitVisionUntil.RemoveAll(expiry => expiry <= Moves);
                if (AntiPowerUntil != null) AntiPowerUntil.RemoveAll(expiry => expiry <= Moves);
                if (AntiVisionUntil != null) AntiVisionUntil.RemoveAll(expiry => expiry <= Moves);

                LargestLength = Math.Max(LargestLength, Body.Count);
                RecentPath.Add(head);
                if (RecentPath.Count > LargestLength)
                    RecentPath.RemoveAt(0);

                if (crossing != null)
                {
                    if (habitat != null) crossing(head);
                    else foreach (Cell point in Body) crossing(point);
                }
                if (arrived != null)
                    arrived(head);

                exposure.Light = SenseBody(field, night, random, habitat != null, out exposure.Color);
                if (choosing && exposure.Light.Feed > priorLight.Feed + ResponseTolerance)
                    exposure.FeedingCause = LastMovingGene;
                if (flickerWon && exposure.Light.Pressure > priorLight.Pressure + ResponseTolerance)
                    exposure.FlickerRaisedPressure = true;
            }

            if (habitat != null)
            {
                if ((paused || missed > 0) && !IdleColorSeconds.HasValue) IdleColorSeconds = 0;
                exposure.MissedStress += ChargeMissedMoves(missed, night, field, habitat);
            }
            if (!initialLight.HasValue)
            {
                SampleAmbient(field, night, seconds, random, habitat, ref exposure);
                return exposure;
            }
            if (exposure.Light.Feed <= initialLight.Value.Feed + ResponseTolerance)
                exposure.FeedingCause = null;
            if (exposure.Light.Pressure <= initialLight.Value.Pressure + ResponseTolerance)
                exposure.FlickerRaisedPressure = false;
            return exposure;
        }

        private double ChargeMissedMoves(int count, double night, ColorField field, World habitat)
        {
            if (count == 0) return 0;
            double baseStress = Express(Gene.MissedMoveStress, night);
            double added = 0;
            for (int missed = 0; missed < count; missed++)
            {
                if (ConsecutiveMissedMoves < Int64.MaxValue) ConsecutiveMissedMoves++;
                double weight = ConsecutiveMissedMoves < VisibleStressMiss
                    ? 0.005 * ConsecutiveMissedMoves : ConsecutiveMissedMoves - VisibleStressMiss + 1;
                added += baseStress * weight;
            }
            if (added <= 0) return 0;
            ActivateGene(Gene.MissedMoveStress);
            Cell parked = Body[0];
            ColorChannels channels = Sensitivity;
            if (channels == ColorChannels.None || !field.Contains(parked)) return added;
            Rgb color = field.At(parked).SensitiveChannels(Skin, channels);
            LightResponse response = LightResponse.Sense(Skin, color);
            ApplyBirthAffinity(color, channels, night, ref response, false);
            double affinity = response.Pressure - Numbers.Unit(response.Feed);
            return added * (1 + 0.5 * affinity * habitat.SquareStrengthAt(parked));
        }

        private void SampleAmbient(ColorField field, double night, double seconds,
            SaverRandom random, World habitat, ref MovementExposure exposure)
        {
            if (IdleColorSeconds.HasValue)
            {
                double elapsed = IdleColorSeconds.Value + seconds;
                if (elapsed < IdleColorInterval)
                {
                    IdleColorSeconds = elapsed;
                    return;
                }
                IdleColorSeconds = elapsed % IdleColorInterval;
            }
            exposure.Light = SenseBody(field, night, random, habitat != null, out exposure.Color);
            exposure.SampledColor = true;
        }

        internal int ManualHeading(ManualSteering manual)
        {
            return manual.Heading == (Heading + 2) % Cell.Directions.Length ? Heading : manual.Heading;
        }

        private bool HasSplitTailAt(Cell point)
        {
            return SplitTailOpacity.HasValue && Body.Count > 0 && Body[Body.Count - 1].Equals(point);
        }

        private void DropTail()
        {
            int tail = Body.Count - 1;
            Body.RemoveAt(tail);
            SplitTailOpacity = null;
            if (LayerOrder != null) LayerOrder.RemoveAt(tail);
        }

        private LightResponse SenseBody(ColorField field, double night, SaverRandom random, bool homeAffinity,
            out Rgb color)
        {
            ColorChannels channels = Sensitivity;
            color = field.Average(Body).SensitiveChannels(Skin, channels);
            if (channels == ColorChannels.None) return new LightResponse();
            LightResponse response = LightResponse.Sense(Skin, color);
            if (homeAffinity)
                ApplyBirthAffinity(color, channels, night, ref response);
            var cells = new HashSet<Cell>();
            int samples = 0;
            foreach (Cell point in Body)
            {
                if (!field.Contains(point)) continue;
                cells.Add(point);
                samples++;
            }
            int overlaps = samples - cells.Count;
            if (overlaps == 0) return response;
            if (Genes[Gene.SelfFeeding] <= 0)
            {
                InhibitGene(Gene.SelfFeeding);
                return response;
            }
            if (random.Next() >= Express(Gene.SelfFeeding, night)) return response;

            Rgb withSkin = Rgb.Mix(color, Skin, 0.05 * overlaps / samples);
            double addedFeed = LightResponse.Sense(Skin, withSkin).Feed;
            if (addedFeed > response.Feed)
            {
                response.Feed = addedFeed;
                ActivateGene(Gene.SelfFeeding);
            }
            return response;
        }

        private void ApplyBirthAffinity(Rgb sensed, ColorChannels channels, double night, ref LightResponse response,
            bool track = true)
        {
            double strength = track ? Express(Gene.BirthColorFeed, night) : EffectiveGene(Gene.BirthColorFeed, night);
            if (strength == 0) return;
            Rgb home = BirthColor.GetValueOrDefault(Skin).SensitiveChannels(sensed, channels);
            double dr = home.R - sensed.R, dg = home.G - sensed.G, db = home.B - sensed.B;
            double distance = Math.Sqrt(dr * dr + dg * dg + db * db);
            double gradient = track ? Express(Gene.BirthColorGradient, night) : EffectiveGene(Gene.BirthColorGradient, night);
            double steepness = 4 + 56 * Numbers.Unit(gradient);
            double homeFeed = (1 + strength) * Math.Exp(-steepness * distance);
            if (homeFeed <= response.Feed) return;
            response.Feed = homeFeed;
            response.Pressure *= 1 - Numbers.Unit(homeFeed);
            if (track)
            {
                ActivateGene(Gene.BirthColorFeed);
                if (distance > 0) ActivateGene(Gene.BirthColorGradient);
            }
        }

        private int ChooseHeading(ColorField field, double night, double stressFraction,
            SaverRandom random, World habitat, out bool flickerWon, out bool jerkTurn)
        {
            jerkTurn = false;
            bool nesting = habitat != null && habitat.HasEgg(Id);
            int? nestHeading = nesting ? habitat.NestHeading(this) : null;
            if (nestHeading.HasValue)
            {
                flickerWon = false;
                return nestHeading.Value;
            }
            if (habitat != null)
            {
                int jerk = TryStressJerk(field, habitat, night, random);
                if (jerk >= 0)
                {
                    flickerWon = false;
                    jerkTurn = true;
                    return jerk;
                }
            }
            double best = Double.NegativeInfinity, bestWithoutFlicker = best;
            int selected = -1, withoutFlicker = -1;
            int opposite = (Heading + 2) % Cell.Directions.Length;
            double flicker = (1 - stressFraction) * Express(Gene.FlickerAttraction, night);
            double radius = habitat == null ? 0 : Express(Gene.VisionRadius, night);
            Cell? fruit = habitat == null ? null : habitat.FindVisibleFruit(Body[0], radius, this);
            Cell? repellent = habitat == null ? null : habitat.FindVisibleFruit(Body[0], radius, this, true);
            if (fruit.HasValue || repellent.HasValue) ActivateGene(Gene.VisionRadius);
            for (int candidate = 0; candidate < Cell.Directions.Length; candidate++)
            {
                Cell next = Body[0].Move(Cell.Directions[candidate]);
                bool inside = field.Contains(next);
                if (!inside && (IsResident || StaysInArena || nesting)) continue;

                double jitter = random.Between(-0.14, 0.14);
                if (candidate == opposite) continue;

                double color = inside ? Rgb.Distance(Skin, field.At(next).SensitiveChannels(Skin, Sensitivity)) : 0.46;
                double baseScore = -color + (candidate == Heading ? 0.07 : 0) + jitter;
                if (fruit.HasValue && inside)
                    baseScore += 13 * (World.FoodDistance(Body[0], fruit.Value) - World.FoodDistance(next, fruit.Value));
                if (repellent.HasValue && inside)
                {
                    int distance = World.FoodDistance(next, repellent.Value);
                    baseScore += 4 * (distance - World.FoodDistance(Body[0], repellent.Value));
                    if (distance == 0) baseScore -= 13;
                }
                if (habitat != null)
                {
                    int jumpHeight = habitat.JumpHeight(this, next);
                    if (jumpHeight > 0)
                        baseScore -= 4 + 2 * jumpHeight * EffectiveGene(Gene.ManeuverCost, night);
                }
                double score = baseScore + (inside ? flicker * field.Flicker(next, Sensitivity) : 0);
                if (baseScore > bestWithoutFlicker) { bestWithoutFlicker = baseScore; withoutFlicker = candidate; }
                if (score > best) { best = score; selected = candidate; }
            }

            flickerWon = selected != withoutFlicker;
            return selected;
        }

        private int TryStressJerk(ColorField field, World habitat, double night, SaverRandom random)
        {
            double chronic = Numbers.Unit((HighStressSeconds - ChronicStressStart) / (ChronicStressLimit - ChronicStressStart));
            double signal = 2 * chronic + 4 * RecentStressSpike;
            if (signal == 0) return -1;
            double rate = Express(Gene.StressJerk, night);
            if (rate == 0 || random.Next() >= Numbers.Unit(rate * signal)) return -1;

            if (SprintRemaining > 0 || random.Next() >= 0.5)
            {
                int firstTurn = random.Next() < 0.5 ? -1 : 1;
                for (int choice = 0; choice < 2; choice++)
                {
                    int turn = choice == 0 ? firstTurn : -firstTurn;
                    int heading = (Heading + turn + 4) % 4;
                    Cell destination = Body[0].Move(Cell.Directions[heading]);
                    if (!field.Contains(destination) || habitat.IsOccupied(destination)) continue;
                    ActivateGene(Gene.StressJerk);
                    return heading;
                }
            }
            if (SprintRemaining <= 0)
            {
                SprintRemaining = SprintDuration(night);
                ActivateGene(Gene.StressJerk);
            }
            return -1;
        }
    }
}
