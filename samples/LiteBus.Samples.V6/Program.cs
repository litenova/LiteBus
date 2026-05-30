using LiteBus.Samples.V6;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApiDocument();
builder.Services.AddLiteBusV6(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUi();
    app.UseOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();
app.Run();
