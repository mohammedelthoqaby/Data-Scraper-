using System.Text.Json.Serialization;
using LazyBean.Domain.Enums;

namespace LazyBean.Application.Collection.Classification;

public sealed class ClassificationRulesConfigFile
{
    [JsonPropertyName("rules")]
    public List<ClassificationRuleDefinition> Rules { get; set; } = [];
}

public sealed class ClassificationRuleDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CanonicalCategory Category { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("confidence")]
    public decimal Confidence { get; set; } = 0.9m;

    [JsonPropertyName("titlePatterns")]
    public List<string> TitlePatterns { get; set; } = [];

    [JsonPropertyName("negativeTitlePatterns")]
    public List<string> NegativeTitlePatterns { get; set; } = [];

    [JsonPropertyName("productTypePatterns")]
    public List<string> ProductTypePatterns { get; set; } = [];

    [JsonPropertyName("tagPatterns")]
    public List<string> TagPatterns { get; set; } = [];

    [JsonPropertyName("vendorPatterns")]
    public List<string> VendorPatterns { get; set; } = [];
}
