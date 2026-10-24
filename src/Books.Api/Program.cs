using System.Text.Json;
using System.Text.Json.Serialization;
using Books.Api;
using Books.Api.Auth;
using Books.Api.Dev;
using Books.Api.Http;
using Books.Api.Mcp;
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
// Malformed bodies reach the exception handler so they get the same problem+json shape.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    json.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    json.SerializerOptions.AllowDuplicateProperties = false;
});
builder.AddBooksAuthentication();
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<BooksReadTools>()
    .WithTools<BooksJournalTools>()
    .WithTools<BooksSetupTools>()
    .WithTools<BooksPurchaseTools>()
    .WithTools<BooksSalesTools>()
    .WithTools<BooksTaxTools>();

var app = builder.Build();

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    // Read-only preview UI for local testing; not served in any other environment.
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapGet("/", () => Results.Redirect("/app/"));
}
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapDevTokenEndpoint();
app.MapDemoDataEndpoint();

app.MapMcp("/mcp").RequireAuthorization(AuthSetup.McpPolicy);

var api = app.MapGroup("/api/v1").RequireAuthorization(AuthSetup.ApiPolicy);
api.MapMeEndpoints();
api.MapOrganizationEndpoints();
api.MapLedgerEndpoints();
api.MapMembershipEndpoints();
api.MapInvitationEndpoints();
api.MapAccountEndpoints();
api.MapPartyEndpoints();
api.MapTaxCodeEndpoints();
api.MapBankEndpoints();
api.MapItemEndpoints();
api.MapTransferEndpoints();
api.MapPurchaseEndpoints();
api.MapSalesEndpoints();
api.MapTaxReturnEndpoints();
api.MapJournalEndpoints();
api.MapReportEndpoints();

app.Run();

public partial class Program;
