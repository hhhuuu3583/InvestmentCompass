using MockInvestment.Client.Models;
using MockInvestment.Client.Services;

namespace MockInvestment.Client.Forms;

public class CompetitionFinalRankingForm : Form
{
    private readonly ApiClient _apiClient;
    private readonly string _accessToken;
    private readonly int _competitionId;
    private readonly string _competitionTitle;

    private readonly DataGridView _grid = new();
    private readonly Label _status = new();

    public CompetitionFinalRankingForm(
        ApiClient apiClient,
        string accessToken,
        int competitionId,
        string competitionTitle)
    {
        _apiClient = apiClient;
        _accessToken = accessToken;
        _competitionId = competitionId;
        _competitionTitle = competitionTitle;

        BuildScreen();

        Shown += async (_, _) =>
            await LoadRankingsAsync();
    }

    private void BuildScreen()
    {
        Text = $"최종 순위 - {_competitionTitle}";
        Size = new Size(1050, 620);
        MinimumSize = new Size(850, 480);

        StartPosition = FormStartPosition.CenterParent;

        Font = new Font("맑은 고딕", 9.5F);
        BackColor = Color.FromArgb(243, 246, 250);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            RowCount = 2,
            ColumnCount = 1
        };

        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));

        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 36));

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoGenerateColumns = false;

        _grid.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;

        _grid.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.Fill;

        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersHeight = 38;
        _grid.RowTemplate.Height = 34;

        _grid.Columns.Add(
            TextColumn(
                "순위",
                nameof(CompetitionFinalRankingResponse.Rank),
                55));

        _grid.Columns.Add(
            TextColumn(
                "닉네임",
                nameof(CompetitionFinalRankingResponse.Nickname),
                100));

        _grid.Columns.Add(
            MoneyColumn(
                "초기 투자금",
                nameof(CompetitionFinalRankingResponse.InitialBalance),
                110));

        _grid.Columns.Add(
            MoneyColumn(
                "현금 잔액",
                nameof(CompetitionFinalRankingResponse.CashBalance),
                110));

        _grid.Columns.Add(
            MoneyColumn(
                "주식 평가금액",
                nameof(CompetitionFinalRankingResponse.StockEvaluation),
                120));

        _grid.Columns.Add(
            MoneyColumn(
                "총자산",
                nameof(CompetitionFinalRankingResponse.TotalAsset),
                120));

        _grid.Columns.Add(
            MoneyColumn(
                "손익",
                nameof(CompetitionFinalRankingResponse.TotalProfit),
                110));

        var rate = TextColumn(
            "수익률(%)",
            nameof(CompetitionFinalRankingResponse.ProfitRate),
            85);

        rate.DefaultCellStyle.Format = "N2";

        _grid.Columns.Add(rate);

        _grid.Columns.Add(
            TextColumn(
                "확정 시각",
                nameof(CompetitionFinalRankingResponse.FinalizedAt),
                130,
                "yyyy-MM-dd HH:mm"));

        _grid.ColumnHeadersDefaultCellStyle.BackColor =
            Color.FromArgb(233, 238, 245);

        _grid.ColumnHeadersDefaultCellStyle.ForeColor =
            Color.FromArgb(20, 36, 61);

        _grid.ColumnHeadersDefaultCellStyle.Font =
            new Font(
                "맑은 고딕",
                9.5F,
                FontStyle.Bold);

        _status.Dock = DockStyle.Fill;

        _status.TextAlign =
            ContentAlignment.MiddleLeft;

        _status.ForeColor =
            Color.FromArgb(75, 90, 110);

        root.Controls.Add(_grid, 0, 0);
        root.Controls.Add(_status, 0, 1);

        Controls.Add(root);
    }

    private static DataGridViewTextBoxColumn TextColumn(
        string title,
        string property,
        float weight,
        string? format = null)
    {
        var column = new DataGridViewTextBoxColumn
        {
            HeaderText = title,
            DataPropertyName = property,
            FillWeight = weight,
            SortMode =
                DataGridViewColumnSortMode.NotSortable
        };

        if (format != null)
            column.DefaultCellStyle.Format = format;

        return column;
    }

    private static DataGridViewTextBoxColumn MoneyColumn(
        string title,
        string property,
        float weight)
    {
        return TextColumn(
            title,
            property,
            weight,
            "N0");
    }

    private async Task LoadRankingsAsync()
    {
        try
        {
            _status.Text =
                "최종 순위를 불러오는 중입니다.";

            var result =
                await _apiClient.GetCompetitionFinalRankingsAsync(
                    _accessToken,
                    _competitionId);

            _grid.DataSource = result.Rankings;

            _status.Text =
                $"대회: {result.CompetitionTitle}   "
                + $"참가자: {result.ParticipantCount:N0}명   "
                + "최종 순위 확정 완료";

            if (result.Rankings.Count == 0)
            {
                _status.Text +=
                    " (저장된 순위가 없습니다.)";
            }
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            || ex is InvalidOperationException
            || ex is TaskCanceledException)
        {
            _status.Text = ex.Message;

            MessageBox.Show(
                this,
                ex.Message,
                "최종 순위 조회 오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}