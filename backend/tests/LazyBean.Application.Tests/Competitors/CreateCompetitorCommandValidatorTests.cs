using LazyBean.Application.Competitors.Commands.CreateCompetitor;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Interfaces;
using Moq;
using Xunit;

namespace LazyBean.Application.Tests.Competitors;

public class CreateCompetitorCommandValidatorTests
{
    private readonly Mock<ICompetitorRepository> _competitors = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly CreateCompetitorCommandValidator _validator;

    public CreateCompetitorCommandValidatorTests()
    {
        _unitOfWork.Setup(u => u.Competitors).Returns(_competitors.Object);
        _validator = new CreateCompetitorCommandValidator(_unitOfWork.Object);
    }

    [Fact]
    public async Task Validate_WellFormedCommand_WithUniqueKey_Passes()
    {
        _competitors.Setup(r => r.KeyExistsAsync("coffee-zone-misr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var command = new CreateCompetitorCommand(
            "coffee-zone-misr", "Coffee Zone Misr", "https://coffeezonemisr.com",
            CompetitorPlatform.Shopify, IsReference: false);

        var result = await _validator.ValidateAsync(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validate_DuplicateKey_FailsWithKeyError()
    {
        _competitors.Setup(r => r.KeyExistsAsync("strong-coffee", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var command = new CreateCompetitorCommand(
            "strong-coffee", "Strong Coffee", "https://strongcoffee.com",
            CompetitorPlatform.WooCommerce, IsReference: false);

        var result = await _validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompetitorCommand.Key));
    }

    [Theory]
    [InlineData("Has-Upper-Case")]
    [InlineData("has spaces")]
    [InlineData("")]
    public async Task Validate_MalformedKey_Fails(string key)
    {
        _competitors.Setup(r => r.KeyExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var command = new CreateCompetitorCommand(
            key, "Some Name", "https://example.com", CompetitorPlatform.Custom, IsReference: false);

        var result = await _validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompetitorCommand.Key));
    }

    [Fact]
    public async Task Validate_NonAbsoluteBaseUrl_Fails()
    {
        _competitors.Setup(r => r.KeyExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var command = new CreateCompetitorCommand(
            "cafelax", "Cafelax", "not-a-url", CompetitorPlatform.Shopify, IsReference: false);

        var result = await _validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompetitorCommand.BaseUrl));
    }
}
