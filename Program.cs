using BotApp.Data;
using BotApp.Filters;
using BotApp.Models;
using BotApp.Services;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.AIPlatform.V1;
using Google.Cloud.Dialogflow.Cx.V3;
using Grpc.Auth;
using Grpc.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Text;
using DE = Google.Cloud.DiscoveryEngine.V1;


var builder = WebApplication.CreateBuilder(args);


// connection string base + password separada
var dsn = builder.Configuration.GetConnectionString("Postgres"); // puede venir del Secret
var dbPwd = builder.Configuration["Db:Password"];                 // secreto aparte
var cs = $"{dsn};Password={dbPwd};SSL Mode=Disable";

builder.Services.AddDbContext<BotDbContext>(opt =>
{
    opt.UseNpgsql(cs, npg =>
    {
        npg.EnableRetryOnFailure(5, TimeSpan.FromSeconds(2), null); // resiliencia

        
        npg.MigrationsHistoryTable("__EFMigrationsHistory", "bot");
    });
});

// Se agrega la configuracion para Redis
var redisCfg = $"{builder.Configuration["Redis:Host"]}:{builder.Configuration["Redis:Port"]}";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisCfg));
// Cliente de estado de Redis
builder.Services.AddSingleton<SessionStateStore>();

//Se agrega la consfiguracion del smtp desde appsettings
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddScoped<IEmailService, EmailService>();

// se registra Cliente de Cx
builder.Services.AddSingleton<CxDetectService>();

// Add services to the container.
builder.Services.AddSingleton<RedisClient>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<DenunciasService>();
builder.Services.AddScoped<ExpedientesService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<MetaSignatureFilter>();
builder.Services.AddScoped<TwilioSignatureFilter>();
builder.Services.AddScoped<GeminiRagService>();

// Servicios para Vertex Search y Gemini
builder.Services.AddSingleton<ISearchService, SearchService>();
builder.Services.AddSingleton<IGeminiService, GeminiService>();
builder.Services.AddScoped<IQueryRewriteService, QueryRewriteService>();
builder.Services.AddScoped<IConversationRagService, ConversationRagService>();
builder.Services.AddScoped<CodigoVerificacionService>();

// SSE emitter
builder.Services.AddSingleton<ISseEmitter, RedisSseEmitter>();

builder.Services.AddSingleton<PredictionServiceClient>(sp=>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var loc = cfg.GetSection("Gcp")["Location"] ?? "us-east4";
    var endpoint = $"{loc}-aiplatform.googleapis.com"; // ? us-east4-aiplatform.googleapis.com
    var b = new PredictionServiceClientBuilder { Endpoint = endpoint };
    return b.Build();
});

// filtro de verificacion de fulfillments para Dialogflow CX
builder.Services.AddScoped<CxApiKeyFilter>();

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// (Opcional) CORS para tu webchat
builder.Services.AddCors(opt => {
    opt.AddPolicy("WebClient", p => p
        .WithOrigins("http://localhost:5173") // ajustá tu origen
        .AllowAnyHeader().AllowAnyMethod());
});
var jwtCfg = builder.Configuration.GetSection("Jwt");
var secret = jwtCfg["Secret"] ?? "dev-secret-change-me";
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.RequireHttpsMetadata = false; // true en PROD
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
// 1) Credenciales base (usuario o SA). Pueden ser las mismas para ambos.
GoogleCredential baseCred = GoogleCredential.GetApplicationDefault();
// obtiene el settings de configuraciones
var cfg = builder.Configuration;

// ---------- Dialogflow CX (flujo-bot-dhr) ----------
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

// ---------- Discovery Engine / Vertex AI Search (para  RAG) ----------
var deLocation = cfg["GoogleCloud:Location"] ?? "us"; // "us" en tu caso
var deEndpoint = deLocation == "global"
    ? "discoveryengine.googleapis.com"
    : $"{deLocation}-discoveryengine.googleapis.com";

// El proyecto que va a facturar las llamadas de Search (normalmente el mismo ProjectId)
var deQuotaProject = cfg["GoogleCloud:ProjectId"];

// IMPORTANTE: algunos entornos piden scope explícito. Cloud Platform cubre todo.
var deCred = baseCred
    .CreateScoped("https://www.googleapis.com/auth/cloud-platform")
    .CreateWithQuotaProject(deQuotaProject);

// Cliente de Discovery Engine con tu ADC + quota project + endpoint regional
builder.Services.AddSingleton(_ =>
    new DE.SearchServiceClientBuilder
    {
        Endpoint = deEndpoint,
        ChannelCredentials = deCred.ToChannelCredentials()
    }.Build()
);

// Kestrel: no comprimir event-stream, timeouts razonables
builder.WebHost.ConfigureKestrel(opt =>
{
    opt.AddServerHeader = false;
    // Opcional: opt.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(5);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("WebClient");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/health"));

app.Run();
