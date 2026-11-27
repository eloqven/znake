using System;
using System.Runtime.Serialization;

namespace Znake
{
    [DataContract]
    internal struct SquareUse
    {
        // Further halving would be below one step of the sampled 8-bit RGB field.
        private const int Exhausted = 8;
        [DataMember] public int Uses;
        [DataMember] public double ReadyAt;

        public double Strength(long tick)
        {
            return tick >= ReadyAt ? 1 : Uses >= Exhausted ? 0 : 1.0 / (1 << Uses);
        }

        public SquareUse Consume(long tick, double recovery)
        {
            int next = tick >= ReadyAt ? 1 : Uses + 1;
            if (next > Exhausted) return this;
            double cooldown = 60 / World.StepSeconds * Math.Pow(2, next - 1 - recovery);
            return new SquareUse { Uses = next, ReadyAt = Numbers.Finite(tick + Math.Max(1, cooldown)) };
        }

        public bool IsValid()
        {
            return Uses >= 1 && Uses <= Exhausted && Numbers.Nonnegative(ReadyAt);
        }
    }
}
