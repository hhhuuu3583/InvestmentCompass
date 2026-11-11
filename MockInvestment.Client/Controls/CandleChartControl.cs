using System.Drawing.Drawing2D;
using MockInvestment.Client.Models;

namespace MockInvestment.Client.Controls;

public class CandleChartControl : Control
{
    private const int VisibleCount = 60;

    private List<CandleItem> _candles = new();

    private readonly HScrollBar _scroll = new()
    {
        Dock = DockStyle.Bottom,
        SmallChange = 1,
        Enabled = false
    };

    private readonly ToolTip _tooltip = new();

    private string _title = string.Empty;
    private string _seriesKey = string.Empty;
    private string _message = "표시할 차트가 없습니다.";

    private int _startIndex;
    private int _hoverIndex = -1;
    private bool _updatingScroll;

    private RectangleF _priceArea;

    private int MaximumStart =>
        Math.Max(0, _candles.Count - VisibleCount);

    public CandleChartControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.White;

        Controls.Add(_scroll);

        _scroll.ValueChanged += (_, _) =>
        {
            if (_updatingScroll)
            {
                return;
            }

            _startIndex = _scroll.Value;

            ClearHover();
            Invalidate();
        };

        MouseMove += HandleMouseMove;
        MouseLeave += (_, _) => ClearHover();
    }

    public void ShowMessage(string message)
    {
        _candles.Clear();

        _seriesKey = string.Empty;
        _title = string.Empty;
        _message = message;
        _startIndex = 0;
        _priceArea = RectangleF.Empty;

        ClearHover();
        UpdateScroll();
        Invalidate();
    }

    public void SetData(
        string stockName,
        List<CandleItem> candles,
        string period = "D",
        string priceType = "ADJUSTED")
    {
        string newKey = $"{stockName}|{period}|{priceType}";

        bool sameSeries = _seriesKey == newKey;

        bool followingLatest =
            _candles.Count == 0
            || _startIndex >= MaximumStart;

        DateTimeOffset? anchor = null;

        if (sameSeries
            && !followingLatest
            && _startIndex < _candles.Count)
        {
            anchor = GetTimestamp(_candles[_startIndex]);
        }

        _candles = candles
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Time)
            .ToList();

        _seriesKey = newKey;

        if (!sameSeries || followingLatest)
        {
            // 처음 조회·종목 변경·최신 구간 추적 중이면 맨 오른쪽 표시
            _startIndex = MaximumStart;
        }
        else if (anchor.HasValue)
        {
            // 이전에 보던 첫 캔들의 시각을 유지
            int index = _candles.FindIndex(
                x => GetTimestamp(x) >= anchor.Value);

            _startIndex = index >= 0
                ? Math.Min(index, MaximumStart)
                : MaximumStart;
        }

        string periodText = period switch
        {
            "W" => "주봉",
            "M" => "월봉",
            "Y" => "연봉",
            _ => "일봉"
        };

        if (period.EndsWith("m", StringComparison.Ordinal)
            && int.TryParse(period[..^1], out int minutes))
        {
            periodText = $"{minutes}분봉";
        }

        string priceText = priceType == "ADJUSTED"
            ? "수정주가"
            : "거래가격";

        _title = $"{stockName} · {periodText} · {priceText}";
        _message = "조회 기간에 표시할 시세가 없습니다.";

        ClearHover();
        UpdateScroll();
        Invalidate();
    }

    private void UpdateScroll()
    {
        _updatingScroll = true;

        try
        {
            _startIndex = Math.Clamp(
                _startIndex, 0, MaximumStart);

            _scroll.Minimum = 0;

            _scroll.LargeChange = Math.Max(
                1,
                Math.Min(VisibleCount, _candles.Count));

            _scroll.Maximum = Math.Max(
                0,
                _candles.Count - 1);

            _scroll.Value = _startIndex;

            _scroll.Enabled =
                _candles.Count > VisibleCount;
        }
        finally
        {
            _updatingScroll = false;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int contentHeight =
            ClientSize.Height - _scroll.Height;

        if (_candles.Count == 0)
        {
            _priceArea = RectangleF.Empty;

            TextRenderer.DrawText(
                g,
                _message,
                Font,
                new Rectangle(
                    0, 0, Width, Math.Max(0, contentHeight)),
                Color.SlateGray,
                TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.WordBreak);

            return;
        }

        if (Width < 300 || contentHeight < 220)
        {
            _priceArea = RectangleF.Empty;
            return;
        }

        int count = Math.Min(
            VisibleCount,
            _candles.Count - _startIndex);

        var visible = _candles.GetRange(
            _startIndex,
            count);

        string heading =
            $"{_title}  "
            + $"({_startIndex + 1:N0}~{_startIndex + count:N0}"
            + $" / 전체 {_candles.Count:N0}개)";

        TextRenderer.DrawText(
            g,
            heading,
            Font,
            new Rectangle(10, 4, Width - 20, 25),
            Color.FromArgb(20, 36, 61),
            TextFormatFlags.EndEllipsis
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine);

        float plotWidth = Width - 100;
        float usableHeight = contentHeight - 100;

        _priceArea = new RectangleF(
            15,
            38,
            plotWidth,
            usableHeight * 0.68f);

        var volumeArea = new RectangleF(
            15,
            _priceArea.Bottom + 25,
            plotWidth,
            usableHeight * 0.20f);

        // 현재 보이는 60개를 기준으로 가격 축 계산
        decimal minimum = visible.Min(x => x.Low);
        decimal maximum = visible.Max(x => x.High);

        decimal padding = (maximum - minimum) * 0.08m;

        if (padding == 0)
        {
            padding = Math.Max(1m, maximum * 0.01m);
        }

        minimum = Math.Max(0m, minimum - padding);
        maximum += padding;

        decimal priceRange = maximum - minimum;

        float PriceY(decimal value)
        {
            return _priceArea.Bottom
                - (float)((value - minimum) / priceRange)
                * _priceArea.Height;
        }

        using var gridPen =
            new Pen(Color.FromArgb(231, 236, 243));

        for (int i = 0; i <= 4; i++)
        {
            float y =
                _priceArea.Top + _priceArea.Height * i / 4;

            g.DrawLine(
                gridPen,
                _priceArea.Left,
                y,
                _priceArea.Right,
                y);

            decimal value =
                maximum - priceRange * i / 4;

            TextRenderer.DrawText(
                g,
                value.ToString("N0"),
                Font,
                new Point(
                    (int)_priceArea.Right + 7,
                    (int)y - 8),
                Color.SlateGray);
        }

        float step = plotWidth / count;
        float bodyWidth = Math.Max(1f, step * 0.62f);

        long maxVolume = Math.Max(
            1L,
            visible.Max(x => x.Volume));

        for (int i = 0; i < count; i++)
        {
            CandleItem candle = visible[i];

            float x =
                _priceArea.Left + step * (i + 0.5f);

            Color color = candle.Close >= candle.Open
                ? Color.FromArgb(213, 65, 74)
                : Color.FromArgb(47, 105, 190);

            using var pen = new Pen(color);
            using var brush = new SolidBrush(color);

            g.DrawLine(
                pen,
                x,
                PriceY(candle.High),
                x,
                PriceY(candle.Low));

            float openY = PriceY(candle.Open);
            float closeY = PriceY(candle.Close);

            g.FillRectangle(
                brush,
                x - bodyWidth / 2,
                Math.Min(openY, closeY),
                bodyWidth,
                Math.Max(1f, Math.Abs(openY - closeY)));

            float barHeight =
                (float)((double)candle.Volume / maxVolume)
                * volumeArea.Height;

            g.FillRectangle(
                brush,
                x - bodyWidth / 2,
                volumeArea.Bottom - barHeight,
                bodyWidth,
                barHeight);
        }

        TextRenderer.DrawText(
            g,
            "거래량",
            Font,
            new Point(
                15,
                (int)_priceArea.Bottom + 5),
            Color.SlateGray);

        bool multipleDates =
            visible[0].Date != visible[^1].Date;

        int previousIndex = -1;

        for (int i = 0; i <= 4; i++)
        {
            int index = (int)Math.Round(
                (count - 1) * i / 4d);

            if (index == previousIndex)
            {
                continue;
            }

            previousIndex = index;

            CandleItem candle = visible[index];

            string label = candle.Time is DateTimeOffset time
                ? time.ToOffset(TimeSpan.FromHours(9))
                    .ToString(
                        multipleDates
                            ? "MM/dd HH:mm"
                            : "HH:mm")
                : candle.Date.ToString("yy/MM/dd");

            float x =
                _priceArea.Left + step * (index + 0.5f);

            int labelWidth = Math.Min(
                100,
                Math.Max(45, (int)(plotWidth / 5)));

            int left = Math.Clamp(
                (int)x - labelWidth / 2,
                (int)_priceArea.Left,
                (int)_priceArea.Right - labelWidth);

            TextRenderer.DrawText(
                g,
                label,
                Font,
                new Rectangle(
                    left,
                    (int)volumeArea.Bottom + 6,
                    labelWidth,
                    24),
                Color.SlateGray,
                TextFormatFlags.HorizontalCenter
                | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis);
        }
    }

    private void HandleMouseMove(
        object? sender,
        MouseEventArgs e)
    {
        if (_candles.Count == 0
            || !_priceArea.Contains(e.Location))
        {
            ClearHover();
            return;
        }

        int count = Math.Min(
            VisibleCount,
            _candles.Count - _startIndex);

        int localIndex = (int)(
            (e.X - _priceArea.Left)
            / _priceArea.Width
            * count);

        localIndex = Math.Clamp(
            localIndex,
            0,
            count - 1);

        int index = _startIndex + localIndex;

        if (index == _hoverIndex)
        {
            return;
        }

        _hoverIndex = index;

        CandleItem candle = _candles[index];

        string timestamp =
            candle.Time is DateTimeOffset time
                ? time.ToOffset(TimeSpan.FromHours(9))
                    .ToString("yyyy-MM-dd HH:mm")
                : candle.Date.ToString("yyyy-MM-dd");

        _tooltip.SetToolTip(
            this,
            timestamp
            + Environment.NewLine
            + $"시가 {candle.Open:N0} / 고가 {candle.High:N0}"
            + Environment.NewLine
            + $"저가 {candle.Low:N0} / 종가 {candle.Close:N0}"
            + Environment.NewLine
            + $"거래량 {candle.Volume:N0}주");
    }

    private static DateTimeOffset GetTimestamp(
        CandleItem candle)
    {
        return candle.Time
            ?? new DateTimeOffset(
                candle.Date.ToDateTime(TimeOnly.MinValue),
                TimeSpan.FromHours(9));
    }

    private void ClearHover()
    {
        _hoverIndex = -1;
        _tooltip.SetToolTip(this, null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tooltip.Dispose();
        }

        base.Dispose(disposing);
    }
}