using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Znake.Headless;

namespace Znake.ControlPanel
{
    /// <summary>
    /// Side-by-side series charts: Population and Moves vs GameSeconds for two runs.
    /// </summary>
    internal sealed class CompareView : UserControl
    {
        private List<SeriesEntry> seriesA = new List<SeriesEntry>();
        private List<SeriesEntry> seriesB = new List<SeriesEntry>();
        private string labelA = "Run A";
        private string labelB = "Run B";

        private readonly ChartPanel populationChart;
        private readonly ChartPanel movesChart;

        public CompareView()
        {
            populationChart = new ChartPanel("Population", Color.FromArgb(80, 200, 140), Color.FromArgb(80, 160, 220));
            movesChart      = new ChartPanel("Moves",      Color.FromArgb(80, 200, 140), Color.FromArgb(80, 160, 220));

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            layout.Controls.Add(populationChart, 0, 0);
            layout.Controls.Add(movesChart,      1, 0);
            Controls.Add(layout);
        }

        public void SetRunA(string folder, string label)
        {
            labelA = label ?? "Run A";
            seriesA = HistoryStore.ReadSeries(folder);
            Refresh();
        }

        public void SetRunB(string folder, string label)
        {
            labelB = label ?? "Run B";
            seriesB = HistoryStore.ReadSeries(folder);
            Refresh();
        }

        public void ClearAll()
        {
            seriesA = new List<SeriesEntry>();
            seriesB = new List<SeriesEntry>();
            Refresh();
        }

        public new void Refresh()
        {
            populationChart.SetData(
                ExtractSeries(seriesA, s => s.GameSeconds, s => s.Population),
                ExtractSeries(seriesB, s => s.GameSeconds, s => s.Population),
                labelA, labelB, "Population");

            movesChart.SetData(
                ExtractSeries(seriesA, s => s.GameSeconds, s => s.Moves),
                ExtractSeries(seriesB, s => s.GameSeconds, s => s.Moves),
                labelA, labelB, "Moves");

            populationChart.Invalidate();
            movesChart.Invalidate();
        }

        private static List<PointF> ExtractSeries(List<SeriesEntry> entries,
            Func<SeriesEntry, double> xSel, Func<SeriesEntry, double> ySel)
        {
            var pts = new List<PointF>();
            if (entries == null) return pts;
            foreach (var e in entries)
                pts.Add(new PointF((float)xSel(e), (float)ySel(e)));
            return pts;
        }

        // ── Inner chart panel ────────────────────────────────────────────────

        private sealed class ChartPanel : Panel
        {
            private List<PointF> dataA = new List<PointF>();
            private List<PointF> dataB = new List<PointF>();
            private string nameA = "A";
            private string nameB = "B";
            private string title = "";
            private readonly Color colorA;
            private readonly Color colorB;

            public ChartPanel(string title, Color colorA, Color colorB)
            {
                this.title  = title;
                this.colorA = colorA;
                this.colorB = colorB;
                Dock = DockStyle.Fill;
                DoubleBuffered = true;
                ResizeRedraw   = true;
            }

            public void SetData(List<PointF> a, List<PointF> b, string na, string nb, string chartTitle)
            {
                dataA  = a  ?? new List<PointF>();
                dataB  = b  ?? new List<PointF>();
                nameA  = na ?? "A";
                nameB  = nb ?? "B";
                title  = chartTitle ?? title;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.Clear(Color.FromArgb(22, 27, 31));

                int margin = 32;
                var area = new RectangleF(margin, margin,
                    Math.Max(1, Width - margin * 2), Math.Max(1, Height - margin * 2));

                // title
                using (var sf = new System.Drawing.StringFormat { Alignment = StringAlignment.Center })
                using (var font = new Font("Segoe UI", 9f))
                using (var brush = new SolidBrush(Color.FromArgb(200, 230, 220)))
                    g.DrawString(title, font, brush, Width / 2f, 4f, sf);

                if ((dataA == null || dataA.Count < 2) && (dataB == null || dataB.Count < 2)) return;

                float xMin = 0, xMax = 1, yMax = 1;
                ComputeRange(dataA, dataB, ref xMin, ref xMax, ref yMax);

                DrawSeries(g, dataA, colorA, nameA, area, xMin, xMax, 0, yMax, margin);
                DrawSeries(g, dataB, colorB, nameB, area, xMin, xMax, 0, yMax, margin);

                // legend
                using (var font = new Font("Segoe UI", 8f))
                {
                    using (var br = new SolidBrush(colorA))
                        g.FillRectangle(br, area.X, area.Bottom + 4, 12, 8);
                    using (var br = new SolidBrush(Color.FromArgb(200, 230, 220)))
                        g.DrawString(nameA, font, br, area.X + 16, area.Bottom + 2);
                    using (var br = new SolidBrush(colorB))
                        g.FillRectangle(br, area.X + 80, area.Bottom + 4, 12, 8);
                    using (var br = new SolidBrush(Color.FromArgb(200, 230, 220)))
                        g.DrawString(nameB, font, br, area.X + 96, area.Bottom + 2);
                }
            }

            private static void ComputeRange(List<PointF> a, List<PointF> b,
                ref float xMin, ref float xMax, ref float yMax)
            {
                xMin = float.MaxValue; xMax = float.MinValue; yMax = 1;
                foreach (var list in new[] { a, b })
                {
                    if (list == null) continue;
                    foreach (var pt in list)
                    {
                        if (pt.X < xMin) xMin = pt.X;
                        if (pt.X > xMax) xMax = pt.X;
                        if (pt.Y > yMax) yMax = pt.Y;
                    }
                }
                if (xMin >= xMax) { xMin = 0; xMax = 1; }
            }

            private static void DrawSeries(Graphics g, List<PointF> data, Color color, string name,
                RectangleF area, float xMin, float xMax, float yMin, float yMax, int margin)
            {
                if (data == null || data.Count < 2) return;
                float xRange = Math.Max(1e-9f, xMax - xMin);
                float yRange = Math.Max(1e-9f, yMax - yMin);
                using (var pen = new Pen(color, 1.5f))
                {
                    PointF prev = default(PointF);
                    bool   first = true;
                    foreach (var pt in data)
                    {
                        float px = area.X + (pt.X - xMin) / xRange * area.Width;
                        float py = area.Bottom - (pt.Y - yMin) / yRange * area.Height;
                        if (!first)
                            g.DrawLine(pen, prev, new PointF(px, py));
                        prev  = new PointF(px, py);
                        first = false;
                    }
                }
            }
        }
    }
}
