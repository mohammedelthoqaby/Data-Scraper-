using LazyBean.Application.Auth.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Exceptions;
using LazyBean.Domain.Interfaces;
using MediatR;

namespace LazyBean.Application.Auth.Queries.GetAccount;

public record GetAccountQuery(Guid UserId) : IRequest<AccountDto>;

public class GetAccountQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetAccountQuery, AccountDto>
{
    public async Task<AccountDto> Handle(GetAccountQuery request, CancellationToken ct)
    {
        var user = await unitOfWork.Users.GetByIdAsync(request.UserId, ct)
            ?? throw new InvalidCredentialsException();

        // PasswordChangedAt is null while the password is still the one set during setup.
        return new AccountDto(user.Id, user.Username, user.PasswordUpdatedAt);
    }
}
