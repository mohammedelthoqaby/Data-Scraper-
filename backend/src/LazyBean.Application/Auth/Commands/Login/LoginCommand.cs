using LazyBean.Application.Auth.Dtos;
using MediatR;

namespace LazyBean.Application.Auth.Commands.Login;

public record LoginCommand(string Username, string Password) : IRequest<AuthenticatedUserDto>;
