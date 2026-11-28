using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace Znake.ControlPanel
{
    /// <summary>
    /// Step 3 — render: launches ShowReplay, writes render.json, updates history.
    /// </summary>
    internal sealed class RenderStep : UserControl
    {
        private AppSettings settings;
        private readonly HistoryStore history;
        private string sourceFolder;

        // controls
        private readonly ComboBox cmbFps     = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        private readonly ComboBox cmbSpeed   = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        private readonly CheckBox chkVision  = new CheckBox { Text = "Vision overlay", Checked = true };
        private readonly ComboBox cmbEncoder = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        private readonly TextBox  txtMp4     = new TextBox  { ReadOnly = false };
        private readonly Button   btnBrowseMp4 = new Button { Text = "...", Width = 28 };
        private readonly Button   btnRender  = new Button { Text = "▶ Render", Tag = "accent" };
        private readonly Button   btnPlay    = new Button { Text = "▶ Play" };
        private readonly Label    lblStatus  = new Label  { AutoSize = true };

        public event EventHandler RenderComplete;

        public RenderStep(AppSettings settings, HistoryStore history)
        {
            this.settings = settings;
            this.history  = history;
            Build();
        }

        public void ApplySettings(AppSettings s) { settings = s; SyncRenderSettings(); }

        public void ApplyTheme(ThemePalette palette) { ThemePalette.Apply(this, palette); }

        public void SetSourceFolder(string folder)
        {
            sourceFolder = folder;
            if (!String.IsNullOrEmpty(folder))
                txtMp4.Text = Path.Combine(folder, "output.mp4");
            btnRender.Enabled = !String.IsNullOrEmpty(folder) && Directory.Exists(folder);
        }

        // ── Launch render ────────────────────────────────────────────────────

        private void StartRender()
        {
            if (String.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
            {
                lblStatus.Text = "No source folder set.";
                return;
            }

            string logPath = Path.Combine(sourceFolder, "show.log.jsonl");
            if (!File.Exists(logPath))
            {
                lblStatus.Text = "show.log.jsonl not found.";
                return;
            }

            string mp4 = txtMp4.Text.Trim();
            if (String.IsNullOrEmpty(mp4))
                mp4 = Path.Combine(sourceFolder, "output.mp4");

            double fps   = SelectedFps();
            double speed = SelectedSpeed();
            bool vision  = chkVision.Checked;
            string enc   = cmbEncoder.SelectedItem != null ? cmbEncoder.SelectedItem.ToString() : "auto";

            string replay = settings.ResolveReplayExe();
            string args = BuildReplayArgs(logPath, mp4, fps, speed, enc, vision);

            var psi = new ProcessStartInfo(replay, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError  = false,
                CreateNoWindow = true
            };

            lblStatus.Text = "Rendering…";
            btnRender.Enabled = false;
            btnPlay.Enabled   = false;

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.Exited += delegate(object s2, EventArgs e2)
            {
                int code = proc.ExitCode;
                proc.Dispose();
                if (InvokeRequired)
                    BeginInvoke(new Action(delegate() { OnRenderExited(code, mp4, fps, speed, vision, enc); }));
                else
                    OnRenderExited(code, mp4, fps, speed, vision, enc);
            };

            try { proc.Start(); }
            catch (Exception ex)
            {
                btnRender.Enabled = true;
                lblStatus.Text = "Error: " + ex.Message;
            }
        }

        private void OnRenderExited(int exitCode, string mp4, double fps, double speed, bool vision, string enc)
        {
            btnRender.Enabled = true;
            if (exitCode == 0 && File.Exists(mp4))
            {
                lblStatus.Text = "Render complete: " + mp4;
                btnPlay.Enabled = true;

                // write render.json and update history
                var sidecar = new RenderSidecar
                {
                    Fps           = fps,
                    Speed         = speed,
                    VisionOverlay = vision,
                    Encoder       = enc,
                    Mp4File       = mp4,
                    RenderedUtc   = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
                };
                if (!String.IsNullOrEmpty(sourceFolder))
                    history.RecordRender(sourceFolder, sidecar);

                // persist to AppSettings
                settings.RenderFps     = fps;
                settings.PlaybackSpeed = speed;
                settings.BurnVisionOverlay = vision;
                settings.EncoderMode   = enc;

                if (RenderComplete != null) RenderComplete(this, EventArgs.Empty);
            }
            else
            {
                lblStatus.Text = "Render failed (exit " + exitCode + ").";
            }
        }

        private void PlayMp4()
        {
            string mp4 = txtMp4.Text.Trim();
            if (String.IsNullOrEmpty(mp4) || !File.Exists(mp4))
            {
                lblStatus.Text = "MP4 file not found.";
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(mp4) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Cannot open: " + ex.Message;
            }
        }

        public void Render()
        {
            btnRender.PerformClick();
        }

        public void Play()
        {
            PlayMp4();
        }

        internal static string BuildReplayArgs(string logPath, string mp4, double fps, double speed,
            string encoder, bool vision)
        {
            string args = "--log \"" + logPath + "\""
                + " --out \"" + mp4 + "\""
                + " --fps " + fps.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                + " --speed " + speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                + " --encoder " + encoder;
            if (vision) args += " --vision";
            return args;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private double SelectedFps()
        {
            object item = cmbFps.SelectedItem;
            if (item == null) return 12.5;
            double v;
            return Double.TryParse(item.ToString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 12.5;
        }

        private double SelectedSpeed()
        {
            object item = cmbSpeed.SelectedItem;
            if (item == null) return 1.0;
            double v;
            return Double.TryParse(item.ToString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 1.0;
        }

        // ── Build UI ─────────────────────────────────────────────────────────

        private void SyncRenderSettings()
        {
            SelectItem(cmbFps, settings.RenderFps.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), "12.5");
            SelectItem(cmbSpeed, settings.PlaybackSpeed.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), "1");
            SelectItem(cmbEncoder, settings.EncoderMode, "auto");
            chkVision.Checked = settings.BurnVisionOverlay;
        }

        private static void SelectItem(ComboBox combo, string value, string fallback)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (String.Equals(combo.Items[i].ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (String.Equals(combo.Items[i].ToString(), fallback, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private void Build()
        {
            cmbFps.Items.AddRange(new object[] { "12.5", "30" });
            cmbSpeed.Items.AddRange(new object[] { "1", "2", "3", "5", "8", "13", "21" });
            cmbEncoder.Items.AddRange(new object[] { "auto", "cpu", "nvenc", "amf", "qsv" });
            SyncRenderSettings();

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true,
                Padding = new Padding(4)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            int row = 0;
            AddRow(layout, row++, "Output FPS:",   cmbFps);
            AddRow(layout, row++, "Speed:",        cmbSpeed);
            AddRow(layout, row++, "",              chkVision);
            AddRow(layout, row++, "Encoder:",      cmbEncoder);
            AddRow(layout, row++, "MP4 path:",     BuildInlineRow(txtMp4, btnBrowseMp4));

            var btnRow = new FlowLayoutPanel { AutoSize = true };
            btnRow.Controls.Add(btnRender);
            btnRow.Controls.Add(btnPlay);
            layout.Controls.Add(new Label(), 0, row);
            layout.Controls.Add(btnRow, 1, row);
            row++;

            layout.Controls.Add(new Label(), 0, row);
            layout.Controls.Add(lblStatus, 1, row);

            Controls.Add(layout);

            btnRender.Enabled = false;
            btnPlay.Enabled   = false;

            btnBrowseMp4.Click += delegate(object s, EventArgs e)
            {
                using (var dlg = new SaveFileDialog { Title = "Save MP4", Filter = "MP4|*.mp4" })
                    if (dlg.ShowDialog() == DialogResult.OK)
                        txtMp4.Text = dlg.FileName;
            };
            btnRender.Click += delegate(object s, EventArgs e) { StartRender(); };
            btnPlay.Click   += delegate(object s, EventArgs e) { PlayMp4(); };
        }

        private static void AddRow(TableLayoutPanel layout, int row, string labelText, Control control)
        {
            while (layout.RowStyles.Count <= row) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = labelText, AutoSize = true, Anchor = AnchorStyles.Right }, 0, row);
            control.Dock = DockStyle.Fill;
            layout.Controls.Add(control, 1, row);
        }

        private static Panel BuildInlineRow(params Control[] controls)
        {
            var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            foreach (var c in controls) p.Controls.Add(c);
            return p;
        }
    }
}
