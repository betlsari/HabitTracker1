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

var builder = WebApplication.CreateBuilder(args);


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
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure()));


builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(options => Encoding.UTF8.GetByteCount(options.Key) >= 32,
        "Jwt:Key en az 32 byte uzunluğunda olmalıdır.")
    .ValidateOnStart();

builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestProperties | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
});

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

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
    };

    // YENİ: (1) purpose=2fa-pending token'ları (TokenService.GeneratePreAuthToken
    // ile üretilenler) normal [Authorize] endpoint'lerinde ASLA kabul edilmesin —
    // önceden sadece 2fa/login endpoint'i bu claim'i kontrol ediyordu, JWT
    // middleware'i seviyesinde hiçbir engel yoktu (2FA bypass açığı: şifresi
    // kırılmış bir kullanıcının PreAuthToken'ı doğrudan Bearer token olarak
    // habits/books/pets/auth-me gibi TÜM korumalı endpoint'lerde kullanılabiliyordu).
    // (2) sstamp claim'i kullanıcının güncel SecurityStamp'i ile eşleşmezse
    // (şifre değişti / 2FA açıldı-kapandı / logout-all yapıldı) token reddedilsin
    // — böylece şifre değiştirmek artık eski access token'ları da gerçekten
    // geçersiz kılıyor (aksi halde JWT süresi dolana kadar, 2 saat boyunca,
    // geçerliliğini korurdu).
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

            var userManager = context.HttpContext.RequestServices
                .GetRequiredService<UserManager<Models.User>>();
            var user = await userManager.FindByIdAsync(userId);
            if (user == null || user.SecurityStamp != tokenStamp)
            {
                context.Fail("Oturum geçersiz kılınmış. Lütfen tekrar giriş yapın.");
            }
        }
    };
});

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
        }
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // DÜZELTİLDİ: Önceden AddFixedWindowLimiter'a partition key verilmediği
    // için TÜM istemciler arasında paylaşılan TEK bir sayaç oluşuyordu — herhangi
    // bir kullanıcı/saldırgan dakikada 5 istek atarak login/register/2FA gibi
    // auth endpoint'lerini TÜM kullanıcılar için kilitleyebiliyordu (trivial DoS).
    // Artık GlobalLimiter'daki gibi IP bazlı partition kullanılıyor; her IP kendi
    // 5-istek/dakika sayacına sahip.
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

builder.Services.AddHttpClient(nameof(FcmPushNotificationSender));
builder.Services.AddHttpClient(nameof(FcmAccessTokenProvider));
builder.Services.AddSingleton<FcmAccessTokenProvider>();
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
builder.Services.AddHostedService<PetMoodBackgroundService>();
builder.Services.AddHostedService<ReminderBackgroundService>();

builder.Services.AddHostedService<RefreshTokenCleanupService>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();


var app = builder.Build();
app.UseExceptionHandler();
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
app.UseRateLimiter();
app.UseCors("DefaultCorsPolicy");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();


app.MapHealthChecks("/health");




app.Run();

public partial class Program;
