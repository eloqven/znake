using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Znake.ControlPanel
{
    /// <summary>
    /// Step 1 — setup: background, dimensions, cast, seed, duration, preset sidebar.
    /// </summary>
    internal sealed class SetupStep : UserControl
    {
        private AppSettings settings;
        private readonly PresetStore presets;
        private bool suppressEvents;

        // ── controls ─────────────────────────────────────────────────────────
        private readonly TextBox txtBackground   = new TextBox { ReadOnly = false };
        private readonly Button  btnBrowseBg     = new Button  { Text = "...", Width = 28 };
        private readonly Label   lblDims         = new Label   { Text = "", AutoSize = true, Tag = "muted" };
        private readonly ComboBox cmbZoom        = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
        private readonly NumericUpDown nudCast   = new NumericUpDown { Minimum = 1, Maximum = 9999, DecimalPlaces = 0 };
        private readonly NumericUpDown nudSeed   = new NumericUpDown { Minimum = 0, Maximum = uint.MaxValue, DecimalPlaces = 0 };
        private readonly Button  btnRandomSeed   = new Button  { Text = "🎲", Width = 36 };
        private readonly TextBox txtSavedSnake   = new TextBox { ReadOnly = false };
        private readonly Button  btnBrowseSnake  = new Button  { Text = "...", Width = 28 };
        private readonly NumericUpDown nudDuration = new NumericUpDown { Minimum = 1, Maximum = 86400 * 7, DecimalPlaces = 0 };
        private readonly NumericUpDown nudSnapshot = new NumericUpDown { Minimum = 1, Maximum = 60, DecimalPlaces = 1, Increment = 0.5m };
        private readonly NumericUpDown nudPace    = new NumericUpDown { Minimum = 0, Maximum = 1000, DecimalPlaces = 1, Increment = 0.1m };
        private readonly ComboBox cmbSpawn       = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        private readonly TextBox txtOutputRoot   = new TextBox { ReadOnly = false };
        private readonly Button  btnBrowseOut    = new Button  { Text = "...", Width = 28 };
        private readonly TextBox txtCliPreview    = new TextBox { ReadOnly = true, Multiline = true, Height = 56, ScrollBars = ScrollBars.Vertical };
        private readonly Button  btnDryRun        = new Button  { Text = "Dry Run" };
        private readonly Label   lblDryResult     = new Label   { Text = "", AutoSize = true };

        // preset sidebar
        private readonly ListBox lstPresets       = new ListBox { Width = 160 };
        private readonly TextBox txtPresetName    = new TextBox { Width = 120 };
        private readonly Button  btnPresetSave    = new Button  { Text = "Save" };
        private readonly Button  btnPresetRename  = new Button  { Text = "Rename" };
        private readonly Button  btnPresetDelete  = new Button  { Text = "Delete" };
        private readonly Button  btnPresetLoad    = new Button  { Text = "Load" };

        public event EventHandler SettingsChanged;

        public SetupStep(AppSettings settings, PresetStore presets)
        {
            this.settings = settings;
            this.presets  = presets;
            Build();
            LoadFromSettings();
        }

        // ── Public API ───────────────────────────────────────────────────────

        public void ApplyTheme(ThemePalette palette)
        {
            ThemePalette.Apply(this, palette);
        }

        public string ResolveOutputFolder()
        {
            return ResolveOutputFolder(settings.Setup.OutputRoot, settings.Setup.Seed, DateTime.Now);
        }

        public static string ResolveOutputFolder(string root, uint seed, DateTime now)
        {
            if (String.IsNullOrWhiteSpace(root)) root = SetupConfig.CreateDefault().OutputRoot;
            string stamp = now.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture);
            string baseName = "seed-" + seed + "-" + stamp;
            string candidate = Path.Combine(root, baseName);
            if (!Directory.Exists(candidate)) return candidate;
            for (int suffix = 2; suffix <= 999; suffix++)
            {
                string alt = candidate + "-" + suffix;
                if (!Directory.Exists(alt)) return alt;
            }
            return candidate + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        public string BuildRunnerArgs(string outputFolder, string stopFilePath)
        {
            return BuildRunnerArgs(settings.Setup, outputFolder, stopFilePath, false, false);
        }

        internal static string BuildRunnerArgs(SetupConfig cfg, string outputFolder, string stopFilePath,
            bool resume, bool validateOnly)
        {
            var sb = new StringBuilder();
            if (validateOnly) sb.Append("--validate-only ");
            sb.Append("--cast " + cfg.Cast);
            sb.Append(" --seed " + cfg.Seed);
            sb.Append(" --square " + cfg.Square);
            sb.Append(" --seconds " + cfg.DurationSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(" --snapshot " + cfg.SnapshotSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            if (cfg.Pace > 0)
                sb.Append(" --pace " + cfg.Pace.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(" --spawn " + cfg.SpawnMode);
            if (!String.IsNullOrWhiteSpace(cfg.SavedSnakePath))
                sb.Append(" --saved-snake \"" + cfg.SavedSnakePath + "\"");
            if (!String.IsNullOrWhiteSpace(cfg.BackgroundPath))
                sb.Append(" --background \"" + cfg.BackgroundPath + "\"");
            if (!String.IsNullOrWhiteSpace(outputFolder))
                sb.Append(" --folder \"" + outputFolder + "\"");
            if (!String.IsNullOrWhiteSpace(stopFilePath))
                sb.Append(" --stop-file \"" + stopFilePath + "\"");
            sb.Append(" --no-replay");
            if (resume) sb.Append(" --resume");
            return sb.ToString();
        }

        // ── Build UI ─────────────────────────────────────────────────────────

        private void Build()
        {
            cmbZoom.Items.AddRange(new object[] { 8, 13, 21, 34 });
            cmbZoom.SelectedIndex = 0;
            cmbSpawn.Items.AddRange(new object[] { "edge", "random" });
            cmbSpawn.SelectedIndex = 0;
            nudSnapshot.Value = 1;

            var main = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Margin = Padding.Empty,
                Padding = new Padding(4)
            };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var form = BuildFormPanel();
            var sidebar = BuildSidebarPanel();
            main.Controls.Add(form, 0, 0);
            main.Controls.Add(sidebar, 1, 0);
            Controls.Add(main);

            WireFormEvents();
            WirePresetEvents();
        }

        private Panel BuildFormPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 8, 0) };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = false,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            int row = 0;
            AddRow(layout, row++, "Background:", BuildInlineRow(txtBackground, btnBrowseBg, lblDims));
            AddRow(layout, row++, "Zoom (px/cell):", cmbZoom);
            AddRow(layout, row++, "Cast:", nudCast);
            AddRow(layout, row++, "Seed:", BuildInlineRow(nudSeed, btnRandomSeed));
            AddRow(layout, row++, "Saved snake:", BuildInlineRow(txtSavedSnake, btnBrowseSnake));
            AddRow(layout, row++, "Duration (s):", nudDuration);
            AddRow(layout, row++, "Snapshot (s):", nudSnapshot);
            AddRow(layout, row++, "Pace (0=max):", nudPace);
            AddRow(layout, row++, "Spawn:", cmbSpawn);
            AddRow(layout, row++, "Output root:", BuildInlineRow(txtOutputRoot, btnBrowseOut));

            var previewLabel = new Label { Text = "CLI preview:", AutoSize = true };
            layout.Controls.Add(previewLabel, 0, row);
            layout.Controls.Add(txtCliPreview, 1, row);
            row++;

            var dryRow = BuildInlineRow(btnDryRun, lblDryResult);
            layout.Controls.Add(new Label(), 0, row);
            layout.Controls.Add(dryRow, 1, row);

            panel.Controls.Add(layout);
            return panel;
        }

        private Panel BuildSidebarPanel()
        {
            var panel = new Panel { Width = 180, Dock = DockStyle.Right, Tag = "raised" };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(4)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // label
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // list
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // name box
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // save/rename
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // load/delete

            layout.Controls.Add(new Label { Text = "Presets", AutoSize = true, Tag = "accent" }, 0, 0);
            lstPresets.Dock = DockStyle.Fill;
            layout.Controls.Add(lstPresets, 0, 1);
            txtPresetName.Dock = DockStyle.Fill;
            layout.Controls.Add(txtPresetName, 0, 2);

            var row3 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            row3.Controls.Add(btnPresetSave);
            row3.Controls.Add(btnPresetRename);
            layout.Controls.Add(row3, 0, 3);

            var row4 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            row4.Controls.Add(btnPresetLoad);
            btnPresetDelete.Tag = "danger";
            row4.Controls.Add(btnPresetDelete);
            layout.Controls.Add(row4, 0, 4);

            panel.Controls.Add(layout);
            return panel;
        }

        private static void AddRow(TableLayoutPanel layout, int row, string labelText, Control control)
        {
            while (layout.RowStyles.Count <= row) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label { Text = labelText, AutoSize = true, Anchor = AnchorStyles.Right };
            layout.Controls.Add(label, 0, row);
            control.Dock = DockStyle.Fill;
            layout.Controls.Add(control, 1, row);
        }

        private static Panel BuildInlineRow(params Control[] controls)
        {
            var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            foreach (var c in controls) p.Controls.Add(c);
            return p;
        }

        // ── Wire Events ──────────────────────────────────────────────────────

        private void WireFormEvents()
        {
            btnBrowseBg.Click    += OnBrowseBackground;
            btnBrowseSnake.Click += OnBrowseSavedSnake;
            btnBrowseOut.Click   += OnBrowseOutputRoot;
            btnRandomSeed.Click  += OnRandomSeed;
            btnDryRun.Click      += OnDryRun;

            txtBackground.TextChanged   += OnFormValueChanged;
            cmbZoom.SelectedIndexChanged += OnFormValueChanged;
            nudCast.ValueChanged    += OnFormValueChanged;
            nudSeed.ValueChanged    += OnFormValueChanged;
            txtSavedSnake.TextChanged   += OnFormValueChanged;
            nudDuration.ValueChanged    += OnFormValueChanged;
            nudSnapshot.ValueChanged    += OnFormValueChanged;
            nudPace.ValueChanged    += OnFormValueChanged;
            cmbSpawn.SelectedIndexChanged += OnFormValueChanged;
            txtOutputRoot.TextChanged    += OnFormValueChanged;
        }

        private void WirePresetEvents()
        {
            lstPresets.SelectedIndexChanged += OnPresetSelected;
            btnPresetLoad.Click   += OnPresetLoad;
            btnPresetSave.Click   += OnPresetSave;
            btnPresetRename.Click += OnPresetRename;
            btnPresetDelete.Click += OnPresetDelete;
        }

        // ── Event Handlers ───────────────────────────────────────────────────

        private void OnFormValueChanged(object sender, EventArgs e)
        {
            if (suppressEvents) return;
            PersistToSettings();
            RefreshCliPreview();
            if (SettingsChanged != null) SettingsChanged(this, EventArgs.Empty);
        }

        private void OnBrowseBackground(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Title = "Select background image", Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp|All files|*.*" })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    txtBackground.Text = dlg.FileName;
                    RefreshDimensions(dlg.FileName);
                }
            }
        }

        private void OnBrowseSavedSnake(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Title = "Select saved-snake file", Filter = "JSON|*.json|All files|*.*" })
                if (dlg.ShowDialog() == DialogResult.OK)
                    txtSavedSnake.Text = dlg.FileName;
        }

        private void OnBrowseOutputRoot(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog { Description = "Select output root folder" })
                if (dlg.ShowDialog() == DialogResult.OK)
                    txtOutputRoot.Text = dlg.SelectedPath;
        }

        private void OnRandomSeed(object sender, EventArgs e)
        {
            var rng = new Random();
            nudSeed.Value = (decimal)(uint)rng.Next(0, int.MaxValue);
        }

        private void OnDryRun(object sender, EventArgs e)
        {
            PersistToSettings();
            lblDryResult.Text = "Running…";
            lblDryResult.ForeColor = Color.Gray;
            btnDryRun.Enabled = false;

            string runner = settings.ResolveRunnerExe();
            string args = BuildRunnerArgs(settings.Setup, null, null, false, true);

            var psi = new System.Diagnostics.ProcessStartInfo(runner, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            var stderr = new StringBuilder();
            var proc = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.ErrorDataReceived += delegate(object s2, System.Diagnostics.DataReceivedEventArgs e2)
            {
                if (e2.Data != null) stderr.AppendLine(e2.Data);
            };
            proc.Exited += delegate(object s2, EventArgs e2)
            {
                int code = proc.ExitCode;
                string err = stderr.ToString().Trim();
                proc.Dispose();
                if (InvokeRequired)
                    BeginInvoke(new Action(delegate() { SetDryResult(code == 0, err); }));
                else
                    SetDryResult(code == 0, err);
            };
            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                btnDryRun.Enabled = true;
                lblDryResult.Text = "Error: " + ex.Message;
                lblDryResult.ForeColor = Color.Red;
            }
        }

        private void SetDryResult(bool ok, string detail)
        {
            btnDryRun.Enabled = true;
            if (ok)
            {
                lblDryResult.Text = "✓ PASS";
                lblDryResult.ForeColor = Color.FromArgb(32, 160, 96);
            }
            else
            {
                lblDryResult.Text = "✗ FAIL: " + (String.IsNullOrEmpty(detail) ? "exit code non-zero" : detail);
                lblDryResult.ForeColor = Color.FromArgb(220, 80, 80);
            }
        }

        // ── Preset Event Handlers ────────────────────────────────────────────

        private void OnPresetSelected(object sender, EventArgs e)
        {
            // only update name textbox
            var item = lstPresets.SelectedItem as PresetEntry;
            if (item != null) txtPresetName.Text = item.Name;
        }

        private void OnPresetLoad(object sender, EventArgs e)
        {
            var item = lstPresets.SelectedItem as PresetEntry;
            if (item == null || item.Setup == null) return;
            settings.Setup = item.Setup.Clone();
            settings.Setup.NormalizeDefaults();
            LoadFromSettings();
            if (SettingsChanged != null) SettingsChanged(this, EventArgs.Empty);
        }

        private void OnPresetSave(object sender, EventArgs e)
        {
            string name = txtPresetName.Text.Trim();
            if (String.IsNullOrEmpty(name)) { MessageBox.Show("Enter a preset name.", "Save Preset"); return; }
            PersistToSettings();
            if (presets.Contains(name))
            {
                if (MessageBox.Show("Overwrite preset '" + name + "'?", "Save Preset",
                    MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            }
            presets.Save(name, settings.Setup);
            RefreshPresetList(name);
        }

        private void OnPresetRename(object sender, EventArgs e)
        {
            var item = lstPresets.SelectedItem as PresetEntry;
            if (item == null) return;
            string to = txtPresetName.Text.Trim();
            if (String.IsNullOrEmpty(to)) return;
            if (!presets.Rename(item.Name, to))
                MessageBox.Show("Rename failed — name may already exist.", "Rename Preset");
            RefreshPresetList(to);
        }

        private void OnPresetDelete(object sender, EventArgs e)
        {
            var item = lstPresets.SelectedItem as PresetEntry;
            if (item == null) return;
            if (MessageBox.Show("Delete preset '" + item.Name + "'?", "Delete Preset",
                MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            presets.Delete(item.Name);
            RefreshPresetList(null);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void LoadFromSettings()
        {
            suppressEvents = true;
            try
            {
                SetupConfig cfg = settings.Setup;
                txtBackground.Text  = cfg.BackgroundPath ?? "";
                RefreshDimensions(cfg.BackgroundPath);
                SelectZoom(cfg.Square);
                nudCast.Value       = cfg.Cast;
                nudSeed.Value       = cfg.Seed;
                txtSavedSnake.Text  = cfg.SavedSnakePath ?? "";
                nudDuration.Value   = (decimal)cfg.DurationSeconds;
                nudSnapshot.Value   = (decimal)cfg.SnapshotSeconds;
                nudPace.Value       = (decimal)cfg.Pace;
                SelectSpawn(cfg.SpawnMode);
                txtOutputRoot.Text  = cfg.OutputRoot ?? "";
                RefreshPresetList(null);
                RefreshCliPreview();
            }
            finally
            {
                suppressEvents = false;
            }
        }

        private void PersistToSettings()
        {
            SetupConfig cfg = settings.Setup;
            cfg.BackgroundPath  = txtBackground.Text.Trim();
            cfg.Square          = cmbZoom.SelectedItem is int ? (int)cmbZoom.SelectedItem : 8;
            cfg.Cast            = (int)nudCast.Value;
            cfg.Seed            = (uint)nudSeed.Value;
            cfg.SavedSnakePath  = txtSavedSnake.Text.Trim();
            cfg.DurationSeconds = (double)nudDuration.Value;
            cfg.SnapshotSeconds = (double)nudSnapshot.Value;
            cfg.Pace            = (double)nudPace.Value;
            cfg.SpawnMode       = cmbSpawn.SelectedItem != null ? cmbSpawn.SelectedItem.ToString() : "edge";
            cfg.OutputRoot      = txtOutputRoot.Text.Trim();
        }

        private void RefreshCliPreview()
        {
            txtCliPreview.Text = "ZnakeHeadless.exe " + BuildRunnerArgs("<output>", "<stop-file>");
        }

        private void RefreshDimensions(string path)
        {
            lblDims.Text = "";
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var bmp = new System.Drawing.Bitmap(fs))
                    lblDims.Text = bmp.Width + "×" + bmp.Height;
            }
            catch (Exception) { }
        }

        private void SelectZoom(int square)
        {
            for (int i = 0; i < cmbZoom.Items.Count; i++)
                if (cmbZoom.Items[i] is int && (int)cmbZoom.Items[i] == square)
                { cmbZoom.SelectedIndex = i; return; }
            cmbZoom.SelectedIndex = 0;
        }

        private void SelectSpawn(string mode)
        {
            for (int i = 0; i < cmbSpawn.Items.Count; i++)
                if (String.Equals(cmbSpawn.Items[i].ToString(), mode, StringComparison.OrdinalIgnoreCase))
                { cmbSpawn.SelectedIndex = i; return; }
            cmbSpawn.SelectedIndex = 0;
        }

        private void RefreshPresetList(string selectName)
        {
            lstPresets.Items.Clear();
            foreach (var entry in presets.List())
                lstPresets.Items.Add(entry);
            lstPresets.DisplayMember = "Name";
            if (selectName != null)
                for (int i = 0; i < lstPresets.Items.Count; i++)
                {
                    var item = lstPresets.Items[i] as PresetEntry;
                    if (item != null && String.Equals(item.Name, selectName, StringComparison.OrdinalIgnoreCase))
                    { lstPresets.SelectedIndex = i; break; }
                }
        }
    }
}
