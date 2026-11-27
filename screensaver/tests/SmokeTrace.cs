using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace Znake
{
    internal static class SmokeTrace
    {
        private static bool sawBlue, sawRed, sawPatch, sawMovement, sawOverlay, sawHitTest, sawAlpha;

        public static Rectangle? ReadBounds()
        {
            string value = Environment.GetEnvironmentVariable("ZNAKE_SMOKE_BOUNDS");
            if (value == null) return null;
            string[] coordinates = value.Split(',');
            var pixels = new int[4];
            if (coordinates.Length != pixels.Length) return Rectangle.Empty;
            for (int index = 0; index < pixels.Length; index++)
                if (!Int32.TryParse(coordinates[index], out pixels[index])) return Rectangle.Empty;
            return new Rectangle(pixels[0], pixels[1], pixels[2], pixels[3]);
        }

        public static void Started(IntPtr overlay, World world, bool debug)
        {
            Write("started desktop=Default pid=" + Process.GetCurrentProcess().Id +
                " hwnd=" + overlay.ToInt64() + " dpi=" + WindowsDesktop.GetDpiForWindow(overlay) + " debug=" + (debug ? "on" : "off"));
            if (world != null) Write("resumed " + Identity(world));
        }

        public static void Saved(World world) { Write("saved " + Identity(world)); }
        public static void Preview(bool attached) { Write("preview parent=" + (attached ? 1 : 0)); }
        public static void Dismissed(string input) { Write("dismissed " + input); }
        public static void Error(string message) { Write("error " + message); }

        public static void Frame(IntPtr window, Rectangle bounds, DibSurface desktop, DibSurface painted,
            Rectangle previous, int previousColor, Rectangle next, Point patch, bool corner)
        {
            if (!previous.IsEmpty)
            {
                int color = desktop.RgbAt(Center(previous));
                if (!sawOverlay && color == previousColor) { sawOverlay = true; Write("covered=self"); }
                if (!sawBlue && color == 0x1d4e85) { sawBlue = true; Write("covered=blue"); }
                if (!sawRed && color == 0xb04653) { sawRed = true; Write("covered=red"); }
                if (!sawMovement && previous.Location != next.Location) { sawMovement = true; Write("draw=changed"); }
            }

            Point center = Center(next);
            if (!sawAlpha)
            {
                sawAlpha = true;
                Write("body alpha=" + painted.AlphaAt(center));
            }
            if (!sawHitTest && painted.AlphaAt(center) == 255 && painted.AlphaAt(patch) == 0 &&
                WindowsDesktop.IsWindowVisible(window) &&
                HitTest(window, new Point(bounds.X + center.X, bounds.Y + center.Y), corner))
            {
                sawHitTest = true;
                Write("shape=opaque, surrounding=transparent");
                if (corner) Write("corner=nonactivating bounds=" + bounds);
            }

            if (!sawPatch && desktop.RgbAt(patch) == 0x812b9d)
            {
                sawPatch = true;
                Write("layered=visible");
            }
        }

        private static bool HitTest(IntPtr window, Point position, bool corner)
        {
            bool ownsHit = WindowsDesktop.WindowFromPoint(position) == window;
            return corner ? !ownsHit && GetForegroundWindow() != window : ownsHit;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private static Point Center(Rectangle rectangle)
        {
            return new Point(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2);
        }

        private static string Identity(World world)
        {
            Snake resident = world.Snakes.Find(snake => snake.IsResident);
            return "tick=" + world.Ticks + " resident=" + resident.Id + " rng=" + world.RandomState;
        }

        private static void Write(string message)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "windows-smoke.log");
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            using (var writer = new StreamWriter(stream))
                writer.WriteLine(message);
        }
    }
}
