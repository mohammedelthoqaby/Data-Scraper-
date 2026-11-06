namespace LazyBean.Application.Ads.Common;

// The format of a Google creative: Text, Image or Video. The listing gives the format. When it does not, the preview
// decides: a display-format preview is a video, any other preview is an image, and a creative with no preview is text.
public static class GoogleCreativeFormat
{
    public const string Text = "Text";
    public const string Image = "Image";
    public const string Video = "Video";

    public static string Resolve(string? current, string? previewUrl)
    {
        if (current is Text or Image or Video)
        {
            return current;
        }

        if (string.IsNullOrWhiteSpace(previewUrl))
        {
            return Text;
        }

        return previewUrl.Contains("displayads-formats", StringComparison.OrdinalIgnoreCase) ? Video : Image;
    }
}
