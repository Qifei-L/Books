using System.Text.Json;
using System.Text.Json.Serialization;
using Books.Api;
using Books.Api.Auth;
using Books.Api.Dev;
using Books.Api.Http;
using Books.Api.Mcp;
using Books.Infrastructure;
using Books.Infrastructure.Persistence;
using Microsoft.AspNetCore.ResponseCompression;

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
// Lists carry every document with its lines (an AI needs them to reconcile), so answers are large:
// the same payload measured 5.9 MB plain and 690 KB gzipped. Compress JSON as well as the defaults.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);
});
// The default level is Fastest, which left a 6.2 MB list at 1.15 MB gzipped and made Brotli larger
// than gzip. At Optimal the same list is about 690 KB.
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = System.IO.Compression.CompressionLevel.Optimal);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = System.IO.Compression.CompressionLevel.Optimal);
builder.AddBooksAuthentication();
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<BooksReadTools>()
    .WithTools<BooksJournalTools>()
    .WithTools<BooksSetupTools>()
    .WithTools<BooksPurchaseTools>()
    .WithTools<BooksSalesTools>()
    .WithTools<BooksTaxTools>()
    .WithTools<BooksBankTools>();

InvoicePdf.Configure();

var app = builder.Build();

app.UseResponseCompression();
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
api.MapClassEndpoints();
api.MapPartyEndpoints();
api.MapTaxCodeEndpoints();
api.MapBankEndpoints();
api.MapItemEndpoints();
api.MapTransferEndpoints();
api.MapPurchaseEndpoints();
api.MapSalesEndpoints();
api.MapPaymentEndpoints();
api.MapPayRunEndpoints();
api.MapFixedAssetEndpoints();
api.MapStockAdjustmentEndpoints();
api.MapLocationEndpoints();
api.MapAssemblyEndpoints();
api.MapFxRevaluationEndpoints();
api.MapTaxReturnEndpoints();
api.MapJournalEndpoints();
api.MapReportEndpoints();

app.Run();

public partial class Program;
