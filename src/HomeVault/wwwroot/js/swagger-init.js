window.addEventListener('load', function () {
    window.ui = SwaggerUIBundle({
        url: '/swagger/v1/swagger.json',
        dom_id: '#swagger-ui',
        presets: [SwaggerUIBundle.presets.apis, SwaggerUIStandalonePreset],
        layout: 'StandaloneLayout',
        validatorUrl: null,
        withCredentials: true,
        requestInterceptor: async function (request) {
            // Fetch a token for the current cookie identity (including after login).
            if (!['GET', 'HEAD', 'OPTIONS'].includes(request.method.toUpperCase())) {
                const response = await fetch('/api/auth/csrf', { credentials: 'same-origin' });
                if (!response.ok) throw new Error('Could not obtain a CSRF token.');
                request.headers['X-CSRF-TOKEN'] = (await response.json()).token;
            }
            return request;
        }
    });
});
