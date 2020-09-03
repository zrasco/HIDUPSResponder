using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HIDUPSResponder.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HIDUPSResponder
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    IConfiguration configuration = hostContext.Configuration;

                    services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
                    //AppSettings appSettings = configuration.GetSection("AppSettings").Get<AppSettings>();   

                    //services.AddSingleton(appSettings);

                    //services.AddTransient<IOptionsSnapshot<AppSettings>>();

                    services.AddSingleton<IConfiguration>(configuration);
                    services.AddHostedService<Worker>();
                }).UseWindowsService();
    }
}
