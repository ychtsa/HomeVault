/*
 * FILE: SecurityHeadersMiddleware.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-05-01
 * DESCRIPTION: Sets a baseline of security-related HTTP response headers on
 *              every response. Mitigates clickjacking, MIME-sniffing, leaky
 *              referrers, unwanted browser features, and most XSS vectors.
 */

namespace HomeVault.Middleware
{
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;

        /*
         * Function: SecurityHeadersMiddleware(RequestDelegate next)
         * Description: Constructor. Captures the next delegate in the pipeline.
         * Parameter: RequestDelegate next - the next middleware to invoke.
         * Return: none (constructor).
         */
        public SecurityHeadersMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        /*
         * Function: InvokeAsync(HttpContext context)
         * Description: Stamps a fixed set of security headers onto the response
         *              before passing the request along to the next middleware.
         * Parameter: HttpContext context - the current request context.
         * Return: Task - completes when the downstream pipeline completes.
         */
        public async Task InvokeAsync(HttpContext context)
        {
            IHeaderDictionary headers = context.Response.Headers;

            // Block MIME-type sniffing by browsers.
            headers["X-Content-Type-Options"] = "nosniff";

            // Disallow framing the site to prevent clickjacking.
            headers["X-Frame-Options"] = "DENY";

            // Send the origin only on cross-origin requests; full URL on same-origin.
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Disable powerful browser APIs the app does not use.
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            // Same-origin-only resource loading. The Razor views ship inline
            // styles via Bootstrap classes only (no inline <style>/<script>),
            // so 'self' is enough.
            headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self'; " +
                "style-src 'self'; " +
                "img-src 'self' data:; " +
                "font-src 'self'; " +
                "connect-src 'self'; " +
                "frame-ancestors 'none'; " +
                "base-uri 'self'; " +
                "form-action 'self'";

            await _next(context);
        }
    }
}
