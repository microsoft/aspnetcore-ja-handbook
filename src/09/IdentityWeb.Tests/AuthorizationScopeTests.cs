using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using IdentityWeb.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IdentityProgram = global::Program;

namespace IdentityWeb.Tests;

public sealed class AuthorizationScopeTests
{
    [Fact]
    public async Task ActionAuthorize_OnlyAnnotatedAction_RequiresAuthentication()
    {
        await using var identity = new IdentityFactory();
        await using var factory = CreateFactory(identity, "none");
        using var anonymous = CreateClient(factory);
        using var authenticated = CreateClient(factory, authenticated: true);

        await AssertProtectedAsync(anonymous, authenticated, "/scope/action/private", "private action");
        await AssertPublicAsync(anonymous, authenticated, "/scope/action/public", "public sibling");
    }

    [Fact]
    public async Task ControllerAuthorize_AllActionsExceptAllowAnonymous_RequireAuthentication()
    {
        await using var identity = new IdentityFactory();
        await using var factory = CreateFactory(identity, "none");
        using var anonymous = CreateClient(factory);
        using var authenticated = CreateClient(factory, authenticated: true);

        await AssertProtectedAsync(anonymous, authenticated, "/scope/controller/privacy", "privacy action");
        await AssertProtectedAsync(anonymous, authenticated, "/scope/controller/second", "second action");
        await AssertPublicAsync(anonymous, authenticated, "/scope/controller/index", "anonymous index");
        await AssertPublicAsync(anonymous, authenticated, "/scope/controller/error", "anonymous error");
    }

    [Theory]
    [InlineData("none")]
    [InlineData("global")]
    [InlineData("fallback")]
    public async Task DefaultProtection_UnannotatedActionsAcrossControllers_RespectsConfiguredScope(string mode)
    {
        await using var identity = new IdentityFactory();
        await using var factory = CreateFactory(identity, mode);
        using var anonymous = CreateClient(factory);
        using var authenticated = CreateClient(factory, authenticated: true);

        // Identity registration alone does not require authentication on these MVC actions.
        foreach (var (path, body) in new[]
        {
            ("/scope/action/public", "public sibling"),
            ("/scope/unannotated/privacy", "privacy action")
        })
        {
            if (mode == "none")
                await AssertPublicAsync(anonymous, authenticated, path, body);
            else
                await AssertProtectedAsync(anonymous, authenticated, path, body);
        }

        await AssertPublicAsync(anonymous, authenticated, "/scope/unannotated/index", "anonymous index");
        await AssertPublicAsync(anonymous, authenticated, "/scope/unannotated/error", "anonymous error");
    }

    [Theory]
    [InlineData("none")]
    [InlineData("global")]
    [InlineData("fallback")]
    public async Task DefaultProtection_NonMvcEndpoint_OnlyFallbackRequiresAuthentication(string mode)
    {
        await using var identity = new IdentityFactory();
        await using var factory = CreateFactory(identity, mode);
        using var anonymous = CreateClient(factory);
        using var authenticated = CreateClient(factory, authenticated: true);

        if (mode == "fallback")
            await AssertProtectedAsync(anonymous, authenticated, "/scope/endpoint", "non-MVC endpoint");
        else
            await AssertPublicAsync(anonymous, authenticated, "/scope/endpoint", "non-MVC endpoint");

        await AssertPublicAsync(anonymous, authenticated, "/scope/endpoint/public", "anonymous endpoint");
    }

    [Theory]
    [InlineData("none", "/Identity/Account/Login")]
    [InlineData("none", "/Identity/Account/Register")]
    [InlineData("global", "/Identity/Account/Login")]
    [InlineData("global", "/Identity/Account/Register")]
    [InlineData("fallback", "/Identity/Account/Login")]
    [InlineData("fallback", "/Identity/Account/Register")]
    public async Task IdentityUi_LoginAndRegister_RemainAnonymous(string mode, string path)
    {
        await using var identity = new IdentityFactory();
        await using var factory = CreateFactory(identity, mode);
        using var anonymous = CreateClient(factory);

        using var response = await anonymous.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("<form", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("none")]
    [InlineData("global")]
    [InlineData("fallback")]
    public async Task IdentityUi_Manage_RequiresAuthenticationEvenWithoutBusinessPagePolicy(string mode)
    {
        await using var identity = new IdentityFactory();
        await using var factory = CreateFactory(identity, mode);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var result = await users.CreateAsync(new IdentityUser
            {
                Id = ScopeAuthenticationHandler.UserId,
                UserName = "scope@example.test",
                Email = "scope@example.test",
                EmailConfirmed = true
            });
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Description)));
        }
        using var anonymous = CreateClient(factory);
        using var authenticated = CreateClient(factory, authenticated: true);

        await AssertLoginRedirectAsync(anonymous, "/Identity/Account/Manage");
        using var response = await authenticated.GetAsync("/Identity/Account/Manage");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("scope@example.test", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("none")]
    [InlineData("global")]
    [InlineData("fallback")]
    public async Task MapStaticAssets_IdentityCss_OnlyFallbackRequiresAuthentication(string mode)
    {
        await using var identity = new IdentityFactory();
        await using var factory = CreateFactory(identity, mode);
        using var anonymous = CreateClient(factory);
        using var authenticated = CreateClient(factory, authenticated: true);

        // Exercise an actual Identity UI asset served by MapStaticAssets; this sample has no wwwroot.
        if (mode == "fallback")
            await AssertLoginRedirectAsync(anonymous, "/Identity/css/site.css");
        else
        {
            using var response = await anonymous.GetAsync("/Identity/css/site.css");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/css", response.Content.Headers.ContentType!.MediaType);
        }

        using var authenticatedResponse = await authenticated.GetAsync("/Identity/css/site.css");
        Assert.Equal(HttpStatusCode.OK, authenticatedResponse.StatusCode);
        Assert.Equal("text/css", authenticatedResponse.Content.Headers.ContentType!.MediaType);
        Assert.NotEmpty(await authenticatedResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FallbackPolicy_ExplicitlyPublicStaticAssets_LeavesBusinessEndpointsProtected()
    {
        await using var identity = new IdentityFactory();
        await using var factory = CreateFactory(identity, "fallback", allowAnonymousStaticAssets: true);
        using var anonymous = CreateClient(factory);
        using var authenticated = CreateClient(factory, authenticated: true);

        using var css = await anonymous.GetAsync("/Identity/css/site.css");
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Equal("text/css", css.Content.Headers.ContentType!.MediaType);
        Assert.NotEmpty(await css.Content.ReadAsStringAsync());

        await AssertProtectedAsync(anonymous, authenticated, "/scope/unannotated/privacy", "privacy action");
        await AssertProtectedAsync(anonymous, authenticated, "/scope/endpoint", "non-MVC endpoint");
        await AssertPublicAsync(anonymous, authenticated, "/scope/unannotated/index", "anonymous index");
        await AssertPublicAsync(anonymous, authenticated, "/scope/unannotated/error", "anonymous error");
        await AssertLoginRedirectAsync(anonymous, "/Identity/Account/Manage");
        foreach (var path in new[] { "/Identity/Account/Login", "/Identity/Account/Register" })
        {
            using var page = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Equal("text/html", page.Content.Headers.ContentType!.MediaType);
        }
    }

    private static WebApplicationFactory<IdentityProgram> CreateFactory(
        IdentityFactory identity, string mode, bool allowAnonymousStaticAssets = false) =>
        identity.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddControllersWithViews()
                .AddApplicationPart(typeof(ScopeActionController).Assembly);
            services.AddSingleton<IStartupFilter>(new ScopeEndpointsStartupFilter(allowAnonymousStaticAssets));

            // Test-only authentication. Leave the real Identity cookie challenge scheme unchanged.
            services.AddAuthentication(options =>
                options.DefaultAuthenticateScheme = ScopeAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, ScopeAuthenticationHandler>(
                    ScopeAuthenticationHandler.SchemeName, _ => { });

            if (mode == "global")
            {
                // MVC filters also apply to Razor Pages, including the built-in Identity UI.
                // Configure<MvcOptions> augments the sample's existing MVC registration.
                services.Configure<MvcOptions>(options => options.Filters.Add(
                    new AuthorizeFilter(new AuthorizationPolicyBuilder()
                        .RequireAuthenticatedUser().Build())));
            }
            else if (mode == "fallback")
            {
                services.AddAuthorization(options => options.FallbackPolicy =
                    new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
            }
        }));

    private static HttpClient CreateClient(WebApplicationFactory<IdentityProgram> factory, bool authenticated = false)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        if (authenticated)
            client.DefaultRequestHeaders.Add(ScopeAuthenticationHandler.HeaderName, "authenticated");
        return client;
    }

    private static async Task AssertLoginRedirectAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal("/Identity/Account/Login", location.AbsolutePath);
        Assert.Equal(path, QueryHelpers.ParseQuery(location.Query)["ReturnUrl"].ToString());
    }

    private static async Task AssertSuccessAsync(HttpClient client, string path, string body)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(body, await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertProtectedAsync(HttpClient anonymous, HttpClient authenticated, string path, string body)
    {
        await AssertLoginRedirectAsync(anonymous, path);
        await AssertSuccessAsync(authenticated, path, body);
    }

    private static async Task AssertPublicAsync(HttpClient anonymous, HttpClient authenticated, string path, string body)
    {
        await AssertSuccessAsync(anonymous, path, body);
        await AssertSuccessAsync(authenticated, path, body);
    }
}

// These controllers are discovered only by the application part added in CreateFactory.
[Route("scope/action")]
public sealed class ScopeActionController : Controller
{
    [Authorize]
    [HttpGet("private")]
    public IActionResult Private() => Content("private action");

    [HttpGet("public")]
    public IActionResult Public() => Content("public sibling");
}

[Authorize]
[Route("scope/controller")]
public sealed class ScopeControllerAuthorizeController : Controller
{
    [HttpGet("privacy")]
    public IActionResult Privacy() => Content("privacy action");

    [HttpGet("second")]
    public IActionResult Second() => Content("second action");

    [AllowAnonymous]
    [HttpGet("index")]
    public IActionResult Index() => Content("anonymous index");

    [AllowAnonymous]
    [HttpGet("error")]
    public IActionResult Error() => Content("anonymous error");
}

[Route("scope/unannotated")]
public sealed class ScopeUnannotatedController : Controller
{
    [HttpGet("privacy")]
    public IActionResult Privacy() => Content("privacy action");

    [AllowAnonymous]
    [HttpGet("index")]
    public IActionResult Index() => Content("anonymous index");

    [AllowAnonymous]
    [HttpGet("error")]
    public IActionResult Error() => Content("anonymous error");
}

internal sealed class ScopeAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "AuthorizationScopeTests";
    internal const string HeaderName = "X-AuthorizationScopeTests-User";
    internal const string UserId = "authorization-scope-test-user";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers[HeaderName] != "authenticated")
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, UserId),
            new Claim(ClaimTypes.Name, "scope@example.test")
        ], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

internal sealed class ScopeEndpointsStartupFilter(bool allowAnonymousStaticAssets) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        // Contribute endpoints to the existing routing pipeline without replacing production middleware.
        app.UseEndpoints(endpoints =>
        {
            if (allowAnonymousStaticAssets)
            {
                // Reuse the existing static asset mapping and apply the documentation's
                // convention only in this test host, never to MVC or other endpoints.
                endpoints.MapStaticAssets().Add(endpointBuilder =>
                    endpointBuilder.Metadata.Add(new AllowAnonymousAttribute()));
            }
            endpoints.MapGet("/scope/endpoint", () => Results.Text("non-MVC endpoint"));
            endpoints.MapGet("/scope/endpoint/public", () => Results.Text("anonymous endpoint"))
                .AllowAnonymous();
        });
    };
}
