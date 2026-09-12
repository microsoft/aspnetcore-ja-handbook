using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using OidcWeb.Controllers;
using OidcProgram = global::Program;

namespace OidcWeb.Tests;

public sealed class OidcTests
{
    [Fact]
    public async Task Authentication_ChapterOptions_UsesCookieWithOidcChallenge()
    {
        await using var factory = new OidcFactory();
        var schemes = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.Equal("Cookies", (await schemes.GetDefaultAuthenticateSchemeAsync())!.Name);
        Assert.Equal("OpenIdConnect", (await schemes.GetDefaultChallengeSchemeAsync())!.Name);
        var oidc = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("OpenIdConnect");
        Assert.Equal(OidcFactory.Authority, oidc.Authority);
        Assert.Equal("chapter-client", oidc.ClientId);
        Assert.Equal("code", oidc.ResponseType);
        Assert.True(oidc.UsePkce);
        Assert.True(oidc.SaveTokens);
        Assert.False(oidc.MapInboundClaims);
        Assert.Equal("name", oidc.TokenValidationParameters.NameClaimType);
        Assert.Equal("roles", oidc.TokenValidationParameters.RoleClaimType);
        Assert.Equal("/signin-oidc", oidc.CallbackPath.Value);
        Assert.Equal("/signout-callback-oidc", oidc.SignedOutCallbackPath.Value);
        Assert.Equal("Cookies", oidc.SignInScheme);
        var cookie = factory.CookieOptions;
        Assert.Equal(CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, cookie.Cookie.SameSite);
    }

    [Fact]
    public async Task Profile_Anonymous_RequestsCodeWithPkceAndSecureCorrelationCookies()
    {
        await using var factory = new OidcFactory();
        using var client = factory.NewClient();

        using var response = await client.GetAsync("/Account/Profile");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal($"{OidcFactory.Authority}/authorize", location.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("chapter-client", query["client_id"].ToString());
        Assert.Equal("code", query["response_type"].ToString());
        Assert.Equal("https://localhost:7043/signin-oidc", query["redirect_uri"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.Matches("^[A-Za-z0-9_-]{43}$", query["code_challenge"].ToString());
        Assert.Contains("openid", query["scope"].ToString().Split(' '));
        Assert.False(string.IsNullOrEmpty(query["state"]));
        Assert.False(string.IsNullOrEmpty(query["nonce"]));
        Assert.False(query.ContainsKey("client_secret"));
        Assert.False(query.ContainsKey("code_verifier"));
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(cookies, value => value.Contains(".AspNetCore.Correlation."));
        Assert.Contains(cookies, value => value.Contains(".AspNetCore.OpenIdConnect.Nonce."));
        Assert.All(cookies, value =>
        {
            Assert.Contains("secure", value, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", value, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=none", value, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Logout_AuthenticatedPostWithoutAntiforgery_ReturnsBadRequest()
    {
        await using var factory = new OidcFactory();
        using var client = factory.NewClient(authenticated: true);

        using var response = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Logout_AuthenticatedGet_IsNotAllowed()
    {
        await using var factory = new OidcFactory();
        using var client = factory.NewClient(authenticated: true);
        using var response = await client.GetAsync("/Account/Logout");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public void Logout_Action_SignsOutBothSchemesWithFixedLocalRedirect()
    {
        var controller = new AccountController();
        var result = Assert.IsType<SignOutResult>(controller.Logout());
        Assert.Equal(new[] { "Cookies", "OpenIdConnect" }, result.AuthenticationSchemes);
        Assert.Equal("/", result.Properties!.RedirectUri);
    }

    [Fact]
    public async Task Logout_ProfileFormWithAntiforgery_DeletesCookieAndRequestsProviderSignout()
    {
        await using var factory = new OidcFactory();
        using var client = factory.NewClient(authenticated: true);
        using var profile = await client.GetAsync("/Account/Profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var html = await profile.Content.ReadAsStringAsync();
        Assert.Contains("Test Reader", html);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        var antiforgeryCookie = profile.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal)).Split(';')[0];
        var authCookie = client.DefaultRequestHeaders.GetValues("Cookie").Single();
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", $"{authCookie}; {antiforgeryCookie}");
        using var response = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value)
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal($"{OidcFactory.Authority}/endsession", location.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("https://localhost:7043/signout-callback-oidc",
            query["post_logout_redirect_uri"].ToString());
        Assert.False(string.IsNullOrEmpty(query["state"]));
        Assert.Contains(response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(".AspNetCore.Cookies=;", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Authority")]
    [InlineData("ClientId")]
    [InlineData("ClientSecret")]
    public void RequiredSetting_MissingConfiguration_FailsFast(string setting)
    {
        using var factory = new OidcFactory();
        using var invalid = factory.WithWebHostBuilder(
            builder => builder.UseSetting($"Authentication:Oidc:{setting}", " "));
        var error = Assert.Throws<InvalidOperationException>(() => invalid.CreateClient());
        Assert.Contains($"Authentication:Oidc:{setting}", error.Message);
    }
}

internal sealed class OidcFactory : WebApplicationFactory<OidcProgram>
{
    public const string Authority = "https://identity.example.test";
    public CookieAuthenticationOptions CookieOptions =>
        Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Cookies");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Authentication:Oidc:Authority", Authority);
        builder.UseSetting("Authentication:Oidc:ClientId", "chapter-client");
        builder.UseSetting("Authentication:Oidc:ClientSecret", "test-only-not-a-real-secret");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            services.PostConfigure<OpenIdConnectOptions>("OpenIdConnect", options =>
            {
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                    new OpenIdConnectConfiguration
                    {
                        Issuer = Authority,
                        AuthorizationEndpoint = $"{Authority}/authorize",
                        TokenEndpoint = $"{Authority}/token",
                        EndSessionEndpoint = $"{Authority}/endsession"
                    });
                options.Backchannel = new HttpClient(new RejectNetworkHandler());
            });
        });
    }

    public HttpClient NewClient(bool authenticated = false)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost:7043"), AllowAutoRedirect = false, HandleCookies = false
        });
        if (authenticated)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", "reader"), new Claim("name", "Test Reader")], "Cookies", "name", "roles"));
            var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
            }, "Cookies");
            var cookie = CookieOptions.TicketDataFormat.Protect(ticket);
            client.DefaultRequestHeaders.Add("Cookie", $"{CookieOptions.Cookie.Name}={cookie}");
        }
        return client;
    }

    private sealed class RejectNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("External OIDC calls are prohibited in these tests.");
    }
}
