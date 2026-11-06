using LazyBean.Collector.App.Api;
using LazyBean.Collector.App.Host;
using LazyBean.Collector.App.Settings;

namespace LazyBean.Collector.App.UI;

// First-run and re-pairing dialog. Signs in with the dashboard account and keeps the device token.
public sealed class PairForm : Form
{
    private readonly CollectorHost _host;
    private readonly TextBox _server = new();
    private readonly TextBox _username = new();
    private readonly TextBox _password = new();
    private readonly TextBox _device = new();
    private readonly Label _message = new();
    private readonly Button _pair = new();

    public PairForm(CollectorHost host)
    {
        _host = host;
        Text = "Pair LazyBean Collector";
        Icon = AppIcons.Load(new Size(32, 32));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = UiStyle.BodyFont;
        ClientSize = new Size(440, 340);
        AcceptButton = _pair;

        var intro = new Label
        {
            Text = "Sign in with your LazyBean dashboard account. This PC will then collect Strong Coffee and Meta ads for the server.",
            Location = new Point(16, 12),
            Size = new Size(408, 44),
        };

        _server.Text = ServerPolicy.PairingDefault(host.Settings.ServerUrl, ServerPolicy.IsReleaseBuild);
        _device.Text = host.Settings.DeviceName ?? Environment.MachineName;
        _password.UseSystemPasswordChar = true;

        AddRow(60, "Server", _server);
        var reset = new LinkLabel { Text = "Reset to default", Location = new Point(132, 88), AutoSize = true };
        reset.LinkClicked += (_, _) => _server.Text = ServerPolicy.ProductionServerUrl;
        Controls.Add(reset);
        AddRow(100, "Username", _username);
        AddRow(140, "Password", _password);
        AddRow(180, "This PC's name", _device);

        _message.Location = new Point(16, 220);
        _message.Size = new Size(408, 56);
        _message.ForeColor = UiStyle.Muted;

        _pair.Text = "Pair";
        _pair.Location = new Point(228, 290);
        _pair.Size = new Size(90, 30);
        _pair.Click += async (_, _) => await PairAsync();

        var cancel = new Button { Text = "Cancel", Location = new Point(326, 290), Size = new Size(90, 30), DialogResult = DialogResult.Cancel };

        Controls.AddRange([intro, _message, _pair, cancel]);
        CancelButton = cancel;
    }

    private void AddRow(int top, string label, TextBox box)
    {
        Controls.Add(new Label { Text = label, Location = new Point(16, top + 4), Size = new Size(110, 20) });
        box.Location = new Point(132, top);
        box.Size = new Size(292, 24);
        Controls.Add(box);
    }

    private async Task PairAsync()
    {
        if (string.IsNullOrWhiteSpace(_username.Text) || string.IsNullOrWhiteSpace(_password.Text))
        {
            _message.Text = "Enter your dashboard username and password.";
            return;
        }

        _pair.Enabled = false;
        _message.ForeColor = UiStyle.Muted;
        _message.Text = "Pairing…";
        try
        {
            await _host.PairAsync(_server.Text, _username.Text.Trim(), _password.Text, _device.Text.Trim(), CancellationToken.None);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (CollectorApiException ex)
        {
            _message.ForeColor = UiStyle.Bad;
            _message.Text = ex.Kind switch
            {
                ApiErrorKind.Unauthorized => "The username or password is not correct.",
                ApiErrorKind.RateLimited => "Too many attempts. Wait 10 minutes, then try again.",
                _ => ex.Message,
            };
        }
        catch (Exception ex)
        {
            _message.ForeColor = UiStyle.Bad;
            _message.Text = $"Could not pair: {ex.Message}";
        }
        finally
        {
            _pair.Enabled = true;
        }
    }
}
