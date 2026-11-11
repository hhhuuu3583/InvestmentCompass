using MockInvestment.Api.Data;
using MockInvestment.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<StockRepository>();
builder.Services.AddSingleton<KisTokenService>();
builder.Services.AddScoped<StockMasterService>();
builder.Services.AddSingleton<KisQuoteService>();
builder.Services.AddSingleton<QuoteCacheService>();
builder.Services.AddScoped<TradeRepository>();
builder.Services.AddScoped<TradingService>();
builder.Services.AddScoped<PortfolioRepository>();
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddScoped<RankingRepository>();
builder.Services.AddScoped<RankingService>();
builder.Services.AddScoped<KisChartService>();
builder.Services.AddSingleton<KisMinuteChartService>();
//대회용
builder.Services.AddScoped<CompetitionTradeRepository>();
builder.Services.AddScoped<CompetitionTradingService>();
builder.Services.AddScoped<CompetitionAccountRepository>();
builder.Services.AddScoped<CompetitionRepository>();
builder.Services.AddScoped<CompetitionParticipantRepository>();
builder.Services.AddScoped<CompetitionParticipantService>();
builder.Services.AddScoped<CompetitionAccountService>();
builder.Services.AddScoped<CompetitionRankingService>();
builder.Services.AddScoped<CompetitionFinalRankingRepository>();
builder.Services.AddScoped<CompetitionApplicationRepository>();
builder.Services.AddScoped<CompetitionApplicationService>();
builder.Services.AddScoped<CompetitionTradeHistoryRepository>();
builder.Services.AddScoped<CompetitionTradeHistoryService>();

builder.Services.AddMemoryCache();

string jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("JWT 발급자 설정이 없습니다.");

string jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("JWT 사용 대상 설정이 없습니다.");

string jwtSecret = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("JWT 비밀키 설정이 없습니다.");

byte[] jwtKeyBytes = Convert.FromBase64String(jwtSecret);

if (jwtKeyBytes.Length < 32)
{
    throw new InvalidOperationException("JWT 비밀키는 최소 32바이트여야 합니다.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,

                ValidateAudience = true,
                ValidAudience = jwtAudience,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey =
                    new SymmetricSecurityKey(jwtKeyBytes),

                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,

                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },

                ClockSkew = TimeSpan.FromSeconds(30),
                RoleClaimType = ClaimTypes.Role,
            };
    });

builder.Services.AddHttpClient("Kis", client =>
{
    string baseUrl = builder.Configuration["Kis:BaseUrl"]
        ?? throw new InvalidOperationException(
            "한국투자증권 BaseUrl 설정이 없습니다.");

    client.BaseAddress =
        new Uri(baseUrl.TrimEnd('/') + "/");

    client.Timeout = TimeSpan.FromSeconds(15);
})
.AddHttpMessageHandler<KisThrottleHandler>();

builder.Services.AddHttpClient("StockMaster", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton<KisRequestGate>();
builder.Services.AddTransient<KisThrottleHandler>();
builder.Services.AddAuthorization();

// AI explanation, retrieval and per-user conversation services.
builder.Services.AddScoped<TavilySearchService>();
builder.Services.AddScoped<ResearchService>();
builder.Services.AddScoped<GeneralExplanationService>();
builder.Services.AddScoped<ResearchDialogueService>();
builder.Services.AddSingleton<ResearchConversationStore>();
builder.Services.AddHttpClient("OpenAI", client =>
{
    client.Timeout = TimeSpan.FromSeconds(180);
});
builder.Services.AddHttpClient("Tavily", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("Research", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.FindFirst("sub")?.Value ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 2,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();
