using LazyBean.Application.Matching.AiMatching;
using Xunit;

namespace LazyBean.Application.Tests.Matching;

/// <summary>Strict parsing of the matching reply: malformed output is never stored.</summary>
public class AiMatchParserTests
{
    [Fact]
    public void Parse_NotJson_ReturnsNull()
    {
        Assert.Null(AiMatchResponseParser.Parse("I think these are the same product.", 1));
    }

    [Fact]
    public void Parse_EmptyOrNull_ReturnsNull()
    {
        Assert.Null(AiMatchResponseParser.Parse(null, 1));
        Assert.Null(AiMatchResponseParser.Parse("   ", 1));
    }

    [Fact]
    public void Parse_ResultsNotArray_ReturnsNull()
    {
        Assert.Null(AiMatchResponseParser.Parse("{\"results\":\"same\"}", 1));
    }

    [Fact]
    public void Parse_OnlyInvalidEntries_ReturnsNull()
    {
        var reply = "{\"results\":[{\"pair\":1,\"verdict\":\"maybe\",\"confidence\":0.9},{\"pair\":2,\"verdict\":\"same\",\"confidence\":1.5}]}";

        Assert.Null(AiMatchResponseParser.Parse(reply, 2));
    }

    [Fact]
    public void Parse_ValidEntry_ReadsVerdictConfidenceAndReason()
    {
        var reply = "{\"results\":[{\"pair\":1,\"verdict\":\"same\",\"confidence\":0.93,\"reason\":\"same line and size\"}]}";

        var judgements = AiMatchResponseParser.Parse(reply, 1);

        Assert.NotNull(judgements);
        Assert.Equal("same", judgements![1].Verdict);
        Assert.Equal(0.93m, judgements[1].Confidence);
        Assert.Equal("same line and size", judgements[1].Reason);
    }

    [Fact]
    public void Parse_KeepsValidEntriesAndDropsBadOnes()
    {
        var reply = "{\"results\":[{\"pair\":1,\"verdict\":\"different\",\"confidence\":0.8,\"reason\":\"different size 250g vs 1kg\"},{\"pair\":9,\"verdict\":\"same\",\"confidence\":0.9}]}";

        var judgements = AiMatchResponseParser.Parse(reply, 2);

        Assert.NotNull(judgements);
        Assert.Single(judgements!);
        Assert.Equal("different", judgements[1].Verdict);
    }

    [Fact]
    public void Parse_AcceptsCodeFencesAndStringConfidence()
    {
        var reply = "```json\n{\"results\":[{\"pair\":\"1\",\"verdict\":\"Same\",\"confidence\":\"0.9\",\"reason\":\"ok\"}]}\n```";

        var judgements = AiMatchResponseParser.Parse(reply, 1);

        Assert.NotNull(judgements);
        Assert.Equal("same", judgements![1].Verdict);
        Assert.Equal(0.9m, judgements[1].Confidence);
    }

    [Fact]
    public void Parse_ReasonIsCappedAndControlCharactersRemoved()
    {
        var longReason = new string('x', 500);
        var reply = "{\"results\":[{\"pair\":1,\"verdict\":\"unsure\",\"confidence\":0.5,\"reason\":\"" + longReason + "\\u0007\"}]}";

        var judgements = AiMatchResponseParser.Parse(reply, 1);

        Assert.NotNull(judgements);
        Assert.True(judgements![1].Reason.Length <= 300);
        Assert.DoesNotContain('\u0007', judgements[1].Reason);
    }
}
