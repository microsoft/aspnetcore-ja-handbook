using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ApiProgram = global::Program;

namespace AuthApi.Tests;

public sealed class DevelopmentJwtTests
{
    [Fact]
    public async Task UserJwts_TemporaryProjectAndSecrets_AuthenticateWithUnmodifiedDevelopmentDefaults()
    {
        var directory = Directory.CreateTempSubdirectory("chapter09-user-jwts-");
        var secretsId = $"chapter09-tests-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "Temporary.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <UserSecretsId>{secretsId}</UserSecretsId>
                  </PropertyGroup>
                </Project>
                """);
            var chapter = new DirectoryInfo(AppContext.BaseDirectory);
            while (chapter is not null && !File.Exists(Path.Combine(chapter.FullName, "Chapter09.slnx")))
            {
                chapter = chapter.Parent;
            }
            Assert.NotNull(chapter);
            File.Copy(Path.Combine(chapter.FullName, "global.json"), Path.Combine(directory.FullName, "global.json"));
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "appsettings.Development.json"), "{}");

            var reader = await CreateTokenAsync(directory.FullName, "reader");
            var reporter = await CreateTokenAsync(directory.FullName, "reporter", "--claim", "permission=reports.read");
            var administrator = await CreateTokenAsync(directory.FullName, "administrator", "--role", "Admin");
            var scoped = await CreateTokenAsync(directory.FullName, "scoped-reader",
                "--scope", "reports.read reports.write");

            await using var factory = new DevelopmentFactory(directory.FullName, secretsId);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost:7051"), AllowAutoRedirect = false
            });
            var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");
            Assert.True(options.MapInboundClaims);
            Assert.Equal(ClaimTypes.Name, options.TokenValidationParameters.NameClaimType);
            Assert.Equal(ClaimTypes.Role, options.TokenValidationParameters.RoleClaimType);

            using (var request = new HttpRequestMessage(HttpMethod.Get, "/me"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", reader);
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("reader", body.GetProperty("name").GetString());
            }
            await AssertStatusAsync(client, reader, "/reports", HttpStatusCode.Forbidden);
            await AssertStatusAsync(client, reporter, "/reports", HttpStatusCode.OK);
            await AssertStatusAsync(client, administrator, "/admin", HttpStatusCode.OK);
            await AssertStatusAsync(client, administrator, "/reports", HttpStatusCode.Forbidden);
            await AssertStatusAsync(client, scoped, "/scoped-reports", HttpStatusCode.OK);
        }
        finally
        {
            // Tokens and signing keys belong only to this unique temporary project, never the sample.
            try
            {
                await RunDotnetAsync(directory.FullName, "user-jwts", "clear", "--force");
                await RunDotnetAsync(directory.FullName, "user-secrets", "clear");
            }
            finally
            {
                directory.Delete(recursive: true);
            }
        }
    }

    private static async Task AssertStatusAsync(HttpClient client, string token, string path, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }

    private static async Task<string> CreateTokenAsync(string directory, string name, params string[] extra)
    {
        var args = new List<string>
        {
            "user-jwts", "create", "--name", name, "--audience", "https://localhost:7051",
            "--valid-for", "1h", "--output", "token"
        };
        args.AddRange(extra);
        return (await RunDotnetAsync(directory, args.ToArray())).Trim();
    }

    private static async Task<string> RunDotnetAsync(string directory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("Temporary user-jwts command exceeded its timeout.");
        }
        await error;
        // Never include captured output in assertions/errors: it may contain a JWT or signing key.
        Assert.Equal(0, process.ExitCode);
        return await output;
    }

    private sealed class DevelopmentFactory(string directory, string secretsId) : WebApplicationFactory<ApiProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddJsonFile(Path.Combine(directory, "appsettings.Development.json"));
                config.AddUserSecrets(secretsId);
            });
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>("Bearer", options =>
                    options.Backchannel = new HttpClient(new RejectNetworkHandler()));
            });
        }
    }

    private sealed class RejectNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Development JWT tests must not make external calls.");
    }
}
