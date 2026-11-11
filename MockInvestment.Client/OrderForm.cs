
using System.Net;
using System.Text.Json;
using MockInvestment.Client.Models;
using MockInvestment.Client.Services;

namespace MockInvestment.Client;

public class OrderForm : Form
{
    private readonly ApiClient _apiClient;
    private readonly LoginResponse _session;
    private readonly StockItem _stock;

    private readonly Label lblPrice = new();
    private readonly Label lblEstimate = new();
    private readonly Label lblStatus = new();

    private readonly NumericUpDown numQuantity = new();

    private readonly Button btnBuy = new();
    private readonly Button btnSell = new();
    private readonly Button btnRetry = new();

    private readonly CancellationTokenSource _closeCts = new();

    private decimal? _displayPrice;
    private bool _sending;
    private bool _completed;
    private PendingTrade? _pending;

    // 앱 실행 중 회원별 미확인 주문 보관
    private static readonly Dictionary<int, PendingTrade>
        PendingOrders = new();

    public bool TradeCompleted { get; private set; }

    public OrderForm(
        ApiClient apiClient,
        LoginResponse session,
        StockItem stock)
    {
        _apiClient = apiClient;
        _session = session;

        if (PendingOrders.TryGetValue(
                session.User.UserId, out var pending))
        {
            _pending = pending;
            _stock = pending.Stock;
        }
        else
        {
            _stock = stock;
        }

        BuildScreen();

        UiTheme.Apply(this);

        btnBuy.BackColor = Color.FromArgb(220, 53, 69);
        btnSell.BackColor = Color.FromArgb(13, 110, 253);

        numQuantity.ValueChanged +=
            (_, _) => UpdateEstimate();

        btnBuy.Click +=
            async (_, _) => await StartOrderAsync(true);

        btnSell.Click +=
            async (_, _) => await StartOrderAsync(false);

        btnRetry.Click +=
            async (_, _) => await SendPendingAsync();

        Shown += async (_, _) =>
        {
            if (_pending is not null)
            {
                lblStatus.Text =
                    "결과가 확인되지 않은 주문이 있습니다."
                    + Environment.NewLine
                    + $"주문번호: {_pending.Order.ClientOrderId}"
                    + Environment.NewLine
                    + "‘동일 주문 재확인’을 눌러주세요.";
            }

            await LoadPriceAsync();
        };

        FormClosing += (_, e) =>
        {
            if (_sending)
            {
                e.Cancel = true;

                MessageBox.Show(
                    this,
                    "주문 응답을 기다리고 있습니다. 잠시 후 닫아주세요.");

                return;
            }

            _closeCts.Cancel();
        };
    }

    private void BuildScreen()
    {
        Text = $"{_stock.StockName} 주문";
        ClientSize = new Size(460, 410);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        Font = new Font("맑은 고딕", 9.5F, FontStyle.Regular);

        var topHeaderPanel = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(460, 65),
            BackColor = Color.FromArgb(248, 249, 250)
        };

        var lblStock = new Label
        {
            Text = $"{_stock.StockName}",
            Location = new Point(20, 12),
            Size = new Size(200, 22),
            Font = new Font("맑은 고딕", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(33, 37, 41)
        };

        var lblCode = new Label
        {
            Text = $"({_stock.StockCode})",
            Location = new Point(115, 14),
            Size = new Size(100, 20),
            Font = new Font("맑은 고딕", 9.5F, FontStyle.Regular),
            ForeColor = Color.FromArgb(108, 117, 125)
        };

        lblPrice.Text = "현재가 조회 중...";
        lblPrice.Location = new Point(20, 36);
        lblPrice.Size = new Size(420, 20);
        lblPrice.Font = new Font("맑은 고딕", 9.5F, FontStyle.Bold);
        lblPrice.ForeColor = Color.FromArgb(13, 110, 253);

        topHeaderPanel.Controls.Add(lblStock);
        topHeaderPanel.Controls.Add(lblCode);
        topHeaderPanel.Controls.Add(lblPrice);

        var separator = new Label
        {
            Location = new Point(0, 65),
            Size = new Size(460, 1),
            BackColor = Color.FromArgb(222, 226, 230)
        };

        var lblQuantity = new Label
        {
            Text = "주문 수량",
            Location = new Point(25, 90),
            Size = new Size(90, 25),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(73, 80, 87)
        };

        numQuantity.Location = new Point(130, 88);
        numQuantity.Size = new Size(160, 27);
        numQuantity.Minimum = 1;
        numQuantity.Maximum = 1_000_000;
        numQuantity.DecimalPlaces = 0;
        numQuantity.ThousandsSeparator = true;
        numQuantity.Value = _pending?.Order.Quantity ?? 1;

        lblEstimate.Location = new Point(25, 130);
        lblEstimate.Size = new Size(410, 25);
        lblEstimate.Text = "예상 주문금액: 조회 중";
        lblEstimate.Font = new Font("맑은 고딕", 10F, FontStyle.Bold);
        lblEstimate.ForeColor = Color.FromArgb(33, 37, 41);

        btnBuy.Text = "매수";
        btnBuy.Location = new Point(130, 175);
        btnBuy.Size = new Size(105, 38);
        btnBuy.Font = new Font("맑은 고딕", 10F, FontStyle.Bold);
        btnBuy.BackColor = Color.FromArgb(13, 110, 253);
        btnBuy.ForeColor = Color.White;
        btnBuy.FlatStyle = FlatStyle.Flat;
        btnBuy.FlatAppearance.BorderSize = 0;

        btnSell.Text = "매도";
        btnSell.Location = new Point(245, 175);
        btnSell.Size = new Size(105, 38);
        btnSell.Font = new Font("맑은 고딕", 10F, FontStyle.Bold);
        btnSell.BackColor = Color.FromArgb(220, 53, 69);
        btnSell.ForeColor = Color.White;
        btnSell.FlatStyle = FlatStyle.Flat;
        btnSell.FlatAppearance.BorderSize = 0;

        btnRetry.Text = "동일 주문 재확인";
        btnRetry.Location = new Point(130, 225);
        btnRetry.Size = new Size(220, 32);

        lblStatus.Location = new Point(25, 275);
        lblStatus.Size = new Size(410, 110);
        lblStatus.Text = "실제 체결 가격은 서버가 결정합니다.";
        lblStatus.ForeColor = Color.FromArgb(108, 117, 125);
        lblStatus.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);

        Controls.AddRange(new Control[]
        {
            topHeaderPanel, separator, lblQuantity,
            numQuantity, lblEstimate,
            btnBuy, btnSell, btnRetry, lblStatus
        });

        UpdateControls();
    }

    private void UpdateControls()
    {
        bool validSession =
            _session.ExpiresAt > DateTimeOffset.UtcNow;

        bool canCreate =
            !_sending
            && !_completed
            && _pending is null
            && _displayPrice.HasValue
            && validSession;

        btnBuy.Enabled = canCreate;
        btnSell.Enabled = canCreate;

        numQuantity.Enabled =
            !_sending && !_completed && _pending is null;

        btnRetry.Visible = _pending is not null;

        btnRetry.Enabled =
            !_sending
            && _pending is not null
            && validSession;
    }

    private async Task LoadPriceAsync()
    {
        try
        {
            var quote = await _apiClient.GetQuoteAsync(
                _session.AccessToken,
                _stock.StockId,
                _closeCts.Token);

            if (_closeCts.IsCancellationRequested)
            {
                return;
            }

            _displayPrice = quote.CurrentPrice;

            lblPrice.Text =
                $"현재가: {quote.CurrentPrice:N0}원"
                + $"  |  수신 {quote.FetchedAt.ToLocalTime():HH:mm:ss}";

            UpdateEstimate();
        }
        catch (OperationCanceledException)
        {
            ShowPriceError("현재가 조회 시간이 초과되었습니다.");
        }
        catch (HttpRequestException)
        {
            ShowPriceError("현재가를 가져오지 못했습니다.");
        }
        catch (InvalidOperationException ex)
        {
            ShowPriceError(ex.Message);
        }
        catch (JsonException)
        {
            ShowPriceError("현재가 응답을 읽지 못했습니다.");
        }
        finally
        {
            if (!_closeCts.IsCancellationRequested)
            {
                UpdateControls();
            }
        }
    }

    private void ShowPriceError(string message)
    {
        if (_closeCts.IsCancellationRequested)
        {
            return;
        }

        lblPrice.Text = "현재가: 확인 불가";

        if (_pending is null)
        {
            lblStatus.Text = message;
        }
    }

    private void UpdateEstimate()
    {
        lblEstimate.Text = _displayPrice is decimal price
            ? $"예상 주문금액: {price * numQuantity.Value:N0}원"
            : "예상 주문금액: 확인 불가";
    }

    private async Task StartOrderAsync(bool isBuy)
    {
        if (_sending || _completed || _pending is not null)
        {
            return;
        }

        if (_displayPrice is null)
        {
            lblStatus.Text = "현재가를 먼저 확인해주세요.";
            return;
        }

        if (_session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            lblStatus.Text = "창을 닫고 다시 로그인해주세요.";
            UpdateControls();
            return;
        }

        int quantity = (int)numQuantity.Value;
        string action = isBuy ? "매수" : "매도";

        // 기본 계좌는 일반 계좌
        int? competitionId = null;
        string accountName = "일반 계좌";

        try
        {
            // 서버에 현재 대회 참가 상태 확인
            var activeCompetition =
                await _apiClient.GetMyActiveCompetitionAsync(
                    _session.AccessToken,
                    _closeCts.Token);

            if (_closeCts.IsCancellationRequested)
            {
                return;
            }

            // 진행 중인 대회에 참가 중이면 대회 계좌 사용
            if (activeCompetition.IsParticipating)
            {
                competitionId =
                    activeCompetition.Competition!.CompetitionId;

                accountName = "대회 계좌";
            }
        }
        catch (OperationCanceledException)
        {
            lblStatus.Text = "대회 참가 상태 조회가 취소되었습니다.";
            return;
        }
        catch (HttpRequestException ex)
        {
            lblStatus.Text =
                "계좌 정보를 확인하지 못해 주문할 수 없습니다."
                + Environment.NewLine
                + ex.Message;
            return;
        }
        catch (InvalidOperationException ex)
        {
            lblStatus.Text = ex.Message;
            return;
        }
        catch (JsonException)
        {
            lblStatus.Text = "계좌 정보 응답을 읽지 못했습니다.";
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"{_stock.StockName} {quantity:N0}주를 {action}할까요?"
            + Environment.NewLine
            + $"사용 계좌: {accountName}"
            + Environment.NewLine
            + "실제 체결 가격은 서버가 결정합니다.",
            "모의 주문 확인",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
        {
            return;
        }

        // 최초 주문 시 선택된 계좌 정보를 주문에 저장
        _pending = new PendingTrade
        {
            Stock = _stock,
            IsBuy = isBuy,
            CompetitionId = competitionId,
            Order = new TradeRequest
            {
                StockId = _stock.StockId,
                Quantity = quantity,
                ClientOrderId = Guid.NewGuid()
            }
        };

        PendingOrders[_session.User.UserId] = _pending;

        await SendPendingAsync();
    }

    private async Task SendPendingAsync()
    {
        if (_sending || _pending is null)
        {
            return;
        }

        if (_session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            lblStatus.Text =
                "창을 닫고 다시 로그인한 뒤 주문 창을 열어 재확인해주세요.";

            UpdateControls();
            return;
        }

        PendingTrade pending = _pending;

        _sending = true;
        UpdateControls();

        lblStatus.Text = "주문 결과를 확인하고 있습니다...";

        try
        {
            TradeResponse result;

            // 대회 참가 중 생성된 주문이면 대회 매매 API
            if (pending.CompetitionId.HasValue)
            {
                result = await _apiClient.SendCompetitionTradeAsync(
                    _session.AccessToken,
                    pending.CompetitionId.Value,
                    pending.IsBuy,
                    pending.Order,
                    _closeCts.Token);
            }
            // 일반 주문이면 기존 일반 매매 API
            else
            {
                result = await _apiClient.SendTradeAsync(
                    _session.AccessToken,
                    pending.IsBuy,
                    pending.Order,
                    _closeCts.Token);
            }

            PendingOrders.Remove(_session.User.UserId);
            _pending = null;
            _completed = true;
            TradeCompleted = true;

            string action =
                result.TradeType == "BUY" ? "매수" : "매도";

            string accountName =
                pending.CompetitionId.HasValue
                    ? "대회 계좌"
                    : "일반 계좌";

            lblStatus.Text =
                $"{action} 완료 · {accountName}"
                + Environment.NewLine
                + $"거래번호 {result.TradeId}"
                + Environment.NewLine
                + $"{result.Quantity:N0}주 × {result.UnitPrice:N0}원"
                + $" = {result.TotalAmount:N0}원"
                + Environment.NewLine
                + (result.IsReplay
                    ? "기존 체결 결과를 확인했습니다."
                    : "새 주문이 체결되었습니다.");
        }
        catch (HttpRequestException ex)
        {
            bool rejected =
                ex.StatusCode is HttpStatusCode status
                && (int)status >= 400
                && (int)status < 500;

            if (rejected && !pending.HadUnknownResult)
            {
                PendingOrders.Remove(_session.User.UserId);
                _pending = null;
                lblStatus.Text = ex.Message;
            }
            else
            {
                KeepPending(pending, ex.Message);
            }
        }
        catch (OperationCanceledException)
        {
            KeepPending(pending, "응답 시간이 초과되었습니다.");
        }
        catch (JsonException)
        {
            KeepPending(pending, "주문 응답을 읽지 못했습니다.");
        }
        catch (InvalidOperationException ex)
        {
            KeepPending(pending, ex.Message);
        }
        finally
        {
            _sending = false;

            if (!IsDisposed)
            {
                UpdateControls();
            }
        }
    }

    private void KeepPending(
        PendingTrade pending,
        string message)
    {
        pending.HadUnknownResult = true;

        lblStatus.Text =
            message
            + Environment.NewLine
            + "체결 여부가 미확인입니다. 동일 주문으로 재확인해주세요."
            + Environment.NewLine
            + $"주문번호: {pending.Order.ClientOrderId}";
    }

    private sealed class PendingTrade
    {
        public StockItem Stock { get; init; } = new();

        public bool IsBuy { get; init; }

        // null이면 일반 계좌, 값이 있으면 대회 계좌
        public int? CompetitionId { get; init; }

        public TradeRequest Order { get; init; } = new();

        public bool HadUnknownResult { get; set; }
    }
}
