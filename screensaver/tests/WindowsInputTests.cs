using System;
using System.Runtime.InteropServices;

namespace Znake.Tests
{
    internal static class WindowsInputTests
    {
        public static void Run()
        {
            IntPtr data = Marshal.AllocHGlobal(24);
            try
            {
                Marshal.Copy(new byte[24], 0, data, 24);
                Marshal.WriteInt16(data, 0, 0x1E);
                Marshal.WriteInt16(data, 2, 1);
                TestRunner.Check(!Read(data).Dismisses(1), "Key release ignores an even scan code");
                Marshal.WriteInt16(data, 0, 0x1F);
                Marshal.WriteInt16(data, 2, 0);
                TestRunner.Check(Read(data).Dismisses(1), "Key press dismisses with an odd scan code");
                Marshal.WriteInt16(data, 2, 2);
                TestRunner.Check(Read(data).Dismisses(1), "An extended key press still dismisses");
                Marshal.WriteInt16(data, 2, 3);
                TestRunner.Check(!Read(data).Dismisses(1), "An extended key release is ignored");
                TestRunner.Check(!Read(data).Dismisses(0), "A stationary mouse report is ignored");
                Marshal.WriteInt16(data, 4, 1);
                TestRunner.Check(Read(data).Dismisses(0), "A mouse button report dismisses");
            }
            finally { Marshal.FreeHGlobal(data); }
            ThreeStandaloneWindowsTaps();
        }

        private static void ThreeStandaloneWindowsTaps()
        {
            var taps = new WindowsTapSequence();
            TestRunner.Check(!Tap(taps, false, 0) && !Tap(taps, true, 150) && Tap(taps, false, 300),
                "Either Windows key counts toward three quick taps");
            TestRunner.Check(!Tap(taps, false, 1000) && !Tap(taps, false, 1200), "Two taps do not resume the snake");
            taps.Key(false, true, 1400);
            taps.CancelTaps();
            TestRunner.Check(!taps.Key(false, false, 1470) && !Tap(taps, false, 1550),
                "Using a Windows-key chord clears the tap sequence");
            TestRunner.Check(!Tap(taps, false, 2500) && !Tap(taps, false, 2700) && Tap(taps, false, 2900),
                "A gap starts a new sequence of three taps");
        }

        private static bool Tap(WindowsTapSequence taps, bool right, long milliseconds)
        {
            taps.Key(right, true, milliseconds);
            return taps.Key(right, false, milliseconds + 45);
        }

        private static WindowsDesktop.RawInputData Read(IntPtr data)
        {
            return (WindowsDesktop.RawInputData)Marshal.PtrToStructure(data, typeof(WindowsDesktop.RawInputData));
        }
    }
}
