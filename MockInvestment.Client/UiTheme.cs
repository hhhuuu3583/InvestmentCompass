using System.Drawing;
using System.Windows.Forms;

namespace MockInvestment.Client;

public static class UiTheme
{
    private static readonly Color Navy = Color.FromArgb(20, 36, 61);
    private static readonly Color Blue = Color.FromArgb(38, 92, 173);
    private static readonly Color Background = Color.FromArgb(243, 246, 250);
    private static readonly Color Border = Color.FromArgb(220, 226, 234);

    public static void Apply(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Navy;

        ApplyControls(form);
    }

    private static void ApplyControls(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            switch (control)
            {
                case Button button:
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderSize = 0;
                    button.UseVisualStyleBackColor = false;
                    button.BackColor = Blue;
                    button.ForeColor = Color.White;
                    button.Cursor = Cursors.Hand;
                    break;

                case DataGridView grid:
                    StyleGrid(grid);
                    break;

                case TextBox textBox:
                    textBox.BackColor = Color.White;
                    textBox.ForeColor = Navy;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case NumericUpDown number:
                    number.BackColor = Color.White;
                    number.ForeColor = Navy;
                    number.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case Panel panel:
                    panel.BackColor = Background;
                    break;

                case Label label:
                    label.ForeColor = Navy;
                    break;
            }

            ApplyControls(control);
        }
    }

    private static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = Border;
        grid.RowHeadersVisible = false;
        grid.EnableHeadersVisualStyles = false;

        grid.CellBorderStyle =
            DataGridViewCellBorderStyle.SingleHorizontal;

        grid.ColumnHeadersHeightSizeMode =
            DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

        grid.ColumnHeadersHeight = 40;
        grid.RowTemplate.Height = 34;

        grid.ColumnHeadersDefaultCellStyle.BackColor = Navy;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Navy;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;

        // 기존 금액 표시 형식과 정렬 유지
        grid.DefaultCellStyle.BackColor = Color.White;
        grid.DefaultCellStyle.ForeColor = Navy;
        grid.DefaultCellStyle.SelectionBackColor =
            Color.FromArgb(221, 234, 252);
        grid.DefaultCellStyle.SelectionForeColor = Navy;

        grid.AlternatingRowsDefaultCellStyle.BackColor =
            Color.FromArgb(249, 251, 254);
    }
}