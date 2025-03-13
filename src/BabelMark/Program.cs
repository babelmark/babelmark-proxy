using System;
using System.Threading.RateLimiting;
using JavaScriptEngineSwitcher.ChakraCore;
using JavaScriptEngineSwitcher.Extensions.MsDependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BabelMark;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add structured logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddApplicationInsights(); // If using Azure App Insights

        // Add services to the container.
        builder.Services.AddRateLimiter(_ => _
            .AddFixedWindowLimiter(policyName: "fixed", options =>
            {
                options.PermitLimit = 1;
                options.Window = TimeSpan.FromSeconds(4);
                options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                options.QueueLimit = 2;
            }));

        builder.Services.AddJsEngineSwitcher(options => options.DefaultEngineName = ChakraCoreJsEngine.EngineName).AddChakraCore();

        builder.Services.AddControllers();

        // Add health checks
        builder.Services.AddHealthChecks();

        var app = builder.Build();

        // Before other middleware that relies on the client information
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        });

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/error");
            app.UseHsts();
        }

        app.UseRateLimiter();

        app.UseCors(x => x.WithOrigins("http://babelmark.github.io", "https://babelmark.github.io", "https://johnmacfarlane.net/", "http://johnmacfarlane.net/").WithMethods("GET").AllowCredentials());

        app.UseHttpsRedirection();

        app.UseRouting();

        app.UseAuthorization();

        app.MapControllers();

        // Map health checks
        app.MapHealthChecks("/health");

        app.Run();
    }
}
