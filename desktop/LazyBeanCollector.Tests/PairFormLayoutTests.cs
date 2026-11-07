using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using LazyBean.Collector.App.Host;
using LazyBean.Collector.App.Runtime;
using LazyBean.Collector.App.Settings;
using LazyBean.Collector.App.UI;

namespace LazyBean.Collector.App.Tests;

// The pairing dialog's layout: no two controls overlap, everything fits, the tab order is Server, Username, Password,
// PC name, Pair, Cancel, then the link, and Enter and Esc are wired. Windows Forms needs a single-threaded apartment,
// so each test runs on its own STA thread.
public sealed class PairFormLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lazybean-pairform-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    public void PairForm_ControlsDoNotOverlap_AndFitTheDialog_AtEachScaling(float scale)
    {
        RunSta(() =>
        {
            using var form = CreateForm();
            form.Scale(new SizeF(scale, scale));
            form.PerformLayout();

            AssertNoOverlapAndInside(form, scale);
        });
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.5f)]
    public void PairForm_WithArabicText_StillFitsWithoutOverlap(float scale)
    {
        RunSta(() =>
        {
            using var form = CreateForm();
            TextBoxAt(form, tabIndex: 0).Text = "https://lazy-bean.runasp.net/مكان-الاختبار";
            TextBoxAt(form, tabIndex: 1).Text = "محمد عبدالرحمن";
            TextBoxAt(form, tabIndex: 3).Text = "حاسوب المكتب";
            form.Scale(new SizeF(scale, scale));
            form.PerformLayout();

            AssertNoOverlapAndInside(form, scale);
        });
    }

    [Fact]
    public void PairForm_TabOrder_IsServerUsernamePasswordNamePairCancelThenTheLink()
    {
        RunSta(() =>
        {
            using var form = CreateForm();

            var controls = Descendants(form)
                .Where(c => c is TextBox or ButtonBase or LinkLabel)
                .OrderBy(c => c.TabIndex)
                .ToList();

            Assert.Equal(7, controls.Count);
            Assert.All(controls, c => Assert.True(c.TabStop, $"{Describe(c)} is not in the tab order."));

            var order = controls.Select(c => c switch
            {
                TextBox => "field",
                Button b => b.Text,
                LinkLabel => "link",
                _ => c.GetType().Name,
            }).ToArray();

            Assert.Equal(new[] { "field", "field", "field", "field", "Pair", "Cancel", "link" }, order);
        });
    }

    [Fact]
    public void PairForm_EnterPairs_AndEscCancels()
    {
        RunSta(() =>
        {
            using var form = CreateForm();

            Assert.Equal("Pair", form.AcceptButton is Button accept ? accept.Text : null);
            Assert.Equal("Cancel", form.CancelButton is Button cancel ? cancel.Text : null);
            Assert.Equal(DialogResult.Cancel, form.CancelButton!.DialogResult);
        });
    }

    [Fact]
    public void PairForm_StartsAtProduction_WhenTheSavedServerIsLocalInARelease()
    {
        RunSta(() =>
        {
            using var form = CreateForm(savedServer: "http://localhost:5188");

            // Debug builds keep the saved server, so only the release rule is checked here.
            var expected = ServerPolicy.IsReleaseBuild ? ServerPolicy.ProductionServerUrl : "http://localhost:5188";
            Assert.Equal(expected, TextBoxAt(form, tabIndex: 0).Text);
        });
    }

    private Form CreateForm(string? savedServer = null)
    {
        var paths = new AppPaths(_root);
        paths.EnsureDirectories();
        new SettingsStore(paths.SettingsFile).Save(new CollectorSettings
        {
            ServerUrl = savedServer ?? ServerPolicy.ProductionServerUrl,
            DeviceName = "Test PC",
        });

        var host = new CollectorHost(paths, new ActivityLog(paths.LogsDirectory), new CollectorState());
        return new PairForm(host);
    }

    private static TextBox TextBoxAt(Control form, int tabIndex) =>
        Descendants(form).OfType<TextBox>().Single(box => box.TabIndex == tabIndex);

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var grandchild in Descendants(child))
            {
                yield return grandchild;
            }
        }
    }

    private static void AssertNoOverlapAndInside(Form form, float scale)
    {
        // The message label is empty while it is hidden (Control.Visible is not reliable on a form that was never shown).
        var leaves = Descendants(form)
            .Where(c => c.Controls.Count == 0 && c.Width > 0 && c.Height > 0 && !(c is Label { Text.Length: 0 }))
            .Select(c => (Control: c, Rect: AbsoluteBounds(c, form)))
            .ToList();

        // A layout that measured nothing would pass every check below, so the count itself is asserted.
        Assert.True(leaves.Count >= 10, $"At {scale * 100}%, only {leaves.Count} controls were measured.");

        var client = new Rectangle(Point.Empty, form.ClientSize);
        foreach (var (control, rect) in leaves)
        {
            Assert.True(
                client.Contains(rect),
                $"At {scale * 100}%, {Describe(control)} at {rect} is outside the dialog's client area {client}.");
        }

        for (var i = 0; i < leaves.Count; i++)
        {
            for (var j = i + 1; j < leaves.Count; j++)
            {
                Assert.False(
                    leaves[i].Rect.IntersectsWith(leaves[j].Rect),
                    $"At {scale * 100}%, {Describe(leaves[i].Control)} {leaves[i].Rect} overlaps {Describe(leaves[j].Control)} {leaves[j].Rect}.");
            }
        }
    }

    private static Rectangle AbsoluteBounds(Control control, Form form)
    {
        var rect = control.Bounds;
        for (var parent = control.Parent; parent is not null && parent != form; parent = parent.Parent)
        {
            rect.Offset(parent.Location);
        }

        return rect;
    }

    private static string Describe(Control control) =>
        control is TextBox box ? $"TextBox(tab {box.TabIndex})" : $"{control.GetType().Name} '{control.Text}'";

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
