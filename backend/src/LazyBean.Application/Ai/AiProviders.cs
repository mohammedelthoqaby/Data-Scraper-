namespace LazyBean.Application.Ai;

/// <summary>
/// Catalog of supported AI providers. Groq is the primary provider by default,
/// Gemini is the fallback. Model names are defaults only: the model is editable
/// text per provider, so no deprecated name has to be hard-coded.
/// </summary>
public static class AiProviders
{
    public const string Groq = "groq";
    public const string Gemini = "gemini";

    public static IReadOnlyList<string> All { get; } = [Groq, Gemini];

    public static bool IsKnown(string? provider) =>
        provider is not null && All.Contains(Normalize(provider));

    public static string Normalize(string provider) => provider.Trim().ToLowerInvariant();

    public static string Label(string provider) =>
        Normalize(provider) == Groq ? "Groq" : "Google Gemini";

    public static string DefaultModel(string provider) =>
        Normalize(provider) == Groq ? "openai/gpt-oss-120b" : "gemini-3.5-flash-lite";

    public static IReadOnlyList<string> SuggestedModels(string provider) =>
        Normalize(provider) == Groq
            ? ["openai/gpt-oss-120b", "qwen/qwen3.8-27b", "openai/gpt-oss-20b"]
            : ["gemini-3.5-flash-lite", "gemini-3.5-flash"];
}
