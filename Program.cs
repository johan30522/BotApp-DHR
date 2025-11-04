using System.Text;
using BotApp.Data;
using BotApp.Filters;
using BotApp.Models;
using BotApp.Services;
using DE = Google.Cloud.DiscoveryEngine.V1;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.AIPlatform.V1;
using Google.Cloud.Dialogflow.Cx.V3;
using Grpc.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// -------------------------------------------------
// Database: Postgres (Npgsql) — DSN + Password aparte
// -------------------------------------------------
var dsn = builder.Configuration.GetConnectionString("Postgres"); // base DSN (ideal sin Password)
var dbPwd = builder.Configuration["Db:Password"];                // secreto aparte
var cs = dsn ?? string.Empty;

// añade Password= solo si no viene en el DSN
if (!string.IsNullOrWhiteSpace(dbPwd) && !cs.Contains("Password=", StringComparison.OrdinalIgnoreCase))
{
    if (!cs.EndsWith(';')) cs += ';';
    cs += $"Password={dbPwd};";
}

// asegura SSL Mode (mantengo Disable )
if (!cs.Contains("SSL Mode", StringComparison.OrdinalIgnoreCase))
{
    if (!cs.EndsWith(';')) cs += ';';
    cs += "SSL Mode=Disable;";
}

builder.Services.AddDbContext<BotDbContext>(opt =>
{
    opt.UseNpgsql(cs, npg =>
    {
        npg.EnableRetryOnFailure(5, TimeSpan.FromSeconds(2), null); // resiliencia
        npg.MigrationsHistoryTable("__EFMigrationsHistory", "bot");
    });
});

// -------------------------------------------------
// Redis (estado y SSE)
// -------------------------------------------------
var redisHost = builder.Configuration["Redis:Host"];
var redisPort = builder.Configuration["Redis:Port"];
var redisPwd = builder.Configuration["Redis:Password"]; // opcional

var redisConfig = new ConfigurationOptions
{
    AbortOnConnectFail = false
};
redisConfig.EndPoints.Add($"{redisHost}:{redisPort}");
if (!string.IsNullOrWhiteSpace(redisPwd))
    redisConfig.Password = redisPwd;

builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConfig));
builder.Services.AddSingleton<RedisClient>();
builder.Services.AddSingleton<SessionStateStore>();
builder.Services.AddSingleton<ISseEmitter, RedisSseEmitter>();

// -------------------------------------------------
// Email
// -------------------------------------------------
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddScoped<IEmailService, EmailService>();

// -------------------------------------------------
// Servicios de dominio / aplicación 
// -------------------------------------------------
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<DenunciasService>();
builder.Services.AddScoped<ExpedientesService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<CodigoVerificacionService>();
builder.Services.AddSingleton<CxDetectService>();

// -------------------------------------------------
// Filtros 
// -------------------------------------------------
builder.Services.AddScoped<CxApiKeyFilter>();
builder.Services.AddScoped<MetaSignatureFilter>();
builder.Services.AddScoped<TwilioSignatureFilter>();

// -------------------------------------------------
// RAG / Búsqueda / Generative 
// -------------------------------------------------
builder.Services.AddSingleton<ISearchService, SearchService>();
builder.Services.AddSingleton<IGeminiService, GeminiService>();
builder.Services.AddScoped<IQueryRewriteService, QueryRewriteService>();
builder.Services.AddScoped<IConversationRagService, ConversationRagService>();

// -------------------------------------------------
// Vertex AI PredictionService — PRIORIDAD: Gcp:Location -> GoogleCloud:Location -> us-east4
// -------------------------------------------------
builder.Services.AddSingleton<PredictionServiceClient>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var loc = cfg["GoogleCloud:Location"]
              ?? "us-east4";
    var endpoint = $"{loc}-aiplatform.googleapis.com";
    var b = new PredictionServiceClientBuilder { Endpoint = endpoint };
    return b.Build();
});

// -------------------------------------------------
// Dialogflow CX SessionsClient (igual que tenías)
// -------------------------------------------------
GoogleCredential baseCred = GoogleCredential.GetApplicationDefault(); // ADC compartida
var cfg = builder.Configuration;

var cxEndpoint = cfg["Cx:Endpoint"] ?? "us-central1-dialogflow.googleapis.com";
var cxQuotaProject = cfg["Cx:QuotaProject"] ?? "flujo-bot-dhr";
var cxCred = baseCred.CreateWithQuotaProject(cxQuotaProject);

builder.Services.AddSingleton(_ =>
    new SessionsClientBuilder
    {
        Endpoint = cxEndpoint,
        ChannelCredentials = cxCred.ToChannelCredentials()
    }.Build()
);

// -------------------------------------------------
// Discovery Engine / Vertex AI Search (igual que tenías)
// -------------------------------------------------
var deLocation = cfg["GoogleCloud:Location"] ?? "us";
var deEndpoint = deLocation.Equals("global", StringComparison.OrdinalIgnoreCase)
    ? "discoveryengine.googleapis.com"
    : $"{deLocation}-discoveryengine.googleapis.com";

var deQuotaProject = cfg["GoogleCloud:ProjectId"];
var deCred = baseCred
    .CreateScoped("https://www.googleapis.com/auth/cloud-platform")
    .CreateWithQuotaProject(deQuotaProject);

builder.Services.AddSingleton(_ =>
    new DE.SearchServiceClientBuilder
    {
        Endpoint = deEndpoint,
        ChannelCredentials = deCred.ToChannelCredentials()
    }.Build()
);

// -------------------------------------------------
// Controllers + JSON
// -------------------------------------------------
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});

// -------------------------------------------------
// Swagger (solo Dev)
// -------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// -------------------------------------------------
// CORS (lee de Cors:Origins; fallback a localhost:5173)
// -------------------------------------------------
builder.Services.AddCors(opt =>
{
    opt.AddPolicy("WebClient", p =>
    {
        var origins = (builder.Configuration["Cors:Origins"] ?? "http://localhost:5173")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        p.WithOrigins(origins)
         .AllowAnyHeader()
         .AllowAnyMethod();
        // .AllowCredentials(); // habilitar si usas cookies cross-site
    });
});

// -------------------------------------------------
// AuthN/AuthZ (JWT) — RequireHttpsMetadata depende del entorno
// -------------------------------------------------
var jwtCfg = builder.Configuration.GetSection("Jwt");
var secret = jwtCfg["Secret"] ?? "dev-secret-change-me";
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.RequireHttpsMetadata = builder.Environment.IsProduction();
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtCfg["Issuer"],
            ValidAudience = jwtCfg["Audience"],
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

// -------------------------------------------------
// Kestrel / Host
// -------------------------------------------------
builder.WebHost.ConfigureKestrel(opt =>
{
    opt.AddServerHeader = false;
    // opt.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(5);
});

var app = builder.Build();

// -------------------------------------------------
// Pipeline
// -------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Detrás de proxy (Cloud Run / Nginx / IIS)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseHttpsRedirection();
app.UseCors("WebClient");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/health"));

app.Run();
