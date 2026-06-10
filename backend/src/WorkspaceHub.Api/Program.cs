using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OData;
using Microsoft.OData.ModelBuilder;
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
});

// Tầng nghiệp vụ + hạ tầng (DI — không new trực tiếp trong controller).
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Exception middleware — bắt mọi lỗi → error format chuẩn (SCRUM-24 / CONVENTIONS.md).
app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
