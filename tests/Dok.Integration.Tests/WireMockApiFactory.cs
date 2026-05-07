namespace Dok.Integration.Tests;

public sealed class WireMockApiFactory : WebApplicationFactory<Program>
{
    public WireMockServer ProviderA { get; }
    public WireMockServer ProviderB { get; }

    public WireMockApiFactory()
    {
        ProviderA = WireMockServer.Start();
        ProviderB = WireMockServer.Start();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Providers:ProviderAUrl"] = ProviderA.Url,
                ["Providers:ProviderBUrl"] = ProviderB.Url,
                // janelas curtas pros testes não esperarem Polly real
                ["Resilience:TotalTimeoutSeconds"] = "5",
                ["Resilience:PerAttemptTimeoutSeconds"] = "2",
                ["Resilience:RetryCount"] = "0",
                ["Resilience:RetryBaseDelayMs"] = "50",
                ["Resilience:CircuitBreakerFailures"] = "100",
                ["Resilience:CircuitBreakerWindowSeconds"] = "60",
                ["Resilience:CircuitBreakerBreakDurationSeconds"] = "30",
            });
        });
        builder.ConfigureTestServices(services =>
        {
            // Fixa a data dos cálculos em 2024-05-10 conforme spec, sem afetar o
            // TimeProvider global (que o Polly usa para a sliding window do CB).
            services.SetDebtsReferenceDate(new DateOnly(2024, 5, 10));

            // Timeouts agressivos pra testes não esperarem Polly real
            services.PostConfigureAll<Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions>(o =>
            {
                // ShouldHandle false = sem retentativa (Polly exige MaxRetryAttempts >= 1)
                o.Retry.MaxRetryAttempts = 1;
                o.Retry.Delay = TimeSpan.FromMilliseconds(50);
                o.Retry.ShouldHandle = static _ => ValueTask.FromResult(false);

                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
                o.CircuitBreaker.MinimumThroughput = 100;
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
                o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
            });
        });
    }

    public void ResetMocks()
    {
        ProviderA.Reset();
        ProviderB.Reset();
    }

    public void StubProviderA(string body, string contentType = "application/json", int status = 200)
        => ProviderA.Given(Request.Create().WithPath("/debts/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", contentType).WithBody(body));

    public void StubProviderB(string body, string contentType = "application/xml", int status = 200)
        => ProviderB.Given(Request.Create().WithPath("/debts/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", contentType).WithBody(body));

    public void StubProviderAFails(int status = 500)
        => ProviderA.Given(Request.Create().WithPath("/debts/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status));

    public void StubProviderBFails(int status = 500)
        => ProviderB.Given(Request.Create().WithPath("/debts/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ProviderA.Stop();
            ProviderA.Dispose();
            ProviderB.Stop();
            ProviderB.Dispose();
        }
        base.Dispose(disposing);
    }
}
