using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Scalar.AspNetCore;
using System;
using System.Threading.Channels;
using Web3Firewall.App.Code.APIs;
using Web3Firewall.App.Code.RpcEndpoints;
using Web3Firewall.App.Code.Settings;
using Web3Firewall.App.Components;
using Web3Firewall.Shared.Database;
using Web3Firewall.Shared.Database.Tables;
using Web3Firewall.Shared.Services;

var builder = WebApplication.CreateBuilder(args);

// Add MudBlazor services
builder.Services.AddMudServices();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOpenApi();


builder.Configuration.AddEnvironmentVariables();
builder.Services.Configure<PROXY>(builder.Configuration);
PROXY proxySettings = builder.Configuration.GetRequiredSection("PROXY").Get<PROXY>() ?? default!;

Channel<RPCRequestLogEntry> rpcLogChannel = Channel.CreateUnbounded<RPCRequestLogEntry>(new UnboundedChannelOptions
{
    SingleReader = false, // Multiple writers (API endpoint)
    SingleWriter = false // Multiple writers (API endpoint)
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1 * 1024 * 1024; // 1MB
    options.Limits.MaxRequestLineSize = 4096; // 4 KB
});//builder.WebHost.ConfigureKestrel(options =>

builder.Services.AddDbContextFactory<AppDBContext>(opt => opt.UseSqlite($"Data Source={proxySettings.DB_PATH};Pooling=True;")
                                                                 .AddInterceptors(new SQLiteInterceptor())
                                                  );//builder.Services.AddDbContextFactory<AppDBContext>

builder.Services.AddSingleton<RPCFilterService>();
builder.Services.AddHostedService<RPCLogPersistenceService>();
builder.Services.AddSingleton<GlobalSettings>();
builder.Services.AddSingleton(rpcLogChannel);
builder.Services.AddHttpClient();

var app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    AppDBContext dbContext = scope.ServiceProvider.GetRequiredService<AppDBContext>();
    _ = await dbContext.Database.EnsureCreatedAsync().ConfigureAwait(false); // Or EnsureCreatedAsync() if not using migrations.


    GlobalSettings globalSettings = scope.ServiceProvider.GetRequiredService<GlobalSettings>();
    globalSettings.IsReadOnlyMode = proxySettings.DEFAULT_READ_ONLY;
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}


app.UseRouting();

app.UseAntiforgery();

app.MapStaticAssets();


app.MapOpenApi()
        .CacheOutput()
        .DisableHttpMetrics();

app.MapScalarApiReference(options =>
{
    //options.OpenApiRoutePattern = "openapi/{documentName}.json";
    options.Title = "The Web3 Firewall API";
});

app.AddStandardJsonRpcProxyEndpoints();
app.AddAdminAPIEndpoints();
app.AddLogsAPIEndpoints();


app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
