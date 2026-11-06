using System.Diagnostics;
using LazyBean.Collector.App.Api;
using LazyBean.Collector.App.Host;
using LazyBean.Collector.App.Runtime;
using LazyBean.Collector.App.Settings;
using LazyBean.Collector.App.Startup;

namespace LazyBean.Collector.App.UI;

// The window: status, the current job, last sync, actions and the activity log. It is created on demand and
// disposed when closed. The app keeps running from its tray context (TrayApplicationContext), not from here.
public sealed class MainForm : Form
{
    private const int MaxLogLines = 500;

    private readonly CollectorHost _host;
    private readonly ActivityLog _log;
    private readonly CollectorState _state;
    private readonly AppPaths _paths;
    private readonly string _exePath;
    private readonly Action _stateChanged;
    private readonly Action<LogEntry> _logAdded;

    private readonly Panel _dot = new();
    private readonly Label _status = new();
    private readonly Label _server = new();
    private readonly Label _jobTitle = new();
    private readonly Label _jobMessage = new();
    private readonly ProgressBar _progress = new();
    private readonly ListView _lastSync = new();
    private readonly ListBox _logList = new();
    private readonly Button _pair = new();
    private readonly Button _adsNow = new();
    private readonly Button _signOut = new();
    private readonly Button _openLogs = new();
    private readonly CheckBox _startWithWindows = new();

    private bool _suppressStartupToggle;

    public MainForm(CollectorHost host, ActivityLog log, CollectorState state, AppPaths paths)
    {
        _host = host;
        _log = log;
        _state = state;
        _paths = paths;
        _exePath = Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath;

        Text = "LazyBean Collector";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = UiStyle.BodyFont;
        MinimumSize = new Size(760, 600);
        ClientSize = new Size(820, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = AppIcons.Load(new Size(32, 32));

        BuildLayout();

        _stateChanged = () => OnUi(RefreshState);
        _logAdded = entry => OnUi(() => AppendLog(entry));
        _state.Changed += _stateChanged;
        _log.Added += _logAdded;

        _suppressStartupToggle = true;
        _startWithWindows.Checked = StartupRegistry.IsEnabled();
        _suppressStartupToggle = false;
        LoadLog();
        RefreshState();
    }

    // Unsubscribes from the app-wide events: a disposed window must not keep receiving updates.
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _state.Changed -= _stateChanged;
            _log.Added -= _logAdded;
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(16, 14, 16, 8) };
        _dot.Size = new Size(14, 14);
        _dot.Location = new Point(16, 22);
        _dot.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(CurrentDotColor());
            e.Graphics.FillEllipse(brush, 0, 0, _dot.Width - 1, _dot.Height - 1);
        };
        _status.Location = new Point(40, 16);
        _status.Size = new Size(760, 24);
        _status.Font = UiStyle.TitleFont;
        _server.Location = new Point(40, 48);
        _server.Size = new Size(760, 20);
        _server.ForeColor = UiStyle.Muted;
        header.Controls.AddRange([_dot, _status, _server]);

        var jobBox = new GroupBox { Text = "Current job", Location = new Point(16, 100), Size = new Size(788, 120) };
        _jobTitle.Location = new Point(14, 26);
        _jobTitle.Size = new Size(760, 22);
        _jobTitle.Font = new Font(UiStyle.BodyFont, FontStyle.Bold);
        _progress.Location = new Point(14, 54);
        _progress.Size = new Size(760, 18);
        _jobMessage.Location = new Point(14, 80);
        _jobMessage.Size = new Size(760, 22);
        _jobMessage.ForeColor = UiStyle.Muted;
        jobBox.Controls.AddRange([_jobTitle, _progress, _jobMessage]);

        var syncBox = new GroupBox { Text = "Last sync", Location = new Point(16, 228), Size = new Size(788, 150) };
        _lastSync.Location = new Point(12, 24);
        _lastSync.Size = new Size(764, 116);
        _lastSync.View = View.Details;
        _lastSync.FullRowSelect = true;
        _lastSync.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _lastSync.Columns.Add("Source", 240);
        _lastSync.Columns.Add("When", 150);
        _lastSync.Columns.Add("Result", 360);
        syncBox.Controls.Add(_lastSync);

        var actions = new FlowLayoutPanel { Location = new Point(16, 388), Size = new Size(788, 44), WrapContents = false };
        _pair.Text = "Pair this PC…";
        _pair.Size = new Size(140, 32);
        _pair.Click += (_, _) => OpenPairing();
        _adsNow.Text = "Collect ads now";
        _adsNow.Size = new Size(140, 32);
        _adsNow.Click += async (_, _) => await RunAdsNowAsync();
        _signOut.Text = "Sign out";
        _signOut.Size = new Size(110, 32);
        _signOut.Click += async (_, _) => await SignOutAsync();
        _openLogs.Text = "Open log folder";
        _openLogs.Size = new Size(140, 32);
        _openLogs.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_paths.LogsDirectory}\"") { UseShellExecute = false });
        actions.Controls.AddRange([_pair, _adsNow, _signOut, _openLogs]);

        _startWithWindows.Text = "Start LazyBean Collector when Windows starts";
        _startWithWindows.Location = new Point(16, 440);
        _startWithWindows.Size = new Size(788, 24);
        _startWithWindows.CheckedChanged += (_, _) => ToggleStartup();

        var logBox = new GroupBox { Text = "Activity", Location = new Point(16, 472), Size = new Size(788, 158), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
        _logList.Location = new Point(12, 22);
        _logList.Size = new Size(764, 124);
        _logList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _logList.IntegralHeight = false;
        _logList.HorizontalScrollbar = true;
        _logList.Font = UiStyle.SmallFont;
        logBox.Controls.Add(_logList);

        Controls.AddRange([header, jobBox, syncBox, actions, _startWithWindows, logBox]);
    }

    private Color CurrentDotColor() => _state.Connection switch
    {
        ConnectionState.Connected => UiStyle.Good,
        ConnectionState.Waiting => UiStyle.Warn,
        ConnectionState.PairingRequired or ConnectionState.Stopped => UiStyle.Bad,
        ConnectionState.NotPaired => UiStyle.Warn,
        _ => UiStyle.Idle,
    };

    private void RefreshState()
    {
        var settings = _host.Settings;
        _dot.Invalidate();
        _status.Text = _state.Connection switch
        {
            ConnectionState.Connected => "Connected",
            ConnectionState.Waiting => "Waiting for the server",
            ConnectionState.NotPaired => "Not paired",
            ConnectionState.PairingRequired => "Pairing required",
            ConnectionState.Stopped => "Stopped",
            _ => "Starting",
        };
        var device = settings.DeviceName is null ? string.Empty : $"   ·   This PC: {settings.DeviceName}";
        _server.Text = $"{_state.ConnectionDetail}   ·   Server: {settings.ServerUrl}{device}";

        var job = _state.CurrentJob;
        if (job is null)
        {
            _jobTitle.Text = "Nothing running";
            _jobMessage.Text = string.Empty;
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 0;
        }
        else
        {
            _jobTitle.Text = job.Title;
            _jobMessage.Text = job.Message;
            if (job.Total is { } total && total > 0)
            {
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Maximum = total;
                _progress.Value = Math.Clamp(job.Done, 0, total);
            }
            else
            {
                _progress.Style = ProgressBarStyle.Marquee;
            }
        }

        RenderLastSync();

        var paired = settings.DeviceId is not null;
        _pair.Visible = !paired;
        _pair.Enabled = !paired;
        _adsNow.Enabled = paired && _state.Connection is ConnectionState.Connected or ConnectionState.Waiting;
        _signOut.Enabled = paired;
    }

    private void RenderLastSync()
    {
        var rows = new List<(string Name, SourceSync Sync)>();
        foreach (var (key, sync) in _state.LastSync().OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            rows.Add((DisplayName(key), sync));
        }

        _lastSync.BeginUpdate();
        _lastSync.Items.Clear();
        foreach (var (name, sync) in rows)
        {
            var item = new ListViewItem(name);
            item.SubItems.Add(sync.At.ToString("yyyy-MM-dd HH:mm"));
            item.SubItems.Add(sync.Result);
            _lastSync.Items.Add(item);
        }

        if (rows.Count == 0)
        {
            _lastSync.Items.Add(new ListViewItem("No sync yet on this PC."));
        }

        _lastSync.EndUpdate();
    }

    private static string DisplayName(string key)
    {
        if (key == "meta")
        {
            return "Meta ads";
        }

        if (key == "google")
        {
            return "Google ads";
        }

        return key.StartsWith("store:", StringComparison.Ordinal) ? $"Store: {key["store:".Length..]}" : key;
    }

    private void LoadLog()
    {
        _logList.Items.Clear();
        foreach (var entry in _log.Recent())
        {
            _logList.Items.Add(FormatEntry(entry));
        }
    }

    private void AppendLog(LogEntry entry)
    {
        _logList.Items.Add(FormatEntry(entry));
        while (_logList.Items.Count > MaxLogLines)
        {
            _logList.Items.RemoveAt(0);
        }

        _logList.TopIndex = Math.Max(0, _logList.Items.Count - 1);
    }

    private static string FormatEntry(LogEntry entry) => $"{entry.At:HH:mm:ss}  {entry.Level,-5}  {entry.Message}";

    private void OpenPairing()
    {
        using var form = new PairForm(_host);
        form.ShowDialog(this);
        RefreshState();
    }

    private async Task RunAdsNowAsync()
    {
        try
        {
            await _host.RequestAdsNowAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowError(ex is CollectorApiException api ? api.Message : $"Could not queue ads collection: {ex.Message}");
        }
    }

    private async Task SignOutAsync()
    {
        var answer = MessageBox.Show(
            this,
            "Sign this PC out? It stops collecting until you pair it again. The server revokes its token.",
            "Sign out",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        await _host.SignOutAsync(CancellationToken.None);
        RefreshState();
    }

    private void ToggleStartup()
    {
        if (_suppressStartupToggle)
        {
            return;
        }

        try
        {
            StartupRegistry.SetEnabled(_startWithWindows.Checked, _exePath);
            _log.Info(_startWithWindows.Checked ? "Will start with Windows." : "Will not start with Windows.");
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _suppressStartupToggle = true;
            _startWithWindows.Checked = StartupRegistry.IsEnabled();
            _suppressStartupToggle = false;
            ShowError($"Could not change the Windows startup setting: {ex.Message}");
        }
    }

    private void ShowError(string message) => MessageBox.Show(this, message, "LazyBean Collector", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void OnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }
}
