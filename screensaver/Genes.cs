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
        Learning, LearningOffset, Count
    }

    [DataContract]
    internal sealed class Genome
    {
        [DataMember] public double[] Values = new double[(int)Gene.Count];

        public double this[Gene gene]
        {
            get { return Values[(int)gene]; }
            set { Values[(int)gene] = value; }
        }

        public static Genome Roll(SaverRandom random, IDictionary<Gene, double> overrides)
        {
            var genes = new Genome();
            for (int index = 0; index < genes.Values.Length; index++)
                genes.Values[index] = random.Between(0.025, 0.17);

            genes[Gene.Length3] = random.Between(0.08, 0.16);
            genes[Gene.Length5] = random.Between(0.22, 0.36);
            genes[Gene.Length8] = random.Between(0.025, 0.07);
            genes[Gene.Length13] = random.Between(0.0003, 0.003);
            genes[Gene.LearningOffset] = random.Between(-0.14, 0.14);

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
            return gene >= Gene.Length3 && gene < Gene.Count &&
                value >= (gene == Gene.LearningOffset ? -1 : 0) && value <= 1;
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
            double total = this[Gene.Length3] + this[Gene.Length5] + this[Gene.Length8] + this[Gene.Length13];
            if (total == 0)
                return 5;

            double draw = random.Next() * total;
            if ((draw -= this[Gene.Length3]) < 0) return 3;
            if ((draw -= this[Gene.Length5]) < 0) return 5;
            return draw < this[Gene.Length8] ? 8 : 13;
        }

        public double Express(Gene gene, double adjustment, double night)
        {
            return Numbers.Unit(this[gene] + adjustment) * (1 + Numbers.Unit(night) * this[Gene.NightBoost]);
        }

        public void BlendFrom(Genome donor, double share)
        {
            for (int index = 0; index < Values.Length; index++)
                Values[index] += (donor.Values[index] - Values[index]) * share;
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
