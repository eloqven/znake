using System;
using System.Linq;

namespace Znake.Tests
{
    internal static class CalibrationTests
    {
        public static void Run()
        {
            var sunny = new World(32, 20, 514, null);
            var opposite = new World(32, 20, 514, null);
            var good = ColorGrid.Uniform(32, 20, Rgb.StartingGreen);
            var bad = ColorGrid.Uniform(32, 20, Rgb.FromBytes(224, 19, 171));
            long initialId = sunny.Snakes.Single().Id;
            int birthLength = sunny.Snakes.Single().Body.Count;
            for (int tick = 0; tick < 650; tick++)
            {
                sunny.Step(good, 0);
                opposite.Step(bad, 0);
            }
            Snake fed = sunny.Snakes.Single(snake => snake.IsResident);
            Snake stressed = opposite.Snakes.Single(snake => snake.IsResident);
            TestRunner.Check(fed.Id == initialId && fed.Body.Count > birthLength, "Matching light produces sustained growth");
            TestRunner.Check(stressed.Id != initialId || stressed.Body.Count < birthLength,
                "Sustained hostile light makes tail loss or succession reachable");
            Console.WriteLine("52-second replay. Birth {0}, matching length {1}, hostile resident {2}",
                birthLength, fed.Body.Count, stressed.Id);

            var imprintWorld = new World(140, 10, 249, null);
            Snake marker = imprintWorld.Snakes.Single();
            marker.Genes[Gene.Imprint] = 0.065;
            var random = new SaverRandom(32100);
            for (int index = 0; index < 1400; index++)
                imprintWorld.Stamp(marker, new Cell(index % 140, index / 140), 0, random);

            TestRunner.Check(imprintWorld.Imprints.Count > 0 && imprintWorld.Imprints.Count < 25,
                "Low first-generation imprint expression makes crossings mostly leave no mark");
            double strength = imprintWorld.Imprints.Values.Max(tint => tint.Alpha);
            TestRunner.Check(strength > 0.01 && strength < 0.03, "An early imprint is only a slight tint");
            var field = new ColorField(ColorGrid.Uniform(140, 10, Rgb.StartingGreen), null, imprintWorld.Imprints);
            TestRunner.Check(field.Tints(true).Count() == imprintWorld.Imprints.Count && !field.Tints(false).Any(),
                "The debug view exposes ordinary imprints");
            Console.WriteLine("Imprints {0}/1400, alpha {1:F4}", imprintWorld.Imprints.Count, strength);

            var arrivals = new World(16, 12, 3, null);
            arrivals.Snakes[0].Genes[Gene.VisitorArrival] = 0.1;
            var schedule = new SaverRandom(7334);
            for (int second = 0; second < 20000; second++)
                arrivals.TryVisitor(schedule, 0, 1);
            int visitors = arrivals.Snakes.Count(snake => !snake.IsResident);
            TestRunner.Check(visitors > 0 && visitors < 12, "The time-based arrival draw permits rare visits");
            Console.WriteLine("Visitor draws {0}/20000 seconds", visitors);
        }
    }
}
