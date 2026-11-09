namespace LazyBean.Application.Common.Interfaces;

// Reads the product name and price shown in a creative's image preview (the optional AI-read). Throws when the image
// or the model cannot be used; the caller counts the attempt and tries again on a later run.
public interface ICreativeImageReader
{
    Task<CreativeImageReading?> ReadAsync(string imageUrl, CancellationToken ct = default);
}

// Null fields mean the model did not see a product name or a price.
public sealed record CreativeImageReading(string? ProductName, decimal? Price);
