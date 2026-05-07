using Dok.FakeProviders;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<FakeProviderService>();

var host = builder.Build();
host.Run();
