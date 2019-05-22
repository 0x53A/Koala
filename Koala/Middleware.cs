using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Koala
{
    public class KoalaMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly HttpHandler _handler;
        private readonly ILoggerFactory _loggerFactory;

        public KoalaMiddleware(RequestDelegate next, HttpHandler handler, ILoggerFactory loggerFactory)
        {
            _next = next;
            _handler = handler;
            _loggerFactory = loggerFactory;
        }

        public async Task Invoke(HttpContext ctx)
        {
            string GetRequestInfo()
            {
                return $"{ctx.Request.Protocol} {ctx.Request.Method} {ctx.Request.Path}";
            }

            var result = await _handler.Invoke(ctx);
            var logger = _loggerFactory.CreateLogger<KoalaMiddleware>();

            if (result)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug($"Koala returned 'true' for {GetRequestInfo()}");
                return;
            }
            else
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug($"Koala returned 'false' for {GetRequestInfo()}");
                await _next.Invoke(ctx);
            }
        }
    }

    public static class KoalaApplicationBuilder
    {
        public static IApplicationBuilder AddKoala(this IApplicationBuilder self, HttpHandler handler)
        {
            self.UseMiddleware<KoalaMiddleware>(handler);
            return self;
        }
    }
}
