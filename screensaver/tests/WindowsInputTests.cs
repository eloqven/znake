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
        }

        private static WindowsDesktop.RawInputData Read(IntPtr data)
        {
            return (WindowsDesktop.RawInputData)Marshal.PtrToStructure(data, typeof(WindowsDesktop.RawInputData));
        }
    }
}
