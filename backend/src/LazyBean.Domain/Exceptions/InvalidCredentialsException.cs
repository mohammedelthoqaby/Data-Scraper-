namespace LazyBean.Domain.Exceptions;

public class InvalidCredentialsException : Exception
{
    public string? Username { get; }

    public InvalidCredentialsException(string? username = null)
        : base("Invalid username or password.")
    {
        Username = username;
    }
}
