using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PharmacyWarehouse.Client;
using PharmacyWarehouse.Client.Auth;
using PharmacyWarehouse.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// در توسعه از appsettings.Development.json می‌آید؛ در تولید خالی است چون Api خودش Client را سرو می‌کند.
var configuredApi = builder.Configuration["ApiBaseUrl"];
var apiBaseUrl = string.IsNullOrWhiteSpace(configuredApi) ? builder.HostEnvironment.BaseAddress : configuredApi;

// پل خبر «نشست رد شد» بین handler و Session (singleton؛ handlerهای factory دامنه‌ی DI جدا دارند).
builder.Services.AddSingleton<SessionSignals>();

// HttpClient نام‌دار «Api»: همه درخواست‌ها از ApiRequestHandler می‌گذرند (کوکی و هدر CSRF).
builder.Services.AddTransient<ApiRequestHandler>();

builder.Services
    .AddHttpClient("Api", client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<ApiRequestHandler>();

builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("Api"));

// احراز هویت
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ApiAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(
    sp => sp.GetRequiredService<ApiAuthenticationStateProvider>());

// فراخوانی‌های Api
builder.Services.AddScoped<AccountClient>();
builder.Services.AddScoped<LocationsClient>();
builder.Services.AddScoped<ProductsClient>();
builder.Services.AddScoped<WithdrawalsClient>();
builder.Services.AddScoped<BackupClient>();
builder.Services.AddScoped<ImportClient>();
builder.Services.AddScoped<UsersClient>();
builder.Services.AddScoped<AlertsClient>();
builder.Services.AddScoped<AppInfoState>();

// وضعیت رابط کاربری
builder.Services.AddScoped<ToastService>();

await builder.Build().RunAsync();
