using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Competitors.Commands.CreateCompetitor;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Interfaces;
using Moq;
using Xunit;

namespace LazyBean.Application.Tests.Competitors;

public class CreateCompetitorCommandHandlerTests
{
    private readonly Mock<ICompetitorRepository> _competitors = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CreateCompetitorCommandHandler _handler;

    public CreateCompetitorCommandHandlerTests()
    {
        _unitOfWork.Setup(u => u.Competitors).Returns(_competitors.Object);
        _handler = new CreateCompetitorCommandHandler(_unitOfWork.Object, _cache.Object);
    }

    [Fact]
    public async Task Handle_PersistsCompetitor_AndReturnsMatchingDto()
    {
        var command = new CreateCompetitorCommand(
            "coffee-zone-misr", "Coffee Zone Misr", "https://coffeezonemisr.com",
            CompetitorPlatform.Shopify, IsReference: false);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.Equal(command.Key, result.Key);
        Assert.Equal(command.Name, result.Name);
        Assert.Equal(command.BaseUrl, result.BaseUrl);
        Assert.Equal(nameof(CompetitorPlatform.Shopify), result.Platform);
        Assert.True(result.Enabled);

        _competitors.Verify(
            r => r.AddAsync(It.Is<Competitor>(c => c.Key == command.Key), It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidatesTheCompetitorsListCache()
    {
        var command = new CreateCompetitorCommand(
            "strong-coffee", "Strong Coffee", "https://strongcoffee.com",
            CompetitorPlatform.WooCommerce, IsReference: false);

        await _handler.Handle(command, CancellationToken.None);

        _cache.Verify(c => c.RemoveByTagAsync("competitors", It.IsAny<CancellationToken>()), Times.Once);
    }
}
