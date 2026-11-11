using System.Text.Json;
using MockInvestment.Client.Models;
using MockInvestment.Client.Services;

namespace MockInvestment.Client;

public class HistoryForm : Form
{
    private readonly ApiClient _apiClient;
    private readonly LoginResponse _session;

    private readonly DataGridView gridHistory = new();
    private readonly Button btnPrevious = new();
    private readonly Button btnNext = new();
    private readonly Button btnRefresh = new();
    private readonly Label lblPage = new();
    private readonly Label lblStatus = new();

    private readonly CancellationTokenSource _closeCts = new();

    private const int PageSize = 20;

    private int _page = 1;
    private long _totalCount;
    private bool _loading;

    public HistoryForm(
        ApiClient apiClient,
        LoginResponse session)
    {
        _apiClient = apiClient;
        _session = session;

        BuildScreen();

        UiTheme.Apply(this);

        Shown += async (_, _) => await LoadPageAsync(1);

        btnRefresh.Click +=
            async (_, _) => await LoadPageAsync(_page);

        btnPrevious.Click += async (_, _) =>
        {
            if (_page > 1)
            {
                await LoadPageAsync(_page - 1);
            }
        };

        btnNext.Click += async (_, _) =>
        {
            if ((long)_page * PageSize < _totalCount
                && _page < int.MaxValue)
            {
                await LoadPageAsync(_page + 1);
            }
        };

        FormClosing += (_, _) => _closeCts.Cancel();
    }

    private void BuildScreen()
    {
        Text = $"거래 내역 - {_session.User.Nickname}";
        ClientSize = new Size(1100, 560);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("맑은 고딕", 10F);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(10),
            WrapContents = false
        };

        btnPrevious.Text = "이전";
        btnNext.Text = "다음";
        btnRefresh.Text = "새로고침";

        btnPrevious.AutoSize = true;
        btnNext.AutoSize = true;
        btnRefresh.AutoSize = true;

        lblPage.AutoSize = true;
        lblPage.Margin = new Padding(15, 7, 0, 0);

        toolbar.Controls.AddRange(new Control[]
        {
            btnPrevious,
            btnNext,
            btnRefresh,
            lblPage
        });

        lblStatus.Dock = DockStyle.Bottom;
        lblStatus.Height = 35;
        lblStatus.Padding = new Padding(10, 7, 0, 0);

        gridHistory.Dock = DockStyle.Fill;
        gridHistory.ReadOnly = true;
        gridHistory.AllowUserToAddRows = false;
        gridHistory.AllowUserToDeleteRows = false;
        gridHistory.AutoGenerateColumns = false;
        gridHistory.RowHeadersVisible = false;

        gridHistory.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.Fill;

        gridHistory.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;

        AddColumn("거래번호", nameof(TradeHistoryItem.TradeId));
        AddColumn("종목코드", nameof(TradeHistoryItem.StockCode));
        AddColumn("종목명", nameof(TradeHistoryItem.StockName));
        AddColumn("구분", nameof(TradeHistoryItem.TradeTypeText));
        AddColumn("수량", nameof(TradeHistoryItem.Quantity), "N0");
        AddColumn("체결가", nameof(TradeHistoryItem.UnitPrice), "N0");
        AddColumn("거래금액", nameof(TradeHistoryItem.TotalAmount), "N0");
        AddColumn("거래 시각", nameof(TradeHistoryItem.LocalTradedAt));

        gridHistory.Columns[7].FillWeight = 160;

        Controls.Add(gridHistory);
        Controls.Add(toolbar);
        Controls.Add(lblStatus);

        UpdateButtons();
    }

    private void AddColumn(
        string title,
        string propertyName,
        string format = "")
    {
        gridHistory.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText = title,
                DataPropertyName = propertyName,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Format = format
                }
            });
    }

    private void UpdateButtons()
    {
        btnRefresh.Enabled = !_loading;

        btnPrevious.Enabled =
            !_loading && _page > 1;

        btnNext.Enabled =
            !_loading
            && _page < int.MaxValue
            && (long)_page * PageSize < _totalCount;
    }

    private async Task LoadPageAsync(int requestedPage)
    {
        if (_loading || _closeCts.IsCancellationRequested)
        {
            return;
        }

        _loading = true;
        UpdateButtons();

        lblStatus.Text =
            "조회 중입니다. 기존 표는 이전 조회 결과입니다.";

        try
        {
            var result = await _apiClient.GetTradeHistoryAsync(
                _session.AccessToken,
                requestedPage,
                PageSize,
                _closeCts.Token);

            if (_closeCts.IsCancellationRequested)
            {
                return;
            }

            // 조회에 성공했을 때만 페이지 번호 변경
            _page = result.Page;
            _totalCount = result.TotalCount;

            gridHistory.DataSource = result.Items;

            long totalPages = _totalCount == 0
                ? 1
                : (_totalCount - 1) / PageSize + 1;

            lblPage.Text =
                $"{_page} / {totalPages:N0} 페이지"
                + $" · 전체 {_totalCount:N0}건";

            lblStatus.Text = result.Items.Count == 0
                ? "표시할 거래 내역이 없습니다."
                : "최신 거래부터 표시합니다. 금액 단위: 원 / 시각: PC 현지 시간";
        }
        catch (OperationCanceledException)
        {
            ShowError("조회 시간이 초과되었습니다.");
        }
        catch (HttpRequestException)
        {
            ShowError("거래 내역을 가져오지 못했습니다.");
        }
        catch (InvalidOperationException ex)
        {
            ShowError(ex.Message);
        }
        catch (JsonException)
        {
            ShowError("거래 내역 응답을 읽지 못했습니다.");
        }
        finally
        {
            _loading = false;

            if (!IsDisposed && !_closeCts.IsCancellationRequested)
            {
                UpdateButtons();
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