using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Amazon.S3;
using Amazon.SecretsManager;
using Amazon.CloudWatch;
using TiendaOnline.Data;
using TiendaOnline.Services;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

// 1. Cadena de conexión principal (apunta a PgBouncer en el puerto 6432)
var db = builder.Configuration.GetConnectionString("Postgres") ?? throw new InvalidOperationException("Configure ConnectionStrings:Postgres; apunte a PgBouncer 6432 en AWS.");
db = DatabaseSecret.BuildConnectionString(builder.Configuration, db);

// Registrar ShopDbContext para la aplicación usando PgBouncer (puerto 6432)
builder.Services.AddDbContext<ShopDbContext>(o => o.UseNpgsql(db));

var redis = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redis)) builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redis);
else if (builder.Environment.IsDevelopment()) builder.Services.AddDistributedMemoryCache();
else throw new InvalidOperationException("Redis es obligatorio fuera de Development para compartir sesiones entre EC2.");

// Claves que cifran la cookie de sesión en Redis (ElastiCache)
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("URPOnline");
if (!string.IsNullOrWhiteSpace(redis))
    dataProtection.PersistKeysToStackExchangeRedis(StackExchange.Redis.ConnectionMultiplexer.Connect(redis), "URPOnline-DataProtection-Keys");

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
builder.Services.AddScoped<BusinessMetrics>();

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

// 2. MIGRACIONES DIRECTAS A RDS (PUERTO 5432)
if (app.Configuration.GetValue<bool>("InitializeDatabase"))
{
    // Construir la cadena directa cambiando el puerto de 6432 a 5432
    var directConnectionStringBuilder = new NpgsqlConnectionStringBuilder(db)
    {
        Port = 5432 // Forzar puerto directo a RDS
    };
    string directDbConnectionString = directConnectionStringBuilder.ConnectionString;

    // Crear DbContextOptions apuntando a la conexión directa de RDS
    var migrationOptions = new DbContextOptionsBuilder<ShopDbContext>()
        .UseNpgsql(directDbConnectionString)
        .Options;

    // Crear un contexto independiente exclusivo para las migraciones
    using var migrationContext = new ShopDbContext(migrationOptions);

    await migrationContext.Database.OpenConnectionAsync();
    try 
    {
        // Advisory Lock requiere una conexión directa persistente
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