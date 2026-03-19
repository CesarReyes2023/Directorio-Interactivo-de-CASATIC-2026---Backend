using System.Text;
using System.Threading.RateLimiting;
using CasaticDirectorio.Api.Mapping;
using CasaticDirectorio.Api.Services;
using CasaticDirectorio.Domain.Enums;
using CasaticDirectorio.Domain.Interfaces;
using CasaticDirectorio.Infrastructure.Data;
using CasaticDirectorio.Infrastructure.Data.Seed;
using CasaticDirectorio.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using CasaticDirectorio.Api.Middleware;

// ══════════════════════════════════════════════════════════════
// Program.cs — Directorio Interactivo CASATIC 2026
// ══════════════════════════════════════════════════════════════

// Permitir DateTime sin Kind explícito en consultas EF Core / Npgsql.
// ASP.NET model binding parsea query params como Kind=Unspecified;
// esta opción evita el error « Cannot write DateTime with Kind=Unspecified ».
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();
builder.Host.UseSerilog();

// ── PostgreSQL + EF Core ─────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Repositorios (DI) ────────────────────────────────────────
builder.Services.AddScoped<ISocioRepository, SocioRepository>();
builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<ILogActividadRepository, LogActividadRepository>();
builder.Services.AddScoped<IFormularioContactoRepository, FormularioContactoRepository>();

// ── Servicios ────────────────────────────────────────────────
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<ILogService, LogService>();

// ── AutoMapper ───────────────────────────────────────────────
builder.Services.AddAutoMapper(typeof(MappingProfile));

// ── JWT Authentication ───────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();

// ── Forzar UTF-8 en toda la salida ───────────────────────────
Console.OutputEncoding = Encoding.UTF8;

// ── Controllers ──────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        opts.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        opts.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    });

// ── Swagger / OpenAPI ────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Directorio Interactivo CASATIC 2026",
        Version = "v1",
        Description = "API para el directorio de socios de CASATIC"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        Description = "Ingrese 'Bearer {token}'"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ── CORS ─────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173", "http://localhost:5174",
                "http://localhost:5175", "http://localhost:3000",
                "http://localhost:80")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// ── Rate Limiting (protección contra abuso) ──────────────────
builder.Services.AddRateLimiter(rl =>
{
    // Formularios de contacto: máx 5 por IP por minuto
    rl.AddFixedWindowLimiter("contacto", options =>
    {
        options.Window = TimeSpan.FromMinutes(1);
        options.PermitLimit = 5;
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 0;
    });

    // Login: máx 10 intentos por IP por minuto (anti-brute-force)
    rl.AddFixedWindowLimiter("auth", options =>
    {
        options.Window = TimeSpan.FromMinutes(1);
        options.PermitLimit = 10;
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 0;
    });

    rl.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

// ── Migraciones automáticas + Seed ───────────────────────────
// En desarrollo: EnsureCreated crea las tablas si no existen.
// En producción: usar MigrateAsync con migraciones generadas.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

    if (env.IsDevelopment())
    {
        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        await db.Database.MigrateAsync();
    }

    // Compatibilidad de esquema para entornos existentes sin migraciones.
    // EnsureCreated no aplica cambios sobre tablas ya creadas.
    await db.Database.ExecuteSqlRawAsync(@"
        ALTER TABLE IF EXISTS socios
        ADD COLUMN IF NOT EXISTS ""MarcasRepresenta"" text NOT NULL DEFAULT '';
    ");

    await db.Database.ExecuteSqlRawAsync(@"
        ALTER TABLE IF EXISTS formularios_contacto
        ADD COLUMN IF NOT EXISTS ""Leido"" boolean NOT NULL DEFAULT false;
    ");

    await db.Database.ExecuteSqlRawAsync(@"
        ALTER TABLE IF EXISTS socios
        ADD COLUMN IF NOT EXISTS ""EmailContacto"" text NOT NULL DEFAULT '';
    ");

<<<<<<< HEAD
    await db.Database.ExecuteSqlRawAsync(@"
        ALTER TABLE IF EXISTS socios
        ADD COLUMN IF NOT EXISTS ""MapaUrl"" text NOT NULL DEFAULT '';
    ");

=======
>>>>>>> a5205bf9dab33cdc0c8ca21fcbbabf55a6faf09a
    // ── Limpieza de datos demo (se ejecuta una sola vez) ─────
    // Detecta los slugs de ejemplo del seeder anterior y los borra.
    // Una vez eliminados, este bloque queda inactivo para siempre.
    var slugsDemo = new[]
    {
        "applaudo-studios-el-salvador",
        "elaniin-el-salvador",
        "tigo-el-salvador",
        "claro-el-salvador",
        "gbm-el-salvador"
    };

    if (await db.Socios.AnyAsync(s => slugsDemo.Contains(s.Slug)))
    {
        // Borrar logs y formularios de demo
        db.LogsActividad.RemoveRange(db.LogsActividad);
        db.FormulariosContacto.RemoveRange(db.FormulariosContacto);
        await db.SaveChangesAsync();

        // Borrar usuarios no administradores
        var usuariosDemo = await db.Usuarios
            .Where(u => u.Rol != Rol.Admin)
            .ToListAsync();
        db.Usuarios.RemoveRange(usuariosDemo);

        // Borrar socios demo (cascade elimina sus formularios)
        var sociosDemo = await db.Socios
            .Where(s => slugsDemo.Contains(s.Slug))
            .ToListAsync();
        db.Socios.RemoveRange(sociosDemo);

        await db.SaveChangesAsync();
        Log.Information("✅ Datos demo eliminados. La base de datos está lista para socios reales.");
    }

    await DataSeeder.SeedAsync(db);
}

// ── Archivos estáticos (logos subidos) ───────────────────────
var logosPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "logos");
Directory.CreateDirectory(logosPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(logosPath),
    RequestPath = "/logos"
});

// ── Middleware Pipeline ──────────────────────────────────────
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseSerilogRequestLogging();

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "CASATIC API v1"));

app.UseCors("AllowFrontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

Log.Information("🚀 Directorio Interactivo CASATIC 2026 iniciado");
app.Run();
