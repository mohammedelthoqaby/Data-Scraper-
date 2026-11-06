using LazyBean.Collector.App.Api;
using LazyBean.Collector.App.Host;
using LazyBean.Collector.App.Settings;

namespace LazyBean.Collector.App.UI;

// First-run and re-pairing dialog. Signs in with the dashboard account and keeps the device token.
//
// The controls sit in a TableLayoutPanel (label column sized to its text, input column filling the rest), so no
// control is placed by pixel coordinates and none can overlap another. Each control has a 4px margin above and below,
// which gives 8px between rows. The dialog sizes itself to its content, so it also works at other display scalings.
public sealed class PairForm : Form
{
    private const int DialogPadding = 12;
    private const int RowMargin = 4;
    private const int ButtonGap = 8;
    private static readonly Size ButtonSize = new(90, 30);

    private readonly CollectorHost _host;
    private readonly TextBox _server = new();
    private readonly TextBox _username = new();
    private readonly TextBox _password = new();
    private readonly TextBox _device = new();
    private readonly Label _message = new();
    private readonly Button _pair = new();
    private readonly LinkLabel _reset = new();

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
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _server.Text = ServerPolicy.PairingDefault(host.Settings.ServerUrl, ServerPolicy.IsReleaseBuild);
        _device.Text = host.Settings.DeviceName ?? Environment.MachineName;
        _password.UseSystemPasswordChar = true;

        ConfigureField(_server, tabIndex: 0);
        ConfigureField(_username, tabIndex: 1);
        ConfigureField(_password, tabIndex: 2);
        ConfigureField(_device, tabIndex: 3);

        _reset.Text = "Reset to default";
        _reset.AutoSize = true;
        _reset.Anchor = AnchorStyles.Left;
        _reset.Margin = new Padding(0, 0, 0, RowMargin);
        _reset.TabIndex = 6;
        _reset.LinkClicked += (_, _) => _server.Text = ServerPolicy.ProductionServerUrl;

        // Hidden until there is something to say, so the empty row takes no space.
        _message.AutoSize = true;
        _message.MaximumSize = new Size(420, 0);
        _message.Margin = new Padding(0, RowMargin, 0, RowMargin);
        _message.Visible = false;

        _pair.Text = "Pair";
        _pair.Size = ButtonSize;
        _pair.Margin = new Padding(0, RowMargin, 0, RowMargin);
        _pair.TabIndex = 4;
        _pair.Click += async (_, _) => await PairAsync();
        AcceptButton = _pair;

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Size = ButtonSize,
            Margin = new Padding(ButtonGap, RowMargin, 0, RowMargin),
            TabIndex = 5,
        };
        CancelButton = cancel;

        // Right to left flow: the first control added sits at the right edge, so Pair is left of Cancel.
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_pair);

        var labelWidth = new[] { "Server", "Username", "Password", "This PC's name" }
            .Max(text => TextRenderer.MeasureText(text, UiStyle.BodyFont).Width) + ButtonGap;

        var table = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(DialogPadding),
            Margin = Padding.Empty,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var intro = new Label
        {
            Text = "Sign in with your LazyBean dashboard account.\r\nThis PC will then collect Strong Coffee and Meta ads for the server.",
            AutoSize = true,
            Margin = new Padding(0, RowMargin, 0, RowMargin),
        };

        table.Controls.Add(intro, 0, 0);
        table.SetColumnSpan(intro, 2);

        AddRow(table, 1, "Server", _server);
        table.Controls.Add(_reset, 1, 2);
        AddRow(table, 3, "Username", _username);
        AddRow(table, 4, "Password", _password);
        AddRow(table, 5, "This PC's name", _device);

        table.Controls.Add(_message, 0, 6);
        table.SetColumnSpan(_message, 2);

        table.Controls.Add(buttons, 0, 7);
        table.SetColumnSpan(buttons, 2);

        table.RowCount = 8;
        Controls.Add(table);

        // Label column width is measured from the labels' text, so the column stays as wide as the longest label.
        foreach (Control control in table.Controls)
        {
            if (control is Label label && label.Dock == DockStyle.Fill)
            {
                label.MinimumSize = new Size(labelWidth, 0);
            }
        }
    }

    private static void ConfigureField(TextBox box, int tabIndex)
    {
        box.Dock = DockStyle.Fill;
        box.MinimumSize = new Size(280, 0);
        box.Margin = new Padding(0, RowMargin, 0, RowMargin);
        box.TabIndex = tabIndex;
    }

    private static void AddRow(TableLayoutPanel table, int row, string text, TextBox box)
    {
        table.Controls.Add(new Label
        {
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, RowMargin, ButtonGap, RowMargin),
        }, 0, row);
        table.Controls.Add(box, 1, row);
    }

    private void ShowMessage(string text, Color color)
    {
        _message.ForeColor = color;
        _message.Text = text;
        _message.Visible = true;
    }

    private async Task PairAsync()
    {
        if (string.IsNullOrWhiteSpace(_username.Text) || string.IsNullOrWhiteSpace(_password.Text))
        {
            ShowMessage("Enter your dashboard username and password.", UiStyle.Muted);
            return;
        }

        _pair.Enabled = false;
        ShowMessage("Pairing…", UiStyle.Muted);
        try
        {
            await _host.PairAsync(_server.Text, _username.Text.Trim(), _password.Text, _device.Text.Trim(), CancellationToken.None);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (CollectorApiException ex)
        {
            ShowMessage(ex.Kind switch
            {
                ApiErrorKind.Unauthorized => "The username or password is not correct.",
                ApiErrorKind.RateLimited => "Too many attempts. Wait 10 minutes, then try again.",
                _ => ex.Message,
            }, UiStyle.Bad);
        }
        catch (Exception ex)
        {
            ShowMessage($"Could not pair: {ex.Message}", UiStyle.Bad);
        }
        finally
        {
            _pair.Enabled = true;
        }
    }
}
