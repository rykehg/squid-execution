using Microsoft.AspNetCore.Builder;
using Squid.Service;
using Squid.Tests.Auth;

namespace Squid.Tests.Backup;

/// <summary>
/// A started <see cref="SquidServiceHost"/> for the backup suites — the same call
/// <c>dotnet run</c> makes — on a dynamic port with its own temp storage and volume. Hosts never
/// overlap (localhost clustering fixes the silo ports), so every class that starts one belongs to
/// the <c>ServiceHost</c> collection.
/// </summary>
internal sealed class BackupHost : IAsyncDisposable
{
    private BackupHost(WebApplication app, HttpClient http)
    {
        App = app;
        Http = http;
    }

    /// <summary>The application under test.</summary>
    public WebApplication App { get; }

    /// <summary>Base client the per-test cookie jars share.</summary>
    public HttpClient Http { get; }

    /// <summary>Builds the host with <paramref name="args"/> plus the optional test seam, and starts it.</summary>
    public static async Task<BackupHost> StartAsync(
        string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var app = SquidServiceHost.Build(args, configure);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        return new BackupHost(app, new HttpClient { BaseAddress = new Uri(app.Urls.Single()) });
    }

    /// <summary>A fresh cookie jar: sessions are never shared between tests.</summary>
    public AuthClient NewClient() => new(Http);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }
}
