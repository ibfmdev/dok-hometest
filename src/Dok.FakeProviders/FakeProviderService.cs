using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Settings;

namespace Dok.FakeProviders;

public sealed class FakeProviderService(
    IConfiguration config,
    ILogger<FakeProviderService> logger) : IHostedService
{
    private WireMockServer? _server;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var port = int.Parse(config["Provider:Port"] ?? "9001");
        var dataFile = config["Provider:DataFile"] ?? "data/providerA.json";
        var contentType = config["Provider:ContentType"] ?? "application/json";
        var name = config["Provider:Name"] ?? "Provider";

        _server = WireMockServer.Start(new WireMockServerSettings
        {
            Port = port,
            StartAdminInterface = false,
        });

        var resolvedPath = Path.IsPathRooted(dataFile)
            ? dataFile
            : Path.Combine(AppContext.BaseDirectory, dataFile);

        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"Data file not found: {resolvedPath}");

        var body = File.ReadAllText(resolvedPath);

        _server
            .Given(Request.Create()
                .WithPath(new RegexMatcher("^/debts/[A-Z0-9]+$"))
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", contentType)
                .WithBody(body));

        logger.LogInformation(
            "{Name} listening on {Url} (data: {DataFile}, content-type: {ContentType})",
            name, _server.Url, dataFile, contentType);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _server?.Stop();
        _server?.Dispose();
        return Task.CompletedTask;
    }
}
