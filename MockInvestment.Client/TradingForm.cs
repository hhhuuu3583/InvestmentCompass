
using System.Text.Json;
using MockInvestment.Client.Controls;
using MockInvestment.Client.Models;
using MockInvestment.Client.Services;

namespace MockInvestment.Client;

public class TradingForm : Form
{
    private readonly ApiClient _apiClient;
    private readonly LoginResponse _session;
    private readonly Button btnAdminCompetitions = new();

    private readonly TextBox txtKeyword = new();
    private readonly Button btnSearch = new();
    private readonly DataGridView gridStocks = new();
    private readonly Label lblStatus = new();
    private readonly Label lblQuote = new();
    private readonly Button btnOpenOrder = new();
    private readonly Button btnHistory = new();
    private readonly Button btnCompetitions = new();
    private readonly Button btnResearch = new();
    private readonly Panel chartHost = new();

    private readonly CancellationTokenSource _closeCts = new();
    private readonly Button btnPortfolio = new();

    private readonly CandleChartControl _candleChart = new();
    private CancellationTokenSource? _chartRequestCts;
    private bool _sessionExpired;

    private bool _refreshTickRunning;
    private int _displayedChartStockId;
    private string _displayedChartRange = string.Empty;

    private string _chartRange = "3M";

    private readonly Dictionary<string, Button>
        _chartRangeButtons = new();

    private readonly System.Windows.Forms.Timer _quoteTimer = new()
    {
        Interval = 10000
    };

    private int _selectionVersion;
    private bool _quoteLoading;
    private bool _stocksLoading;

    public TradingForm(
        ApiClient apiClient,
        LoginResponse session)
    {
        _apiClient = apiClient;
        _session = session;

        BuildScreen();

        btnResearch.Click += (_, _) =>
        {
            if (!CheckSession()) return;
            using var form = new ResearchForm(_apiClient, _session.AccessToken);
            ShowChildDialog(form);
        };


        btnHistory.Click += (_, _) =>
        {
            using var historyForm =
                new HistoryForm(_apiClient, _session);

            ShowChildDialog(historyForm);
        };

        btnPortfolio.Click += (_, _) =>
        {
            using var portfolioForm =
                new PortfolioForm(_apiClient, _session);

            ShowChildDialog(portfolioForm);
        };

        btnCompetitions.Click += (_, _) =>
        {
            using var competitionForm =
                new CompetitionListForm(_apiClient, _session);

            ShowChildDialog(competitionForm);
        };

        btnAdminCompetitions.Click += (_, _) =>
        {
            using var adminForm =
                new MockInvestment.Client.Forms.AdminCompetitionForm(
                    _apiClient,
                    _session);

            ShowChildDialog(adminForm);
        };

        btnOpenOrder.Click += BtnOpenOrder_Click;

        Shown += async (_, _) =>
        {
            await LoadStocksAsync();

            if (!_closeCts.IsCancellationRequested)
            {
                _quoteTimer.Start();
            }
        };

        btnSearch.Click +=
            async (_, _) => await LoadStocksAsync();

        gridStocks.SelectionChanged +=
            GridStocks_SelectionChanged;

        _quoteTimer.Tick += async (_, _) =>
        {
            if (_refreshTickRunning
                || _stocksLoading
                || !_quoteTimer.Enabled
                || !CheckSession())
            {
                return;
            }

            _refreshTickRunning = true;

            try
            {
                await RefreshQuoteAsync();

                if (!_quoteTimer.Enabled
                    || _closeCts.IsCancellationRequested
                    || _sessionExpired)
                {
                    return;
                }

                DateTimeOffset koreaNow =
                    DateTimeOffset.UtcNow.ToOffset(
                        TimeSpan.FromHours(9));

                bool weekday =
                    koreaNow.DayOfWeek != DayOfWeek.Saturday
                    && koreaNow.DayOfWeek != DayOfWeek.Sunday;

                bool refreshWindow =
                    weekday
                    && koreaNow.TimeOfDay >= new TimeSpan(9, 0, 0)
                    && koreaNow.TimeOfDay < new TimeSpan(15, 31, 0);

                if ((_chartRange is "1D" or "1W") && refreshWindow)
                {
                    await LoadSelectedChartAsync(background: true);
                }
            }
            finally
            {
                _refreshTickRunning = false;
            }
        };

        FormClosing += (_, _) =>
        {
            _quoteTimer.Stop();
            _closeCts.Cancel();
        };

        FormClosed += (_, _) =>
        {
            _quoteTimer.Dispose();
            _closeCts.Dispose();
        };
    }

    private void BuildScreen()
    {
        Color navy = Color.FromArgb(20, 36, 61);
        Color blue = Color.FromArgb(38, 92, 173);
        Color background = Color.FromArgb(243, 246, 250);
        Color border = Color.FromArgb(220, 226, 234);

        Text = $"모의투자 - {_session.User.Nickname}";
        ClientSize = new Size(1280, 780);
        MinimumSize = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;

        BackColor = background;
        Font = new Font("맑은 고딕", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = background
        };

        root.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 130));

        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 60));

        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));

        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 32));

        // 상단 헤더
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = navy,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(16, 8, 12, 8)
        };

        header.AutoSize = false;

        header.RowStyles.Clear();

        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));

        var brand = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"MOCK INVESTMENT  |  {_session.User.Nickname}",
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 13F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = Padding.Empty
        };

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 0),
            Margin = Padding.Empty,
            BackColor = navy,
            AutoScroll = true,
        };

        StyleButton(btnOpenOrder, "선택 종목 주문", true);
        StyleButton(btnPortfolio, "내 자산");
        StyleButton(btnHistory, "거래 내역");
        StyleButton(btnCompetitions, "모의투자 대회");

        // 관리자일 때만 대회 관리 버튼 스타일 적용
        if (string.Equals(
            _session.User.Role,
            "Admin",
            StringComparison.OrdinalIgnoreCase))
        {
            StyleButton(btnAdminCompetitions, "대회 관리");
        }


        btnOpenOrder.Width = 135;
        btnCompetitions.Width = 125;
        StyleButton(btnResearch, "AI 챗봇");
        btnResearch.Width = 100;

        navigation.Controls.Add(btnResearch);
        navigation.Controls.Add(btnHistory);
        navigation.Controls.Add(btnPortfolio);
        navigation.Controls.Add(btnOpenOrder);
        navigation.Controls.Add(btnCompetitions);

        if (string.Equals(
    _session.User.Role,
    "Admin",
    StringComparison.OrdinalIgnoreCase))
        {
            btnAdminCompetitions.Width = 100;
            navigation.Controls.Add(btnAdminCompetitions);
        }

        header.Controls.Add(brand, 0, 0);
        header.Controls.Add(navigation, 0, 1);

        // 검색 영역
        var searchPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            BackColor = Color.White,
            Padding = new Padding(12, 10, 12, 8),
            Margin = new Padding(0, 0, 0, 10)
        };

        var searchTitle = new Label
        {
            Text = "종목 검색",
            AutoSize = true,
            ForeColor = navy,
            Margin = new Padding(0, 5, 15, 0)
        };

        txtKeyword.Width = 290;
        txtKeyword.PlaceholderText = "종목명 또는 종목코드";
        txtKeyword.Margin = new Padding(0, 0, 10, 0);

        StyleButton(btnSearch, "검색", true);
        btnSearch.Width = 85;
        btnSearch.Height = 30;
        btnSearch.Margin = Padding.Empty;

        searchPanel.Controls.Add(searchTitle);
        searchPanel.Controls.Add(txtKeyword);
        searchPanel.Controls.Add(btnSearch);

        // 왼쪽 종목 목록 / 오른쪽 시세·차트
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(1200, 540),
            Orientation = Orientation.Vertical,
            SplitterWidth = 10,
            SplitterDistance = 340,
            Panel1MinSize = 260,
            Panel2MinSize = 420,
            BackColor = background,
            Margin = Padding.Empty
        };

        split.Panel1.BackColor = Color.White;
        split.Panel2.BackColor = Color.White;

        var listTitle = new Label
        {
            Text = "거래 종목",
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(12, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = navy,
            Font = new Font("맑은 고딕", 11F, FontStyle.Bold),
            BackColor = Color.White
        };

        // 종목 표
        gridStocks.Dock = DockStyle.Fill;
        gridStocks.ReadOnly = true;
        gridStocks.AllowUserToAddRows = false;
        gridStocks.AllowUserToDeleteRows = false;
        gridStocks.MultiSelect = false;
        gridStocks.RowHeadersVisible = false;
        gridStocks.AutoGenerateColumns = false;

        gridStocks.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;

        gridStocks.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.Fill;

        gridStocks.BackgroundColor = Color.White;
        gridStocks.BorderStyle = BorderStyle.None;
        gridStocks.GridColor = border;
        gridStocks.CellBorderStyle =
            DataGridViewCellBorderStyle.SingleHorizontal;

        gridStocks.EnableHeadersVisualStyles = false;
        gridStocks.ColumnHeadersHeight = 36;
        gridStocks.ColumnHeadersHeightSizeMode =
            DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

        gridStocks.ColumnHeadersDefaultCellStyle =
            new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(233, 238, 245),
                ForeColor = navy,
                Padding = new Padding(5, 0, 0, 0)
            };

        gridStocks.DefaultCellStyle =
            new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = navy,
                SelectionBackColor = Color.FromArgb(221, 234, 252),
                SelectionForeColor = navy,
                Padding = new Padding(5, 0, 0, 0)
            };

        gridStocks.AlternatingRowsDefaultCellStyle.BackColor =
            Color.FromArgb(249, 251, 254);

        gridStocks.RowTemplate.Height = 34;

        gridStocks.Columns.Clear();

        AddColumn("종목코드", nameof(StockItem.StockCode));
        AddColumn("종목명", nameof(StockItem.StockName));
        AddColumn("시장", nameof(StockItem.Market));

        gridStocks.Columns[0].FillWeight = 85;
        gridStocks.Columns[1].FillWeight = 150;
        gridStocks.Columns[2].FillWeight = 65;

        split.Panel1.Controls.Add(gridStocks);
        split.Panel1.Controls.Add(listTitle);

        // 현재가 영역
        var quotePanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 95,
            Padding = new Padding(18, 12, 18, 10),
            BackColor = Color.White
        };

        lblQuote.Dock = DockStyle.Fill;
        lblQuote.Padding = Padding.Empty;
        lblQuote.Text = "종목을 선택해주세요.";
        lblQuote.ForeColor = navy;
        lblQuote.Font = new Font(
            "맑은 고딕",
            12F,
            FontStyle.Bold);

        quotePanel.Controls.Add(lblQuote);

        // 실제 차트가 들어갈 영역
        chartHost.Dock = DockStyle.Fill;
        chartHost.BackColor = Color.White;
        chartHost.Padding = new Padding(18);
        chartHost.Controls.Clear();

        BuildChartArea();

        split.Panel2.Controls.Add(chartHost);
        split.Panel2.Controls.Add(quotePanel);

        // 하단 상태
        lblStatus.Dock = DockStyle.Fill;
        lblStatus.Padding = new Padding(0, 7, 0, 0);
        lblStatus.ForeColor = Color.FromArgb(92, 106, 125);
        lblStatus.Margin = Padding.Empty;

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(searchPanel, 0, 1);
        root.Controls.Add(split, 0, 2);
        root.Controls.Add(lblStatus, 0, 3);

        Controls.Add(root);

        AcceptButton = btnSearch;

        void StyleButton(
            Button button,
            string text,
            bool primary = false)
        {
            button.Text = text;
            button.AutoSize = false;
            button.Size = new Size(100, 36);
            button.Margin = new Padding(6, 0, 0, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.UseVisualStyleBackColor = false;

            button.BackColor = primary
                ? blue
                : Color.FromArgb(43, 60, 84);

            button.ForeColor = Color.White;
            button.Cursor = Cursors.Hand;
        }
    }

    private void AddColumn(
        string title,
        string propertyName)
    {
        gridStocks.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText = title,
                DataPropertyName = propertyName,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
    }

    private async Task LoadStocksAsync()
    {
        if (!CheckSession())
            return;

        if (!btnSearch.Enabled
            || _closeCts.IsCancellationRequested)
            return;

        btnSearch.Enabled = false;
        _stocksLoading = true;
        _selectionVersion++;

        lblStatus.Text = "종목 목록을 조회하고 있습니다.";
        lblQuote.Text = "종목 목록을 조회하고 있습니다.";

        try
        {
            var stocks = await _apiClient.GetStocksAsync(
                _session.AccessToken,
                txtKeyword.Text.Trim(),
                _closeCts.Token);

            if (_closeCts.IsCancellationRequested)
                return;

            gridStocks.DataSource = stocks;
            lblStatus.Text = $"{stocks.Count:N0}개 종목";

            _stocksLoading = false;
            _selectionVersion++;

            lblQuote.Text = stocks.Count == 0
                ? "검색 결과가 없습니다."
                : "현재가 조회 중입니다.";

            Task chartTask = LoadSelectedChartAsync();

            await RefreshQuoteAsync();
            await chartTask;
        }
        catch (OperationCanceledException)
        {
            if (!_closeCts.IsCancellationRequested)
                lblStatus.Text = "서버 응답 시간이 초과되었습니다.";
        }
        catch (HttpRequestException)
        {
            if (!_closeCts.IsCancellationRequested)
                lblStatus.Text = "종목 목록을 가져오지 못했습니다.";
        }
        catch (InvalidOperationException ex)
        {
            if (!_closeCts.IsCancellationRequested)
                lblStatus.Text = ex.Message;
        }
        catch (JsonException)
        {
            if (!_closeCts.IsCancellationRequested)
                lblStatus.Text = "서버 응답을 읽지 못했습니다.";
        }
        finally
        {
            _stocksLoading = false;

            if (!IsDisposed
                && !_closeCts.IsCancellationRequested)
                btnSearch.Enabled = true;
        }
    }

    private async void GridStocks_SelectionChanged(
        object? sender,
        EventArgs e)
    {
        if (_stocksLoading
            || _closeCts.IsCancellationRequested)
            return;

        _selectionVersion++;
        lblQuote.Text = "현재가 조회 중입니다.";

        Task chartTask = LoadSelectedChartAsync();

        await RefreshQuoteAsync();
        await chartTask;
    }

    private async Task RefreshQuoteAsync()
    {
        if (!CheckSession())
            return;

        if (_quoteLoading
            || _stocksLoading
            || _closeCts.IsCancellationRequested)
            return;

        if (gridStocks.CurrentRow?.DataBoundItem
            is not StockItem stock)
        {
            lblQuote.Text = "종목을 선택해주세요.";
            return;
        }

        if (stock.Market is not ("KOSPI" or "KOSDAQ"))
        {
            lblQuote.Text =
                $"{stock.StockName}: 실제 시세 조회 대상이 아닙니다.";
            return;
        }

        int version = _selectionVersion;
        _quoteLoading = true;

        try
        {
            var quote = await _apiClient.GetQuoteAsync(
                _session.AccessToken,
                stock.StockId,
                _closeCts.Token);

            if (_closeCts.IsCancellationRequested
                || version != _selectionVersion)
                return;

            string receivedAt = quote.FetchedAt
                .ToLocalTime()
                .ToString("HH:mm:ss");

            lblQuote.Text =
                $"{quote.StockName} | 현재가 {quote.CurrentPrice:N0}원"
                + Environment.NewLine
                + $"서버 수신 {receivedAt}"
                + (quote.IsCached
                    ? " · 캐시 사용"
                    : " · 새 시세 조회");
        }
        catch (OperationCanceledException)
        {
            if (!_closeCts.IsCancellationRequested
                && version == _selectionVersion)
                lblQuote.Text = "현재가 조회 시간이 초과되었습니다.";
        }
        catch (HttpRequestException)
        {
            if (!_closeCts.IsCancellationRequested
                && version == _selectionVersion)
                lblQuote.Text = "현재가를 가져오지 못했습니다.";
        }
        catch (InvalidOperationException ex)
        {
            if (!_closeCts.IsCancellationRequested
                && version == _selectionVersion)
                lblQuote.Text = ex.Message;
        }
        catch (JsonException)
        {
            if (!_closeCts.IsCancellationRequested
                && version == _selectionVersion)
                lblQuote.Text = "현재가 응답을 읽지 못했습니다.";
        }
        finally
        {
            _quoteLoading = false;
        }
    }

    private void BtnOpenOrder_Click(
        object? sender,
        EventArgs e)
    {
        if (_stocksLoading)
        {
            MessageBox.Show(this, "종목 조회가 끝난 뒤 선택해주세요.");
            return;
        }

        if (gridStocks.CurrentRow?.DataBoundItem
            is not StockItem stock)
        {
            MessageBox.Show(this, "주문할 종목을 선택해주세요.");
            return;
        }

        if (stock.Market is not ("KOSPI" or "KOSDAQ"))
        {
            MessageBox.Show(this, "실제 주문 테스트 대상이 아닙니다.");
            return;
        }

        using var orderForm =
            new OrderForm(_apiClient, _session, stock);

        ShowChildDialog(orderForm);

        if (orderForm.TradeCompleted)
        {
            lblStatus.Text =
                "주문이 완료되었습니다. 자산·보유 정보는 자산 조회에서 확인할 수 있습니다.";
        }
    }

    private async Task LoadSelectedChartAsync(
        bool background = false)
    {
        if (background && _chartRequestCts is not null)
            return;

        if (!CheckSession())
            return;

        _chartRequestCts?.Cancel();

        if (_closeCts.IsCancellationRequested)
            return;

        if (gridStocks.CurrentRow?.DataBoundItem
            is not StockItem stock)
        {
            _candleChart.ShowMessage("종목을 선택해주세요.");
            return;
        }

        if (background
            && (_displayedChartStockId != stock.StockId
                || _displayedChartRange != _chartRange))
            return;

        if (stock.Market is not ("KOSPI" or "KOSDAQ"))
        {
            _candleChart.ShowMessage(
                "차트 조회를 지원하지 않는 종목입니다.");
            return;
        }

        using var requestCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                _closeCts.Token);

        _chartRequestCts = requestCts;

        int version = _selectionVersion;
        string requestedRange = _chartRange;

        if (!background)
            _candleChart.ShowMessage("차트를 조회하고 있습니다.");

        try
        {
            var result = await _apiClient.GetCandlesAsync(
                _session.AccessToken,
                stock.StockId,
                requestCts.Token,
                requestedRange);

            if (requestCts.IsCancellationRequested
                || version != _selectionVersion
                || requestedRange != _chartRange)
                return;

            string rangeText = requestedRange switch
            {
                "1W" => "1주",
                "1M" => "1개월",
                "3M" => "3개월",
                "1Y" => "1년",
                "5Y" => "5년",
                "1D" => "1일",
                _ => requestedRange
            };

            _candleChart.SetData(
                $"{result.StockName} · {rangeText}",
                result.Items,
                result.Period,
                result.PriceType);

            _displayedChartStockId = stock.StockId;
            _displayedChartRange = requestedRange;

            if (background)
            {
                string candleText = result.Period switch
                {
                    "5m" => "5분봉",
                    "1h" => "60분봉",
                    _ => "차트"
                };

                lblStatus.Text =
                    $"{candleText} 갱신 완료 · "
                    + $"{DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)):HH:mm:ss}";
            }
        }
        catch (OperationCanceledException)
        {
            if (!requestCts.IsCancellationRequested
                && version == _selectionVersion)
                _candleChart.ShowMessage("차트 조회 시간이 초과되었습니다.");
        }
        catch (HttpRequestException)
        {
            if (!requestCts.IsCancellationRequested
                && version == _selectionVersion)
                _candleChart.ShowMessage(
                    "차트를 가져오지 못했습니다. 종목을 다시 선택해주세요.");
        }
        catch (InvalidOperationException ex)
        {
            if (!requestCts.IsCancellationRequested
                && version == _selectionVersion)
                _candleChart.ShowMessage(ex.Message);
        }
        catch (JsonException)
        {
            if (!requestCts.IsCancellationRequested
                && version == _selectionVersion)
                _candleChart.ShowMessage("차트 응답을 읽지 못했습니다.");
        }
        finally
        {
            if (ReferenceEquals(_chartRequestCts, requestCts))
                _chartRequestCts = null;
        }
    }

    private void ShowChildDialog(Form dialog)
    {
        if (!IsDisposed
            && !Disposing
            && !_closeCts.IsCancellationRequested
            && !_sessionExpired)
        {
            _quoteTimer.Start();
        }

        if (!CheckSession())
            return;

        _quoteTimer.Stop();

        try
        {
            dialog.ShowDialog(this);
        }
        finally
        {
            if (!IsDisposed
                && !Disposing
                && !_closeCts.IsCancellationRequested)
            {
                _quoteTimer.Start();
            }
        }
    }

    private bool CheckSession()
    {
        if (_closeCts.IsCancellationRequested || IsDisposed)
            return false;

        if (!_sessionExpired
            && _session.ExpiresAt > DateTimeOffset.UtcNow)
            return true;

        if (!_sessionExpired)
        {
            _sessionExpired = true;

            _quoteTimer.Stop();
            _chartRequestCts?.Cancel();

            btnSearch.Enabled = false;
            txtKeyword.Enabled = false;
            gridStocks.Enabled = false;

            btnOpenOrder.Enabled = false;
            btnPortfolio.Enabled = false;
            btnHistory.Enabled = false;
            btnCompetitions.Enabled = false;
            btnResearch.Enabled = false;
            btnAdminCompetitions.Enabled = false;

            lblQuote.Text = "로그인이 만료되었습니다.";
            lblStatus.Text =
                "메인 창을 닫으면 로그인 화면으로 돌아갑니다.";

            MessageBox.Show(
                this,
                "로그인이 만료되었습니다.\n"
                + "메인 창을 닫고 다시 로그인해주세요.",
                "로그인 만료",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }


        return false;
    }

    private void RenderChartRange()
    {
    }

    private void BuildChartArea()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };

        layout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        layout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));

        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 52));

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 8, 0, 0),
            Margin = Padding.Empty,
            BackColor = Color.White
        };

        var ranges = new[]
        {
            (Code: "1D", Text: "1일"),
            (Code: "1W", Text: "1주"),
            (Code: "1M", Text: "1개월"),
            (Code: "3M", Text: "3개월"),
            (Code: "1Y", Text: "1년"),
            (Code: "5Y", Text: "5년")
        };

        foreach (var range in ranges)
        {
            string code = range.Code;

            var button = new Button
            {
                Text = range.Text,
                Width = 60,
                Height = 34,
                Margin = new Padding(0, 0, 4, 0),
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand,
                Enabled = true
            };

            button.FlatAppearance.BorderSize = 0;

            button.Click += async (_, _) =>
            {
                if (!CheckSession())
                    return;

                _chartRange = code;
                UpdateChartRangeButtons();

                await LoadSelectedChartAsync();
            };

            _chartRangeButtons.Add(code, button);
            buttons.Controls.Add(button);
        }

        _candleChart.Dock = DockStyle.Fill;

        layout.Controls.Add(_candleChart, 0, 0);
        layout.Controls.Add(buttons, 0, 1);

        chartHost.Controls.Add(layout);

        UpdateChartRangeButtons();
    }

    private void UpdateChartRangeButtons()
    {
        foreach (var pair in _chartRangeButtons)
        {
            bool selected = pair.Key == _chartRange;

            pair.Value.BackColor = selected
                ? Color.FromArgb(38, 92, 173)
                : Color.FromArgb(243, 246, 250);

            pair.Value.ForeColor = selected
                ? Color.White
                : Color.FromArgb(20, 36, 61);
        }
    }

    private void ShowChartError(string message, bool background)
    {
        if (background)
        {
            lblStatus.Text =
                message + " 기존 차트는 이전 조회 결과입니다.";
        }
        else
        {
            _candleChart.ShowMessage(message);
        }
    }
}
