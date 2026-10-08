using Microsoft.Extensions.Options;

namespace MeshMuster.Config;

/// <summary>
/// Runs AppOptions.Validate through the options pipeline.
/// </summary>
public class ValidateAppOptions : IValidateOptions<AppOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AppOptions options)
    {
        var errors = options.Validate();
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
