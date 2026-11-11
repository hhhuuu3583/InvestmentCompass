using System.Text.Json;
using MockInvestment.Client.Models;
using MockInvestment.Client.Services;

namespace MockInvestment.Client;

public class PortfolioForm : Form
{
    private readonly ApiClient _apiClient;
    private readonly LoginResponse _session;

    // 상단 요약 카드 내부의 개별 값 레이블들
    private readonly Label lblValInitial = new();
    private readonly Label lblValCash = new();
    private readonly Label lblValStockEval = new();
    private readonly Label lblValHoldPL = new();
    private readonly Label lblValTotalAsset = new();
    private readonly Label lblValTotalPL = new();
    private readonly Label lblValReturnRate = new();

    private readonly Label lblStatus = new();
    private readonly Button btnRefresh = new();
    private readonly DataGridView gridHoldings = new();
    private readonly Button btnOpenOrder = new();
    private readonly CancellationTokenSource _closeCts = new();

    private bool _loading;

    public PortfolioForm(
        ApiClient apiClient,
        LoginResponse session)
    {
        _apiClient = apiClient;
        _session = session;

        BuildScreen();

        UiTheme.Apply(this);

        btnOpenOrder.Click += BtnOpenOrder_Click;

        // 보유 종목을 더블클릭해도 주문 창 열기
        gridHoldings.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && btnOpenOrder.Enabled)
            {
                btnOpenOrder.PerformClick();
            }
        };

        Shown += async (_, _) => await RefreshPortfolioAsync();

        btnRefresh.Click +=
            async (_, _) => await RefreshPortfolioAsync();

        FormClosing += (_, _) => _closeCts.Cancel();
    }

    private void BuildScreen()
    {
        Text = $"내 자산 - {_session.User.Nickname}";
        ClientSize = new Size(1050, 600);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("맑은 고딕", 9.5F);

        // 상단 전체 헤더 패널
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 135,
            BackColor = Color.FromArgb(248, 249, 250),
            Padding = new Padding(15, 12, 15, 12)
        };

        // 우측 버튼 영역 패널
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = 130,
            BackColor = Color.FromArgb(248, 249, 250)
        };

        btnRefresh.Text = "새로고침";
        btnRefresh.Location = new Point(10, 0);
        btnRefresh.Size = new Size(120, 36);
        btnRefresh.Font = new Font("맑은 고딕", 9.5F, FontStyle.Bold);

        btnOpenOrder.Text = "선택 종목 주문";
        btnOpenOrder.Location = new Point(10, 44);
        btnOpenOrder.Size = new Size(120, 36);
        btnOpenOrder.Font = new Font("맑은 고딕", 9.5F, FontStyle.Bold);
        btnOpenOrder.BackColor = Color.FromArgb(13, 110, 253);
        btnOpenOrder.ForeColor = Color.White;
        btnOpenOrder.FlatStyle = FlatStyle.Flat;
        btnOpenOrder.FlatAppearance.BorderSize = 0;

        buttonPanel.Controls.Add(btnRefresh);
        buttonPanel.Controls.Add(btnOpenOrder);

        // 자산 정보 요약 카드 내부 그리드 레이아웃 스타일 패널
        var summaryCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(15, 10, 15, 10)
        };
        // 테두리 느낌을 주는 가벼운 패널 감싸기
        summaryCard.Paint += (s, e) =>
        {
            var p = s as Panel;
            using var pen = new Pen(Color.FromArgb(222, 226, 230));
            e.Graphics.DrawRectangle(pen, 0, 0, p!.Width - 1, p.Height - 1);
        };

        // 요약 정보 라벨들 배치 (3열 구조)
        CreateSummaryLabelItem(summaryCard, "초기 투자금", lblValInitial, 15, 12, 220);
        CreateSummaryLabelItem(summaryCard, "현금 잔고", lblValCash, 250, 12, 220);
        CreateSummaryLabelItem(summaryCard, "주식 평가금액", lblValStockEval, 485, 12, 220);

        CreateSummaryLabelItem(summaryCard, "보유 평가손익", lblValHoldPL, 15, 42, 220);
        CreateSummaryLabelItem(summaryCard, "총 손익", lblValTotalPL, 250, 42, 220);
        CreateSummaryLabelItem(summaryCard, "총 수익률", lblValReturnRate, 485, 42, 220);

        // 강조형 총자산 항목 (하단 행 통째로 또는 넓게)
        var lblTitleTotalAsset = new Label { Text = "총 자 산", Location = new Point(15, 74), Size = new Size(90, 22), Font = new Font("맑은 고딕", 9.5F, FontStyle.Bold), ForeColor = Color.FromArgb(108, 117, 125) };
        lblValTotalAsset.Text = "조회 중...";
        lblValTotalAsset.Location = new Point(110, 72);
        lblValTotalAsset.Size = new Size(300, 24);
        lblValTotalAsset.Font = new Font("맑은 고딕", 11F, FontStyle.Bold);
        lblValTotalAsset.ForeColor = Color.FromArgb(13, 110, 253);

        summaryCard.Controls.Add(lblTitleTotalAsset);
        summaryCard.Controls.Add(lblValTotalAsset);

        // 기본 텍스트 초기화
        lblValInitial.Text = "조회 중...";
        lblValCash.Text = "조회 중...";
        lblValStockEval.Text = "조회 중...";
        lblValHoldPL.Text = "조회 중...";
        lblValTotalPL.Text = "조회 중...";
        lblValReturnRate.Text = "조회 중...";

        header.Controls.Add(summaryCard);
        header.Controls.Add(buttonPanel);

        // 하단 상태바
        lblStatus.Dock = DockStyle.Bottom;
        lblStatus.Height = 32;
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblStatus.Padding = new Padding(5, 0, 0, 0);
        lblStatus.ForeColor = Color.FromArgb(108, 117, 125);

        // 보유 종목 그리드
        gridHoldings.Dock = DockStyle.Fill;
        gridHoldings.ReadOnly = true;
        gridHoldings.AllowUserToAddRows = false;
        gridHoldings.AllowUserToDeleteRows = false;
        gridHoldings.AutoGenerateColumns = false;
        gridHoldings.RowHeadersVisible = false;

        gridHoldings.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.Fill;

        gridHoldings.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;

        AddColumn("종목코드", nameof(HoldingResponse.StockCode), "", 80);
        AddColumn("종목명", nameof(HoldingResponse.StockName), "", 140);
        AddColumn("수량", nameof(HoldingResponse.Quantity), "N0", 70);
        AddColumn("평균 매입단가", nameof(HoldingResponse.AverageBuyPrice), "N2", 100);
        AddColumn("현재가", nameof(HoldingResponse.CurrentPrice), "N0", 90);
        AddColumn("평가금액", nameof(HoldingResponse.EvaluationAmount), "N0", 110);
        AddColumn("평가손익", nameof(HoldingResponse.UnrealizedProfitLoss), "N0", 110);

        // 컨트롤 추가 순서 (Dock 배치 기준)
        Controls.Add(gridHoldings);
        Controls.Add(header);
        Controls.Add(lblStatus);
    }

    private void CreateSummaryLabelItem(Control parent, string title, Label valueLabel, int x, int y, int width)
    {
        var lblTitle = new Label
        {
            Text = title,
            Location = new Point(x, y),
            Size = new Size(95, 20),
            Font = new Font("맑은 고딕", 9F, FontStyle.Regular),
            ForeColor = Color.FromArgb(108, 117, 125)
        };

        valueLabel.Location = new Point(x + 95, y - 1);
        valueLabel.Size = new Size(width - 95, 22);
        valueLabel.Font = new Font("맑은 고딕", 9.5F, FontStyle.Bold);
        valueLabel.ForeColor = Color.FromArgb(33, 37, 41);

        parent.Controls.Add(lblTitle);
        parent.Controls.Add(valueLabel);
    }

    private void AddColumn(
        string title,
        string propertyName,
        string format = "",
        int minWidth = 0)
    {
        var col = new DataGridViewTextBoxColumn
        {
            HeaderText = title,
            DataPropertyName = propertyName,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Format = format,
                NullValue = "확인 불가"
            }
        };

        if (minWidth > 0)
        {
            col.MinimumWidth = minWidth;
        }

        gridHoldings.Columns.Add(col);
    }

    private async Task RefreshPortfolioAsync()
    {
        if (_loading || _closeCts.IsCancellationRequested)
        {
            return;
        }

        _loading = true;
        btnRefresh.Enabled = false;
        btnOpenOrder.Enabled = false;

        lblStatus.Text = "조회 중입니다. 기존 표는 이전 조회 결과입니다.";

        try
        {
            var portfolio = await _apiClient.GetPortfolioAsync(
                _session.AccessToken,
                _closeCts.Token);

            if (_closeCts.IsCancellationRequested)
            {
                return;
            }

            // 요약 카드 데이터 바인딩
            lblValInitial.Text = Money(portfolio.InitialBalance);
            lblValCash.Text = Money(portfolio.CashBalance);
            lblValStockEval.Text = Money(portfolio.TotalEvaluationAmount);
            lblValHoldPL.Text = Money(portfolio.TotalUnrealizedProfitLoss);
            lblValTotalAsset.Text = Money(portfolio.TotalAssets);
            lblValTotalPL.Text = Money(portfolio.TotalProfitLoss);
            lblValReturnRate.Text = Rate(portfolio.TotalReturnRate);

            // 손익 금액에 따른 컬러 포인팅 (플러스: 빨강, 마이너스: 파랑 등 원하시는 경우 지정 가능)
            if (portfolio.TotalProfitLoss.HasValue)
            {
                lblValTotalPL.ForeColor = portfolio.TotalProfitLoss.Value >= 0
                    ? Color.FromArgb(220, 53, 69)   // 수익 (빨강)
                    : Color.FromArgb(13, 110, 253); // 손실 (파랑)
            }

            gridHoldings.DataSource = portfolio.Holdings;

            string time = portfolio.CalculatedAt
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss");

            lblStatus.Text =
                portfolio.ValuationStatus == "AVAILABLE"
                ? $"조회 시각 {time} · 보유 {portfolio.Holdings.Count}종목"
                : $"조회 시각 {time} · 일부 시세를 확보하지 못해 전체 평가 불가";
        }
        catch (OperationCanceledException)
        {
            ShowError("조회 시간이 초과되었습니다.");
        }
        catch (HttpRequestException)
        {
            ShowError("서버에서 자산 정보를 가져오지 못했습니다.");
        }
        catch (InvalidOperationException ex)
        {
            ShowError(ex.Message);
        }
        catch (JsonException)
        {
            ShowError("자산 응답을 읽지 못했습니다.");
        }
        finally
        {
            _loading = false;

            if (!IsDisposed && !_closeCts.IsCancellationRequested)
            {
                btnRefresh.Enabled = true;
                btnOpenOrder.Enabled = true;
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

    private static string Money(decimal? value)
    {
        return value.HasValue
            ? $"{value.Value:N0}원"
            : "확인 불가";
    }

    private static string Rate(decimal? value)
    {
        return value.HasValue
            ? $"{value.Value:N2}%"
            : "확인 불가";
    }

    private async void BtnOpenOrder_Click(
    object? sender,
    EventArgs e)
    {
        if (_loading || _closeCts.IsCancellationRequested)
        {
            return;
        }

        if (gridHoldings.CurrentRow?.DataBoundItem
            is not HoldingResponse holding)
        {
            MessageBox.Show(
                this,
                "주문할 보유 종목을 선택해주세요.");

            return;
        }

        _loading = true;
        btnRefresh.Enabled = false;
        btnOpenOrder.Enabled = false;

        bool refreshAfterOrder = false;

        try
        {
            lblStatus.Text = "선택 종목의 정보를 확인하고 있습니다.";

            var stocks = await _apiClient.GetStocksAsync(
                _session.AccessToken,
                holding.StockCode,
                _closeCts.Token);

            if (_closeCts.IsCancellationRequested)
            {
                return;
            }

            var stock = stocks.FirstOrDefault(
                x => x.StockId == holding.StockId);

            if (stock is null)
            {
                lblStatus.Text =
                    "현재 거래를 지원하는 종목 정보를 찾지 못했습니다.";

                return;
            }

            if (stock.Market is not ("KOSPI" or "KOSDAQ"))
            {
                lblStatus.Text =
                    "실제 주문 테스트를 지원하지 않는 종목입니다.";

                return;
            }

            using var orderForm =
                new OrderForm(_apiClient, _session, stock);

            orderForm.ShowDialog(this);

            refreshAfterOrder = orderForm.TradeCompleted;

            lblStatus.Text = refreshAfterOrder
                ? "주문이 완료되었습니다. 자산을 갱신합니다."
                : "주문 창을 닫았습니다. 필요하면 새로고침해주세요.";
        }
        catch (OperationCanceledException)
        {
            ShowError("종목 확인 시간이 초과되었습니다.");
        }
        catch (HttpRequestException)
        {
            ShowError("주문할 종목 정보를 가져오지 못했습니다.");
        }
        catch (InvalidOperationException ex)
        {
            ShowError(ex.Message);
        }
        catch (JsonException)
        {
            ShowError("종목 응답을 읽지 못했습니다.");
        }
        finally
        {
            _loading = false;

            if (!IsDisposed && !_closeCts.IsCancellationRequested)
            {
                btnRefresh.Enabled = true;
                btnOpenOrder.Enabled = true;
            }
        }

        if (refreshAfterOrder
            && !IsDisposed
            && !_closeCts.IsCancellationRequested)
        {
            await RefreshPortfolioAsync();
        }
    }
}