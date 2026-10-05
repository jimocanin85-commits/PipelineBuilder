namespace PipelineBuilder.Web.Security;

/// <summary>
/// Headers that tell the browser what this site may do. They cost nothing, and they limit the damage
/// if a mistake elsewhere ever lets foreign content into a page.
/// </summary>
public static class SecurityHeaders
{
    /// <summary>
    /// What the pages may load and run: only files from this site.
    /// <list type="bullet">
    /// <item><c>script-src 'self'</c>: no script written into a page runs, so injected markup cannot run code.
    /// The app's own scripts are therefore files in <c>wwwroot/js</c>.</item>
    /// <item><c>style-src</c> also allows inline styles: Blazor draws its "reconnecting" box with them.</item>
    /// <item><c>frame-ancestors 'none'</c>: no other site can show the wizard inside its own page and
    /// trick a signed-in user into clicking in it.</item>
    /// </list>
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    /// <summary>Adds the headers to every response, also errors and static files.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff"; // never guess a file's type
            headers.XFrameOptions = "DENY";          // for browsers that do not know frame-ancestors
            headers["Referrer-Policy"] = "no-referrer";
            await next(context);
        });
}
