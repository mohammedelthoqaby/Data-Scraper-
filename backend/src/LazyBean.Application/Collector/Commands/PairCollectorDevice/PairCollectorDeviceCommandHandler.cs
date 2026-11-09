using LazyBean.Application.Collector.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Exceptions;
using LazyBean.Domain.Interfaces;
using MediatR;

namespace LazyBean.Application.Collector.Commands.PairCollectorDevice;

// Pairing signs in with the dashboard username and password, then issues a device token. The
// raw token is returned here and never stored; only its hash is persisted.
public class PairCollectorDeviceCommandHandler(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher)
    : IRequestHandler<PairCollectorDeviceCommand, PairCollectorDeviceResultDto>
{
    public async Task<PairCollectorDeviceResultDto> Handle(PairCollectorDeviceCommand request, CancellationToken ct)
    {
        var user = await unitOfWork.Users.GetByUsernameAsync(request.Username, ct);
        if (user is null || !passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            throw new InvalidCredentialsException(request.Username);
        }

        var token = CollectorTokens.Generate();
        var device = new CollectorDevice
        {
            Name = request.DeviceName.Trim(),
            TokenHash = CollectorTokens.Hash(token),
            Scopes = $"{CollectorDevice.AdsIngestScope} {CollectorDevice.StoreIngestScope}",
            PairedByUsername = user.Username,
        };

        await unitOfWork.CollectorDevices.AddAsync(device, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return new PairCollectorDeviceResultDto(device.Id, device.Name, token, device.ScopeList);
    }
}
