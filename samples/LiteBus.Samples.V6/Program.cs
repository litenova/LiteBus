using LiteBus.Extensions.AspNetCore;
using LiteBus.Extensions.Diagnostics.HealthChecks;
using LiteBus.Samples.V6;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApiDocument();
builder.Services.AddLiteBusV6(builder.Configuration);
builder.Services.AddLiteBusManagement(options =>
{
    options.FailHealthWhenNoProbes = false;
    options.AllowAnonymousManagement = builder.Environment.IsDevelopment();
});
builder.Services.AddHealthChecks().AddLiteBus();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUi();
    app.UseOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();
app.MapHealthChecks("/health");
app.AddLiteBusManagementEndpoints();
app.Run();
