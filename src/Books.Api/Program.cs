using System.Text.Json;
using System.Text.Json.Serialization;
using Books.Api;
using Books.Api.Auth;
using Books.Api.Http;
using Books.Infrastructure;
using Books.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Books")
    ?? throw new InvalidOperationException("ConnectionStrings:Books is not configured.");

builder.Services.AddBooksInfrastructure(connectionString);
builder.Services.AddHealthChecks().AddDbContextCheck<BooksDbContext>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<PrincipalAccessor>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    json.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    json.SerializerOptions.AllowDuplicateProperties = false;
});
builder.AddBooksAuthentication();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapDevTokenEndpoint();

var api = app.MapGroup("/api/v1").RequireAuthorization();
api.MapMeEndpoints();

app.Run();

public partial class Program;
