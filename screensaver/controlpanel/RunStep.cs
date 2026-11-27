using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Windows.Forms;
using Znake.Headless;

namespace Znake.ControlPanel
{
    /// <summary>
    /// Step 2 — run: launches ZnakeHeadless, polls progress + log tail, graceful stop, resume.
    /// </summary>
    internal sealed class RunStep : UserControl
    {
        private AppSettings settings;
        private readonly HistoryStore history;
        private readonly RunStateMachine machine = new RunStateMachine();
        private readonly GridPreview preview;

        private Process runnerProcess;
        private string  outputFolder;
        private string  stopFilePath;
        private LogTail logTail;
        private bool    isResume;

        // timers
        private readonly Timer statsTimer    = new Timer { Interval = 750 };
        private readonly Timer geometryTimer = new Timer { Interval = 1000 };

        // stats controls
        private readonly Label lblState      = new Label { AutoSize = true };
        private readonly Label lblGameSecs   = new Label { AutoSize = true, Tag = "muted" };
        private readonly Label lblPopulation = new Label { AutoSize = true, Tag = "muted" };
        private readonly Label lblMoves      = new Label { AutoSize = true, Tag = "muted" };
        private readonly Label lblCountdown  = new Label { AutoSize = true, Tag = "warning" };

        private readonly Button btnStart  = new Button { Text = "▶ Start",  Tag = "accent" };
        private readonly Button btnStop   = new Button { Text = "■ Stop",   Tag = "danger" };
        private readonly Button btnResume = new Button { Text = "↺ Resume" };
        private readonly Button btnNewRun = new Button { Text = "New Run" };

        // summary
        private readonly Panel       pnlSummary = new Panel { Visible = false, Tag = "raised" };
        private readonly Label       lblSummaryText = new Label { AutoSize = true };
        private readonly Button      btnStayOnRun = new Button { Text = "Stay on Run" };
        private readonly Button      btnProceed   = new Button { Text = "Proceed →", Tag = "accent" };

        public event EventHandler ProceedToRender;
        public event EventHandler<string> OutputFolderReady;

        public string OutputFolder { get { return outputFolder; } }
        public RunStateMachine Machine { get { return machine; } }

        public RunStep(AppSettings settings, HistoryStore history, GridPreview preview)
        {
            this.settings = settings;
            this.history  = history;
            this.preview  = preview;
            Build();
            statsTimer.Tick    += OnStatsTick;
            geometryTimer.Tick += OnGeometryTick;
            UpdateButtons();
        }

        public void ApplySettings(AppSettings s) { settings = s; }

        public void ApplyTheme(ThemePalette palette) { ThemePalette.Apply(this, palette); }

        // ── Launch / Stop / Resume ───────────────────────────────────────────

        public void StartRun(string folder, bool resume)
        {
            if (machine.State == RunStepState.Running) return;

            isResume = resume;
            outputFolder  = folder;
            stopFilePath  = Path.Combine(folder, "stop.signal");
            Directory.CreateDirectory(folder);
            if (File.Exists(stopFilePath))
            {
                try { File.Delete(stopFilePath); } catch (IOException) { }
            }

            string logPath = Path.Combine(folder, "show.log.jsonl");
            logTail = new LogTail(logPath);

            geometryTimer.Interval = Math.Max(500, (int)(settings.Setup.SnapshotSeconds * 1000));

            string runner = settings.ResolveRunnerExe();
            var psi = BuildProcessStartInfo(runner, folder, resume);

            runnerProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
            runnerProcess.Exited += OnRunnerExited;

            machine.BeginRun();
            UpdateButtons();
            lblState.Text = "Running…";
            pnlSummary.Visible = false;

            runnerProcess.Start();
            statsTimer.Start();
            geometryTimer.Start();

            if (OutputFolderReady != null) OutputFolderReady(this, folder);
        }

        private ProcessStartInfo BuildProcessStartInfo(string runner, string folder, bool resume)
        {
            string args = SetupStep.BuildRunnerArgs(settings.Setup, folder, stopFilePath, resume, false);

            return new ProcessStartInfo(runner, args)
            {
                UseShellExecute = false,
                CreateNoWindow  = true
            };
        }

        public void RequestStop()
        {
            if (machine.State != RunStepState.Running) return;
            if (!String.IsNullOrEmpty(stopFilePath))
            {
                try { File.WriteAllText(stopFilePath, "stop"); }
                catch (Exception) { }
            }
            // poll up to 4 s for natural exit
            if (runnerProcess != null && !runnerProcess.HasExited)
            {
                runnerProcess.WaitForExit(4000);
                if (!runnerProcess.HasExited)
                {
                    try { runnerProcess.Kill(); } catch (Exception) { }
                }
            }
        }

        // ── Timer Callbacks ──────────────────────────────────────────────────

        private void OnStatsTick(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(outputFolder)) return;
            string path = Path.Combine(outputFolder, "progress.json");
            ProgressRecord rec = JsonFiles.Read<ProgressRecord>(path, false);
            if (rec == null) return;

            lblGameSecs.Text   = "Game: " + rec.GameSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
            lblPopulation.Text = "Pop: " + rec.Population + "/" + rec.Cast;
            lblMoves.Text      = "Moves: " + rec.Moves;

            ClassifyAndFinalize(rec);

            UpdateButtons();
            UpdateCountdownLabel();
        }

        private void ClassifyAndFinalize(ProgressRecord rec)
        {
            if (rec == null || machine.State != RunStepState.Running) return;
            string status = rec.Status ?? "";
            if (IsOneOf(status, "complete", "extinct", "time-limit"))
            {
                bool extinct = String.Equals(status, "extinct", StringComparison.OrdinalIgnoreCase)
                    || rec.Population == 0;
                machine.MarkCompleted(extinct);
                FinalizeRun();
            }
            else if (IsOneOf(status, "failed", "error", "replay-failed", "replay-missing"))
            {
                machine.MarkFailed("Runner reported " + status + ".");
                FinalizeRun();
            }
        }

        private static bool IsOneOf(string status, params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (String.Equals(status, candidate, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void OnGeometryTick(object sender, EventArgs e)
        {
            if (logTail == null) return;
            if (logTail.Poll() && logTail.StartRecord != null)
            {
                var scene = new GridSceneState
                {
                    BackgroundPath = settings.Setup.BackgroundPath,
                    Start          = logTail.StartRecord,
                    Snapshot       = logTail.LatestSnapshot
                };
                preview.UpdateScene(scene);
            }
        }

        private void OnRunnerExited(object sender, EventArgs e)
        {
            if (InvokeRequired) { BeginInvoke(new Action(OnRunnerExitedOnUi)); return; }
            OnRunnerExitedOnUi();
        }

        private void OnRunnerExitedOnUi()
        {
            statsTimer.Stop();
            geometryTimer.Stop();
            if (machine.State == RunStepState.Running)
            {
                ProgressRecord final = String.IsNullOrEmpty(outputFolder) ? null
                    : JsonFiles.Read<ProgressRecord>(Path.Combine(outputFolder, "progress.json"), false);
                ClassifyAndFinalize(final);
                if (machine.State == RunStepState.Running)
                {
                    // no terminal progress record — treat as stopped and still surface the summary
                    machine.MarkStopped();
                    FinalizeRun();
                }
            }
            UpdateButtons();
        }

        private void FinalizeRun()
        {
            statsTimer.Stop();
            geometryTimer.Stop();
            RecordRunOutcome();
            ShowSummaryBanner();
            UpdateButtons();
        }

        private void RecordRunOutcome()
        {
            if (!String.IsNullOrEmpty(outputFolder))
                history.RecordStopped(outputFolder, settings.Setup.Seed);
        }

        // ── Summary Banner ───────────────────────────────────────────────────

        private void ShowSummaryBanner()
        {
            lblSummaryText.Text = machine.State == RunStepState.CompletedExtinction
                ? "Run complete — extinction!"
                : machine.State == RunStepState.CompletedTimeLimit
                    ? "Run complete — time limit reached."
                    : machine.State == RunStepState.Failed
                    ? "Run failed: " + machine.FailureMessage
                    : "Run stopped.";
            pnlSummary.Visible = true;
            if (machine.CountdownActive)
                StartCountdownUi();
        }

        private System.Threading.Timer countdownSystemTimer;

        private void StartCountdownUi()
        {
            UpdateCountdownLabel();
            countdownSystemTimer = new System.Threading.Timer(OnCountdownSystemTick, null, 1000, 1000);
        }

        private void OnCountdownSystemTick(object state)
        {
            if (InvokeRequired) { BeginInvoke(new Action(TickCountdownOnUi)); return; }
            TickCountdownOnUi();
        }

        private void TickCountdownOnUi()
        {
            if (machine.TickCountdown())
            {
                // countdown expired — auto-proceed
                if (countdownSystemTimer != null) { countdownSystemTimer.Dispose(); countdownSystemTimer = null; }
                lblCountdown.Visible = false;
                if (ProceedToRender != null) ProceedToRender(this, EventArgs.Empty);
            }
            UpdateCountdownLabel();
        }

        private void UpdateCountdownLabel()
        {
            if (machine.CountdownActive)
            {
                lblCountdown.Text    = "Proceeding in " + machine.CountdownRemainingSeconds + "s…";
                lblCountdown.Visible = true;
            }
            else
            {
                lblCountdown.Visible = false;
            }
        }

        // ── Button state ─────────────────────────────────────────────────────

        private void UpdateButtons()
        {
            btnStart.Enabled  = machine.CanStart;
            btnStop.Enabled   = machine.CanStop;
            btnResume.Enabled = machine.CanResume;
            btnNewRun.Enabled = machine.CanNewRun;
        }

        // ── Build UI ─────────────────────────────────────────────────────────

        private void Build()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(4)
            };

            // button row
            var btnRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            btnRow.Controls.Add(btnStart);
            btnRow.Controls.Add(btnStop);
            btnRow.Controls.Add(btnResume);
            btnRow.Controls.Add(btnNewRun);
            layout.Controls.Add(btnRow);

            // stats row
            var statsRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            statsRow.Controls.Add(lblState);
            statsRow.Controls.Add(lblGameSecs);
            statsRow.Controls.Add(lblPopulation);
            statsRow.Controls.Add(lblMoves);
            statsRow.Controls.Add(lblCountdown);
            layout.Controls.Add(statsRow);

            // summary banner
            BuildSummaryPanel();
            layout.Controls.Add(pnlSummary);

            // grid preview (passed in)
            preview.Dock = DockStyle.Fill;
            layout.Controls.Add(preview);

            Controls.Add(layout);

            // wire button events
            btnStart.Click  += delegate(object s, EventArgs e)
            {
                string folder = SetupStep.ResolveOutputFolder(
                    settings.Setup.OutputRoot, settings.Setup.Seed, DateTime.Now);
                StartRun(folder, false);
            };
            btnStop.Click   += delegate(object s, EventArgs e) { RequestStop(); };
            btnResume.Click += delegate(object s, EventArgs e)
            {
                if (!String.IsNullOrEmpty(outputFolder)) StartRun(outputFolder, true);
            };
            btnNewRun.Click += delegate(object s, EventArgs e)
            {
                if (machine.State == RunStepState.Running) return;
                machine.ResetToIdle();
                pnlSummary.Visible = false;
                UpdateButtons();
            };
        }

        private void BuildSummaryPanel()
        {
            var inner = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            inner.Controls.Add(lblSummaryText);
            inner.Controls.Add(btnStayOnRun);
            inner.Controls.Add(btnProceed);
            pnlSummary.Controls.Add(inner);
            pnlSummary.Height = 48;

            btnStayOnRun.Click += delegate(object s, EventArgs e)
            {
                machine.StayOnRun();
                if (countdownSystemTimer != null) { countdownSystemTimer.Dispose(); countdownSystemTimer = null; }
                lblCountdown.Visible = false;
            };
            btnProceed.Tag = "accent";
            btnProceed.Click += delegate(object s, EventArgs e)
            {
                machine.StayOnRun();
                if (countdownSystemTimer != null) { countdownSystemTimer.Dispose(); countdownSystemTimer = null; }
                if (ProceedToRender != null) ProceedToRender(this, EventArgs.Empty);
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                statsTimer.Dispose();
                geometryTimer.Dispose();
                if (countdownSystemTimer != null) { countdownSystemTimer.Dispose(); countdownSystemTimer = null; }
                if (runnerProcess != null) { try { runnerProcess.Dispose(); } catch (Exception) { } }
            }
            base.Dispose(disposing);
        }
    }
}
