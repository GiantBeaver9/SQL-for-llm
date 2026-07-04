using System.Text;

namespace EOQuoter.Web;

/// <summary>
/// Gate for the internal viewer. This app deploys inside the network, never publicly; basic auth
/// against configured credentials is deliberate scope control — an identity provider would be
/// infrastructure for its own sake in a viewer whose point is the audit and ops views behind it.
/// </summary>
public class BasicAuthMiddleware(RequestDelegate next, IConfiguration config)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var expectedUser = config["Viewer:User"] ?? "ops";
        var expectedPassword = config["Viewer:Password"] ?? "change-me";

        if (context.Request.Headers.Authorization.ToString() is var header
            && header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
            var separator = decoded.IndexOf(':');
            if (separator > 0
                && decoded[..separator] == expectedUser
                && decoded[(separator + 1)..] == expectedPassword)
            {
                await next(context);
                return;
            }
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"EOQuoter internal viewer\"";
    }
}
