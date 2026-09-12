using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IdentityApi.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityApi.Tests;

public sealed class IdentityFlowTests
{
    private const string Email = "reader@example.test";
    private const string Password = "SamplePass123!";

    [Fact]
    public async Task Register_ShortPassword_Returns400WithoutCreatingUser()
    {
        await using var factory = new IdentityApiFactory();
        using var client = factory.NewClient();

        using var response = await client.PostAsJsonAsync("/identity/register",
            new { email = Email, password = "Short1!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("PasswordTooShort", await response.Content.ReadAsStringAsync());
        Assert.Empty(factory.Emails.Confirmations);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.ToListAsync());
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns400WithoutChangingUser()
    {
        await using var factory = new IdentityApiFactory();
        using var client = factory.NewClient();
        await RegisterAsync(client);

        using var duplicate = await client.PostAsJsonAsync("/identity/register",
            new { email = Email, password = Password });

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("DuplicateUserName", await duplicate.Content.ReadAsStringAsync());
        Assert.Single(factory.Emails.Confirmations);
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.SingleAsync();
        Assert.Equal(Email, user.Email);
        Assert.False(user.EmailConfirmed);
        Assert.True(await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>()
            .CheckPasswordAsync(user, Password));
    }

    [Fact]
    public async Task RegisterConfirmLogin_RealHttpFlow_PersistsUserAndIssuesSecureCookie()
    {
        await using var factory = new IdentityApiFactory();
        using var client = factory.NewClient();
        await RegisterAsync(client);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Single(await db.Database.GetAppliedMigrationsAsync());
            var user = await db.Users.SingleAsync();
            Assert.Equal(Email, user.Email);
            Assert.False(user.EmailConfirmed);
            Assert.False(string.IsNullOrEmpty(user.PasswordHash));
            Assert.NotEqual(Password, user.PasswordHash);
            Assert.True(await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>()
                .CheckPasswordAsync(user, Password));
        }

        using (var unconfirmedLogin = await LoginAsync(client))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, unconfirmedLogin.StatusCode);
            Assert.False(unconfirmedLogin.Headers.Contains("Set-Cookie"));
        }
        await AssertMeAsync(client, HttpStatusCode.Unauthorized);
        await ConfirmAsync(factory, client);

        using (var scope = factory.Services.CreateScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.SingleAsync();
            Assert.True(user.EmailConfirmed);
        }

        using var login = await LoginAsync(client);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));
        Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", cookie, StringComparison.OrdinalIgnoreCase);
        await AssertMeAsync(client, HttpStatusCode.OK);

        using var freshClient = factory.NewClient();
        await AssertMeAsync(freshClient, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_CsrfValidation_RejectsInvalidTokensAndClearsCookieOnlyWithValidToken()
    {
        await using var factory = new IdentityApiFactory();
        using var client = factory.NewClient();
        await RegisterConfirmLoginAsync(factory, client);

        using var tokenResponse = await client.GetAsync("/api/antiforgery");
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        Assert.True(tokenResponse.Headers.CacheControl?.NoStore);
        var antiforgeryCookie = Assert.Single(tokenResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal));
        Assert.Contains("; secure", antiforgeryCookie, StringComparison.OrdinalIgnoreCase);
        var token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("requestToken").GetString();
        Assert.False(string.IsNullOrEmpty(token));

        foreach (var invalidToken in new string?[] { null, "invalid-token" })
        {
            using var response = await LogoutAsync(client, invalidToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.False(response.Headers.Contains("Set-Cookie"));
            await AssertMeAsync(client, HttpStatusCode.OK);
        }

        using var logout = await LogoutAsync(client, token);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(".AspNetCore.Identity.Application=;", StringComparison.Ordinal)
                && value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        await AssertMeAsync(client, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_TokenFromDifferentUser_Returns400AndRetainsLogin()
    {
        await using var factory = new IdentityApiFactory();
        using var firstClient = factory.NewClient();
        using var secondClient = factory.NewClient();
        await RegisterConfirmLoginAsync(factory, firstClient);
        await RegisterConfirmLoginAsync(factory, secondClient, "other@example.test");
        var firstToken = (await firstClient.GetFromJsonAsync<JsonElement>("/api/antiforgery"))
            .GetProperty("requestToken").GetString();
        using var secondToken = await secondClient.GetAsync("/api/antiforgery");

        using var response = await LogoutAsync(secondClient, firstToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertMeAsync(secondClient, HttpStatusCode.OK, "other@example.test");
    }

    [Fact]
    public async Task Login_FiveIncorrectPasswords_LocksConfirmedAccount()
    {
        await using var factory = new IdentityApiFactory();
        using var client = factory.NewClient();
        await RegisterAsync(client);
        await ConfirmAsync(factory, client);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await client.PostAsJsonAsync("/identity/login?useCookies=true",
                new { email = Email, password = "WrongPassword1!" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }
        using var correctPassword = await LoginAsync(client);
        Assert.Equal(HttpStatusCode.Unauthorized, correctPassword.StatusCode);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await users.FindByEmailAsync(Email);
        Assert.NotNull(user);
        Assert.True(await users.IsLockedOutAsync(user));
    }

    private static async Task RegisterAsync(HttpClient client, string email = Email)
    {
        using var response = await client.PostAsJsonAsync("/identity/register", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task ConfirmAsync(IdentityApiFactory factory, HttpClient client, string email = Email)
    {
        Assert.True(factory.Emails.Confirmations.TryDequeue(out var message));
        Assert.Equal(email, message.Email);
        var link = new Uri(message.Link);
        Assert.Equal("https", link.Scheme);
        Assert.Equal("localhost", link.Host);
        Assert.Equal("/identity/confirmEmail", link.AbsolutePath);
        var query = QueryHelpers.ParseQuery(link.Query);
        Assert.Equal(["code", "userId"], query.Keys.Order().ToArray());
        Assert.All(query.Values, value => Assert.False(string.IsNullOrEmpty(value.ToString())));
        using var confirmation = await client.GetAsync(link);
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email = Email) =>
        client.PostAsJsonAsync("/identity/login?useCookies=true", new { email, password = Password });

    private static async Task RegisterConfirmLoginAsync(
        IdentityApiFactory factory, HttpClient client, string email = Email)
    {
        await RegisterAsync(client, email);
        await ConfirmAsync(factory, client, email);
        using var login = await LoginAsync(client, email);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    private static async Task AssertMeAsync(HttpClient client, HttpStatusCode expected, string email = Email)
    {
        using var response = await client.GetAsync("/api/me");
        Assert.Equal(expected, response.StatusCode);
        Assert.Null(response.Headers.Location);
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal($"{{\"email\":\"{email}\"}}", await response.Content.ReadAsStringAsync());
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(email, body.GetProperty("email").GetString());
        }
    }

    private static async Task<HttpResponseMessage> LogoutAsync(HttpClient client, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/logout");
        if (token is not null)
        {
            request.Headers.Add("X-CSRF-TOKEN", token);
        }
        return await client.SendAsync(request);
    }
}
