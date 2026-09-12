using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AuthApi.Tests;

public sealed class AuthenticationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void JwtBearer_ProductionOptions_UseUnmappedProviderClaimsAndValidation()
    {
        Assert.Equal("Production", factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");
        Assert.Equal(ApiFactory.Issuer, options.Authority);
        Assert.Equal(ApiFactory.Audience, options.Audience);
        Assert.False(options.MapInboundClaims);
        Assert.Equal("name", options.TokenValidationParameters.NameClaimType);
        Assert.Equal("roles", options.TokenValidationParameters.RoleClaimType);
        Assert.True(options.RequireHttpsMetadata);
        Assert.True(options.TokenValidationParameters.RequireSignedTokens);
        Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.True(options.TokenValidationParameters.ValidateIssuer);
        Assert.True(options.TokenValidationParameters.ValidateLifetime);
        Assert.Equal(TimeSpan.FromMinutes(5), options.TokenValidationParameters.ClockSkew);
    }

    [Fact]
    public async Task Health_Anonymous_ReturnsOkJson()
    {
        using var client = factory.NewClient();
        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("/me")]
    [InlineData("/reports")]
    [InlineData("/admin")]
    [InlineData("/api/reports")]
    [InlineData("/api/reports/audit")]
    [InlineData("/scoped-reports")]
    [InlineData("/new-endpoint")]
    public async Task ProtectedEndpoint_Anonymous_Returns401WithBearerChallenge(string path)
    {
        using var client = factory.NewClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Me_ValidSignedProviderToken_ReturnsNameFromRawNameClaim()
    {
        using var client = factory.NewClient(factory.Token());
        using var response = await client.GetAsync("/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Test Reader", body.GetProperty("name").GetString());
        Assert.Single(body.EnumerateObject());
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("audience")]
    [InlineData("issuer")]
    [InlineData("expired")]
    [InlineData("future")]
    public async Task Me_InvalidSignedToken_Returns401(string invalid)
    {
        using var client = factory.NewClient(factory.Token(invalid: invalid));
        using var response = await client.GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
    }

    [Theory]
    [InlineData("/reports", null, null, 403)]
    [InlineData("/reports", "reports.read", null, 200)]
    [InlineData("/reports", "reports.read.all", null, 403)]
    [InlineData("/reports", "reports.read reports.write", null, 403)]
    [InlineData("/reports", null, "Admin", 403)]
    [InlineData("/admin", null, null, 403)]
    [InlineData("/admin", null, "Admin", 200)]
    [InlineData("/admin", null, "Auditor", 403)]
    [InlineData("/admin", "reports.read", null, 403)]
    [InlineData("/api/reports", null, null, 403)]
    [InlineData("/api/reports", "reports.read", null, 200)]
    [InlineData("/api/reports", null, "Admin", 403)]
    [InlineData("/api/reports/audit", null, "Admin", 403)]
    [InlineData("/api/reports/audit", null, "Auditor", 403)]
    [InlineData("/api/reports/audit", "reports.read", null, 403)]
    [InlineData("/api/reports/audit", "reports.read", "Reader", 403)]
    [InlineData("/api/reports/audit", "reports.read", "Admin", 200)]
    [InlineData("/api/reports/audit", "reports.read", "Auditor", 200)]
    public async Task ReportsAndAdmin_PermissionAndRole_EnforcePolicyAndControllerAndOr(
        string path, string? permission, string? role, int expected)
    {
        var claims = new List<Claim>();
        if (permission is not null) claims.Add(new Claim("permission", permission));
        if (role is not null) claims.Add(new Claim("roles", role));
        using var client = factory.NewClient(factory.Token(claims));
        using var response = await client.GetAsync(path);
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        if (expected == 403)
        {
            Assert.Empty(response.Headers.WwwAuthenticate);
            Assert.Null(response.Headers.Location);
        }
        else if (path is "/reports" or "/api/reports")
        {
            Assert.Equal(new[] { "月次レポート" }, await response.Content.ReadFromJsonAsync<string[]>());
        }
        else
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(path == "/admin" ? "管理者向け情報" : "監査用情報",
                body.GetProperty("message").GetString());
        }
    }

    [Theory]
    [InlineData("reports.read", 200)]
    [InlineData("reports.write reports.read", 200)]
    [InlineData("  reports.read   reports.write  ", 200)]
    [InlineData("reports.read.all", 403)]
    [InlineData("Reports.Read", 403)]
    [InlineData("", 403)]
    public async Task ScopedReports_SignedScopeClaim_UsesExactSpaceSeparatedScope(string scope, int expected)
    {
        using var client = factory.NewClient(factory.Token([new Claim("scope", scope)]));
        using var response = await client.GetAsync("/scoped-reports");
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
    }

    [Fact]
    public async Task Fallback_NewUnannotatedEndpoint_AllowsAuthenticatedUserWithoutExtraClaims()
    {
        using var client = factory.NewClient(factory.Token());
        using var response = await client.GetAsync("/new-endpoint");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Authority")]
    [InlineData("Audience")]
    public void JwtBearer_ProductionMissingSettings_FailsFast(string setting)
    {
        using var invalid = factory.WithWebHostBuilder(
            builder => builder.UseSetting($"Authentication:{setting}", ""));
        var error = Assert.Throws<InvalidOperationException>(() => invalid.CreateClient());
        Assert.Contains("Authentication:Authority と Authentication:Audience", error.Message);
    }
}
