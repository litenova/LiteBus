using LiteBus.Commands.Extensions.MicrosoftDependencyInjection;
using LiteBus.Events.Extensions.MicrosoftDependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using LiteBus.Queries.Extensions.MicrosoftDependencyInjection;
using LiteBus.WebApi.Commands;
using LiteBus.WebApi.Events;
using LiteBus.WebApi.Queries;

namespace LiteBus.WebApi
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }


        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLiteBus(builder =>
            {
                builder.AddCommands(commandBuilder =>
                       {
                           commandBuilder.Register(typeof(CreatePersonCommand).Assembly)
                                         .RegisterPostHandleHook<GlobalCommandPostHandleHook>();
                       })
                       .AddQueries(queryBuilder =>
                       {
                           queryBuilder.Register(typeof(ColorQuery).Assembly); 
                       })
                       .AddEvents(eventBuilder =>
                       {
                           eventBuilder.Register(typeof(ColorCreatedEvent).Assembly);
                       });
            });

            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1",
                             new OpenApiInfo
                             {
                                 Title = "LiteBus.WebApi", Version = "v1"
                             });
            });

            services.AddTransient(typeof(CreateColorCommandWithResult));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "LiteBus.WebApi v1"));
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
        }
    }
}