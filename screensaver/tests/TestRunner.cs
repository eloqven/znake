using System;

namespace Znake.Tests
{
    internal static class TestRunner
    {
        private static int tests;

        private static int Main()
        {
            try
            {
                LightTests.Run();
                SnakeTests.Run();
                EcologyTests.Run();
                StateTests.Run();
                WindowsInputTests.Run();
                CalibrationTests.Run();
                Console.WriteLine("PASS {0} deterministic screensaver checks", tests);
                return 0;
            }
            catch (Exception failure)
            {
                Console.Error.WriteLine(failure);
                return 1;
            }
        }

        public static void Check(bool condition, string scenario)
        {
            if (!condition) throw new InvalidOperationException(scenario);
            tests++;
        }

        public static void Near(double actual, double expected, double tolerance, string scenario)
        {
            Check(Math.Abs(actual - expected) <= tolerance,
                scenario + ". Expected " + expected + ", got " + actual);
        }
    }
}
