using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Amazon.S3;
using Amazon.SecretsManager;
using Amazon.CloudWatch;
using TiendaOnline.Data;
using TiendaOnline.Services;
using Npgsql;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<BusinessMetrics>();

// 1. Cadena de conexión principal
var db = builder.Configuration.GetConnectionString("Postgres") ?? throw new InvalidOperationException("Configure ConnectionStrings:Postgres");
db = DatabaseSecret.BuildConnectionString(builder.Configuration, db);

// Registrar ShopDbContext para la aplicación
builder.Services.AddDbContext<ShopDbContext>(o => o.UseNpgsql(db));

// Configuración de Redis y conexión reutilizable IConnectionMultiplexer
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");

if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    var multiplexer = ConnectionMultiplexer.Connect(redisConnectionString);
    builder.Services.AddSingleton<IConnectionMultiplexer>(multiplexer);

    builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConnectionString);

    builder.Services.AddDataProtection()
        .SetApplicationName("URPOnline")
        .PersistKeysToStackExchangeRedis(multiplexer, "URPOnline-DataProtection-Keys");
}
else if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    throw new InvalidOperationException("Redis es obligatorio fuera de Development para compartir sesiones entre EC2.");
}

builder.Services.AddSession(o => {
    o.Cookie.Name = ".URPOnline.Session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.IsEssential = true;
    o.IdleTimeout = TimeSpan.FromMinutes(30);
});

builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<BusinessEvents>();
builder.Services.AddHostedService<ActiveCartsPublisher>();

// Búsqueda semántica: modelo local ONNX + pgvector en PostgreSQL.
builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddScoped<ProductVectorStore>();
builder.Services.AddHostedService<ProductEmbeddingIndexer>();

builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddAWSService<IAmazonSecretsManager>();
builder.Services.AddAWSService<IAmazonCloudWatch>();

var app = builder.Build();

if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions {
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownNetworks = { },
    KnownProxies = { }
});

app.UseStaticFiles();
app.UseRouting();
app.UseSession();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

// 2. MIGRACIONES (Respeta el puerto 5434 en Development, o 5432 en AWS RDS)
if (app.Configuration.GetValue<bool>("InitializeDatabase"))
{
    var directConnectionStringBuilder = new NpgsqlConnectionStringBuilder(db);
    
    // Solo forzar puerto 5432 si NO estamos en entorno Development
    if (!app.Environment.IsDevelopment())
    {
        directConnectionStringBuilder.Port = 5432;
    }

    string directDbConnectionString = directConnectionStringBuilder.ConnectionString;

    var migrationOptions = new DbContextOptionsBuilder<ShopDbContext>()
        .UseNpgsql(directDbConnectionString)
        .Options;

    using var migrationContext = new ShopDbContext(migrationOptions);

    await migrationContext.Database.OpenConnectionAsync();
    try 
    {
        await migrationContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(727001)");
        await migrationContext.Database.MigrateAsync();
        await SeedData.LoadAsync(migrationContext);
    } 
    finally 
    {
        await migrationContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(727001)");
        await migrationContext.Database.CloseConnectionAsync();
    }
}

app.Run();