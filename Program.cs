using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Middlewares;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Media;
using ToBeClarify.Api.Repositories.Admin.Auth;
using ToBeClarify.Api.Repositories.Client.Events;
using ToBeClarify.Api.Repositories.Client.Gallery;
using ToBeClarify.Api.Repositories.Client.Guestbook;
using ToBeClarify.Api.Repositories.Client.Home;
using ToBeClarify.Api.Repositories.Client.Menu;
using ToBeClarify.Api.Repositories.Client.Media;
using ToBeClarify.Api.Repositories.Client.Rankings;
using ToBeClarify.Api.Repositories.Client.Reservations;
using ToBeClarify.Api.Repositories.Client.Site;
using ToBeClarify.Api.Repositories.Client.Staff;
using ToBeClarify.Api.Services.Client.Events;
using ToBeClarify.Api.Services.Client.Gallery;
using ToBeClarify.Api.Services.Client.Guestbook;
using ToBeClarify.Api.Services.Client.Home;
using ToBeClarify.Api.Services.Client.Menu;
using ToBeClarify.Api.Services.Client.Rankings;
using ToBeClarify.Api.Services.Client.Reservations;
using ToBeClarify.Api.Services.Client.Site;
using ToBeClarify.Api.Services.Client.Staff;
using ToBeClarify.Api.Services.Admin.Auth;
using ToBeClarify.Api.Services.Logging;
using ToBeClarify.Api.Services.Media;

var builder = WebApplication.CreateBuilder(args);

// Optional developer-only secrets. This file is ignored by Git and can be edited manually.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

const string apiPrefix = "api";
const string webCorsPolicy = "WebClient";

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();

builder.Services.Configure<JwtAuthOptions>(builder.Configuration.GetSection(JwtAuthOptions.SectionName));
builder.Services.Configure<AdminAuthOptions>(builder.Configuration.GetSection(AdminAuthOptions.SectionName));
builder.Services.Configure<ApiLoggingOptions>(builder.Configuration.GetSection(ApiLoggingOptions.SectionName));
builder.Services.Configure<MediaOptions>(builder.Configuration.GetSection(MediaOptions.SectionName));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<AppDbContext>();
builder.Services.AddSingleton<IAppClock, TaiwanAppClock>();
builder.Services.AddSingleton<PasswordHashService>();
builder.Services.AddScoped<IApiLogService, ApiLogService>();
builder.Services.AddScoped<IAdminAuthRepository, AdminAuthRepository>();
builder.Services.AddScoped<IAdminAuthService, AdminAuthService>();
builder.Services.AddScoped<IHomeRepository, HomeRepository>();
builder.Services.AddScoped<ISiteRepository, SiteRepository>();
builder.Services.AddScoped<IMenuRepository, MenuRepository>();
builder.Services.AddScoped<IStaffRepository, StaffRepository>();
builder.Services.AddScoped<IEventRepository, EventRepository>();
builder.Services.AddScoped<IGalleryRepository, GalleryRepository>();
builder.Services.AddScoped<IGuestbookRepository, GuestbookRepository>();
builder.Services.AddScoped<IReservationRepository, ReservationRepository>();
builder.Services.AddScoped<IRankingRepository, RankingRepository>();
builder.Services.AddScoped<IMediaRepository, MediaRepository>();
builder.Services.AddScoped<IHomeService, HomeService>();
builder.Services.AddScoped<ISiteService, SiteService>();
builder.Services.AddScoped<IMenuService, MenuService>();
builder.Services.AddScoped<IStaffService, StaffService>();
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<IGalleryService, GalleryService>();
builder.Services.AddScoped<IGuestbookService, GuestbookService>();
builder.Services.AddScoped<IReservationService, ReservationService>();
builder.Services.AddScoped<IRankingService, RankingService>();
builder.Services.AddSingleton<MediaUrlService>();
builder.Services.AddScoped<MediaFileService>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            ApiResponse<object>.Fail("RATE_LIMITED", "Too many requests.", context.HttpContext.TraceIdentifier),
            cancellationToken);
    };
    options.AddPolicy("guestbook-write", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("admin-login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy(webCorsPolicy, policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        }
    });
});
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var message = context.ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => error.ErrorMessage)
            .FirstOrDefault() ?? "Request validation failed.";
        return new BadRequestObjectResult(ApiResponse<object>.Fail(
            "VALIDATION_ERROR", message, context.HttpContext.TraceIdentifier));
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("client", new OpenApiInfo { Title = "Client API", Version = "v1" });
    options.SwaggerDoc("admin", new OpenApiInfo { Title = "Admin API", Version = "v1" });

    options.DocInclusionPredicate((docName, apiDesc) =>
    {
        var route = apiDesc.RelativePath ?? string.Empty;
        if (docName == "client") return route.StartsWith($"{apiPrefix}/client", StringComparison.OrdinalIgnoreCase);
        if (docName == "admin") return route.StartsWith($"{apiPrefix}/admin", StringComparison.OrdinalIgnoreCase);
        return false;
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter: Bearer {token}"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", document, "Bearer"),
            []
        }
    });

    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var jwtOptions = builder.Configuration.GetSection(JwtAuthOptions.SectionName).Get<JwtAuthOptions>() ?? new JwtAuthOptions();
var signingKey = Encoding.UTF8.GetBytes(jwtOptions.SigningKey);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(signingKey),
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = AdminAuthConstants.RoleClaimType,
            NameClaimType = ClaimTypes.Name
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrWhiteSpace(context.Token) &&
                    context.Request.Cookies.TryGetValue(AdminAuthConstants.CookieName, out var cookieToken))
                {
                    context.Token = cookieToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireAuthenticatedUser().RequireRole(AdminRole.All));
});

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestInterceptorMiddleware>();
app.UseRateLimiter();
app.UseCors(webCorsPolicy);

if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwagger"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/client/swagger.json", "Client API");
        options.SwaggerEndpoint("/swagger/admin/swagger.json", "Admin API");
        options.RoutePrefix = "swagger";
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
