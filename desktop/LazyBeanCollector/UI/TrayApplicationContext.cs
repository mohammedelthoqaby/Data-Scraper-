using LazyBean.Collector.App.Host;
using LazyBean.Collector.App.Runtime;
using LazyBean.Collector.App.Settings;

namespace LazyBean.Collector.App.UI;

// Owns the app's lifetime. The host, the worker loop and the tray live here, not in the window. Closing the
// window (X) disposes it and leaves the app running; only the tray's Exit ends the app.
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly CollectorHost _host;
    private readonly ActivityLog _log;
    private readonly CollectorState _state;
    private readonly AppPaths _paths;
    private readonly SynchronizationContext _ui;
    private readonly TrayController _tray;
    private readonly EventWaitHandle _showRequests;
    private readonly ManualResetEvent _stopWatching = new(initialState: false);
    private readonly Thread _watcher;
    private readonly Action _stateChanged;
    private MainForm? _window;
    private bool _exiting;

    public TrayApplicationContext(
        CollectorHost host,
        ActivityLog log,
        CollectorState state,
        AppPaths paths,
        EventWaitHandle showRequests,
        bool startInBackground)
    {
        _host = host;
        _log = log;
        _state = state;
        _paths = paths;
        _showRequests = showRequests;
        _ui = SynchronizationContext.Current
            ?? throw new InvalidOperationException("The tray must be created on the UI thread.");

        _tray = new TrayController(
            openWindow: ShowWindow,
            adsNow: () => _ = RunAdsNowAsync(),
            exit: () => _ = ExitAsync());

        _stateChanged = () => _ui.Post(_ => RefreshTray(), null);
        _state.Changed += _stateChanged;
        _state.AllDone += OnAllDone;
        RefreshTray();

        // A second launch sets this event; the window opens here, on the UI thread.
        _watcher = new Thread(WatchForShowRequests) { IsBackground = true, Name = "show-requests" };
        _watcher.Start();

        if (!startInBackground)
        {
            ShowWindow();
        }
    }

    // Opens the window, or brings the open one to the front. The window is created on demand and disposed when closed.
    public void ShowWindow()
    {
        if (_window is null)
        {
            _window = new MainForm(_host, _log, _state, _paths);
            _window.FormClosed += (_, _) => OnWindowClosed();
        }

        if (!_window.Visible)
        {
            _window.Show();
        }

        if (_window.WindowState == FormWindowState.Minimized)
        {
            _window.WindowState = FormWindowState.Normal;
        }

        _window.Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _state.Changed -= _stateChanged;
            _state.AllDone -= OnAllDone;
            _stopWatching.Set();
            _watcher.Join(TimeSpan.FromSeconds(2));
            _stopWatching.Dispose();
            _window?.Dispose();
            _tray.Dispose();
        }

        base.Dispose(disposing);
    }

    private void WatchForShowRequests()
    {
        var handles = new WaitHandle[] { _showRequests, _stopWatching };
        while (WaitHandle.WaitAny(handles) == 0)
        {
            _ui.Post(_ => ShowWindow(), null);
        }
    }

    private void OnWindowClosed()
    {
        var closed = _window;
        _window = null;

        // Dispose after the close has finished, not from inside its own event.
        _ui.Post(_ => closed?.Dispose(), null);

        if (!_exiting)
        {
            ShowFirstTimeHint();
        }
    }

    private void ShowFirstTimeHint()
    {
        if (_host.Settings.TrayHintShown)
        {
            return;
        }

        _host.MarkTrayHintShown();
        _tray.ShowBalloon("LazyBean Collector is still running in the background. Right-click the tray icon > Exit to quit.");
    }

    // Balloon once per finished batch: the app's work for the run is done, so the user may close it.
    private void OnAllDone() =>
        _ui.Post(_ => _tray.ShowBalloon("All done for this run - you can close the app."), null);

    private void RefreshTray()
    {
        var working = _state.CurrentJob is not null;
        _tray.Show(TrayStatus.Describe(_state.Connection, working, _state.Batch), working);
    }

    private async Task RunAdsNowAsync()
    {
        try
        {
            await _host.RequestAdsNowAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex is Api.CollectorApiException api ? api.Message : $"Could not queue ads collection: {ex.Message}",
                "LazyBean Collector",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    // Exit is the only way out. A job in progress is handed back to the server before the app ends.
    private async Task ExitAsync()
    {
        if (_exiting)
        {
            return;
        }

        if (_state.CurrentJob is not null)
        {
            var answer = MessageBox.Show(
                "A collection job is still running on this PC. Exit now? The job is handed back to the server so it can be collected again later.",
                "LazyBean Collector",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                return;
            }
        }

        _exiting = true;
        _window?.Close();
        await _host.StopAsync();
        ExitThread();
    }
}
