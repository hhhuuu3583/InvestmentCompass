using System.Text.Json;
using MockInvestment.Client.Models;
using MockInvestment.Client.Services;

namespace MockInvestment.Client;

public class RankingForm : Form
{
    private readonly ApiClient _apiClient;
    private readonly LoginResponse _session;

    private readonly DataGridView gridRankings = new();
    private readonly Button btnRefresh = new();
    private readonly Label lblStatus = new();

    private readonly CancellationTokenSource _closeCts = new();

    private bool _loading;

    public RankingForm(
        ApiClient apiClient,
        LoginResponse session)
    {
        _apiClient = apiClient;
        _session = session;

        BuildScreen();

        UiTheme.Apply(this);

        Shown += async (_, _) => await LoadRankingsAsync();

        btnRefresh.Click +=
            async (_, _) => await LoadRankingsAsync();

        FormClosing += (_, _) => _closeCts.Cancel();
    }

    private void BuildScreen()
    {
        Text = $"투자 수익률 순위 - {_session.User.Nickname}";
        ClientSize = new Size(850, 520);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("맑은 고딕", 10F);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(10),
            WrapContents = false
        };

        btnRefresh.Text = "새로고침";
        btnRefresh.AutoSize = true;

        var lblGuide = new Label
        {
            Text = "수익률 내림차순 · 노란색 행은 본인",
            AutoSize = true,
            Margin = new Padding(15, 7, 0, 0)
        };

        toolbar.Controls.Add(btnRefresh);
        toolbar.Controls.Add(lblGuide);

        lblStatus.Dock = DockStyle.Bottom;
        lblStatus.Height = 40;
        lblStatus.Padding = new Padding(10, 8, 0, 0);

        gridRankings.Dock = DockStyle.Fill;
        gridRankings.ReadOnly = true;
        gridRankings.AllowUserToAddRows = false;
        gridRankings.AllowUserToDeleteRows = false;
        gridRankings.AutoGenerateColumns = false;
        gridRankings.RowHeadersVisible = false;
        gridRankings.MultiSelect = false;

        gridRankings.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;

        gridRankings.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.Fill;

        AddColumn("순위", nameof(RankingItem.Rank), "N0");
        AddColumn("닉네임", nameof(RankingItem.Nickname));
        AddColumn(
            "총자산(원)",
            nameof(RankingItem.TotalAssets),
            "N0");
        AddColumn(
            "수익률(%)",
            nameof(RankingItem.ReturnRate),
            "N2");
        AddColumn("구분", nameof(RankingItem.MeText));

        Controls.Add(gridRankings);
        Controls.Add(toolbar);
        Controls.Add(lblStatus);
    }

    private void AddColumn(
        string title,
        string propertyName,
        string format = "")
    {
        gridRankings.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText = title,
                DataPropertyName = propertyName,

                // 서버에서 계산한 순서를 화면에서 임의 변경하지 않음
                SortMode = DataGridViewColumnSortMode.NotSortable,

                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Format = format
                }
            });
    }

    private async Task LoadRankingsAsync()
    {
        if (_loading || _closeCts.IsCancellationRequested)
        {
            return;
        }

        _loading = true;
        btnRefresh.Enabled = false;

        lblStatus.Text =
            "순위 조회 중입니다. 기존 표는 이전 조회 결과입니다.";

        try
        {
            var result = await _apiClient.GetRankingsAsync(
                _session.AccessToken,
                _closeCts.Token);

            if (_closeCts.IsCancellationRequested)
            {
                return;
            }

            gridRankings.DataSource = result.Items;

            foreach (DataGridViewRow row in gridRankings.Rows)
            {
                if (row.DataBoundItem is RankingItem item
                    && item.IsMe)
                {
                    row.DefaultCellStyle.BackColor =
                        Color.LightGoldenrodYellow;

                    row.DefaultCellStyle.ForeColor =
                        Color.DarkBlue;
                }
            }

            // 첫 행의 선택 색이 본인 강조를 가리지 않도록 해제
            gridRankings.ClearSelection();
            gridRankings.CurrentCell = null;

            string time = result.CalculatedAt
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss");

            lblStatus.Text =
                $"계산 시각 {time} · {result.Items.Count:N0}명";
        }
        catch (OperationCanceledException)
        {
            ShowError("순위 조회 시간이 초과되었습니다.");
        }
        catch (HttpRequestException)
        {
            ShowError("순위를 가져오지 못했습니다.");
        }
        catch (InvalidOperationException ex)
        {
            ShowError(ex.Message);
        }
        catch (JsonException)
        {
            ShowError("순위 응답을 읽지 못했습니다.");
        }
        finally
        {
            _loading = false;

            if (!IsDisposed && !_closeCts.IsCancellationRequested)
            {
                btnRefresh.Enabled = true;
            }
        }
    }

    private void ShowError(string message)
    {
        if (!_closeCts.IsCancellationRequested)
        {
            lblStatus.Text =
                message + " 기존 표는 이전 조회 결과입니다.";
        }
    }
}