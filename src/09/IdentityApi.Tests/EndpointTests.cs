using System.Net;
using System.Net.Http.Json;
using IdentityApi.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IdentityApi.Tests;

public sealed class EndpointTests
{
    [Theory]
    [InlineData("GET", "/api/me")]
    [InlineData("GET", "/api/antiforgery")]
    [InlineData("POST", "/api/logout")]
    [InlineData("GET", "/identity/manage/info")]
    [InlineData("POST", "/identity/manage/info")]
    [InlineData("POST", "/identity/manage/2fa")]
    public async Task ProtectedEndpoint_Anonymous_Returns401WithoutRedirect(string method, string path)
    {
        await using var factory = new IdentityApiFactory();
        using var client = factory.NewClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { });
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Startup_OutsideDevelopment_RequiresReplacingDevelopmentEmailSender(string environment)
    {
        await using var factory = new IdentityApiFactory(environment);

        var error = Assert.Throws<InvalidOperationException>(() => factory.NewClient());

        Assert.Contains("Development-only", error.Message);
        Assert.Contains("Replace DevelopmentEmailSender", error.Message);
    }

    [Fact]
    public async Task Startup_IdentityOptionsAndRoles_MatchChapterConfiguration()
    {
        await using var factory = new IdentityApiFactory();
        var identity = factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        Assert.True(identity.SignIn.RequireConfirmedAccount);
        Assert.Equal(12, identity.Password.RequiredLength);
        Assert.True(identity.Password.RequireDigit);
        Assert.True(identity.Password.RequireLowercase);
        Assert.True(identity.Password.RequireUppercase);
        Assert.True(identity.Password.RequireNonAlphanumeric);
        Assert.Equal(5, identity.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(5), identity.Lockout.DefaultLockoutTimeSpan);
        var antiforgery = factory.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
        Assert.Equal("X-CSRF-TOKEN", antiforgery.HeaderName);
        Assert.Equal(CookieSecurePolicy.Always, antiforgery.Cookie.SecurePolicy);

        using var scope = factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        Assert.True((await roles.CreateAsync(new IdentityRole("Admin"))).Succeeded);
        Assert.True(await roles.RoleExistsAsync("Admin"));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.ToListAsync());
    }

    [Fact]
    public async Task Endpoints_RouteGroups_ProtectApiAndIdentityManagementWithoutAnonymousOverride()
    {
        await using var factory = new IdentityApiFactory();
        using var client = factory.NewClient();
        using var root = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>().ToArray();
        var protectedEndpoints = endpoints.Where(endpoint =>
            endpoint.RoutePattern.RawText!.StartsWith("/api/", StringComparison.Ordinal)
            || endpoint.RoutePattern.RawText.StartsWith("/identity/manage/", StringComparison.Ordinal)).ToArray();
        Assert.Equal(6, protectedEndpoints.Length);
        Assert.All(protectedEndpoints, endpoint =>
        {
            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        });

        foreach (var path in new[]
        {
            "/identity/register", "/identity/login", "/identity/refresh", "/identity/confirmEmail",
            "/identity/resendConfirmationEmail", "/identity/forgotPassword", "/identity/resetPassword"
        })
        {
            Assert.Contains(endpoints, endpoint => endpoint.RoutePattern.RawText == path);
        }
    }
}
