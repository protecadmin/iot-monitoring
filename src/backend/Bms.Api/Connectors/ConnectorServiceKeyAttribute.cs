using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Bms.Api.Connectors;

/// <summary>
/// Lets only the Connector Service in: it presents the shared key in
/// X-Connector-Service-Key. Not a user login — the service acts for every
/// company, so no user's tenant scope fits it.
/// </summary>
public sealed class ConnectorServiceKeyAttribute : Attribute, IAuthorizationFilter
{
    public const string HeaderName = "X-Connector-Service-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<ConnectorOptions>>().Value;
        if (string.IsNullOrEmpty(options.ServiceApiKey))
        {
            context.Result = new ObjectResult(new { success = false, message = "Connector Service key is not configured", errorCode = "CONNECTOR_SERVICE_DISABLED" }) { StatusCode = 503 };
            return;
        }

        var presented = context.HttpContext.Request.Headers[HeaderName].ToString();
        var expected = Encoding.UTF8.GetBytes(options.ServiceApiKey);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), expected))
            context.Result = new UnauthorizedResult();
    }
}
