using Autodesk.Forge.Core;
using Autodesk.Forge.DesignAutomation;

namespace BlockConfigurator
{
    public class Program
    {
        public static void Main(string[] args)
        {
         CreateHostBuilder(args).Build().Run();
        }
        public static IHostBuilder CreateHostBuilder(string[] args) =>
          Host.CreateDefaultBuilder(args)
          .ConfigureAppConfiguration(configureDelegate: (context, config) =>
          {
              config.AddJsonFile("appsettings.user.json", optional: false, reloadOnChange: true);
              config.AddEnvironmentVariables();
          })
          .ConfigureServices((hostContext, services) =>
          {
              services.AddDesignAutomation(hostContext.Configuration);
          })
          .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>());


    }
}
