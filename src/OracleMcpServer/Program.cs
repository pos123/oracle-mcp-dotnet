using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OracleMcpServer.Config;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<ConfigLoader>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

try
{
    var host = builder.Build();

    var loader = host.Services.GetRequiredService<ConfigLoader>();
    loader.Load();

    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Starting Oracle read-only MCP server");

    await host.RunAsync();
}
catch (ConfigError ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.Exit(1);
}