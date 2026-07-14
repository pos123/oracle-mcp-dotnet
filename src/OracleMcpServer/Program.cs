using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using OracleMcpServer.Config;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("OracleMcpServer", Serilog.Events.LogEventLevel.Debug)
    .WriteTo.Console(standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose)
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "logs", "oracle-mcp-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 5,
        fileSizeLimitBytes: 10L * 1024 * 1024,
        rollOnFileSizeLimit: true)
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog(Log.Logger);

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

    Log.Information("Starting Oracle read-only MCP server");

    await host.RunAsync();
}
catch (ConfigError ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.Exit(1);
}