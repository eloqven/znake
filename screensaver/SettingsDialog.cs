using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Znake
{
    internal sealed class SettingsDialog : Form
    {
        private readonly string root;
        private bool browserSkin;
        private double? timeScale;
        private readonly CheckBox debug = new CheckBox { Text = "Show debug colors", AutoSize = true };
        private readonly TextBox latitude = new TextBox { Width = 85 };
        private readonly TextBox longitude = new TextBox { Width = 85 };
        private readonly DataGridView genes = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToOrderColumns = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.CellSelect
        };
        private readonly Label status = new Label { AutoSize = true, ForeColor = Color.Firebrick };

        public SettingsDialog(string root)
        {
            this.root = root;
            Text = "Znake Settings";
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(555, 548);
            MinimumSize = new Size(500, 465);
            MaximizeBox = false;
            MinimizeBox = false;

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                ColumnCount = 1,
                RowCount = 6
            };
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(content);

            var coordinates = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            coordinates.Controls.Add(new Label { Text = "Latitude", AutoSize = true, Margin = new Padding(3, 6, 6, 3) });
            coordinates.Controls.Add(latitude);
            coordinates.Controls.Add(new Label { Text = "Longitude", AutoSize = true, Margin = new Padding(14, 6, 6, 3) });
            coordinates.Controls.Add(longitude);
            coordinates.Controls.Add(new Label { Text = "Optional", AutoSize = true, Margin = new Padding(8, 6, 3, 3) });

            genes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "First-birth gene", ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            genes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Override", FillWeight = 50, SortMode = DataGridViewColumnSortMode.NotSortable });
            var help = new Label
            {
                Text = "Enter finite gene values, or leave an entry blank to use its default.",
                AutoSize = true,
                Margin = new Padding(3, 10, 3, 5)
            };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
            var save = new Button { Text = "Save", AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            save.Click += delegate { TrySave(); };
            footer.Controls.Add(cancel);
            footer.Controls.Add(save);

            content.Controls.Add(debug, 0, 0);
            content.Controls.Add(coordinates, 0, 1);
            content.Controls.Add(help, 0, 2);
            content.Controls.Add(genes, 0, 3);
            content.Controls.Add(status, 0, 4);
            content.Controls.Add(footer, 0, 5);
            AcceptButton = save;
            CancelButton = cancel;

            SaverSettings settings;
            try { settings = SaverSettings.Load(root); }
            catch (IOException error) { settings = new SaverSettings(); status.Text = error.Message; }
            catch (InvalidDataException error) { settings = new SaverSettings(); status.Text = error.Message; }
            catch (UnauthorizedAccessException error) { settings = new SaverSettings(); status.Text = error.Message; }
            debug.Checked = settings.Debug;
            browserSkin = settings.BrowserSkin;
            timeScale = settings.TimeScale;
            latitude.Text = Present(settings.Latitude);
            longitude.Text = Present(settings.Longitude);
            var overrides = settings.BirthOverrides();
            for (int index = 0; index < (int)Gene.Count; index++)
            {
                Gene gene = (Gene)index;
                double value;
                genes.Rows.Add(gene.ToString(), overrides.TryGetValue(gene, out value) ? Present(value) : "");
            }
        }

        private static string Present(double? number)
        {
            return number.HasValue ? number.Value.ToString("G", CultureInfo.InvariantCulture) : "";
        }

        private void TrySave()
        {
            genes.EndEdit();
            var next = new SaverSettings { Debug = debug.Checked, BrowserSkin = browserSkin, TimeScale = timeScale };
            double lat, lon;
            if (!String.IsNullOrWhiteSpace(latitude.Text) || !String.IsNullOrWhiteSpace(longitude.Text))
            {
                if (!ReadNumber(latitude.Text, out lat) || !ReadNumber(longitude.Text, out lon) ||
                    !(lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180))
                {
                    status.Text = "Enter both coordinates within their latitude and longitude ranges.";
                    return;
                }
                next.Latitude = lat;
                next.Longitude = lon;
            }

            for (int index = 0; index < genes.Rows.Count; index++)
            {
                Gene gene = (Gene)index;
                string raw = Convert.ToString(genes.Rows[index].Cells[1].Value);
                if (String.IsNullOrWhiteSpace(raw)) continue;
                double value;
                if (!ReadNumber(raw, out value) || !Genome.IsValid(gene, value))
                {
                    status.Text = "Check the override for " + gene + ".";
                    genes.CurrentCell = genes.Rows[index].Cells[1];
                    return;
                }
                next.Genes.Add(gene.ToString(), value);
            }

            try
            {
                next.Save(root);
                DialogResult = DialogResult.OK;
            }
            catch (IOException error) { status.Text = error.Message; }
            catch (InvalidDataException error) { status.Text = error.Message; }
            catch (UnauthorizedAccessException error) { status.Text = error.Message; }
        }

        private static bool ReadNumber(string input, out double number)
        {
            return Double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ||
                Double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out number);
        }
    }
}
