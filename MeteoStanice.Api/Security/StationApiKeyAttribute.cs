using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MeteoStanice.Api.Security;

/// <summary>
/// Requires the station upload API key when one is configured.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class StationApiKeyAttribute : Attribute, IAuthorizationFilter
{
    private const string HeaderName = "X-Api-Key";

    /// <inheritdoc />
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        IConfiguration configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        string? configuredApiKey = configuration["StationApi:ApiKey"];
        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var submittedApiKey)
            || submittedApiKey.Count == 0
            || !string.Equals(submittedApiKey[0], configuredApiKey, StringComparison.Ordinal))
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Invalid or missing station API key." });
        }
    }
}
