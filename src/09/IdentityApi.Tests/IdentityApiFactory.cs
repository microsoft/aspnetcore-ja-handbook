using System.Collections.Concurrent;
using System.Net;
using IdentityApi.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ApiProgram = global::Program;

namespace IdentityApi.Tests;

internal sealed class IdentityApiFactory(string environment = "Development")
    : WebApplicationFactory<ApiProgram>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    public CapturingEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            connection.Open();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            services.RemoveAll<IEmailSender<IdentityUser>>();
            services.AddSingleton<IEmailSender<IdentityUser>>(Emails);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();
        return host;
    }

    // CreateDefaultClient は Cookie の自動処理を追加しないため、専用ハンドラーで明示的に管理する。
    public HttpClient NewClient() =>
        CreateDefaultClient(new Uri("https://localhost"), new CookieJarHandler());

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await connection.DisposeAsync();
    }

    private sealed class CookieJarHandler : DelegatingHandler
    {
        private readonly CookieContainer cookies = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var cookieHeader = cookies.GetCookieHeader(uri);
            if (cookieHeader.Length > 0)
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var cookie in setCookies)
                {
                    cookies.SetCookies(uri, cookie);
                }
            }
            return response;
        }
    }
}

internal sealed class CapturingEmailSender : IEmailSender<IdentityUser>
{
    public ConcurrentQueue<(string Email, string Link)> Confirmations { get; } = new();

    public Task SendConfirmationLinkAsync(IdentityUser user, string email, string confirmationLink)
    {
        Confirmations.Enqueue((email, WebUtility.HtmlDecode(confirmationLink)));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetLinkAsync(IdentityUser user, string email, string resetLink) =>
        Task.CompletedTask;

    public Task SendPasswordResetCodeAsync(IdentityUser user, string email, string resetCode) =>
        Task.CompletedTask;
}
