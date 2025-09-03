// Importações necessárias para autenticação JWT, Entity Framework, Swagger e outros serviços
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Shared.Security.Services;
using Shared.Security.Interfaces;
using System.Text;
using Shared.Data;
using Microsoft.AspNetCore.Identity;
using Shared.Models.AuthUser;
using Shared.Interface;
using PromocaoiPhone.Services;

var builder = WebApplication.CreateBuilder(args);

// ================= CONFIGURAÇÕES BÁSICAS =================

// Carrega configurações do arquivo appsettings.json
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// ================= CONFIGURAÇÃO DO BANCO DE DADOS =================

// Configura o DbContext para usar SQLite (pode ser trocado por outro provider como SQL Server, PostgreSQL, etc.)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// ================= INJEÇÃO DE DEPENDÊNCIAS =================

// Serviço de hash de senha (Identity)
builder.Services.AddScoped<IPasswordHasher<UserModel>, PasswordHasher<UserModel>>();

// Serviço de Promoção (exemplo específico do projeto)
builder.Services.AddScoped<IPromocaoService, PromocaoService>();

// ================= CONFIGURAÇÃO JWT =================

// Lê configurações do appsettings.json (chave, issuer e tempo de expiração)
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new Exception("JWT Key ausente!");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? throw new Exception("JWT Issuer ausente!");
var jwtExpiry = int.Parse(builder.Configuration["Jwt:ExpiryMinutes"] ?? throw new Exception("JWT Expiry ausente!"));

// Registra o gerador/validador de tokens JWT
builder.Services.AddSingleton<IJwtTokenService>(sp =>
    new JwtTokenGenerator(jwtKey, jwtIssuer, jwtExpiry)
);

// Configuração do middleware de autenticação com JWT
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme; // Esquema padrão de autenticação
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;   // Esquema padrão para desafios (ex.: 401 Unauthorized)
})
.AddJwtBearer(options =>
{
    // Parâmetros de validação do token
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, // Valida emissor
        ValidateAudience = true, // Valida audiência
        ValidateLifetime = true, // Valida tempo de expiração
        ValidateIssuerSigningKey = true, // Valida a chave de assinatura
        ValidIssuer = jwtIssuer, // Emissor válido
        ValidAudience = jwtIssuer, // Audiência válida
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)) // Chave de assinatura
    };
});

// Adiciona suporte a autorização baseada em roles/policies
builder.Services.AddAuthorization();

// ================= CORS (Cross-Origin Resource Sharing) =================

// Permite que o frontend Angular (localhost:4200) acesse a API
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins("http://localhost:4200") // origem permitida
              .AllowAnyHeader() // permite qualquer cabeçalho
              .AllowAnyMethod(); // permite qualquer método (GET, POST, PUT, DELETE, etc.)
    });
});

// ================= SWAGGER (Documentação da API) =================

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Configuração do Swagger com suporte para JWT
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "StockService API", Version = "v1" });

    // Define o esquema de autenticação JWT no Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Digite 'Bearer {seu token JWT}'"
    });

    // Exige o esquema de segurança em todas as requisições
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
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

var app = builder.Build();

// ================= MIGRAÇÕES AUTOMÁTICAS =================

// Aplica migrations automaticamente ao iniciar a aplicação
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate(); // Cria o banco e aplica migrations (garante que Users e outras tabelas existam)
}

// ================= PIPELINE DE MIDDLEWARE =================

// Habilita Swagger somente em ambiente de desenvolvimento
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Middleware de redirecionamento para HTTPS
app.UseHttpsRedirection();

// Habilita CORS (necessário para comunicação Angular ↔ API)
app.UseCors("AllowAngular");

// Ativa autenticação e autorização
app.UseAuthentication();
app.UseAuthorization();

// Mapeia os controllers
app.MapControllers();

// Inicia a aplicação
app.Run();
