using Microsoft.EntityFrameworkCore;
using Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Services;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using System.Security.Claims;
using Configuration;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
}

// Swagger Configuration
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT token'ı 'Bearer {token}' formatında girin"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
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

// Controllers & JSON Options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Database Configuration
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure()));

var healthChecksBuilder = builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: new[] { "critical" })
    .AddCheck<FcmHealthCheck>("fcm", tags: new[] { "dependency" });
    

// Options Configuration
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(options => Encoding.UTF8.GetByteCount(options.Key) >= 32,
        "Jwt:Key en az 32 byte uzunluğunda olmalıdır.")
    .Validate(options => options.AccessTokenLifetimeMinutes is > 0 and <= 1440,
        "Jwt:AccessTokenLifetimeMinutes 1 ile 1440 (24 saat) arasında olmalıdır.")
    .Validate(options => options.RefreshTokenLifetimeDays is > 0 and <= 90,
        "Jwt:RefreshTokenLifetimeDays 1 ile 90 arasında olmalıdır.")
    .ValidateOnStart();

builder.Services.AddOptions<AppLimitsOptions>()
    .Bind(builder.Configuration.GetSection(AppLimitsOptions.SectionName))
    .ValidateOnStart();

// Production Validations
if (builder.Environment.IsProduction())
{
    var allowedOriginsCheck = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
    if (allowedOriginsCheck == null || allowedOriginsCheck.Length == 0)
    {
        throw new InvalidOperationException(
            "Production ortamında Cors:AllowedOrigins boş olamaz. appsettings.Production.json veya ortam değişkeni ile en az bir origin tanımlayın.");
    }

    var smtpHost = builder.Configuration["Email:SmtpHost"];
    var senderEmail = builder.Configuration["Email:SenderEmail"];
    var senderPassword = builder.Configuration["Email:SenderPassword"];

    if (string.IsNullOrWhiteSpace(smtpHost) ||
        string.IsNullOrWhiteSpace(senderEmail) ||
        string.IsNullOrWhiteSpace(senderPassword))
    {
        throw new InvalidOperationException(
            "Production ortamında Email:SmtpHost, Email:SenderEmail ve Email:SenderPassword tanımlanmalıdır (ortam değişkeni veya secret store üzerinden).");
    }

    var healthCheckApiKey = builder.Configuration["HealthCheck:ApiKey"];
    if (string.IsNullOrWhiteSpace(healthCheckApiKey))
    {
        throw new InvalidOperationException(
            "Production ortamında HealthCheck:ApiKey boş olamaz. Aksi halde /health endpoint'i monitoring/load balancer dahil herkes için erişilemez hale gelir. Ortam değişkeni veya secret store üzerinden tanımlayın.");
    }
}

// HTTP Logging (built-in ASP.NET Core, Serilog'a bağımlı değil)
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestProperties | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
});

// Identity Configuration
builder.Services.AddIdentity<Models.User, Microsoft.AspNetCore.Identity.IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedEmail = true;

    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.AllowedForNewUsers = true;

    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredUniqueChars = 4;
})
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddMemoryCache();

// Authentication & JWT
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    var jwtConfiguration = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtConfiguration.Issuer,
        ValidAudience = jwtConfiguration.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfiguration.Key))
    };
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCorsPolicy", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? Array.Empty<string>();

        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
            return;
        }

        if (builder.Environment.IsDevelopment())
        {
            policy.SetIsOriginAllowed(origin =>
                    Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                    (uri.Host is "localhost" or "127.0.0.1" or "10.0.2.2" or "[::1]"))
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});


builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("AuthPolicy", httpContext =>
    {
        var partitionKey = $"auth-ip:{httpContext.Connection.RemoteIpAddress}";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var partitionKey = $"ip:{httpContext.Connection.RemoteIpAddress}";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
});

// HTTP Clients
builder.Services.AddHttpClient(nameof(FcmPushNotificationSender));
builder.Services.AddHttpClient(nameof(FcmAccessTokenProvider));

// Dependency Injection (Services)
builder.Services.AddSingleton<FcmAccessTokenProvider>();

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<XpService>();
builder.Services.AddScoped<HabitProgressService>();
builder.Services.AddScoped<FlowerService>();
builder.Services.AddScoped<IPushNotificationSender, FcmPushNotificationSender>();



builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<BadgeService>();
builder.Services.AddScoped<PetMoodService>();
builder.Services.AddScoped<BookService>();
builder.Services.AddScoped<PetGrowthService>();
builder.Services.AddScoped<PetCosmeticsService>();

builder.Services.AddScoped<ReminderService>();

// Hosted Services (Background Tasks)
builder.Services.AddHostedService<PetMoodBackgroundService>();
builder.Services.AddHostedService<ReminderBackgroundService>();
builder.Services.AddHostedService<MaintenanceCleanupService>();




// Exception Handling
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Middleware Pipeline
app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["X-XSS-Protection"] = "0";
    headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    await next();
});

// DÜZELTİLDİ: Serilog.Context.LogContext yerine built-in ILogger.BeginScope
// kullanılıyor; correlation ID hâlâ tüm loglara ekleniyor, sadece Serilog'a
// bağımlılık kalktı.
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    using (app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
    {
        await next();
    }
});

app.UseHttpLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseCors("DefaultCorsPolicy");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

async ValueTask<object?> HealthCheckApiKeyFilter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
{
    if (app.Environment.IsProduction())
    {
        var expectedKey = app.Configuration["HealthCheck:ApiKey"];
        var providedKey = context.HttpContext.Request.Headers["X-Health-Key"].FirstOrDefault();

        if (string.IsNullOrEmpty(expectedKey))
        {
            return Results.NotFound();
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
        var providedBytes = Encoding.UTF8.GetBytes(providedKey ?? string.Empty);

        if (expectedBytes.Length != providedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
        {
            return Results.NotFound();
        }
    }
    return await next(context);
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).AddEndpointFilter(HealthCheckApiKeyFilter);

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = _ => true
}).AddEndpointFilter(HealthCheckApiKeyFilter);

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true
}).AddEndpointFilter(HealthCheckApiKeyFilter);

try
{
    app.Logger.LogInformation("HabitTrackerApi başlatılıyor...");
    app.Run();
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "HabitTrackerApi beklenmedik şekilde durdu.");
    throw;
}

public partial class Program { }