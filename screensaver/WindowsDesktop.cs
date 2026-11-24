using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Znake
{
    internal static class WindowsDesktop
    {
        internal const int LayeredWindowStyle = 0x00080000;
        public static Rectangle DisplayBounds
        {
            get { return new Rectangle(GetSystemMetrics(76), GetSystemMetrics(77),
                GetSystemMetrics(78), GetSystemMetrics(79)); }
        }

        public static bool IsCurrent()
        {
            if (!Environment.UserInteractive ||
                !HasName(GetProcessWindowStation(), "WinSta0") ||
                !HasName(GetThreadDesktop(GetCurrentThreadId()), "Default"))
                return false;

            IntPtr input = OpenInputDesktop(0, false, 1); // DESKTOP_READOBJECTS
            if (input == IntPtr.Zero)
                return false;
            try { return HasName(input, "Default"); }
            finally { CloseDesktop(input); }
        }

        private static bool HasName(IntPtr handle, string expected)
        {
            if (handle == IntPtr.Zero)
                return false;
            var name = new StringBuilder(64);
            uint required;
            return GetUserObjectInformation(handle, 2, name, (uint)name.Capacity * 2, out required)
                && String.Equals(name.ToString(), expected, StringComparison.OrdinalIgnoreCase);
        }

        public static bool ExcludeFromCapture(IntPtr window)
        {
            const int ExtendedStyle = -20;
           // On Windows 10, affinity needs a redirected surface when it is first set.
            // Apply it before the per-pixel layered surface is attached.
            int style = GetWindowLong(window, ExtendedStyle);
            SetWindowLong(window, ExtendedStyle, style & ~LayeredWindowStyle);
            bool excluded = SetWindowDisplayAffinity(window, 0x11);
            SetWindowLong(window, ExtendedStyle, style);
            return excluded && (GetWindowLong(window, ExtendedStyle) & LayeredWindowStyle) != 0;
        }

        public static bool RaiseOverlay(IntPtr window)
        {
            const uint NoMoveSizeActivate = 0x0013;
            return SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, NoMoveSizeActivate);
        }

        public static bool AttachPreview(IntPtr window, IntPtr parent)
        {
            const int WindowStyle = -16;
            const int Child = 0x40000000;
            const int Popup = unchecked((int)0x80000000);
            SetWindowLong(window, WindowStyle, (GetWindowLong(window, WindowStyle) & ~Popup) | Child);
            SetParent(window, parent);
            return GetParent(window) == parent;
        }

        public static Size ClientSize(IntPtr window)
        {
            NativeRect bounds;
            return GetClientRect(window, out bounds)
                ? new Size(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top)
                : Size.Empty;
        }

        public static Point Pointer()
        {
            Point point;
            return GetPhysicalCursorPos(out point) ? point : System.Windows.Forms.Cursor.Position;
        }

        private static Point CursorFallback() { return System.Windows.Forms.Cursor.Position; }

        [DllImport("user32.dll")]
        private static extern bool GetPhysicalCursorPos(out Point position);

        public static bool ObserveInput(IntPtr target)
        {
            const uint InputSink = 0x00000100;
            var devices = new[]
            {
                new RawDevice { Page = 1, Usage = 2, Flags = InputSink, Target = target },
                new RawDevice { Page = 1, Usage = 6, Flags = InputSink, Target = target }
            };
            return RegisterRawInputDevices(devices, (uint)devices.Length,
                (uint)Marshal.SizeOf(typeof(RawDevice)));
        }

        public static bool IsDismissalInput(IntPtr messageData)
        {
            const uint ReadInput = 0x10000003;
            uint size = (uint)Marshal.SizeOf(typeof(RawInput));
            uint headerSize = (uint)Marshal.SizeOf(typeof(RawHeader));
            RawInput input;
            uint result = GetRawInputData(messageData, ReadInput, out input, ref size, headerSize);
            return result != UInt32.MaxValue && result >= headerSize + 6 &&
                input.Data.Dismisses(input.Header.Type);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawDevice
        {
            public ushort Page, Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawHeader
        {
            public uint Type, Size;
            public IntPtr Device, Parameter;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInput
        {
            public RawHeader Header;
            public RawInputData Data;
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        internal struct RawInputData
        {
            [FieldOffset(2)] public ushort KeyboardFlags;
            [FieldOffset(4)] public ushort MouseButtonFlags;

            public bool Dismisses(uint deviceType)
            {
                const uint Mouse = 0, Keyboard = 1;
                const ushort KeyBreak = 1;
                if (deviceType == Keyboard)
                    return (KeyboardFlags & KeyBreak) == 0;

                // Pointer movement is measured separately from raw button and wheel events.
                return deviceType == Mouse && MouseButtonFlags != 0;
            }
        }

        [DllImport("user32.dll")]
        internal static extern bool IsWindow(IntPtr handle);
        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr handle);
        [DllImport("user32.dll")]
        private static extern IntPtr SetParent(IntPtr window, IntPtr parent);
        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(IntPtr handle);
        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr handle, out NativeRect bounds);
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")]
        private static extern IntPtr GetProcessWindowStation();
        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDesktop(uint thread);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")]
        private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")]
        private static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetUserObjectInformation(IntPtr handle, int index,
            StringBuilder name, uint size, out uint required);
        [DllImport("user32.dll")]
        private static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint deviceSize);
        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr handle, uint command,
            out RawInput data, ref uint size, uint headerSize);
        [DllImport("user32.dll")]
        internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y,
            int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr handle, uint affinity);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr handle, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr handle, int index, int style);
    }

    internal sealed class DibSurface : IDisposable
    {
        private readonly Size size;
        private IntPtr dc;
        private IntPtr dib;
        private IntPtr previous;
        private IntPtr pixels;

        public Bitmap Image { get; private set; }

        public DibSurface(Size size)
        {
            this.size = size;
            try
            {
                var header = new DibHeader
                {
                    Size = (uint)Marshal.SizeOf(typeof(DibHeader)),
                    Width = size.Width,
                    Height = -size.Height,
                    Planes = 1,
                    Depth = 32
                };
                dc = CreateCompatibleDC(IntPtr.Zero);
                if (dc == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                dib = CreateDIBSection(dc, ref header, 0, out pixels, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                previous = SelectObject(dc, dib);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                Image = new Bitmap(size.Width, size.Height, checked(size.Width * 4),
                    PixelFormat.Format32bppPArgb, pixels);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public bool CopyDesktop(Point origin)
        {
            IntPtr desktop = GetDC(IntPtr.Zero);
            if (desktop == IntPtr.Zero)
                return false;
            try
            {
                const uint SourceCopy = 0x00CC0020;
                const uint CaptureLayered = 0x40000000;
                return BitBlt(dc, 0, 0, size.Width, size.Height, desktop, origin.X, origin.Y,
                    SourceCopy | CaptureLayered) && GdiFlush();
            }
            finally { ReleaseDC(IntPtr.Zero, desktop); }
        }

        public void ReadPixels(int[] destination)
        {
            Marshal.Copy(pixels, destination, 0, checked(size.Width * size.Height));
        }

        public void DrawInvertedBorder(int[] desktop)
        {
            int width = size.Width;
            int bottom = (size.Height - 1) * width;
            for (int column = 0; column < width; column++)
            {
                InvertBorderPixel(desktop, column);
                InvertBorderPixel(desktop, bottom + column);
            }
            for (int row = width; row < bottom; row += width)
            {
                InvertBorderPixel(desktop, row);
                InvertBorderPixel(desktop, row + width - 1);
            }
        }

        private void InvertBorderPixel(int[] desktop, int index)
        {
            const int Opaque = unchecked((int)0xFF000000);
            Marshal.WriteInt32(pixels, checked(index * 4), Opaque | (~desktop[index] & 0xFFFFFF));
        }

        public int RgbAt(Point point)
        {
            if (point.X < 0 || point.Y < 0 || point.X >= size.Width || point.Y >= size.Height)
                return -1;
            return Marshal.ReadInt32(pixels, checked((point.Y * size.Width + point.X) * 4)) & 0xFFFFFF;
        }

        public int AlphaAt(Point point)
        {
            return Marshal.ReadByte(pixels, checked((point.Y * size.Width + point.X) * 4 + 3));
        }

        public bool Publish(IntPtr window, Point origin)
        {
            Size extent = size;
            Point source = Point.Empty;
            var alpha = new Blend { Opacity = 255, Format = 1 };
            return UpdateLayeredWindow(window, IntPtr.Zero, ref origin, ref extent, dc, ref source,
                0, ref alpha, 2); // ULW_ALPHA
        }

        public void Dispose()
        {
            if (Image != null) { Image.Dispose(); Image = null; }
            if (previous != IntPtr.Zero && previous != new IntPtr(-1))
                SelectObject(dc, previous);
            if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
            if (dc != IntPtr.Zero) { DeleteDC(dc); dc = IntPtr.Zero; }
            previous = IntPtr.Zero;
            pixels = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Blend { public byte Operation, Flags, Opacity, Format; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DibHeader
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, Depth;
            public uint Compression, ImageSize;
            public int HorizontalPixelsPerMeter, VerticalPixelsPerMeter;
            public uint Colors, ImportantColors;
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr source);
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(IntPtr dc, ref DibHeader header, uint mode,
            out IntPtr pixels, IntPtr section, uint offset);
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")]
        private static extern bool GdiFlush();
        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height,
            IntPtr src, int sourceX, int sourceY, uint operation);
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen,
            ref Point location, ref Size size, IntPtr source, ref Point offset, uint key,
            ref Blend blend, uint flags);
    }
}
