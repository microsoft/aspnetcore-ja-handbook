using System.Net;
using IdentityWeb.Data;
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
using Microsoft.Extensions.Options;
using IdentityProgram = global::Program;

namespace IdentityWeb.Tests;

public sealed class IdentityTests
{
    [Fact]
    public async Task UserManager_ValidPassword_CreatesHashedUserAndRequiresConfirmation()
    {
        await using var factory = new IdentityFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        Assert.Contains("00000000000000_CreateIdentitySchema", await db.Database.GetAppliedMigrationsAsync());
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<IdentityUser>>();
        var user = new IdentityUser { UserName = "reader@example.test", Email = "reader@example.test" };
        const string password = "ValidPass1!xx";

        var created = await users.CreateAsync(user, password);

        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Description)));
        Assert.NotEqual(password, user.PasswordHash);
        Assert.True(await users.CheckPasswordAsync(user, password));
        Assert.False(await signIn.CanSignInAsync(user));
        var denied = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        Assert.True(denied.IsNotAllowed);
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        Assert.True((await users.ConfirmEmailAsync(user, token)).Succeeded);
        Assert.True(await signIn.CanSignInAsync(user));
        Assert.True((await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)).Succeeded);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("ValidPass1!", false)]
    [InlineData("ValidPass1!x", true)]
    [InlineData("ValidPass1!xx", true)]
    [InlineData("ValidPass1!xxx", true)]
    [InlineData("lowercaseonly", false)]
    public async Task UserManager_PasswordBoundary_EnforcesTwelveCharactersAndDefaultComplexity(
        string password, bool expected)
    {
        await using var factory = new IdentityFactory();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = new IdentityUser { UserName = "boundary@example.test" };

        var result = await users.CreateAsync(user, password);

        Assert.Equal(expected, result.Succeeded);
        if (password.Length < 12)
        {
            Assert.Contains(result.Errors, error => error.Code == "PasswordTooShort");
        }
    }

    [Fact]
    public async Task RoleManager_Registration_DoesNotCreateRolesButSupportsAssignment()
    {
        await using var factory = new IdentityFactory();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        Assert.False(await roles.RoleExistsAsync("Admin"));
        Assert.True((await roles.CreateAsync(new IdentityRole("Admin"))).Succeeded);
        var user = new IdentityUser { UserName = "admin@example.test" };
        Assert.True((await users.CreateAsync(user, "ValidPass1!xx")).Succeeded);
        Assert.False(await users.IsInRoleAsync(user, "Admin"));
        Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
        Assert.True(await users.IsInRoleAsync(user, "Admin"));
        var principal = await scope.ServiceProvider
            .GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>().CreateAsync(user);
        Assert.True(principal.IsInRole("Admin"));
    }

    [Fact]
    public async Task IdentityOptions_ChapterSettings_KeepComplexityAndLockoutDefaults()
    {
        await using var factory = new IdentityFactory();
        var options = factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        Assert.True(options.SignIn.RequireConfirmedAccount);
        Assert.Equal(12, options.Password.RequiredLength);
        Assert.True(options.Password.RequireDigit);
        Assert.True(options.Password.RequireUppercase);
        Assert.True(options.Password.RequireLowercase);
        Assert.True(options.Password.RequireNonAlphanumeric);
        Assert.Equal(5, options.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(5), options.Lockout.DefaultLockoutTimeSpan);
    }

    [Fact]
    public async Task Privacy_Anonymous_RedirectsToIdentityLogin()
    {
        await using var factory = new IdentityFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/Home/Privacy");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal("/Identity/Account/Login", location.AbsolutePath);
        Assert.Contains("ReturnUrl=%2FHome%2FPrivacy", location.Query);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Identity/Account/Register")]
    [InlineData("/Identity/Account/Login")]
    public async Task IdentityUi_PublicPage_RendersWithoutVendoredLibraries(string path)
    {
        await using var factory = new IdentityFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType!.MediaType);
    }
}

internal sealed class IdentityFactory : WebApplicationFactory<IdentityProgram>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public IdentityFactory() => connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await connection.DisposeAsync();
    }
}
