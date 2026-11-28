using System;
using System.Drawing;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace Znake.ControlPanel
{
    /// <summary>
    /// 3-step studio: Setup / Run / Render step pills, History button, theme toggle.
    /// F5=Start, F6=Stop, F7=Render, F8=Play.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private readonly string        dataRoot;
        private          AppSettings   settings;
        private          ThemePalette  palette;

        private readonly PresetStore   presets;
        private readonly HistoryStore  history;
        private readonly GridPreview   preview;
        private          IGridRenderer renderer;

        private readonly SetupStep  setupStep;
        private readonly RunStep    runStep;
        private readonly RenderStep renderStep;

        // step pill buttons
        private readonly Button btnSetupPill  = new Button { Text = "1 · Setup" };
        private readonly Button btnRunPill    = new Button { Text = "2 · Run"   };
        private readonly Button btnRenderPill = new Button { Text = "3 · Render" };
        private readonly Button btnHistory    = new Button { Text = "History"   };
        private readonly Button btnTheme      = new Button { Text = "☀ Light"  };

        // panels
        private readonly Panel pnlSetup  = new Panel { Dock = DockStyle.Fill };
        private readonly Panel pnlRun    = new Panel { Dock = DockStyle.Fill };
        private readonly Panel pnlRender = new Panel { Dock = DockStyle.Fill };

        private int    currentStep = 0;  // 0=setup, 1=run, 2=render

        // toast — only once per completion
        private NotifyIcon notifyIcon;
        private bool       toastShownForCurrentRun;

        public MainForm(string dataRoot, AppSettings settings, ThemePalette palette)
        {
            this.dataRoot = dataRoot;
            this.settings = settings;
            this.palette  = palette;

            Text            = "Znake Control Panel";
            Size            = new Size(1100, 760);
            MinimumSize     = new Size(800, 540);
            StartPosition   = FormStartPosition.CenterScreen;

            presets = new PresetStore(dataRoot);
            history = new HistoryStore(dataRoot);
            preview = new GridPreview();

            // create renderer
            renderer = GridRendererFactory.Create(settings.UseGpuPreview);
            preview.SetRenderer(renderer);
            preview.ApplySettings(settings);

            setupStep   = new SetupStep(settings, presets);
            runStep     = new RunStep(settings, history, preview);
            renderStep  = new RenderStep(settings, history);

            BuildForm();
            WireEvents();
            ApplyTheme();
            ShowStep(0);

            SetupNotifyIcon();
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void BuildForm()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var toolbar = BuildToolbar();
            root.Controls.Add(toolbar, 0, 0);

            var body = new Panel { Dock = DockStyle.Fill };
            pnlSetup.Controls.Add(setupStep);    setupStep.Dock = DockStyle.Fill;
            pnlRun.Controls.Add(runStep);         runStep.Dock   = DockStyle.Fill;
            pnlRender.Controls.Add(renderStep);   renderStep.Dock = DockStyle.Fill;
            body.Controls.Add(pnlSetup);
            body.Controls.Add(pnlRun);
            body.Controls.Add(pnlRender);
            root.Controls.Add(body, 0, 1);

            Controls.Add(root);
        }

        private Panel BuildToolbar()
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = 36, Tag = "raised" };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(2)
            };
            foreach (var btn in new[] { btnSetupPill, btnRunPill, btnRenderPill, btnHistory })
            {
                btn.Height = 28;
                btn.FlatStyle = FlatStyle.Flat;
                flow.Controls.Add(btn);
            }
            flow.Controls.Add(new Panel { Width = 8 }); // spacer
            btnTheme.Height = 28;
            btnTheme.FlatStyle = FlatStyle.Flat;
            flow.Controls.Add(btnTheme);
            bar.Controls.Add(flow);
            return bar;
        }

        // ── Wire Events ──────────────────────────────────────────────────────

        private void WireEvents()
        {
            btnSetupPill.Click  += delegate(object s, EventArgs e) { ShowStep(0); };
            btnRunPill.Click    += delegate(object s, EventArgs e) { ShowStep(1); };
            btnRenderPill.Click += delegate(object s, EventArgs e) { ShowStep(2); };
            btnHistory.Click    += OnShowHistory;
            btnTheme.Click      += OnToggleTheme;

            setupStep.SettingsChanged += delegate(object s, EventArgs e)
            {
                settings.Save(dataRoot);
            };

            runStep.ProceedToRender += delegate(object s, EventArgs e)
            {
                ShowStep(2);
                renderStep.SetSourceFolder(runStep.OutputFolder);
                if (!toastShownForCurrentRun)
                {
                    toastShownForCurrentRun = true;
                    ToastComplete();
                }
            };

            runStep.OutputFolderReady += delegate(object s, string folder)
            {
                toastShownForCurrentRun = false;
            };

            renderStep.RenderComplete += delegate(object s, EventArgs e)
            {
                settings.Save(dataRoot);
            };

            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                preview.PersistToSettings(settings);
                settings.Save(dataRoot);
                CleanupNotifyIcon();
            };
        }

        // ── Keyboard Shortcuts ───────────────────────────────────────────────

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // suppress shortcuts when a text input is focused
            Control focused = FindFocusedControl(this);
            if (focused is TextBoxBase || focused is ComboBox || focused is NumericUpDown)
                return base.ProcessCmdKey(ref msg, keyData);

            if (keyData == Keys.F5)
            {
                ShowStep(1);
                string folder = SetupStep.ResolveOutputFolder(
                    settings.Setup.OutputRoot, settings.Setup.Seed, DateTime.Now);
                runStep.StartRun(folder, false);
                return true;
            }
            if (keyData == Keys.F6)
            {
                runStep.RequestStop();
                return true;
            }
            if (keyData == Keys.F7)
            {
                ShowStep(2);
                renderStep.Render();
                return true;
            }
            if (keyData == Keys.F8)
            {
                ShowStep(2);
                renderStep.Play();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static Control FindFocusedControl(Control root)
        {
            if (!root.ContainsFocus) return null;
            foreach (Control c in root.Controls)
            {
                Control found = FindFocusedControl(c);
                if (found != null) return found;
            }
            return root.Focused ? root : null;
        }

        // ── Step Navigation ──────────────────────────────────────────────────

        private void ShowStep(int step)
        {
            currentStep = step;
            pnlSetup.Visible  = step == 0;
            pnlRun.Visible    = step == 1;
            pnlRender.Visible = step == 2;
            UpdatePillStyles();
        }

        private void UpdatePillStyles()
        {
            SetPillActive(btnSetupPill,  currentStep == 0);
            SetPillActive(btnRunPill,    currentStep == 1);
            SetPillActive(btnRenderPill, currentStep == 2);
        }

        private void SetPillActive(Button btn, bool active)
        {
            btn.BackColor = active ? palette.Accent   : palette.Raised;
            btn.ForeColor = active ? palette.AccentInk : palette.Ink;
        }

        // ── History ──────────────────────────────────────────────────────────

        private void OnShowHistory(object sender, EventArgs e)
        {
            var entries = history.List();
            var msg = new System.Text.StringBuilder();
            msg.AppendLine("Recent runs (" + entries.Count + "):");
            foreach (var entry in entries)
            {
                string star = entry.Starred ? "★ " : "  ";
                msg.AppendLine(star + entry.StartedUtc + " | " + entry.Status
                    + " | pop=" + entry.Population + "/" + entry.Cast
                    + " | " + entry.Folder);
            }
            MessageBox.Show(msg.ToString(), "Run History", MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        // ── Theme ────────────────────────────────────────────────────────────

        private void OnToggleTheme(object sender, EventArgs e)
        {
            ThemeMode next = palette.Mode == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
            palette  = ThemePalette.For(next);
            settings.Theme = next == ThemeMode.Dark ? "dark" : "light";
            settings.Save(dataRoot);
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            ThemePalette.Apply(this, palette);
            setupStep.ApplyTheme(palette);
            runStep.ApplyTheme(palette);
            renderStep.ApplyTheme(palette);
            btnTheme.Text = palette.Mode == ThemeMode.Dark ? "☀ Light" : "🌙 Dark";
            UpdatePillStyles();
        }

        // ── Notify Icon / Toast ──────────────────────────────────────────────

        private void SetupNotifyIcon()
        {
            try
            {
                notifyIcon = new NotifyIcon
                {
                    Text    = "Znake Control Panel",
                    Icon    = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                    Visible = true
                };
            }
            catch (Exception)
            {
                notifyIcon = null;
            }
        }

        private void ToastComplete()
        {
            SystemSounds.Asterisk.Play();
            if (notifyIcon != null)
            {
                try
                {
                    notifyIcon.BalloonTipTitle = "Znake Run Complete";
                    notifyIcon.BalloonTipText  = "Your run has finished. Proceeding to render.";
                    notifyIcon.ShowBalloonTip(4000);
                }
                catch (Exception) { }
            }
        }

        private void CleanupNotifyIcon()
        {
            if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
                notifyIcon = null;
            }
        }
    }
}
