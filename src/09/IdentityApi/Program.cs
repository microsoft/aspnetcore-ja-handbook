using System.Security.Claims;
using IdentityApi.Data;
using IdentityApi.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "This sample is Development-only. Replace DevelopmentEmailSender with a real email sender " +
        "and review the deployment configuration before running outside Development.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.")));
builder.Services.AddIdentityApiEndpoints<IdentityUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddSingleton<IEmailSender<IdentityUser>, DevelopmentEmailSender>();
builder.Services.ConfigureApplicationCookie(options =>
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always);
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => TypedResults.Ok(new { name = "Identity Minimal API" }));
app.MapGroup("/identity").MapIdentityApi<IdentityUser>();

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/me", (ClaimsPrincipal user) =>
    TypedResults.Ok(new { email = user.FindFirstValue(ClaimTypes.Email) }));
api.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    context.Response.Headers.CacheControl = "no-store";
    return TypedResults.Ok(new { requestToken = tokens.RequestToken });
});
api.MapPost("/logout", async (
    HttpContext context, IAntiforgery antiforgery, SignInManager<IdentityUser> signInManager) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid antiforgery token.");
    }

    await signInManager.SignOutAsync();
    return Results.NoContent();
});

app.Run();

// ドキュメント外: テスト用に追加
public partial class Program { }
