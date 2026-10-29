using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using NtftEnhanced;
using NtftEnhanced.Infrastructure;
using NtftEnhanced.Ipc;
using NtftEnhanced.Lists;
using NtftEnhanced.Protection;
using NtftEnhanced.Proxy;
using NtftEnhanced.ServiceHost;

if (!WindowsServiceHelpers.IsWindowsService())
    return;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = Product.ServiceName);
builder.Services.Configure<HostOptions>(options =>
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
builder.Services.AddHostedService<FilterWorker>();
await builder.Build().RunAsync();

internal sealed class FilterWorker(IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var paths = ProtectionPaths.Production();
        Directory.CreateDirectory(paths.DataDirectory);
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var log = new FileLog(paths.LogPath);
        var coordinator = new ProtectionCoordinator(
            paths,
            ProtectionOptions.Production,
            new StevenBlackClient(http),
            new SystemClock(),
            new WindowsNameProbe(),
            new WindowsDnsFlush(),
            log,
            new LocalProxy(),
            new WindowsPolicyWriter(),
            new WindowsDnsConfigurator(),
            new WindowsFirewallConfigurator());

        var api = new ControlApi(coordinator, log);
        var pipe = PipeEndpoint.ServeAsync(Product.PipeName, api.Handle, stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            await coordinator.MaintainAsync();
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await coordinator.MaintainAsync();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lifetime.StopApplication();
            await pipe.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }
}
