using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SadrWeb.Api.Data;
using SadrWeb.Api.Endpoints;
using SadrWeb.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Database: SQLite by default (portable, zero setup). Point the API at the
// desktop app's SQL Server database with Database:Provider=SqlServer.
// ---------------------------------------------------------------------------
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var dataDir = Path.Combine(AppContext.BaseDirectory, "App_Data");
Directory.CreateDirectory(dataDir); // SQLite cannot create its own folder
builder.Services.AddDbContext<SadrDbContext>(options =>
{
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer"),
            sql => sql.EnableRetryOnFailure(2));
    else
    {
        // An empty ConnectionStrings:Sqlite simply means "use the local file".
        var sqlite = builder.Configuration.GetConnectionString("Sqlite");
        if (string.IsNullOrWhiteSpace(sqlite)) sqlite = $"Data Source={Path.Combine(dataDir, "sadr.db")}";
        options.UseSqlite(sqlite);
    }
});
builder.Services.AddHttpContextAccessor();

// ---------------------------------------------------------------------------
// Authentication: JWT access tokens + rotating refresh tokens, roles identical
// to the desktop app (Admin / Warehouse / Sales / User).
// ---------------------------------------------------------------------------
var jwt = JwtOptions.From(builder.Configuration);
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<TokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.Admin, p => p.RequireRole(RoleConsts.Admin));
    options.AddPolicy(Policies.Warehouse, p => p.RequireRole(RoleConsts.Admin, RoleConsts.Warehouse, RoleConsts.User));
    options.AddPolicy(Policies.Invoices, p => p.RequireRole(RoleConsts.Admin, RoleConsts.Sales, RoleConsts.User));
    options.AddPolicy(Policies.AnyUser, p => p.RequireAuthenticatedUser());

    // Business data is never public: endpoints must opt out explicitly (login, health,
    // the constants catalogue, the OpenAPI document and the SPA shell).
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().Build();
});

// Native mobile / desktop clients are not browsers: allow any origin for the REST API.
const string CorsPolicy = "sadr-clients";
builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
    .AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

// Product images and other stored files live beside the database, like the desktop's
// UploadedFiles folder next to the executable.
var filesRoot = builder.Configuration["Files:Root"];
if (string.IsNullOrWhiteSpace(filesRoot))
    filesRoot = Path.Combine(AppContext.BaseDirectory, "App_Data", "UploadedFiles");
Directory.CreateDirectory(filesRoot);
builder.Services.AddSingleton(new FileStore(filesRoot));

var app = builder.Build();

// ---------------------------------------------------------------------------
// Schema + seed. Idempotent: creates missing tables only, never drops data.
// ---------------------------------------------------------------------------
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SadrDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SeedData.EnsureAsync(db, app.Configuration, app.Logger);
}

app.UseCors(CorsPolicy);

// Static assets must be served before authorization: with a fallback authorization policy,
// any request that matches no endpoint (e.g. /assets/app.js) would otherwise be rejected
// with 401 before the static-file middleware gets a chance to answer it.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi().AllowAnonymous();

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapMetaEndpoints();
app.MapLookupEndpoints();
app.MapPersonEndpoints();
app.MapProductEndpoints();
app.MapInvoiceEndpoints();
app.MapWarehouseEndpoints();
app.MapBankingEndpoints();
app.MapMoneyEndpoints();
app.MapProjectEndpoints();
app.MapAccountingEndpoints();
app.MapReportEndpoints();

// The built React SPA (SadrWeb/web) is served from wwwroot: /api/* stays the REST API and
// every unknown client-side route falls back to the SPA shell.
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();
