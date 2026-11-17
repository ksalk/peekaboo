using Peekaboo.Api.Data;
using Peekaboo.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

var databasePath = builder.Configuration["PEEKABOO_DB_PATH"] ?? "data/peekaboo.db";
builder.Services.AddSingleton(new AppDatabase(databasePath));

var app = builder.Build();

app.Services.GetRequiredService<AppDatabase>()
    .Initialize(app.Services.GetRequiredService<ILogger<AppDatabase>>());

app.MapHealthEndpoints();

app.Run();

public partial class Program;
