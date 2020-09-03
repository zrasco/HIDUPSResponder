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
using Serilog;

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
                    // Set working directory if in production
                    if (!hostContext.HostingEnvironment.IsDevelopment())
                    {
                        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
                    }

                    IConfiguration configuration = hostContext.Configuration;
                    services.Configure<AppSettings>(configuration.GetSection("AppSettings"));

                    // Add serilog support
                    Log.Logger = new LoggerConfiguration()
                        .ReadFrom.Configuration(configuration)
                        .Enrich.FromLogContext()
                        .CreateLogger();

                    services.AddSingleton<IConfiguration>(configuration);
                    services.AddHostedService<Worker>();
                })
            .UseSerilog()
            .UseWindowsService();
    }
}
