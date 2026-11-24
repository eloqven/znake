using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ZnakeSmoke
{
    public sealed class Fixture : Form
    {
        private readonly Timer timer = new Timer();
        private const int PassiveWindow = 0x08000020;
        private readonly Patch patch;
        private readonly bool corner;
        private bool blue;

        public Fixture(bool corner)
        {
            this.corner = corner;
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            int centerX = screen.Left + screen.Width / 2;
            int centerY = screen.Top + screen.Height / 2;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Bounds = corner ? new Rectangle(work.Right - 352, work.Bottom - 184, 336, 168)
                : new Rectangle(centerX - 220, centerY - 110, 440, 220);
            Text = "Znake live color fixture";
            Rectangle marker = corner ? new Rectangle(Bounds.Right - 52, Bounds.Top + 6, 36, 36)
                : new Rectangle(centerX + 116, centerY - 18, 36, 36);
            patch = new Patch(marker, corner);
            timer.Interval = 650;
            timer.Tick += delegate { PaintNextColor(); };
            PaintNextColor();
        }

        protected override bool ShowWithoutActivation { get { return corner; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                if (corner) parameters.ExStyle |= PassiveWindow;
                return parameters;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            patch.Show(this);
            timer.Start();
        }

        private void PaintNextColor()
        {
            blue = !blue;
            BackColor = blue ? Color.FromArgb(29, 78, 133) : Color.FromArgb(176, 70, 83);
            Invalidate();
            Update();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                patch.Dispose();
            }
            base.Dispose(disposing);
        }

        public static bool OnDefaultDesktop()
        {
            IntPtr desktop = OpenInputDesktop(0, false, 1);
            if (desktop == IntPtr.Zero) return false;
            try
            {
                var name = new StringBuilder(120);
                uint length;
                return GetUserObjectInformation(desktop, 2, name, (uint)name.Capacity * 2, out length)
                    && name.ToString() == "Default";
            }
            finally { CloseDesktop(desktop); }
        }

        public static uint IdleMilliseconds()
        {
            var info = new LastInput { Size = 8 };
            return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Time) : 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LastInput { public uint Size, Time; }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LastInput info);

        public static int GetSetting(uint action)
        {
            int value = 0;
            if (!SystemParametersInfo(action, 0, ref value, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return value;
        }

        public static void SetSetting(uint action, int value)
        {
            if (!SystemParametersInfo(action, (uint)value, IntPtr.Zero, 3))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public static bool RequestClose(IntPtr window, uint processId)
        {
            uint ownerId;
            const uint CloseMessage = 0x0010;
            return GetWindowThreadProcessId(window, out ownerId) != 0 && ownerId == processId &&
                PostMessage(window, CloseMessage, IntPtr.Zero, IntPtr.Zero);
        }

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        public static void MovePointer()
        {
            mouse_event(1, 20, 0, 0, UIntPtr.Zero);
        }

        public static void PressDismissKey()
        {
            keybd_event(0x87, 0, 0, UIntPtr.Zero);
            keybd_event(0x87, 0, 2, UIntPtr.Zero);
        }

        private sealed class Patch : Form
        {
            private readonly bool corner;

            public Patch(Rectangle rectangle, bool corner)
            {
                this.corner = corner;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                Bounds = rectangle;
                BackColor = Color.FromArgb(129, 43, 157);
            }

            protected override bool ShowWithoutActivation { get { return corner; } }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams parameters = base.CreateParams;
                    parameters.ExStyle |= 0x00080000;
                    if (corner) parameters.ExStyle |= PassiveWindow;
                    return parameters;
                }
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                if (!SetLayeredWindowAttributes(Handle, 0, 255, 2))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint color, byte alpha, uint flags);
        [DllImport("user32.dll")]
        private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")]
        private static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value,
            uint size, out uint needed);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint parameter, ref int data, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint parameter, IntPtr data, uint flags);
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, int x, int y, uint data, UIntPtr extraInfo);
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
    }
}
