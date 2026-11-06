using LazyBean.Application.Collection.Orchestration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LazyBean.Infrastructure.Collection.Orchestration;

// Runs at startup (ValidateOnStart) and logs which GitHub settings the process received,
// as booleans and non-secret names only. It never fails validation: a missing token is
// reported when a dispatch is attempted, not by taking the whole site down.
public class GitHubOptionsValidator(ILogger<GitHubOptionsValidator> logger) : IValidateOptions<GitHubOptions>
{
    public ValidateOptionsResult Validate(string? name, GitHubOptions options)
    {
        logger.LogInformation(
            "GitHub config received: dispatchTokenPresent={DispatchTokenPresent}, repo={Repo}, ref={Ref}, workflowFile={WorkflowFile}",
            !string.IsNullOrWhiteSpace(options.DispatchToken),
            options.Repo,
            options.Ref,
            options.WorkflowFile);

        return ValidateOptionsResult.Success;
    }
}
