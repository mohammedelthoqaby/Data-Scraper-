using FluentValidation.Results;
using LazyBean.Application.Collector.Commands.RevokeCollectorDevice;
using LazyBean.Application.Common.Exceptions;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Exceptions;
using LazyBean.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LazyBean.Application.Auth.Commands.ChangePassword;

public sealed class ChangePasswordCommandHandler(
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ISender sender,
    TimeProvider clock,
    ILogger<ChangePasswordCommandHandler> logger)
    : IRequestHandler<ChangePasswordCommand, ChangePasswordResult>
{
    public async Task<ChangePasswordResult> Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        var user = await unitOfWork.Users.GetByIdAsync(request.UserId, ct)
            ?? throw new InvalidCredentialsException();

        // Logged without any password value.
        if (!passwordHasher.Verify(user.PasswordHash, request.CurrentPassword))
        {
            logger.LogWarning("Password change refused for user {UserId}: the current password did not match.", user.Id);
            throw new ValidationException(new[]
            {
                new ValidationFailure(nameof(ChangePasswordCommand.CurrentPassword), "Current password is not correct."),
            });
        }

        var now = clock.GetUtcNow();
        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.PasswordUpdatedAt = now;
        user.SecurityStamp = User.NewSecurityStamp();
        unitOfWork.Users.Update(user);
        await unitOfWork.SaveChangesAsync(ct);

        var devicesRevoked = 0;
        if (request.RevokeDevices)
        {
            // Same path as revoking one device: a job the device holds goes back to the queue.
            var activeDevices = await unitOfWork.CollectorDevices.FindAsync(d => d.RevokedAt == null, ct);
            foreach (var device in activeDevices)
            {
                await sender.Send(new RevokeCollectorDeviceCommand(device.Id), ct);
                devicesRevoked++;
            }
        }

        logger.LogInformation(
            "Dashboard password changed for user {UserId}. Other browser sessions signed out. Collector devices signed out: {DevicesRevoked}.",
            user.Id,
            devicesRevoked);

        return new ChangePasswordResult(user.SecurityStamp, now, devicesRevoked);
    }
}
