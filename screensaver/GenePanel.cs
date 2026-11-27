using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Znake
{
    internal sealed class GenePanel : Form
    {
        private static readonly Color Surface = Color.FromArgb(22, 27, 31);
        private static readonly Color Raised = Color.FromArgb(34, 42, 48);
        private static readonly Color Ink = Color.FromArgb(232, 241, 238);
        private static readonly Color Muted = Color.FromArgb(155, 175, 177);
        private static readonly Color Accent = Color.FromArgb(135, 220, 173);
        private readonly ScreenOverlay overlay;
        private readonly SpeciesLibrary species;
        private readonly ListBox genes;
        private readonly TextBox value;
        private readonly TextBox changeBy;
        private readonly TrackBar slider;
        private readonly TextBox speciesName;
        private readonly CheckBox live;
        private readonly Label snakeLabel, detail, feedback, moonLabel, tick;
        private readonly Button pause, step;
        private readonly Panel connections;
        private readonly ProgressBar moon;
        private readonly Timer refresh = new Timer { Interval = 80 };
        private readonly ToolTip hints = new ToolTip { InitialDelay = 300, ReshowDelay = 100, AutoPopDelay = 8500 };
        private readonly List<KeyValuePair<Rectangle, Gene>> diagramNodes = new List<KeyValuePair<Rectangle, Gene>>();
        private int hoveredGene = -1;
        private long inspectedId;
        private bool sliderPending;
        protected override bool ShowWithoutActivation { get { return true; } }

        public GenePanel(ScreenOverlay overlay, SpeciesLibrary species)
        {
            this.overlay = overlay;
            this.species = species;
            Text = "Znake Genes";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ClientSize = new Size(336, 572);
            MinimumSize = SizeFromClientSize(new Size(330, 539));
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Surface;
            ForeColor = Ink;

            snakeLabel = LabelAt("", 14, 13, 185, 22, Accent);
            Controls.Add(snakeLabel);
            tick = LabelAt("", 204, 13, 118, 22, Muted);
            tick.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(tick);

            live = new CheckBox
            {
                Text = "Live activity", Location = new Point(14, 44), AutoSize = true, ForeColor = Ink
            };
            Controls.Add(live);
            moonLabel = LabelAt("", 204, 43, 118, 18, Muted);
            moonLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(moonLabel);
            moon = new ProgressBar
            {
                Bounds = new Rectangle(204, 65, 118, 9), Anchor = AnchorStyles.Top | AnchorStyles.Right,
                AccessibleName = "Moon"
            };
            Controls.Add(moon);
            pause = ButtonAt("Pause", 14, 64, 72);
            step = ButtonAt("Step tick", 94, 64, 82);
            pause.Click += delegate { overlay.SetDebugPause(!overlay.DebugPaused); UpdateView(); };
            step.Click += delegate { overlay.StepDebug(); };
            Controls.Add(pause);
            Controls.Add(step);
            genes = new ListBox
            {
                Location = new Point(14, 98), Size = new Size(308, 173), IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 21, BorderStyle = BorderStyle.None,
                BackColor = Raised, ForeColor = Ink,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            for (int index = 0; index < (int)Gene.Count; index++)
                genes.Items.Add(LabelFor((Gene)index));
            genes.DrawItem += DrawGene;
            genes.SelectedIndexChanged += delegate { ReadSelection(); };
            BindFineTune(genes);
            genes.MouseMove += delegate(object sender, MouseEventArgs args) { Hover(genes, genes.IndexFromPoint(args.Location)); };
            genes.MouseLeave += delegate { Hover(genes, -1); };
            live.CheckedChanged += delegate
            {
                if (!live.Checked) overlay.SetDebugPause(false);
                UpdateView();
            };
            Controls.Add(genes);

            value = new TextBox
            {
                Location = new Point(14, 281), Size = new Size(178, 24), BackColor = Raised, ForeColor = Ink,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(value);
            BindFineTune(value);
            Button apply = ButtonAt("Apply value", 203, 279, 119);
            apply.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            apply.Click += delegate { ApplyValue(); };
            Controls.Add(apply);
            AcceptButton = apply;

            slider = new TrackBar
            {
                Location = new Point(14, 308), Size = new Size(308, 30), AutoSize = false,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                SmallChange = 1, LargeChange = 100, TickStyle = TickStyle.None,
                Maximum = 10000, AccessibleName = "Selected gene slider", BackColor = Surface
            };
            slider.Scroll += delegate
            {
                if (genes.SelectedIndex < 0) return;
                sliderPending = true;
                value.Text = (slider.Value / SliderScale((Gene)genes.SelectedIndex)).ToString("G17", CultureInfo.InvariantCulture);
                if ((Control.MouseButtons & MouseButtons.Left) == 0) CommitSlider();
            };
            slider.MouseUp += delegate { CommitSlider(); };
            slider.MouseCaptureChanged += delegate
            {
                if ((Control.MouseButtons & MouseButtons.Left) == 0) CommitSlider();
            };
            BindFineTune(slider);
            Controls.Add(slider);

            var stepLabel = LabelAt("Change by (%)", 14, 344, 99, 20, Muted);
            stepLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(stepLabel);
            changeBy = new TextBox
            {
                Bounds = new Rectangle(111, 342, 88, 24), Text = FormatStep(overlay.GeneEditStep),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AccessibleName = "Change by percentage points", BackColor = Raised, ForeColor = Ink
            };
            BindFineTune(changeBy);
            Controls.Add(changeBy);
            var minus = ButtonAt("−", 210, 340, 53);
            var plus = ButtonAt("+", 270, 340, 52);
            minus.Anchor = plus.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            minus.Click += delegate { Nudge(-1, true); };
            plus.Click += delegate { Nudge(1, true); };
            Controls.Add(minus);
            Controls.Add(plus);
            BindFineTune(minus);
            BindFineTune(plus);

            detail = LabelAt("", 14, 371, 308, 18, Muted);
            detail.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            connections = new Panel
            {
                Bounds = new Rectangle(14, 393, 308, 86), BackColor = Surface,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            connections.Paint += DrawConnections;
            connections.MouseMove += delegate(object sender, MouseEventArgs args)
            {
                foreach (var node in diagramNodes)
                    if (node.Key.Contains(args.Location)) { Hover(connections, (int)node.Value); return; }
                Hover(connections, -1);
            };
            connections.MouseLeave += delegate { Hover(connections, -1); };
            feedback = LabelAt("", 14, 485, 308, 19, Accent);
            feedback.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(detail);
            Controls.Add(connections);
            Controls.Add(feedback);
            var speciesLabel = LabelAt("Species name", 14, 511, 176, 18, Muted);
            speciesLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(speciesLabel);
            speciesName = new TextBox
            {
                Location = new Point(14, 532), Size = new Size(178, 24),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                MaxLength = 60, BackColor = Raised, ForeColor = Ink
            };
            speciesName.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode != Keys.Enter) return;
                args.SuppressKeyPress = true;
                SaveSpecies();
            };
            Controls.Add(speciesName);
            Button saveSpecies = ButtonAt("Save species", 203, 529, 119);
            saveSpecies.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            saveSpecies.Click += delegate { SaveSpecies(); };
            Controls.Add(saveSpecies);
            genes.SelectedIndex = (int)Gene.Speed;

            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(Math.Max(work.Left, work.Right - Width - 16), work.Top + 16);
            refresh.Tick += delegate { UpdateView(); };
            refresh.Start();
            UpdateView();
        }

        private static Label LabelAt(string text, int x, int y, int width, int height, Color color)
        {
            return new Label { Text = text, Bounds = new Rectangle(x, y, width, height), ForeColor = color };
        }

        private static Button ButtonAt(string text, int x, int y, int width)
        {
            var button = new Button
            {
                Text = text, Bounds = new Rectangle(x, y, width, 24), FlatStyle = FlatStyle.Flat,
                BackColor = Raised, ForeColor = Ink
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private static string LabelFor(Gene gene)
        {
            return Regex.Replace(gene.ToString(), "([a-z])([A-Z0-9])", "$1 $2");
        }

        private static Color GeneColor(Gene gene)
        {
            double hue = ((int)gene * 137.507764 + 29) * Math.PI / 180;
            return Color.FromArgb(178 + (int)(63 * Math.Cos(hue)),
                178 + (int)(63 * Math.Cos(hue - 2.094395)), 178 + (int)(63 * Math.Cos(hue + 2.094395)));
        }

        private void Hover(Control control, int gene)
        {
            if (hoveredGene == gene) return;
            hoveredGene = gene;
            hints.SetToolTip(control, gene < 0 ? null : LabelFor((Gene)gene) + "\n" + Describe((Gene)gene));
        }

        private static string Describe(Gene gene)
        {
            if (gene <= Gene.Length13)
                return "Weights this length at birth and contributes to comfort near it during life.";
            switch (gene)
            {
                case Gene.NightBoost: return "Amplifies behavior genes as real local darkness deepens.";
                case Gene.ColorDrift: return "Changes the rate of skin adaptation to sustained light and the decay rate of donor vines.";
                case Gene.ShedShift: return "How far a shed shifts the skin toward remembered stressful colors.";
                case Gene.StressBuildup: return "How quickly unsuitable light raises stress.";
                case Gene.StressMemory: return "How long stressful colors remain influential in the snake's memory.";
                case Gene.GrowSave: return "Weights the choice between growing now and saving energy for a shed.";
                case Gene.Imprint: return "Weights the chance and strength of a faint color trace on a crossed square.";
                case Gene.StressCapacity: return "Sets maximum stress and affects fruit production and vine protection.";
                case Gene.TailDwell: return "Time the snake can remain at maximum stress before losing its tail square.";
                case Gene.TailRelief: return "How much stress is relieved when a tail square is lost.";
                case Gene.Speed: return "Sets the snake's cruising speed.";
                case Gene.Acceleration: return "How quickly the snake reaches a new target speed.";
                case Gene.SpeedVariability: return "How much speed fluctuates under stress.";
                case Gene.FlickerAttraction: return "Draws ordinary steering toward changing pixels, especially when calm.";
                case Gene.Sprint: return "Weights automatic bursts and the speed added to a manual burst.";
                case Gene.SprintDuration: return "Sets the duration of an automatic burst.";
                case Gene.SprintCost: return "Sets the stress added during an automatic burst.";
                case Gene.SprintFrequency: return "Weights how often an automatic burst starts.";
                case Gene.VisitorArrival: return "Sets the likelihood of a visitor arriving from the edge.";
                case Gene.Learning: return "Sets the size of a learned adjustment after an identifiable calm or stress event.";
                case Gene.LearningOffset: return "Biases the signed learning update toward calm or away from it.";
                case Gene.SelfFeeding: return "Chance of sampling its own live skin color when its body overlaps itself.";
                case Gene.ManeuverCost: return "Sets the energy and stress cost of turning and clearing occupied layers.";
                case Gene.StressSmoothing: return "Controls how slowly the leading edge reflects a change in stress.";
                case Gene.BirthColorFeed: return "Sets the extra feeding and calm from the snake's birth color.";
                case Gene.BirthColorGradient: return "Controls how sharply the birth-color reward falls off for nearby colors.";
                case Gene.VisionRadius: return "Sees fruit in all directions. 0 disables food sight, 5 sees five squares, and 100 sees the whole arena.";
                case Gene.LengthComfort: return "Controls extra energy drain and stress away from the preferred body length.";
                case Gene.FruitInheritance: return "Sets how strongly a vine fruit's donor genes blend into this snake's genes.";
                case Gene.SquareRecovery: return "Positive values speed up worn squares recovering. Negative values slow them down.";
                case Gene.FruitSurge: return "Moves of doubled speed, acceleration, feeding and light-stress resistance per berry. Berry timers stack independently, and vision doubles for at least eight moves.";
                case Gene.MissedMoveStress: return "Stress per missed move. The first four misses are faint. The fifth adds the full base, then the cost climbs with the streak. The parked color scales it. Moving resets the streak.";
                case Gene.StressJerk: return "Chance of an automatic random turn or sprint. Recent stress spikes and time spent at high stress raise the chance.";
                default: return "";
            }
        }

        private void DrawGene(object sender, DrawItemEventArgs args)
        {
            if (args.Index < 0) return;
            Snake snake = overlay.InspectedSnake;
            Gene gene = (Gene)args.Index;
            bool selected = (args.State & DrawItemState.Selected) != 0;
            Color fill = selected ? Color.FromArgb(46, 65, 62) : Raised;
            using (var brush = new SolidBrush(fill))
                args.Graphics.FillRectangle(brush, args.Bounds);
            int activity = live.Checked && snake != null ? snake.GeneActivity(gene) : 0;
            Color color = GeneColor(gene);
            using (var marker = new SolidBrush(color))
                args.Graphics.FillEllipse(marker, args.Bounds.Left + 5, args.Bounds.Top + 7,
                    activity == 3 ? 9 : 7, activity == 3 ? 9 : 7);
            var nameBounds = new Rectangle(args.Bounds.Left + 19, args.Bounds.Top,
                args.Bounds.Width - 122, args.Bounds.Height);
            var numberBounds = new Rectangle(args.Bounds.Right - 101, args.Bounds.Top, 90, args.Bounds.Height);
            TextRenderer.DrawText(args.Graphics, (string)genes.Items[args.Index], Font, nameBounds, Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            string number = snake == null ? "" : snake.Genes[gene].ToString("G6", CultureInfo.InvariantCulture);

            TextRenderer.DrawText(args.Graphics, number, Font, numberBounds, selected ? Accent : Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        private void ReadSelection()
        {
            Snake snake = overlay.InspectedSnake;
            inspectedId = snake == null ? 0 : snake.Id;
            value.Text = inspectedId == 0 || genes.SelectedIndex < 0 ? "" :
                snake.Genes[(Gene)genes.SelectedIndex].ToString("G17", CultureInfo.InvariantCulture);
            value.Modified = false;
            sliderPending = false;
            if (genes.SelectedIndex >= 0 && snake != null)
            {
                Gene gene = (Gene)genes.SelectedIndex;
                slider.Minimum = -10000;
                slider.Maximum = 10000;
                slider.Value = (int)Math.Round(Math.Max(slider.Minimum,
                    Math.Min(slider.Maximum, snake.Genes[gene] * SliderScale(gene))));
            }
            feedback.Text = "";
            connections.Invalidate();
            UpdateView();
        }

        private void ApplyValue()
        {
            if (genes.SelectedIndex < 0) return;
            Gene gene = (Gene)genes.SelectedIndex;
            string input = gene == Gene.VisionRadius ? value.Text.Trim().TrimEnd('%') : value.Text;
            double number;
            if ((!Double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out number) &&
                !Double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out number)) ||
                !Genome.IsValid(gene, number))
            {
                feedback.Text = "Enter any finite gene value.";
                return;
            }
            SaveEdit(gene, number);
        }

        private void SaveEdit(Gene gene, double number, double? step = null)
        {
            long id = inspectedId;
            bool applied = overlay.SetSnakeGene(id, gene, number, step);
            ReadSelection();
            feedback.Text = applied ? "Saved for snake " + id : "The gene edit could not be saved.";
        }

        private static double SliderScale(Gene gene) { return gene == Gene.VisionRadius ? 100.0 : 10000.0; }
        private static string FormatStep(double number) { return number.ToString("G", CultureInfo.InvariantCulture); }

        private void CommitSlider()
        {
            if (!sliderPending || genes.SelectedIndex < 0) return;
            sliderPending = false;
            Gene gene = (Gene)genes.SelectedIndex;
            SaveEdit(gene, slider.Value / SliderScale(gene));
        }

        private void FineTune(object sender, KeyEventArgs args)
        {
            if (args.KeyCode != Keys.Up && args.KeyCode != Keys.Down) return;
            args.SuppressKeyPress = true;
            Nudge(args.KeyCode == Keys.Up ? 1 : -1, false);
        }

        private void BindFineTune(Control control)
        {
            control.PreviewKeyDown += delegate(object sender, PreviewKeyDownEventArgs args)
            {
                if (args.KeyCode == Keys.Up || args.KeyCode == Keys.Down) args.IsInputKey = true;
            };
            control.KeyDown += FineTune;
        }

        private void Nudge(int direction, bool custom)
        {
            Snake snake = overlay.InspectedSnake;
            if (snake == null || snake.Id != inspectedId || genes.SelectedIndex < 0) return;
            double points = 0.01;
            if (custom)
            {
                string input = changeBy.Text.Trim().TrimEnd('%');
                if ((!Double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out points) &&
                    !Double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out points)) ||
                    !Numbers.Nonnegative(points) || points == 0)
                {
                    feedback.Text = "Enter a positive change percentage.";
                    return;
                }
            }
            Gene gene = (Gene)genes.SelectedIndex;
            SaveEdit(gene, Genome.Nudge(gene, snake.Genes[gene], direction * points), custom ? (double?)points : null);
        }

        private void SaveSpecies()
        {
            Snake snake = overlay.InspectedSnake;
            if (snake == null || snake.Id != inspectedId)
            {
                feedback.Text = "Select a snake first.";
                return;
            }
            string name = speciesName.Text.Trim();
            try
            {
                species.Save(name, SpeciesTemplate.FromSnake(snake));
                feedback.Text = "Saved " + name + " to Birth.";
                speciesName.Clear();
            }
            catch (ArgumentException error) { feedback.Text = error.Message; }
            catch (IOException error) { feedback.Text = error.Message; }
            catch (UnauthorizedAccessException error) { feedback.Text = error.Message; }
            catch (SerializationException error) { feedback.Text = error.Message; }
        }

        private void UpdateView()
        {
            Snake snake = overlay.InspectedSnake;
            if (snake == null || genes.SelectedIndex < 0) return;
            if (snake.Id != inspectedId)
            {
                ReadSelection();
                return;
            }
            Gene gene = (Gene)genes.SelectedIndex;
            string sensitivity = snake.Sensitivity == ColorChannels.All ? "" : " / " + snake.Sensitivity;
            snakeLabel.Text = (snake.IsResident ? "Resident " : "Snake ") + snake.Id + sensitivity;
            moon.Value = (int)Math.Round(100 * overlay.Night);
            moonLabel.Text = "Moon " + moon.Value + "%" + (overlay.HasDaylightLocation ? "" : " (clock)");
            tick.Text = live.Checked ? "Tick " + overlay.Tick : "";
            pause.Visible = step.Visible = live.Checked;
            pause.Text = overlay.DebugPaused ? "Resume" : "Pause";
            pause.Enabled = overlay.TimeScale > 0;
            step.Enabled = overlay.SimulationPaused;
            detail.Text = "Effective " + snake.EffectiveGene(gene, overlay.Night).ToString("G6", CultureInfo.InvariantCulture);
            if (gene == Gene.MissedMoveStress) detail.Text += "   |   Streak " + snake.ConsecutiveMissedMoves;
            if (live.Checked)
            {
                string[] states = { "Idle", "Used", "Inhibited", "Active" };
                detail.Text += "   |   " + states[snake.GeneActivity(gene)];
            }
            if (snake.Learned[(int)gene] != 0)
                detail.Text += "   |   Learned " + snake.Learned[(int)gene].ToString("+0.####;-0.####;0", CultureInfo.InvariantCulture);
            genes.Invalidate();
            if (live.Checked) connections.Invalidate();
        }

        private void DrawConnections(object sender, PaintEventArgs args)
        {
            diagramNodes.Clear();
            if (genes.SelectedIndex < 0) return;
            Gene selected = (Gene)genes.SelectedIndex;
            Snake snake = overlay.InspectedSnake;
            if (selected == Gene.Sprint || selected == Gene.SprintFrequency ||
                selected == Gene.SprintDuration || selected == Gene.SprintCost)
            {
                DrawSprintNetwork(args.Graphics, selected, snake);
                return;
            }
            string output;
            Gene[] inputs = ConnectionsFor(selected, out output);
            int right = connections.ClientSize.Width - 114;
            int row = 80 / inputs.Length;
            var destination = new Rectangle(right, 29, 114, 26);
            using (var pen = new Pen(Muted) { EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor })
            using (var surface = new SolidBrush(Raised))
            using (var selectedSurface = new SolidBrush(Color.FromArgb(46, 65, 62)))
            {
                for (int index = 0; index < inputs.Length; index++)
                {
                    var node = new Rectangle(0, 3 + index * row, right - 24, row - 3);
                    Gene input = inputs[index];
                    Color color = GeneColor(input);
                    int activity = live.Checked && snake != null ? snake.GeneActivity(input) : 0;
                    pen.Color = activity >= 1 ? color : Color.FromArgb(88, 110, 117);
                    pen.Width = activity == 3 ? 2 : 1;
                    pen.DashStyle = activity == 2 ? System.Drawing.Drawing2D.DashStyle.Dash : System.Drawing.Drawing2D.DashStyle.Solid;
                    args.Graphics.DrawLine(pen, node.Right + 3, node.Top + node.Height / 2, right - 4, 42);
                    args.Graphics.FillRectangle(input == selected ? selectedSurface : surface, node);
                    DrawNode(args.Graphics, LabelFor(input), node, color);
                    diagramNodes.Add(new KeyValuePair<Rectangle, Gene>(node, input));
                }
                args.Graphics.FillRectangle(surface, destination);
                DrawNode(args.Graphics, output, destination, Ink);
            }
        }

        private void DrawSprintNetwork(Graphics graphics, Gene selected, Snake snake)
        {
            int width = (connections.ClientSize.Width - 44) / 3;
            var trigger = new Rectangle(0, 17, width, 46);
            var sprint = new Rectangle(width + 22, 17, width, 46);
            var duration = new Rectangle((width + 22) * 2, 4, width, 30);
            var cost = new Rectangle(duration.X, 49, width, 30);
            bool active = live.Checked && snake != null && snake.GeneActivity(Gene.Sprint) == 3;
            using (var line = new Pen(active ? GeneColor(Gene.Sprint) : Muted, active ? 2 : 1)
            {
                EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor
            })
            {
                graphics.DrawLine(line, trigger.Right, 40, sprint.Left - 3, 40);
                graphics.DrawLine(line, sprint.Right, 40, duration.Left - 3, 19);
                graphics.DrawLine(line, sprint.Right, 40, cost.Left - 3, 64);
            }
            DrawGeneNode(graphics, Gene.SprintFrequency, selected, trigger, "Frequency");
            DrawGeneNode(graphics, Gene.Sprint, selected, sprint, "Sprint");
            DrawGeneNode(graphics, Gene.SprintDuration, selected, duration, "Duration");
            DrawGeneNode(graphics, Gene.SprintCost, selected, cost, "Stress cost");
        }

        private void DrawGeneNode(Graphics graphics, Gene gene, Gene selected, Rectangle box, string label)
        {
            using (var fill = new SolidBrush(gene == selected ? Color.FromArgb(46, 65, 62) : Raised))
                graphics.FillRectangle(fill, box);
            DrawNode(graphics, label, box, GeneColor(gene));
            diagramNodes.Add(new KeyValuePair<Rectangle, Gene>(box, gene));
        }

        private void DrawNode(Graphics graphics, string text, Rectangle bounds, Color color)
        {
            TextRenderer.DrawText(graphics, text, Font, bounds, color, TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        private static Gene[] ConnectionsFor(Gene gene, out string output)
        {
            if (gene <= Gene.Length13 || gene == Gene.LengthComfort)
            {
                output = "Length pressure";
                return new[] { Gene.Length3, Gene.Length5, Gene.Length8, Gene.Length13, Gene.LengthComfort };
            }
            switch (gene)
            {
                case Gene.Speed: case Gene.Acceleration: case Gene.SpeedVariability:
                    output = "Movement pace"; return new[] { Gene.Speed, Gene.Acceleration, Gene.SpeedVariability };
                case Gene.Sprint: case Gene.SprintFrequency: case Gene.SprintDuration: case Gene.SprintCost:
                    output = "Sprint cycle"; return new[] { Gene.Sprint, Gene.SprintFrequency, Gene.SprintDuration, Gene.SprintCost };
                case Gene.BirthColorFeed: case Gene.BirthColorGradient:
                    output = "Birth-color food"; return new[] { Gene.BirthColorFeed, Gene.BirthColorGradient };
                case Gene.VisionRadius: case Gene.ManeuverCost:
                    output = "Steering"; return new[] { Gene.VisionRadius, Gene.ManeuverCost, Gene.FlickerAttraction };
                case Gene.Learning: case Gene.LearningOffset:
                    output = "Learned change"; return new[] { Gene.Learning, Gene.LearningOffset };
                case Gene.NightBoost:
                    output = "Phase boost"; return new[] { Gene.NightBoost, Gene.Imprint, Gene.SelfFeeding };
                case Gene.FruitInheritance:
                    output = "Donor influence"; return new[] { Gene.FruitInheritance, Gene.NightBoost };
                case Gene.MissedMoveStress:
                    output = "Stress per miss"; return new[] { Gene.MissedMoveStress, Gene.NightBoost, Gene.BirthColorFeed };
                case Gene.StressJerk:
                    output = "Turn or sprint"; return new[] { Gene.StressBuildup, Gene.MissedMoveStress, Gene.StressJerk };
                default:
                    output = "Effective value"; return new[] { gene, Gene.NightBoost };
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (overlay.DebugPaused) overlay.SetDebugPause(false);
                hints.Dispose();
                refresh.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
