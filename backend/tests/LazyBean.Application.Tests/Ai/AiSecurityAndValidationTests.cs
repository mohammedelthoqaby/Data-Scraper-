using LazyBean.Application.Ai;
using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LazyBean.Application.Tests.Ai;

public class AiSecurityAndValidationTests
{
    private readonly IAiKeyProtector _protector;

    public AiSecurityAndValidationTests()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IDataProtectionProvider>();
        _protector = new AiKeyProtector(provider);
    }

    [Fact]
    public void Test1_EncryptionRoundTrip_EncryptsAndDecryptsSuccessfully()
    {
        // 1. Encryption round trip
        const string rawKey = "test-key-0000";

        var encrypted = _protector.EncryptKey(rawKey);

        Assert.NotNull(encrypted);
        Assert.NotEmpty(encrypted);
        Assert.NotEqual(rawKey, encrypted); // Must be encrypted / ciphertext

        var decrypted = _protector.DecryptKey(encrypted);
        Assert.Equal(rawKey, decrypted); // Round-trip restores original exactly
    }

    [Fact]
    public void Test2_KeyNeverInResponses_MaskKeyOnlyExposesLastFourCharacters()
    {
        // 2. Key never in responses
        const string rawKey = "test-key-0000";
        var masked = _protector.MaskKey(rawKey);

        Assert.NotNull(masked);
        Assert.DoesNotContain("test-key", masked);
        Assert.EndsWith("0000", masked);
        Assert.Equal("test••••0000", masked); // first four characters, bullets, last four

        // Groq-style keys show their gsk_ prefix, matching the required "gsk_••••1234" display.
        Assert.Equal("gsk_••••1234", _protector.MaskKey("gsk_abcdefghijk1234"));

        // Edge case: empty/null
        Assert.Null(_protector.MaskKey(null));
        Assert.Null(_protector.MaskKey("   "));
    }

    [Fact]
    public void Test3_NumberValidation_AcceptsFactsWithExactNumbers()
    {
        // 3. Number validation
        var fact = new AttentionFactItem(
            Id: "fact_1",
            Type: "price_gap",
            ProductTitle: "Lavazza Oro Pods 15",
            Brand: "Lavazza",
            CompetitorName: "Strong Coffee",
            LazyBeanPrice: 375.00m,
            CompetitorPrice: 330.00m,
            Difference: 45.00m,
            DiscountPercent: null,
            AdCount: null,
            ProductUrl: "https://example.com",
            Numbers: [375.00m, 330.00m, 45.00m],
            FallbackHeadline: "Lazy Bean is 45.00 EGP more expensive",
            FallbackExplanation: "Lazy Bean is 375.00 EGP while Strong Coffee is 330.00 EGP",
            FallbackHeadlineAr: "سعر ليزي بين أعلى بمقدار 45.00 ج.م",
            FallbackExplanationAr: "سعر ليزي بين 375.00 ج.م والمنافس 330.00 ج.م");

        var validEnHeadline = "Price gap of 45.00 EGP on Lavazza Oro";
        var validArHeadline = "فارق سعر قدره 45 ج.م على لافازا";
        var validEnExpl = "Strong Coffee offers this item for 330.00 EGP vs 375.00 EGP at Lazy Bean.";
        var validArExpl = "يقدم سترونج كوفي هذا المنتج بسعر 330 ج.م مقارنة بـ 375 ج.م لدى ليزي بين.";

        var isValid = AiFactNumberValidator.ValidateNumbers(fact, validEnHeadline, validArHeadline, validEnExpl, validArExpl);
        Assert.True(isValid);
    }

    [Fact]
    public void Test4_NumberValidation_RejectsHallucinatedNumbers()
    {
        var fact = new AttentionFactItem(
            Id: "fact_1",
            Type: "price_gap",
            ProductTitle: "Lavazza Oro Pods 15",
            Brand: "Lavazza",
            CompetitorName: "Strong Coffee",
            LazyBeanPrice: 375.00m,
            CompetitorPrice: 330.00m,
            Difference: 45.00m,
            DiscountPercent: null,
            AdCount: null,
            ProductUrl: "https://example.com",
            Numbers: [375.00m, 330.00m, 45.00m],
            FallbackHeadline: "Lazy Bean is 45.00 EGP more expensive",
            FallbackExplanation: "Lazy Bean is 375.00 EGP while Strong Coffee is 330.00 EGP",
            FallbackHeadlineAr: "سعر ليزي بين أعلى بمقدار 45.00 ج.م",
            FallbackExplanationAr: "سعر ليزي بين 375.00 ج.م والمنافس 330.00 ج.م");

        // Model hallucinated 120.00 EGP or 50% discount not in fact
        var hallucinatedEn = "Strong Coffee dropped the price by 120.00 EGP to 330.00 EGP.";
        var isValid = AiFactNumberValidator.ValidateNumbers(fact, hallucinatedEn, "", "", "");

        Assert.False(isValid); // Must fail validation and trigger fallback
    }

    [Fact]
    public void Test5_NumberValidation_HandlesArabicIndicDigits()
    {
        var fact = new AttentionFactItem(
            Id: "fact_2",
            Type: "competitor_discount",
            ProductTitle: "Illy Ground 250g",
            Brand: "Illy",
            CompetitorName: "Fengany",
            LazyBeanPrice: 400.00m,
            CompetitorPrice: 350.00m,
            Difference: 50.00m,
            DiscountPercent: 12.00m,
            AdCount: null,
            ProductUrl: "https://example.com",
            Numbers: [400.00m, 350.00m, 50.00m, 12.00m],
            FallbackHeadline: "Discount",
            FallbackExplanation: "Explanation",
            FallbackHeadlineAr: "خصم",
            FallbackExplanationAr: "توضيح");

        // Arabic Indic numbers: ١٢% (12%) and ٣٥٠ (350)
        var arHeadline = "خصم ١٢% على قهوة إيلي";
        var arExpl = "السعر الحالي ٣٥٠ ج.م بدلاً من ٤٠٠ ج.م";

        var isValid = AiFactNumberValidator.ValidateNumbers(fact, "", arHeadline, "", arExpl);
        Assert.True(isValid);

        // Hallucinated Arabic Indic: ٩٩% (99%)
        var fakeAr = "خصم ٩٩% غير حقيقي";
        var isFakeValid = AiFactNumberValidator.ValidateNumbers(fact, "", fakeAr, "", "");
        Assert.False(isFakeValid);
    }
}
