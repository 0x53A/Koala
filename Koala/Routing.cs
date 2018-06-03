﻿using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using Microsoft.Net.Http.Headers;
using System.Linq;
using System.Security.Principal;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;

namespace Koala
{
    internal interface IKoalaModelBinder
    {
        Task<ValueOption<T>> TryBind<T>(HttpContext ctx);
    }

    public static class Routing
    {
        const string RouteKey = "koala_route";

        public static HttpHandler text(string s)
        {
            return HttpHandler.FromFunc(async ctx =>
            {
                ctx.Response.Headers[HeaderNames.ContentType] = "text/plain; charset=utf-8";
                var bytes = Encoding.UTF8.GetBytes(s);
                ctx.Response.Headers[HeaderNames.ContentLength] = bytes.Length.ToString();
                await ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length);
                return ValueOption.Some(ctx);
            });
        }

        public static HttpHandler json(string s)
        {
            return HttpHandler.FromFunc(async ctx =>
            {
                ctx.Response.Headers[HeaderNames.ContentType] = "application/json; charset=utf-8"; // note: the charset is theoretically a spec violation
                var bytes = Encoding.UTF8.GetBytes(s);
                ctx.Response.Headers[HeaderNames.ContentLength] = bytes.Length.ToString();
                await ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length);
                return ValueOption.Some(ctx);
            });
        }

        public static HttpHandler serialize_json(object o)
        {
            return HttpHandler.FromFunc(async ctx =>
            {
                // REVIEW: do we need error handling? What happens if an exception bubbles up to asp.net core?
                var s = Newtonsoft.Json.JsonConvert.SerializeObject(o);
                var bytes = Encoding.UTF8.GetBytes(s);
                ctx.Response.Headers[HeaderNames.ContentType] = "application/json; charset=utf-8"; // note: the charset is theoretically a spec violation
                ctx.Response.Headers[HeaderNames.ContentLength] = bytes.Length.ToString();
                await ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length);
                return ValueOption.Some(ctx);
            });
        }

        public static HttpHandler serialize_conneg(object o)
        {
            return HttpHandler.FromFunc(async ctx =>
            {
                var executor = ctx.RequestServices.GetRequiredService<IActionResultExecutor<ObjectResult>>();
                var routeCtx = new RouteContext(ctx);
                var actx = new ActionContext(ctx, routeCtx.RouteData, new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
                await executor.ExecuteAsync(actx, new ObjectResult(o));
                return ValueOption.Some(ctx);
            });
        }

        public static PartialHttpHandler GET() =>
            PartialHttpHandler.FromFunc(async (next, ctx) =>
            {
                if (ctx.Request.Method == HttpMethods.Get)
                    return await next.Invoke(ctx);
                return await Task.FromResult(ValueOption<HttpContext>.None);
            });

        public static HttpHandler GET(HttpHandler next)
        {
            return HttpHandler.FromFunc(async ctx =>
            {
                if (ctx.Request.Method == HttpMethods.Get)
                    return await next.Invoke(ctx);
                return await Task.FromResult(ValueOption<HttpContext>.None);
            });
        }
        public static HttpHandler POST(HttpHandler next)
        {
            return HttpHandler.FromFunc(async ctx =>
            {
                if (ctx.Request.Method == HttpMethods.Post)
                    return await next.Invoke(ctx);
                return await Task.FromResult(ValueOption<HttpContext>.None);
            });
        }
        public static HttpHandler choose(IEnumerable<HttpHandler> handlers)
        {
            return HttpHandler.FromFunc(async ctx =>
            {
                foreach (var h in handlers)
                {
                    var result = await h.Invoke(ctx);
                    if (result.IsSome)
                        return ValueOption.Some(result.Value);
                }
                return ValueOption.None;
            });
        }
        public static HttpHandler choose(params HttpHandler[] handlers)
        {
            return choose(handlers: handlers as IEnumerable<HttpHandler>);
        }

        private static ValueOption<string> GetSavedSubPath(HttpContext ctx)
        {
            if (ctx.Items.TryGetValue(RouteKey, out var val) &&
                val is string s &&
                !string.IsNullOrWhiteSpace(s) &&
                ctx.Request.Path.Value.StartsWith(s))
            {
                return ValueOption.Some(s);
            }
            return ValueOption.None;
        }

        private static string GetPath(HttpContext ctx)
        {
            return GetSavedSubPath(ctx)
                    .Match(
                        s => ctx.Request.Path.Value.Substring(s.Length),
                        () => ctx.Request.Path.Value);
        }

        public static PartialHttpHandler route(string path)
        {
            return PartialHttpHandler.FromFunc(async (next, ctx) =>
            {
                var currentPath = GetPath(ctx);
                if (currentPath == path)
                    return await next.Invoke(ctx);
                return ValueOption.None;
            });
        }

        public static HttpHandler route(string path, HttpHandler next)
        {
            return route(path).WithNext(next);
        }


        private static (string user, string pw)? ReadBasicAuth(HttpContext ctx)
        {
            var authHeaders = ctx.Request.Headers[HeaderNames.Authorization];

            var basic = authHeaders.Where(x => x.StartsWith("Basic ")).ToArray();
            if (basic.Length == 0)
                return null;
            if (basic.Length > 1)
            {
                // uhh, do we want to error? Just take the first ...
            }

            var base64 = basic.First().Substring("Basic ".Length);
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var iColon = decoded.IndexOf(':');
            var user = decoded.Substring(0, iColon);
            var pw = decoded.Substring(iColon + 1);
            return (user, pw);
        }

        public static PartialHttpHandler basicAuth(Func<(string user, string pw), bool> auth)
        {
            return PartialHttpHandler.FromFunc(async (next, ctx) =>
            {
                var credentials = ReadBasicAuth(ctx);
                if (credentials != null && auth(credentials.Value))
                {
                    // user supplied credentials, and the callback said they were valid
                    ctx.User = new System.Security.Claims.ClaimsPrincipal(new GenericIdentity(credentials.Value.user, "Basic"));
                    return await next.Invoke(ctx);
                }

                // the user didn't specify credentials, OR they were invalid
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.Headers.Add("WWW-Authenticate", "Basic");
                // DON'T return None, we did handle the request and it failed!
                return ValueOption.Some(ctx);
            });
        }

        public static HttpHandler basicAuth(Func<(string user, string pw), bool> auth, HttpHandler next)
        {
            return basicAuth(auth).WithNext(next);
        }

    }
}
