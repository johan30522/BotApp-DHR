using BotApp.Data;
using BotApp.Services;
using Microsoft.EntityFrameworkCore;

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("WebClient");

app.UseAuthorization();

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/health"));

app.Run();
