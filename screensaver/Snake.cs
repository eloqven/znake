using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Znake
{
    internal enum Affect { Neutral, Calm, Stressed }

    [DataContract]
    internal sealed class Snake
    {
        private const double ResponseTolerance = 1e-9;

        [DataMember] public long Id;
        [DataMember] public bool IsResident;
        [DataMember] public Genome Genes;
        [DataMember] public double[] Learned = new double[(int)Gene.Count];
        [DataMember] public List<Cell> Body;
        [DataMember] public List<Cell> RecentPath;
        [DataMember] public int LargestLength;
        [DataMember] public int Heading;
        [DataMember] public Rgb Skin;
        [DataMember] public double Energy;
        [DataMember] public bool Saving;
        [DataMember] public int PendingGrowth;
        [DataMember] public double Stress;
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
        [DataMember] public Affect Mood = Affect.Calm;
        [DataMember] public Gene LastMovingGene = Gene.Speed;

        public Snake(long id, bool resident, Genome genes, Rgb skin, List<Cell> body, int heading)
        {
            Id = id;
            IsResident = resident;
            Genes = genes;
            Skin = skin;
            Body = body;
            Heading = heading;
            LargestLength = body.Count;
            RecentPath = new List<Cell>(body);
            RecentPath.Reverse();
        }

        public double Express(Gene gene, double night)
        {
            return Genes.Express(gene, Learned[(int)gene], night);
        }

        public double Capacity(double night) { return 0.8 + 0.8 * Express(Gene.StressCapacity, night); }
        public double TailDwell(double night) { return 1.25 + 9 * Express(Gene.TailDwell, night); }
        public double SprintDuration(double night) { return 0.25 + 2.6 * Express(Gene.SprintDuration, night); }
        public double SprintCost(double night) { return 0.14 + 0.46 * Express(Gene.SprintCost, night); }
        public double SprintRate(double night)
        {
            return Express(Gene.Sprint, night) * (0.023 + 0.32 * Express(Gene.SprintFrequency, night));
        }

        public double TailOpacity(double night)
        {
            return 1 - 0.74 * Numbers.Unit(SaturatedFor / TailDwell(night));
        }

        public double ImprintWeight(double night) { return 0.015 + 0.027 * Express(Gene.Imprint, night); }

        public void Advance(ColorField field, double night, double seconds, SaverRandom random,
            Action<Cell> crossing, Action<Cell> arrived)
        {
            double previousRatio = Numbers.Unit(Stress / Capacity(night));
            if (SprintRemaining <= 0 && random.Next() < 1 - Math.Exp(-seconds *
                SprintRate(night) * (1 - previousRatio)))
                SprintRemaining = SprintDuration(night);

            double sprintTime = Math.Min(SprintRemaining, seconds);
            SprintRemaining -= sprintTime;
            MovementExposure exposure = MoveAndSample(field, night, seconds, sprintTime / seconds, previousRatio,
                random, crossing, arrived);
            if (!IsResident && !VisibleOn(field))
                return;

            Rgb environment = exposure.Color;
            LightResponse response = exposure.Light;
            Energy += 0.72 * response.Feed * seconds;
            Assimilation += (response.Feed - Assimilation) * (1 - Math.Exp(-seconds / 12));

            double stressFromLight = response.Pressure * (0.16 + 0.95 * Express(Gene.StressBuildup, night)) * seconds;
            double stressFromSprint = SprintCost(night) * sprintTime;
            double capacity = Capacity(night);
            double calm = response.Feed * 0.52 * seconds;
            Stress = Math.Max(0, Math.Min(capacity, Stress + stressFromLight + stressFromSprint - calm));
            if (stressFromLight > 0)
                RememberStress(environment, night, seconds);
            ObserveColor(environment, night, seconds);

            Gene? calmCause = exposure.FeedingCause;
            Gene? stressCause = null;
            if (stressFromSprint > stressFromLight && stressFromSprint > calm)
                stressCause = Gene.SprintCost;
            else if (stressFromLight > 0)
                stressCause = exposure.FlickerRaisedPressure ? Gene.FlickerAttraction : Gene.StressBuildup;

            if (Stress >= capacity)
            {
                SaturatedFor += seconds;
                if (SaturatedFor >= TailDwell(night))
                {
                    Body.RemoveAt(Body.Count - 1);
                    SaturatedFor = 0;
                    double relief = (0.017 + 0.24 * Express(Gene.TailRelief, night)) * (1 + 0.25 * Assimilation);
                    Stress = Math.Max(0, Stress - relief);
                    calmCause = Gene.TailRelief;
                }
            }
            else
                SaturatedFor = 0;

            RecordAffect(Stress / capacity, calmCause, stressCause, night);
            if (Body.Count > 0)
                Spend(environment, night, seconds, random);
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
            public Gene? FeedingCause;
            public bool FlickerRaisedPressure;
        }

        private MovementExposure MoveAndSample(ColorField field, double night, double seconds, double burstFraction,
            double stressFraction, SaverRandom random, Action<Cell> crossing, Action<Cell> arrived)
        {
            var exposure = new MovementExposure();
            LightResponse? initialLight = null;
            double cruise = 1.15 + 3.6 * Express(Gene.Speed, night);
            double variation = random.Between(-1, 1) * Express(Gene.SpeedVariability, night) * stressFraction;
            double target = cruise * Math.Max(0.28, 1 + variation) + burstFraction * (2.8 + cruise);
            double acceleration = (0.58 + 2.5 * Express(Gene.Acceleration, night)) *
                (1 + 0.06 * Body.Count + 0.88 * stressFraction);
            Speed = Numbers.Approach(Speed, target, acceleration * seconds);
            Motion += Speed * seconds;

            while (Motion >= 1)
            {
                Motion -= 1;
                bool choosing = !Exiting;
                bool flickerWon = false;
                int next = choosing
                    ? ChooseHeading(field, night, stressFraction, random, out flickerWon) : Heading;
                if (next < 0)
                    continue;

                if (!initialLight.HasValue)
                {
                    exposure.Light = LightResponse.Sense(Skin, field.Average(Body));
                    initialLight = exposure.Light;
                }
                LightResponse priorLight = exposure.Light;
                if (choosing)
                    LastMovingGene = flickerWon ? Gene.FlickerAttraction :
                        (burstFraction > 0 ? Gene.Sprint : Gene.Speed);

                Heading = next;
                Cell head = Body[0].Move(Cell.Directions[Heading]);
                if (!IsResident && !field.Contains(head))
                    Exiting = true;
                Body.Insert(0, head);
                if (PendingGrowth > 0)
                    PendingGrowth--;
                else
                    Body.RemoveAt(Body.Count - 1);

                LargestLength = Math.Max(LargestLength, Body.Count);
                RecentPath.Add(head);
                if (RecentPath.Count > LargestLength)
                    RecentPath.RemoveAt(0);

                if (crossing != null)
                    foreach (Cell point in Body) crossing(point);
                if (arrived != null)
                    arrived(head);

                exposure.Color = field.Average(Body);
                exposure.Light = LightResponse.Sense(Skin, exposure.Color);
                if (choosing && exposure.Light.Feed > priorLight.Feed + ResponseTolerance)
                    exposure.FeedingCause = LastMovingGene;
                if (flickerWon && exposure.Light.Pressure > priorLight.Pressure + ResponseTolerance)
                    exposure.FlickerRaisedPressure = true;
            }

            if (!initialLight.HasValue)
            {
                exposure.Color = field.Average(Body);
                exposure.Light = LightResponse.Sense(Skin, exposure.Color);
            }
            else
            {
                if (exposure.Light.Feed <= initialLight.Value.Feed + ResponseTolerance)
                    exposure.FeedingCause = null;
                if (exposure.Light.Pressure <= initialLight.Value.Pressure + ResponseTolerance)
                    exposure.FlickerRaisedPressure = false;
            }
            return exposure;
        }

        private int ChooseHeading(ColorField field, double night, double stressFraction,
            SaverRandom random, out bool flickerWon)
        {
            double best = Double.NegativeInfinity, bestWithoutFlicker = best;
            int selected = -1, withoutFlicker = -1;
            double flicker = (1 - stressFraction) * Express(Gene.FlickerAttraction, night);
            for (int candidate = 0; candidate < Cell.Directions.Length; candidate++)
            {
                Cell next = Body[0].Move(Cell.Directions[candidate]);
                bool inside = field.Contains(next);
                if (!inside && IsResident)
                    continue;

                double color = inside ? Rgb.Distance(Skin, field.At(next)) : 0.46;
                double baseScore = -color + (candidate == Heading ? 0.07 : 0) + random.Between(-0.14, 0.14);
                double score = baseScore + (inside ? flicker * field.Flicker(next) : 0);
                if (baseScore > bestWithoutFlicker) { bestWithoutFlicker = baseScore; withoutFlicker = candidate; }
                if (score > best) { best = score; selected = candidate; }
            }

            flickerWon = selected != withoutFlicker;
            return selected;
        }
    }
}
