using MockInvestment.Client.Services;

namespace MockInvestment.Client;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var apiClient = new ApiClient();

        Application.Run(new LoginForm(apiClient));
    }
}