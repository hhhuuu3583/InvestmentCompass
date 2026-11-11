namespace MockInvestment.Api.Services;

public class TradeException : Exception
{
    public string Code { get; }

    public TradeException(
        string code,
        string message)
        : base(message)
    {
        Code = code;
    }
}