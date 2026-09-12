using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

string RequiredSetting(string key)
{
    var value = builder.Configuration[key];
    return !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException($"設定 {key} が必要です。");
}

var authority = RequiredSetting("Authentication:Oidc:Authority");
var clientId = RequiredSetting("Authentication:Oidc:ClientId");
var clientSecret = RequiredSetting("Authentication:Oidc:ClientSecret");

builder.Services.AddControllersWithViews();
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
    .AddCookie(options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always)
    .AddOpenIdConnect(options =>
    {
        options.Authority = authority;
        options.ClientId = clientId;
        options.ClientSecret = clientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.SaveTokens = true;
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "roles";
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();
app.Run();

// ドキュメント外: テスト用に追加
public partial class Program { }
