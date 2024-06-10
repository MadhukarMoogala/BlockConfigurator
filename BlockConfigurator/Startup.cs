using System;
using Autodesk.Forge.DesignAutomation;
using BlockConfigurator.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BlockConfigurator
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            var clientID = Configuration["APS_CLIENT_ID"];
            var clientSecret = Configuration["APS_CLIENT_SECRET"];           
            if (string.IsNullOrEmpty(clientID) || string.IsNullOrEmpty(clientSecret))
            {
                throw new ApplicationException("Missing required environment variables APS_CLIENT_ID or APS_CLIENT_SECRET.");
            }
            string? bucket = Configuration["APS_BUCKET"]; // Optional
            services.AddMvc(options => options.EnableEndpointRouting = false).AddNewtonsoftJson();
            services.AddSingleton(new Models.APS(clientID, clientSecret, bucket ?? string.Empty));           
            services.AddSignalR().AddNewtonsoftJsonProtocol(opts => 
            {
                opts.PayloadSerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
            });

        }
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<DesignAutomationHub>("/api/signalr/designautomation");
                endpoints.MapControllers();
            });
        }
    }
}
