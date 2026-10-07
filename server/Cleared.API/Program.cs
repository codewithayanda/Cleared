using System.Text;
using System.Text.Json.Serialization;
using Cleared.API.Middleware;
using Cleared.API.RateLimiting;
using Cleared.API.Security;
using Cleared.Application.Abstractions;
using Cleared.Application.Auditing;
using Cleared.Application.CreditNotes;
using Cleared.Application.Customers;
using Cleared.Application.Idempotency;
using Cleared.Application.Invoices;
using Cleared.Application.Payments;
using Cleared.Application.Tenants;
using Cleared.Infrastructure;
using Cleared.Infrastructure.Documents;
using Cleared.Infrastructure.Identity;
using Cleared.Infrastructure.Persistence;
using Cleared.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using Scalar.AspNetCore;

// Community license: free under $1M USD annual revenue. See the package comment in
// Directory.Packages.props.
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

builder.Services.AddScoped<TenantWriteInterceptor>();
builder.Services.AddDbContext<ClearedDbContext>((serviceProvider, options) =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Cleared"))
        .UseSnakeCaseNamingConvention()
        .AddInterceptors(serviceProvider.GetRequiredService<TenantWriteInterceptor>()));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<ClearedDbContext>()
    .AddSignInManager();

var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException(
        "Jwt:SigningKey is not configured. Set it via: dotnet user-secrets set \"Jwt:SigningKey\" \"<a long random string>\"");

// HS256 wants a key as long as its own 256-bit hash. A short key can be guessed, and whoever has
// the key can mint a token for any tenant, so the API refuses to start with one.
const int MinimumSigningKeyBytes = 32;
if (Encoding.UTF8.GetByteCount(jwtSigningKey) < MinimumSigningKeyBytes)
{
    throw new InvalidOperationException(
        $"Jwt:SigningKey must be at least {MinimumSigningKeyBytes} bytes. Make one with: openssl rand -base64 48");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "Cleared",
            ValidateAudience = true,
            ValidAudience = "Cleared",
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

// Default deny: an endpoint with no [Authorize] still needs a token. Anything meant to be
// public must say [AllowAnonymous] or .AllowAnonymous(). See DefaultDenyTests.
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddHttpContextAccessor();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ClearedDbContext>("database", tags: ["ready"]);

builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddClearedRateLimiting(builder.Configuration);

builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<ITenantRepository, TenantRepository>();
builder.Services.AddScoped<ICreditNoteRepository, CreditNoteRepository>();
builder.Services.AddScoped<IVatRateRepository, VatRateRepository>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IInvoiceLock, InvoiceLock>();
builder.Services.AddScoped<IIdempotencyStore, IdempotencyStore>();
builder.Services.AddScoped<IdempotentExecutor>();
builder.Services.AddScoped<IInvoiceNumberAllocator, InvoiceNumberAllocator>();
builder.Services.AddScoped<ICreditNoteNumberAllocator, CreditNoteNumberAllocator>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddOptions<SessionLifetimeOptions>()
    .Bind(builder.Configuration.GetSection(SessionLifetimeOptions.SectionName))
    .Validate(
        options => options.IdleLifetime > TimeSpan.Zero && options.AbsoluteLifetime >= options.IdleLifetime,
        "Session:AbsoluteLifetime must be at least Session:IdleLifetime, and both must be positive.")
    .ValidateOnStart();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddOptions<SignInThrottleOptions>()
    .Bind(builder.Configuration.GetSection(SignInThrottleOptions.SectionName))
    .Validate(
        options => options.MaxTries > 0 && options.LockDuration > TimeSpan.Zero && options.ForgetAfter > options.LockDuration,
        "SignInThrottle needs MaxTries above zero, and a ForgetAfter longer than LockDuration.")
    .ValidateOnStart();
builder.Services.AddScoped<ISignInThrottle, SignInThrottle>();
builder.Services.AddScoped<RefreshCookie>();
builder.Services.AddScoped<PasswordTimingEqualizer>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IInvoicePdfRenderer, QuestPdfInvoiceRenderer>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<CreditNoteService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<CreateCustomerService>();
builder.Services.AddScoped<RegisterTenantService>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.UseHttpsRedirection();

app.UseAuthentication();

// After authentication, so signed-in people are counted one by one. Before authorization, so a
// flood of calls with no token is throttled and not just turned away.
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

// Probes from the load balancer carry no token, and neither endpoint returns any data.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false,
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
}).AllowAnonymous();

app.Run();

// Lets the integration tests' WebApplicationFactory see the top-level entry point.
public partial class Program;
