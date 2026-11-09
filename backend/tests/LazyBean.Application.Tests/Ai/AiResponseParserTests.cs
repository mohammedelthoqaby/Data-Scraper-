using LazyBean.Application.Ai.Dtos;
using LazyBean.Infrastructure.Ai;
using Xunit;

namespace LazyBean.Application.Tests.Ai;

public class AiResponseParserTests
{

    /// <summary>Extracts the model text from a Gemini-shaped envelope; raw text is returned unchanged.</summary>
    private static string ModelText(string envelope)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(envelope);
            return doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
        }
        catch (System.Text.Json.JsonException)
        {
            return envelope;
        }
    }
    [Fact]
    public void ParseTriageResults_StandardGeminiCandidatesShape_ExtractsResultsCorrectly()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var batch = new List<AiReviewTriagePairRequest>
        {
            new(id1, "Lavazza Espresso Qualita Oro 250g", "Lavazza Qualita Oro Coffee Beans 250g", "Lavazza", "Lavazza", 250, 250),
            new(id2, "Davidoff Rich Aroma 100g", "Davidoff Fine Aroma 100g", "Davidoff", "Davidoff", 100, 100)
        };

        var responseJson = $$"""
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "text": "[{\"reviewId\": \"{{id1}}\", \"verdict\": \"same\", \"confidence\": 0.95, \"reason\": \"Identical Lavazza Oro 250g\"}, {\"reviewId\": \"{{id2}}\", \"verdict\": \"different\", \"confidence\": 0.90, \"reason\": \"Rich Aroma vs Fine Aroma\"}]"
                  }
                ],
                "role": "model"
              },
              "finishReason": "STOP"
            }
          ]
        }
        """;

        var results = AiResponseParser.ParseTriageResults(ModelText(responseJson), batch);

        Assert.Equal(2, results.Count);
        Assert.Equal(id1, results[0].ReviewId);
        Assert.Equal("same", results[0].Verdict);
        Assert.Equal(0.95m, results[0].Confidence);
        Assert.Equal("Identical Lavazza Oro 250g", results[0].Reason);

        Assert.Equal(id2, results[1].ReviewId);
        Assert.Equal("different", results[1].Verdict);
        Assert.Equal(0.90m, results[1].Confidence);
    }

    [Fact]
    public void ParseTriageResults_MarkdownCodeFences_ExtractsResults()
    {
        var id = Guid.NewGuid();
        var batch = new List<AiReviewTriagePairRequest>
        {
            new(id, "Caffè Milano Platinum 250g", "Caffè Milano Platinum Whole Beans 250g", "Caffè Milano", "Caffè Milano", 250, 250)
        };

        var responseJson = $$"""
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "text": "```json\n[\n  {\n    \"reviewId\": \"{{id}}\",\n    \"verdict\": \"same\",\n    \"confidence\": \"0.98\",\n    \"reason\": \"Exact same brand, line and weight\"\n  }\n]\n```"
                  }
                ]
              }
            }
          ]
        }
        """;

        var results = AiResponseParser.ParseTriageResults(ModelText(responseJson), batch);

        Assert.Single(results);
        Assert.Equal(id, results[0].ReviewId);
        Assert.Equal("same", results[0].Verdict);
        Assert.Equal(0.98m, results[0].Confidence);
    }

    [Fact]
    public void ParseTriageResults_WrapperObjectWithReviewsProperty_ExtractsResults()
    {
        var id = Guid.NewGuid();
        var batch = new List<AiReviewTriagePairRequest>
        {
            new(id, "Illy Classico Roast 250g", "Illy Intenso Roast 250g", "Illy", "Illy", 250, 250)
        };

        var responseJson = $$"""
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "text": "{\n  \"reviews\": [\n    {\n      \"id\": \"{{id}}\",\n      \"verdict\": \"different\",\n      \"confidence\": 0.85,\n      \"reason\": \"Different roasts (Classico vs Intenso)\"\n    }\n  ]\n}"
                  }
                ]
              }
            }
          ]
        }
        """;

        var results = AiResponseParser.ParseTriageResults(ModelText(responseJson), batch);

        Assert.Single(results);
        Assert.Equal(id, results[0].ReviewId);
        Assert.Equal("different", results[0].Verdict);
        Assert.Equal(0.85m, results[0].Confidence);
    }

    [Fact]
    public void ParseTriageResults_MissingReviewId_FallsBackToBatchIndex()
    {
        var id1 = Guid.NewGuid();
        var batch = new List<AiReviewTriagePairRequest>
        {
            new(id1, "Starbucks Pike Place 200g", "Starbucks Pike Place 200g", "Starbucks", "Starbucks", 200, 200)
        };

        var responseJson = """
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "text": "[{\"verdict\": \"same\", \"confidence\": 0.99, \"reason\": \"Exact match without id\"}]"
                  }
                ]
              }
            }
          ]
        }
        """;

        var results = AiResponseParser.ParseTriageResults(ModelText(responseJson), batch);

        Assert.Single(results);
        Assert.Equal(id1, results[0].ReviewId);
        Assert.Equal("same", results[0].Verdict);
    }

    [Fact]
    public void ParseTriageResults_MalformedResponse_CreatesSafeFallbackUnsure()
    {
        var id = Guid.NewGuid();
        var batch = new List<AiReviewTriagePairRequest>
        {
            new(id, "Item A", "Item B", "Brand", "Brand", null, null)
        };

        var responseJson = "Internal server error 500";

        var results = AiResponseParser.ParseTriageResults(ModelText(responseJson), batch);

        Assert.Single(results);
        Assert.Equal(id, results[0].ReviewId);
        Assert.Equal("unsure", results[0].Verdict);
        Assert.Equal(0.5m, results[0].Confidence);
    }
}
