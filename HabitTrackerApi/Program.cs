using Microsoft.EntityFrameworkCore;
using Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Services;
using Services.Observability;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using System.Security.Claims;
using Configuration;
using Microsoft.AspNetCore.HttpLogging;
using Asp.Versioning;
using Serilog;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Resources;
using System.Security.Cryptography;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

// Sentry is optional; an empty DSN keeps local and test environments disabled.
builder.WebHost.UseSentry();


Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        "logs/habittracker-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

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


builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("X-Api-Version"));
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// Database Configuration
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure()));

// YENİ (🔴 ölçeklenebilirlik): Dağıtık cache (SecurityStampCache bunun
// üzerine kurulu). Redis:ConnectionString tanımlıysa Redis kullanılır —
// böylece birden fazla API instance'ı aynı SecurityStamp iptal durumunu
// görür. Tanımlı değilse (yalnızca Development/Testing'de izin verilir)
// bellek içi bir IDistributedCache'e düşülür; Production'da Redis
// zorunludur (aşağıdaki fail-fast kontrolüne bakın).
var redisConnectionString = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "habittracker:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: new[] { "critical" })
    .AddCheck<EmailQueueHealthCheck>("email-queue", tags: new[] { "dependency" })
    .AddCheck<FcmHealthCheck>("fcm", tags: new[] { "dependency" })
    .AddCheck<RecalculationQueueHealthCheck>("recalculation-queue", tags: new[] { "dependency" });

// Options Configuration
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(options => Encoding.UTF8.GetByteCount(options.Key) >= 32,
        "Jwt:Key en az 32 byte uzunluğunda olmalıdır.")
    .Validate(options => options.AccessTokenLifetimeMinutes is > 0 and <= 1440,
        "Jwt:AccessTokenLifetimeMinutes 1 ile 1440 (24 saat) arasında olmalıdır.")
    .Validate(options => options.RefreshTokenLifetimeDays is > 0 and <= 90,
        "Jwt:RefreshTokenLifetimeDays 1 ile 90 arasında olmalıdır.")
    .Validate(options => options.PreAuthTokenLifetimeMinutes is > 0 and <= 30,
        "Jwt:PreAuthTokenLifetimeMinutes 1 ile 30 arasında olmalıdır.")
    .ValidateOnStart();

builder.Services.AddOptions<AppLimitsOptions>()
    .Bind(builder.Configuration.GetSection(AppLimitsOptions.SectionName))
    .Validate(options => options.MaxHistoryLookbackDays is > 0 and <= 3650,
        "AppLimits:MaxHistoryLookbackDays 1 ile 3650 (10 yıl) gün arasında olmalıdır.")
    .ValidateOnStart();

builder.Services.AddOptions<CaptchaOptions>()
    .Bind(builder.Configuration.GetSection(CaptchaOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddHttpClient<CaptchaVerifier>();

// Production Validations
if (builder.Environment.IsProduction())
{
    var allowedOriginsCheck = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
    if (allowedOriginsCheck == null || allowedOriginsCheck.Length == 0)
    {
        throw new InvalidOperationException(
            "Production ortamında Cors:AllowedOrigins boş olamaz. appsettings.Production.json veya ortam değişkeni ile en az bir origin tanımlayın.");
    }

    if (string.IsNullOrWhiteSpace(redisConnectionString))
    {
        throw new InvalidOperationException(
            "Production ortamında Redis:ConnectionString boş olamaz. SecurityStampCache ve dashboard cache için dağıtık bir cache gereklidir.");
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

    // YENİ (🔴 ölçeklenebilirlik fail-fast): Production'da Redis
    // zorunludur. Aksi halde birden fazla instance'ta SecurityStamp
    // iptalleri (şifre değişimi, 2FA kapatma, hesap silme) tutarsız
    // davranır — bu sessiz bir güvenlik açığıdır, bu yüzden başlangıçta
    // gürültülü şekilde engelleniyor.
    if (string.IsNullOrWhiteSpace(redisConnectionString))
    {
        throw new InvalidOperationException(
            "Production ortamında Redis:ConnectionString boş olamaz. SecurityStampCache birden fazla instance arasında " +
            "tutarlı çalışabilmek için dağıtık bir cache'e (Redis) ihtiyaç duyar.");
    }
}

// HTTP Logging
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

// Memory Cache (yerel, kısa ömürlü amaçlar için hâlâ kullanılabilir;
// SecurityStampCache artık IDistributedCache üzerine kurulu)
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<SecurityStampCache>();

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

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var principal = context.Principal;
            if (principal == null)
            {
                context.Fail("Geçersiz token.");
                return;
            }

            var purpose = principal.FindFirstValue("purpose");
            if (purpose == "2fa-pending")
            {
                context.Fail("Bu token sadece 2FA doğrulama akışında kullanılabilir.");
                return;
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var tokenStamp = principal.FindFirstValue("sstamp");
            if (string.IsNullOrEmpty(userId))
            {
                context.Fail("Geçersiz token.");
                return;
            }

            var stampCache = context.HttpContext.RequestServices.GetRequiredService<SecurityStampCache>();
            var (found, currentStamp) = await stampCache.TryGetAsync(userId);
            if (!found)
            {
                var userManager = context.HttpContext.RequestServices
                    .GetRequiredService<UserManager<Models.User>>();
                var user = await userManager.FindByIdAsync(userId);

                if (user == null)
                {
                    context.Fail("Oturum geçersiz kılınmış. Lütfen tekrar giriş yapın.");
                    return;
                }

                currentStamp = user.SecurityStamp;
                await stampCache.SetAsync(userId, currentStamp);
            }

            if (currentStamp != tokenStamp)
            {
                context.Fail("Oturumunuz güvenlik nedeniyle geçersiz kılındı. Lütfen tekrar giriş yapın.");
            }
        }
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

// Rate Limiting
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
        var partitionKey = httpContext.User.Identity?.IsAuthenticated == true
            ? $"user:{httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value}"
            : $"ip:{httpContext.Connection.RemoteIpAddress}";

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

// DÜZELTİLDİ (🔴 kalıcılık): EmailQueue/RecalculationQueue artık singleton
// in-memory Channel değil; her ikisi de AppDbContext kullandığı için Scoped
// olarak kayıtlı. Aynı somut instance hem "yazma" (IEmailQueue/
// IRecalculationQueue) hem "worker okuma" (IEmailOutboxProcessor/
// IRecalculationOutboxProcessor) arayüzlerine map'leniyor — böylece aynı
// HTTP isteği/scope içinde tek bir DbContext üzerinden çalışılır.
builder.Services.AddScoped<EmailOutboxService>();
builder.Services.AddScoped<IEmailQueue>(sp => sp.GetRequiredService<EmailOutboxService>());
builder.Services.AddScoped<IEmailOutboxProcessor>(sp => sp.GetRequiredService<EmailOutboxService>());

builder.Services.AddScoped<RecalculationOutboxService>();
builder.Services.AddScoped<IRecalculationQueue>(sp => sp.GetRequiredService<RecalculationOutboxService>());
builder.Services.AddScoped<IRecalculationOutboxProcessor>(sp => sp.GetRequiredService<RecalculationOutboxService>());

builder.Services.AddScoped<DashboardCacheService>();

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuthAuditService>();
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
builder.Services.AddScoped<TwoFactorLockoutService>();
builder.Services.AddScoped<UserDataExportService>();
builder.Services.AddScoped<NotificationDigestService>();
builder.Services.AddScoped<ReminderService>();
builder.Services.AddScoped<TwoFactorFallbackCodeService>();

// Hosted Services (Background Tasks)
builder.Services.AddHostedService<PetMoodBackgroundService>();
builder.Services.AddHostedService<ReminderBackgroundService>();
builder.Services.AddHostedService<RefreshTokenCleanupService>();
builder.Services.AddHostedService<AuthAuditCleanupService>();
builder.Services.AddHostedService<ArchivedRecordsCleanupService>();
// DÜZELTİLDİ: Artık DB outbox tablosunu poll ediyor (bkz. yukarıdaki not).
builder.Services.AddHostedService<EmailSenderBackgroundService>();
builder.Services.AddHostedService<RecalculationBackgroundService>();
builder.Services.AddHostedService<NotificationDigestBackgroundService>();

var otlpEndpoint = builder.Configuration["Otel:OtlpEndpoint"];

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: AppDiagnostics.ServiceName,
        serviceVersion: "1.0.0"))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(AppDiagnostics.ServiceName)
            .AddAspNetCoreInstrumentation(options =>
            {
                options.Filter = httpContext =>
                    !httpContext.Request.Path.StartsWithSegments("/health");
            })
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
        else if (builder.Environment.IsDevelopment())
        {
            tracing.AddConsoleExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(AppDiagnostics.ServiceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
        else if (builder.Environment.IsDevelopment())
        {
            metrics.AddConsoleExporter();
        }
    });

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

// Correlation ID Middleware
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
    using (app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
    {
        await next();
    }
});

app.UseSerilogRequestLogging();

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

app.Use(async (context, next) =>
{
    await next();

    if (context.User.Identity?.IsAuthenticated == true &&
        context.Request.Method is not "GET" and not "HEAD" and not "OPTIONS")
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var dashboardCache = context.RequestServices.GetRequiredService<DashboardCacheService>();
            await dashboardCache.InvalidateAsync(userId, context.RequestAborted);
        }
    }
});

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
    Log.Information("HabitTrackerApi başlatılıyor...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "HabitTrackerApi beklenmedik şekilde durdu.");
    throw;
}
finally
{
   
    Log.CloseAndFlush();
}

public partial class Program { }