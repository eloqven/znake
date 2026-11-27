using System;
using System.Drawing;
using System.Windows.Forms;

namespace Znake.ControlPanel
{
    internal enum ThemeMode
    {
        Dark = 0,
        Light = 1
    }

    internal sealed class ThemePalette
    {
        public ThemeMode Mode;
        public Color Surface;
        public Color Raised;
        public Color Input;
        public Color Border;
        public Color Ink;
        public Color Muted;
        public Color Accent;
        public Color AccentInk;
        public Color Danger;
        public Color Warning;
        public Color GridBackdrop;

        public static ThemePalette For(ThemeMode mode)
        {
            if (mode == ThemeMode.Light)
            {
                return new ThemePalette
                {
                    Mode = ThemeMode.Light,
                    Surface = Color.FromArgb(244, 246, 244),
                    Raised = Color.FromArgb(255, 255, 255),
                    Input = Color.FromArgb(250, 251, 250),
                    Border = Color.FromArgb(204, 212, 206),
                    Ink = Color.FromArgb(24, 32, 28),
                    Muted = Color.FromArgb(92, 106, 98),
                    Accent = Color.FromArgb(32, 134, 84),
                    AccentInk = Color.White,
                    Danger = Color.FromArgb(182, 48, 48),
                    Warning = Color.FromArgb(172, 112, 18),
                    GridBackdrop = Color.FromArgb(228, 234, 230)
                };
            }
            return new ThemePalette
            {
                Mode = ThemeMode.Dark,
                Surface = Color.FromArgb(22, 27, 31),
                Raised = Color.FromArgb(34, 42, 48),
                Input = Color.FromArgb(26, 33, 38),
                Border = Color.FromArgb(56, 68, 76),
                Ink = Color.FromArgb(232, 241, 238),
                Muted = Color.FromArgb(155, 175, 177),
                Accent = Color.FromArgb(135, 220, 173),
                AccentInk = Color.FromArgb(14, 22, 18),
                Danger = Color.FromArgb(232, 102, 102),
                Warning = Color.FromArgb(236, 186, 92),
                GridBackdrop = Color.FromArgb(14, 18, 20)
            };
        }

        public static void Apply(Control root, ThemePalette palette)
        {
            if (root == null || palette == null) return;
            ApplySingle(root, palette);
            foreach (Control child in root.Controls)
                Apply(child, palette);
        }

        private static void ApplySingle(Control control, ThemePalette palette)
        {
            if (control is Form || control is UserControl || control is TableLayoutPanel ||
                control is FlowLayoutPanel || control is SplitContainer)
            {
                control.BackColor = palette.Surface;
                control.ForeColor = palette.Ink;
                return;
            }
            if (control is Panel || control is GroupBox)
            {
                string tag = control.Tag as string;
                control.BackColor = tag == "raised" ? palette.Raised :
                    tag == "grid" ? palette.GridBackdrop : palette.Surface;
                control.ForeColor = palette.Ink;
                return;
            }
            if (control is Button)
            {
                var button = (Button)control;
                string tag = button.Tag as string;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = palette.Border;
                if (tag == "accent")
                {
                    button.BackColor = palette.Accent;
                    button.ForeColor = palette.AccentInk;
                }
                else if (tag == "danger")
                {
                    button.BackColor = palette.Danger;
                    button.ForeColor = Color.White;
                }
                else
                {
                    button.BackColor = palette.Raised;
                    button.ForeColor = palette.Ink;
                }
                return;
            }
            if (control is TextBoxBase || control is ComboBox || control is NumericUpDown || control is ListBox)
            {
                control.BackColor = palette.Input;
                control.ForeColor = palette.Ink;
                return;
            }
            if (control is ListView)
            {
                control.BackColor = palette.Raised;
                control.ForeColor = palette.Ink;
                return;
            }
            if (control is Label)
            {
                string tag = control.Tag as string;
                if (tag == "muted") control.ForeColor = palette.Muted;
                else if (tag == "accent") control.ForeColor = palette.Accent;
                else if (tag == "danger") control.ForeColor = palette.Danger;
                else if (tag == "warning") control.ForeColor = palette.Warning;
                else control.ForeColor = palette.Ink;
                return;
            }
            if (control is CheckBox || control is RadioButton)
            {
                control.ForeColor = palette.Ink;
            }
        }
    }
}
