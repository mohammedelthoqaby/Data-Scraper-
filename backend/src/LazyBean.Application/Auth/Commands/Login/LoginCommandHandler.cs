using LazyBean.Application.Auth.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Exceptions;
using LazyBean.Domain.Interfaces;
using MediatR;

namespace LazyBean.Application.Auth.Commands.Login;

// Credential verification only - issuing the auth cookie needs HttpContext, which
// stays in the API layer (AuthController) to keep Application free of ASP.NET Core.
public class LoginCommandHandler(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher)
    : IRequestHandler<LoginCommand, AuthenticatedUserDto>
{
    public async Task<AuthenticatedUserDto> Handle(LoginCommand request, CancellationToken ct)
    {
        var user = await unitOfWork.Users.GetByUsernameAsync(request.Username, ct);

        if (user is null || !passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            throw new InvalidCredentialsException(request.Username);
        }

        return new AuthenticatedUserDto(user.Id, user.Username);
    }
}
