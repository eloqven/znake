using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Znake
{
    internal enum Gene
    {
        Length3, Length5, Length8, Length13, NightBoost, ColorDrift, ShedShift,
        StressBuildup, StressMemory, GrowSave, Imprint, StressCapacity,
        TailDwell, TailRelief, Speed, Acceleration, SpeedVariability, FlickerAttraction,
        Sprint, SprintDuration, SprintCost, SprintFrequency, VisitorArrival,
        Learning, LearningOffset, SelfFeeding, ManeuverCost, StressSmoothing,
        BirthColorFeed, BirthColorGradient, VisionRadius, LengthComfort, FruitInheritance, SquareRecovery,
        FruitSurge, MissedMoveStress, StressJerk, Count
    }

    [DataContract]
    internal sealed class Genome
    {
        [DataMember] public double[] Values = new double[(int)Gene.Count];
        [DataMember(EmitDefaultValue = false)] private bool stackedBerries = true;

        [OnDeserialized]
        private void RestoreNewGenes(StreamingContext context)
        {
            if (!stackedBerries && Values != null && Values.Length > (int)Gene.FruitSurge &&
                Math.Abs(this[Gene.FruitSurge] - 3) < 1e-9) this[Gene.FruitSurge] = 55;
            stackedBerries = true;
            if (Values == null || Values.Length < (int)Gene.SelfFeeding || Values.Length >= (int)Gene.Count)
                return;
            int originalLength = Values.Length;
            Array.Resize(ref Values, (int)Gene.Count);
            SetPrototypeDefaults(originalLength);
        }

        private void SetPrototypeDefaults(int firstMissing)
        {
            if (firstMissing <= (int)Gene.ManeuverCost) this[Gene.ManeuverCost] = 0.035;
            if (firstMissing <= (int)Gene.StressSmoothing) this[Gene.StressSmoothing] = 0.85;
            if (firstMissing <= (int)Gene.BirthColorFeed) this[Gene.BirthColorFeed] = 0.08;
            if (firstMissing <= (int)Gene.BirthColorGradient) this[Gene.BirthColorGradient] = 0.45;
            if (firstMissing <= (int)Gene.SquareRecovery) this[Gene.VisionRadius] = 5;
            if (firstMissing <= (int)Gene.LengthComfort) this[Gene.LengthComfort] = 0.08;
            if (firstMissing <= (int)Gene.FruitInheritance) this[Gene.FruitInheritance] = 0.14;
            if (firstMissing <= (int)Gene.FruitSurge) this[Gene.FruitSurge] = 55;
            if (firstMissing <= (int)Gene.MissedMoveStress) this[Gene.MissedMoveStress] = 0.20;
            if (firstMissing <= (int)Gene.StressJerk) this[Gene.StressJerk] = 0.015;
        }

        public double this[Gene gene]
        {
            get { return Values[(int)gene]; }
            set { Values[(int)gene] = value; }
        }

        public static Genome Roll(SaverRandom random, IDictionary<Gene, double> overrides)
        {
            var genes = new Genome();
            for (int index = 0; index < (int)Gene.SelfFeeding; index++)
                genes.Values[index] = random.Between(0.025, 0.17);

            genes[Gene.Length3] = random.Between(0.08, 0.16);
            genes[Gene.Length5] = random.Between(0.22, 0.36);
            genes[Gene.Length8] = random.Between(0.025, 0.07);
            genes[Gene.Length13] = random.Between(0.0003, 0.003);
            genes[Gene.LearningOffset] = random.Between(-0.14, 0.14);
            genes.SetPrototypeDefaults((int)Gene.SelfFeeding);

            if (overrides != null)
            {
                foreach (var entry in overrides)
                {
                    if (!IsValid(entry.Key, entry.Value))
                        throw new ArgumentOutOfRangeException("overrides", "Invalid gene value for " + entry.Key);
                    genes[entry.Key] = entry.Value;
                }
            }
            return genes;
        }

        public static bool IsValid(Gene gene, double value)
        {
            return gene >= Gene.Length3 && gene < Gene.Count && !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        internal static double Nudge(Gene gene, double value, double percentagePoints)
        {
            return Numbers.Finite(value + percentagePoints / (gene == Gene.VisionRadius ? 1 : 100));
        }

        public bool IsValid()
        {
            if (Values == null || Values.Length != (int)Gene.Count)
                return false;
            for (int index = 0; index < Values.Length; index++)
                if (!IsValid((Gene)index, Values[index])) return false;
            return true;
        }

        public Genome Copy()
        {
            return new Genome { Values = (double[])Values.Clone() };
        }

        public int BirthLength(SaverRandom random)
        {
            double a = Math.Max(0, this[Gene.Length3]), b = Math.Max(0, this[Gene.Length5]);
            double c = Math.Max(0, this[Gene.Length8]), d = Math.Max(0, this[Gene.Length13]);
            double scale = Math.Max(Math.Max(a, b), Math.Max(c, d));
            if (scale == 0) return 5;
            a /= scale;
            b /= scale;
            c /= scale;
            d /= scale;
            double draw = random.Next() * (a + b + c + d);
            if ((draw -= a) < 0) return 3;
            if ((draw -= b) < 0) return 5;
            return draw < c ? 8 : 13;
        }

        public double Express(Gene gene, double adjustment, double night)
        {
            const double arithmeticHeadroom = Double.MaxValue / 1099511627776.0;
            double factor = Math.Max(0, 1 + Numbers.Unit(night) * this[Gene.NightBoost]);
            if (factor == 0) return 0;
            double value = (this[gene] + adjustment) * factor;
            return Math.Max(gene == Gene.SquareRecovery ? -arithmeticHeadroom : 0,
                Math.Min(arithmeticHeadroom, value));
        }

        public void BlendFrom(Genome donor, double share, bool mirrored = false)
        {
            for (int index = 0; index < Values.Length; index++)
                Values[index] = Numbers.Finite(Values[index] * (1 - share) +
                    (mirrored ? -donor.Values[index] : donor.Values[index]) * share);
        }

        internal void MutateSplit(SaverRandom random, double night)
        {
            double spread = 0.05 + 0.15 * Numbers.Unit(night);
            int changes = random.Next() < Numbers.Unit(night) ? 2 : 1;
            for (int index = 0; index < changes; index++)
            {
                int gene = (int)Gene.NightBoost + random.Index(Values.Length - (int)Gene.NightBoost);
                double delta = Math.Max(0.01, Math.Abs(Values[gene])) * random.Between(-spread, spread);
                Values[gene] = Numbers.Finite(Values[gene] + delta);
            }
        }
    }

    internal sealed class SaverRandom
    {
        public uint State { get; private set; }

        public SaverRandom(uint state) { State = state; }

        // Mulberry32 keeps the same explicit 32-bit sequence as the existing seeded fixtures.
        public double Next()
        {
            unchecked
            {
                State += 0x6D2B79F5;
                uint value = State;
                value = (value ^ (value >> 15)) * (value | 1);
                value ^= value + (value ^ (value >> 7)) * (value | 61);
                return (value ^ (value >> 14)) / 4294967296.0;
            }
        }

        public double Between(double low, double high) { return low + (high - low) * Next(); }
        public int Index(int count) { return (int)(Next() * count); }
    }

    internal static class Numbers
    {
        public static double Finite(double value) { return Math.Max(-Double.MaxValue, Math.Min(Double.MaxValue, value)); }
        public static double Unit(double value) { return Math.Max(0, Math.Min(1, value)); }
        public static double Signed(double value) { return Math.Max(-1, Math.Min(1, value)); }

        public static bool Nonnegative(double value)
        {
            return value >= 0 && !Double.IsInfinity(value);
        }

        public static double Approach(double current, double target, double amount)
        {
            return current < target ? Math.Min(current + amount, target) : Math.Max(current - amount, target);
        }
    }
}
