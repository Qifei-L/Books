using Books.Application;
using Books.Infrastructure;
using Books.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    Environment.SetEnvironmentVariable("ASPNETCORE_HTTP_PORTS", port);
}

var builder = WebApplication.CreateBuilder(args);

DotEnv.Load(Path.Combine(builder.Environment.ContentRootPath, ".env"));
port = Environment.GetEnvironmentVariable("PORT");
var usesPlatformHttpPort = !string.IsNullOrWhiteSpace(port);

var allowedOrigins = (Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS")
        ?? "http://localhost:4200,https://laudable-blessing-production-afd7.up.railway.app")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(origin => origin.TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await SeedData.InitializeAsync(db);
}

if (!usesPlatformHttpPort)
{
    app.UseHttpsRedirection();
}
app.UseCors("AllowFrontend");
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();
app.Run();
