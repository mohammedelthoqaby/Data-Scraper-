using LazyBean.Infrastructure.External.LazyBeanApi;
using LazyBean.Application.Collector.Exceptions;
using LazyBean.Collector.App.Host;
using LazyBean.Collector.App.Runtime;
using LazyBean.Collector.App.Settings;
using LazyBean.Collector.App.UI;

namespace LazyBean.Collector.App;

internal static class Program
{
    private const string SingleInstanceName = "LazyBean.Collector.SingleInstance.v1";
    private const string ShowRequestName = "LazyBean.Collector.Show.v1";

    [STAThread]
    private static int Main(string[] args)
    {
        var options = LaunchOptions.Parse(args);
#if DEBUG
        // A development build must never write to the real settings of this Windows user.
        if (!AppPaths.DataDirOverridden)
        {
            const string message = "Debug builds need LAZYBEAN_DATA_DIR set to a scratch folder, so they never write to the real "
                + "LazyBean Collector settings. Example: set LAZYBEAN_DATA_DIR=C:\\scratch\\lazybean-dev";
            if (options.Headless)
            {
                Console.Error.WriteLine(message);
            }
            else
            {
                System.Windows.Forms.MessageBox.Show(message, "LazyBean Collector", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            }

            return 2;
        }
#endif
        return options.Headless
            ? HeadlessRunner.RunAsync(options).GetAwaiter().GetResult()
            : RunWindow(options);
    }

    private static int RunWindow(LaunchOptions options)
    {
        using var singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out var first);
        if (!first)
        {
            // The app is already running. Ask it to show its window, and leave without a message.
            AskRunningInstanceToShow();
            return 0;
        }

        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        // The tray and the window marshal their updates from background threads through this context.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        var paths = AppPaths.Default;
        paths.EnsureDirectories();
        var log = new ActivityLog(paths.LogsDirectory);
        var state = new CollectorState();

        var host = new CollectorHost(paths, log, state);
        host.StartAsync().GetAwaiter().GetResult();
        try
        {
            using var showRequests = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ShowRequestName);
            using var context = new TrayApplicationContext(host, log, state, paths, showRequests, startInBackground: options.Background);
            System.Windows.Forms.Application.Run(context);
        }
        finally
        {
            host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        return 0;
    }

    private static void AskRunningInstanceToShow()
    {
        try
        {
            using var showRequests = EventWaitHandle.OpenExisting(ShowRequestName);
            showRequests.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The first instance has not created its signal yet, or it has already exited. Nothing to show.
        }
    }
}

// Command-line options. --headless runs the same collector without a window (support and testing).
public sealed record LaunchOptions(
    bool Background,
    bool Headless,
    bool PairRequested,
    bool AdsNow,
    string? Server,
    string? Username,
    string? Password,
    string? Device)
{
    public static LaunchOptions Parse(string[] args)
    {
        string? Value(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        return new LaunchOptions(
            Background: args.Contains("--background"),
            Headless: args.Contains("--headless"),
            PairRequested: args.Contains("--pair"),
            AdsNow: args.Contains("--ads-now"),
            Server: Value("--server"),
            Username: Value("--username"),
            Password: Value("--password"),
            Device: Value("--device"));
    }
}

internal static class HeadlessRunner
{
    public static async Task<int> RunAsync(LaunchOptions options)
    {
        var paths = AppPaths.Default;
        paths.EnsureDirectories();
        var log = new ActivityLog(paths.LogsDirectory);
        var state = new CollectorState();
        log.Added += entry => Console.WriteLine(FormatLine(entry));
        state.Changed += () => Console.WriteLine($"[state] {state.Connection}: {state.ConnectionDetail}");

        await using var host = new CollectorHost(paths, log, state);
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };

        try
        {
            if (options.PairRequested)
            {
                // The password is read from the environment so it does not show in the process list.
                var password = Environment.GetEnvironmentVariable("LAZYBEAN_PASSWORD") ?? options.Password;
                if (options.Server is null || options.Username is null || password is null)
                {
                    Console.Error.WriteLine("--pair needs --server, --username and LAZYBEAN_PASSWORD (or --password).");
                    return 2;
                }

                await host.PairAsync(options.Server, options.Username, password, options.Device ?? Environment.MachineName, stop.Token);
            }
            else
            {
                await host.StartAsync();
            }

            if (options.AdsNow)
            {
                await host.RequestAdsNowAsync(stop.Token);
            }

            await Task.Delay(Timeout.Infinite, stop.Token);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C.
        }
        catch (CollectorApiException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            await host.StopAsync();
        }

        return 0;
    }

    private static string FormatLine(LogEntry entry) => $"{entry.At:HH:mm:ss} {entry.Level,-5} {entry.Message}";
}
