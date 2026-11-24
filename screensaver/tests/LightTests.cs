using System;
using System.Collections.Generic;

namespace Znake.Tests
{
    internal static class LightTests
    {
        public static void Run()
        {
            var random = new SaverRandom(227);
            var counts = new Dictionary<int, int> { { 3, 0 }, { 5, 0 }, { 8, 0 }, { 13, 0 } };
            for (int sample = 0; sample < 6000; sample++)
            {
                Genome genome = Genome.Roll(random, null);
                counts[genome.BirthLength(random)]++;
            }
            TestRunner.Check(counts[5] > counts[3] && counts[3] > counts[8] &&
                counts[8] > counts[13] && counts[13] > 0 && counts[13] < 60,
                "Default independent birth weights favor 5 and make 13 rare");
            Console.WriteLine("Birth draws 3={0}, 5={1}, 8={2}, 13={3}", counts[3], counts[5], counts[8], counts[13]);

            var overrideValues = new Dictionary<Gene, double>
            {
                { Gene.NightBoost, 0.7 }, { Gene.SprintDuration, 0.5 },
                { Gene.Length3, 0 }, { Gene.Length5, 0 }, { Gene.Length8, 0 }, { Gene.Length13, 1 }
            };
            Genome configured = Genome.Roll(new SaverRandom(421), overrideValues);
            TestRunner.Check(configured.BirthLength(random) == 13, "An explicit birth weight selects length 13");
            TestRunner.Near(configured.Express(Gene.SprintDuration, 0, 0), 0.5, 1e-9, "Day expression");
            TestRunner.Near(configured.Express(Gene.SprintDuration, 0, 1), 0.85, 1e-9, "Night expression");

            var noon = new DateTimeOffset(2025, 3, 20, 12, 0, 0, TimeSpan.Zero);
            TestRunner.Near(Daylight.NightAt(noon, 0, 0), 0, 1e-9, "Equatorial noon");
            TestRunner.Check(Daylight.NightAt(noon.AddHours(12), 0, 0) > 0.97, "Local solar midnight");
            TestRunner.Near(Daylight.NightAt(noon, null, null), 0, 1e-9, "Daytime expression with no location");

            Rgb green = Rgb.StartingGreen;
            Rgb opposite = Rgb.FromBytes(225, 22, 170);
            Rgb neutral = Rgb.Mix(green, opposite, 0.50);
            LightResponse match = LightResponse.Sense(green, green);
            LightResponse bad = LightResponse.Sense(green, opposite);
            LightResponse middle = LightResponse.Sense(green, neutral);
            TestRunner.Check(match.Feed > 0.99 && match.Pressure == 0, "Matching light feeds and calms");
            TestRunner.Check(bad.Feed == 0 && bad.Pressure > 0, "Opposite light yields stress without energy");
            TestRunner.Check(middle.Feed == 0 && middle.Pressure == 0, "Neutral band leaves both quantities steady");

            var raw = ColorGrid.Uniform(5, 2, green);
            raw[new Cell(0, 0)] = opposite;
            var field = new ColorField(raw, null, null);
            var shortBody = new[] { new Cell(0, 0), new Cell(1, 0) };
            var longBody = new[] { new Cell(0, 0), new Cell(1, 0), new Cell(2, 0), new Cell(3, 0) };
            TestRunner.Check(Rgb.Distance(green, field.Average(longBody)) < Rgb.Distance(green, field.Average(shortBody)),
                "One mismatching square counts less on a longer body");

            var imprint = new Tint(green, 0.02);
            field.Add(new Cell(0, 0), imprint, true);
            Rgb effective = field.At(new Cell(0, 0));
            TestRunner.Check(Rgb.Distance(green, effective) < Rgb.Distance(green, opposite), "An imprint affects later light");
            TestRunner.Near(field.DebugTint(new Cell(0, 0)).Alpha, 0.02, 1e-9, "Debug shows the same imprint weight");
        }
    }
}
