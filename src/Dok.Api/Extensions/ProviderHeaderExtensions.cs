namespace Dok.Api.Extensions;

internal static class ProviderHeaderExtensions
{
    public const string HeaderName = "X-Dok-Provider";

    extension(WebApplication app)
    {
        /// <summary>
        /// Adiciona o header <c>X-Dok-Provider</c> em cada response indicando qual provider
        /// efetivamente respondeu (lê de <see cref="ProviderUsage"/>, scoped). Body permanece
        /// literal conforme a spec — header é metadado HTTP, não payload.
        /// </summary>
        public WebApplication UseProviderHeader()
        {
            app.Use((ctx, next) =>
            {
                ctx.Response.OnStarting(() =>
                {
                    var usage = ctx.RequestServices.GetService<ProviderUsage>();
                    if (!string.IsNullOrEmpty(usage?.Name))
                        ctx.Response.Headers[HeaderName] = usage.Name;
                    return Task.CompletedTask;
                });
                return next();
            });

            return app;
        }
    }
}
