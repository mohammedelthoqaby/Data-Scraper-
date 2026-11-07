namespace LazyBean.Application.Ads.Common;

// Reads the pixel size of a JPEG from its frame header, so an uploaded image can be checked without decoding it.
public static class JpegDimensions
{
    public static bool TryRead(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return false;
        }

        var i = 2;
        while (i + 3 < data.Length)
        {
            if (data[i] != 0xFF)
            {
                i++;
                continue;
            }

            var marker = data[i + 1];
            if (marker == 0xFF)
            {
                // A fill byte before a marker.
                i++;
                continue;
            }

            // Markers without a length: start of image, the temporary marker, restart markers.
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                i += 2;
                continue;
            }

            // The end of the image came before any frame header.
            if (marker == 0xD9)
            {
                return false;
            }

            var length = (data[i + 2] << 8) | data[i + 3];
            if (length < 2)
            {
                return false;
            }

            // Start-of-frame markers, without the ones that are not frames (define Huffman table, JPG, define arithmetic table).
            var isFrame = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isFrame)
            {
                if (i + 9 > data.Length)
                {
                    return false;
                }

                height = (data[i + 5] << 8) | data[i + 6];
                width = (data[i + 7] << 8) | data[i + 8];
                return width > 0 && height > 0;
            }

            i += 2 + length;
        }

        return false;
    }
}
