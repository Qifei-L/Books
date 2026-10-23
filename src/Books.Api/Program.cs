using Books.Infrastructure;
using Books.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Books")
    ?? throw new InvalidOperationException("ConnectionStrings:Books is not configured.");

builder.Services.AddBooksInfrastructure(connectionString);
builder.Services.AddHealthChecks().AddDbContextCheck<BooksDbContext>();

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();

public partial class Program;
