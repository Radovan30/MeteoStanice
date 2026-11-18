using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MeteoStanice.Api.Security;

/// <summary>
/// Protects administrative endpoints (for example the SPS30 correction) with a key that is
/// separate from the station upload key, because the station key is stored readable in every station.
/// Without a configured <c>AdminApi:ApiKey</c> the endpoint is disabled (HTTP 403).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdminApiKeyAttribute : Attribute, IAuthorizationFilter
{
    private const string HeaderName = "X-Admin-Key";

    /// <inheritdoc />
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        IConfiguration configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        string? configuredKey = configuration["AdminApi:ApiKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            context.Result = new ObjectResult(new { message = "Administrative API is disabled: AdminApi:ApiKey is not configured." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var submittedKey)
            || submittedKey.Count == 0
            || !string.Equals(submittedKey[0], configuredKey, StringComparison.Ordinal))
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Invalid or missing administrator key." });
        }
    }
}
