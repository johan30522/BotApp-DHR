using BotApp.Data;
using BotApp.Filters;
using BotApp.Services;
using Google.Cloud.AIPlatform.V1;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.IdentityModel.Tokens;
using System.Text;

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

// Add services to the container.
builder.Services.AddSingleton<RedisClient>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<DenunciasService>();
builder.Services.AddScoped<ExpedientesService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<MetaSignatureFilter>();
builder.Services.AddScoped<TwilioSignatureFilter>();
builder.Services.AddScoped<GeminiRagService>();

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
});
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// (Opcional) CORS para tu webchat
builder.Services.AddCors(opt => {
    opt.AddPolicy("WebClient", p => p
        .WithOrigins("https://localhost:3000") // ajustá tu origen
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
