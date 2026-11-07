using LazyBean.Domain.Common;

namespace LazyBean.Domain.Entities;

// One stored image of an ad. Position 0 is the card image; a carousel can have up to five. The image is a JPEG at most 480 px
// wide, kept in the database. Only the latest image per position is kept, and the rows go when the ad has not been seen for
// the retention period.
public class AdThumbnail : BaseEntity, IAggregateRoot
{
    public Guid AdId { get; set; }
    public int Position { get; set; }
    public byte[] Data { get; set; } = [];
    public string ContentType { get; set; } = "image/jpeg";
    public int Width { get; set; }
    public int Height { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}
