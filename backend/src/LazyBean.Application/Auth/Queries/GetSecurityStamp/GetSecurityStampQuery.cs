using LazyBean.Domain.Interfaces;
using MediatR;

namespace LazyBean.Application.Auth.Queries.GetSecurityStamp;

// The stamp a new dashboard session is issued with. Null when the user no longer exists.
public record GetSecurityStampQuery(Guid UserId) : IRequest<string?>;

public class GetSecurityStampQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetSecurityStampQuery, string?>
{
    public async Task<string?> Handle(GetSecurityStampQuery request, CancellationToken ct) =>
        (await unitOfWork.Users.GetByIdAsync(request.UserId, ct))?.SecurityStamp;
}
