using System.Runtime.Serialization;

namespace Znake
{
    [DataContract]
    internal sealed class Egg
    {
        [DataMember] public Cell Position;
        [DataMember] public long DueUtcTicks;
        [DataMember] public SpeciesTemplate Parent;

        public int? ReturnHeading(World world, Cell head, int heading)
        {
            int distance = World.FoodDistance(head, Position);
            if (distance == 0) return null;
            int? sidestep = null;
            for (int turn = -1; turn <= 1; turn++)
            {
                int nextHeading = (heading + turn + 4) % 4;
                Cell next = head.Move(Cell.Directions[nextHeading]);
                if (!world.Contains(next)) continue;
                if (World.FoodDistance(next, Position) < distance) return nextHeading;
                if (turn != 0 && !sidestep.HasValue) sidestep = nextHeading;
            }
            return sidestep;
        }
    }
}
