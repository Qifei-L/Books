using Books.Application;
using Books.Blazor;
using Books.Blazor.Services;
using Books.Blazor.Components;
using Books.Infrastructure;
using Books.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    Environment.SetEnvironmentVariable("ASPNETCORE_HTTP_PORTS", port);
}

var builder = WebApplication.CreateBuilder(args);
DotEnv.Load(Path.Combine(builder.Environment.ContentRootPath, ".env"));
port = Environment.GetEnvironmentVariable("PORT");
var usesPlatformHttpPort = !string.IsNullOrWhiteSpace(port);

// Add services to the container.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<AccountingContextService>();
builder.Services.AddScoped<ReportDownloadService>();
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await SeedData.InitializeAsync(db);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStaticFiles();

// Only redirect to not-found page for navigation requests (no file extension).
// Static assets like /_framework/blazor.web.js must return a real 404, not an HTML page,
// otherwise the browser gets HTML where it expects JavaScript and Blazor fails to load.
app.UseWhen(
    ctx => !Path.HasExtension(ctx.Request.Path.Value),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true)
);
if (!usesPlatformHttpPort)
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapReportExportEndpoints();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
