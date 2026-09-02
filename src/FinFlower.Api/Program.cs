using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FinFlower.Api.Endpoints;
using FinFlower.Api.Middleware;
using FinFlower.Api.Security;
using FinFlower.Application;
using FinFlower.Application.Common;
using FinFlower.Infrastructure;
using FinFlower.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// --- Autenticação -----------------------------------------------------------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// A configuração do bearer vive em ConfigureJwtBearerOptions e é resolvida pelo DI.
builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();

builder.Services.AddAuthorization();

// --- Limite de requisições --------------------------------------------------
builder.Services.AddOptions<RateLimitingOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Rotas de credencial: janela curta e estreita, por IP. É a barreira contra
    // força bruta distribuída, complementar ao bloqueio por conta.
    options.AddPolicy(AuthEndpoints.AuthRateLimitPolicy, context =>
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

        return RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.AuthPermitLimit,
                Window = TimeSpan.FromSeconds(limits.AuthWindowSeconds),
                QueueLimit = 0,
            });
    });

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

        return RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.GlobalPermitLimit,
                Window = TimeSpan.FromSeconds(limits.GlobalWindowSeconds),
                QueueLimit = 0,
            });
    });

    static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
});

// --- CORS -------------------------------------------------------------------
builder.Services.AddOptions<CorsSettings>()
    .Bind(builder.Configuration.GetSection(CorsSettings.SectionName));

builder.Services.AddCors();

// A política vive em ConfigureCorsOptions e é resolvida pelo DI.
builder.Services.AddSingleton<IConfigureOptions<CorsOptions>, ConfigureCorsOptions>();

// --- Documentação -----------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Fin Flower API",
        Version = "v1",
        Description = "Controle financeiro por eventos: lançamentos, resultado por evento e caixa consolidado.",
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Informe apenas o access token; o prefixo 'Bearer' é adicionado automaticamente.",
    });

    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer")] = [],
    });
});

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();

var app = builder.Build();

// --- Pipeline ---------------------------------------------------------------
// A ordem importa: erros primeiro para capturar tudo abaixo; autenticação
// obrigatoriamente antes da autorização.
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    // Atrás de proxy, sem isso o limite por IP enxergaria um único cliente.
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Fin Flower API v1"));
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors(ConfigureCorsOptions.PolicyName);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Em container não há quem rode 'Update-Database' antes de subir, então a própria
// aplicação aplica as migrations pendentes — mas só quando explicitamente
// configurada para isso. O padrão é false: em produção o schema é aplicado por
// um passo próprio do deploy, não por uma instância que acabou de subir.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapEventEndpoints();
app.MapContractEndpoints();
app.MapReportEndpoints();

app.Run();

/// <summary>Exposto para os testes de integração poderem instanciar a aplicação.</summary>
public partial class Program;
