using System.Diagnostics;

namespace MockInvestment.Api.Services;

// 서버 전체에서 한국투자증권 요청 간격을 공유
public sealed class KisRequestGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _lastStarted;

    // 개발 단계에서 보수적으로 적용하는 간격
    private static readonly TimeSpan MinimumInterval =
        TimeSpan.FromMilliseconds(1100);

    public async Task<HttpResponseMessage> SendAsync(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_lastStarted != 0)
            {
                TimeSpan elapsed =
                    Stopwatch.GetElapsedTime(_lastStarted);

                TimeSpan remaining = MinimumInterval - elapsed;

                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(
                        remaining,
                        cancellationToken);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            _lastStarted = Stopwatch.GetTimestamp();

            return await send();
        }
        finally
        {
            _gate.Release();
        }
    }
}

// "Kis" HttpClient의 요청에 공통 적용
public sealed class KisThrottleHandler : DelegatingHandler
{
    private readonly KisRequestGate _gate;

    public KisThrottleHandler(KisRequestGate gate)
    {
        _gate = gate;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return _gate.SendAsync(
            () => base.SendAsync(request, cancellationToken),
            cancellationToken);
    }
}