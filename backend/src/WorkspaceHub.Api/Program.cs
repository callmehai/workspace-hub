using System.Text;
using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OData;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OData.ModelBuilder;
using Microsoft.OpenApi.Models;
using WorkspaceHub.Api.Middleware;
using WorkspaceHub.Application;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// OData EDM model — expose FolderResponse cho $filter/$orderby/$select/$top/$skip/$count.
var edmBuilder = new ODataConventionModelBuilder();
edmBuilder.EntitySet<FolderResponse>("Folders");

// Controllers + serialize enum dạng string (khớp cách lưu DB) + OData.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddOData(o => o
        .SetMaxTop(100)
        .Filter()
        .OrderBy()
        .Select()
        .Expand()
        .Count()
        .SkipToken()
        .AddRouteComponents("api", edmBuilder.GetEdmModel()));

// Swagger.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new() { Title = "Workspace Hub API", Version = "v1" });
    // Giải quyết xung đột giữa OData convention route và attribute route của Controller
    o.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());

    // Cấu hình Swagger hỗ trợ gửi JWT Bearer Token
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token"
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// Authentication & Authorization
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var jwtSection = builder.Configuration.GetSection("Jwt");
    // Không fallback secret mặc định — thiếu config thì fail ngay lúc startup (CLAUDE.md: không hardcode secret).
    var secretKey = jwtSection["Secret"];
    if (string.IsNullOrEmpty(secretKey))
        throw new InvalidOperationException("Jwt:Secret is not configured. Set it in appsettings or user-secrets.");

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSection["Issuer"] ?? "WorkspaceHub",
        ValidAudience = jwtSection["Audience"] ?? "WorkspaceHub",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero
    };
});

// Register application and infrastructure services
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Exception middleware — bắt mọi lỗi → error format chuẩn (SCRUM-24 / CONVENTIONS.md).
app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseStaticFiles();
    app.UseSwagger();
    app.UseSwaggerUI(o => o.InjectJavascript("/swagger-auto-auth.js"));
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
