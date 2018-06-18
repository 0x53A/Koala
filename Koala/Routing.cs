using System;
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
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Internal;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing.Template;

namespace Koala
{
    internal interface IKoalaModelBinder
    {
        Task<ValueOption<T>> TryBind<T>(HttpContext ctx);
    }

    public static class Routing
    {
        const string RouteKey = "koala_route";

        // --------------------------------------------------------------------------------------
        // Helpers: Routing

        private static ValueOption<string> GetSavedSubPath(HttpContext ctx)
        {
            if (ctx.Items.TryGetValue(RouteKey, out var val) &&
                val is string s &&
                !string.IsNullOrWhiteSpace(s))
            {
                return ValueOption.Some(s);
            }
            return ValueOption.None;
        }

        private static string GetPath(HttpContext ctx)
        {
            return GetSavedSubPath(ctx)
                    .Match(
                        s =>
                        {
                            if (ctx.Request.Path.Value.StartsWith(s))
                                return ctx.Request.Path.Value.Substring(s.Length);
                            else
                                return ctx.Request.Path.Value;
                        },
                        () => ctx.Request.Path.Value);
        }

        private static PartialHttpHandler handlerWithRootedPath(string path)
        {
            return PartialHttpHandler.FromFunc(async (next, ctx) =>
            {
                var saved = GetSavedSubPath(ctx);
                ctx.Items[RouteKey] = saved.OrDefault("") + path;

                var result = await next.Invoke(ctx);

                result.Match(r => { }, () =>
                {
                    saved.Match(s => ctx.Items[RouteKey] = s, () => ctx.Items.Remove(RouteKey));
                });

                return result;
            });
        }


        // --------------------------------------------------------------------------------------
        // Helpers: Basic Auth

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


        // --------------------------------------------------------------------------------------
        // Helpers: Output

        private static async Task WriteBytesToResponse(HttpContext ctx, byte[] bytes)
        {
            ctx.Response.Headers[HeaderNames.ContentLength] = bytes.Length.ToString();
            await ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length);
        }

        private static async Task WriteUtf8StringToResponse(string s, HttpContext ctx)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            await WriteBytesToResponse(ctx, bytes);
        }

        private static void SetContentType(HttpContext ctx, string ct)
        {
            ctx.Response.Headers[HeaderNames.ContentType] = ct;
        }


        // --------------------------------------------------------------------------------------
        // Handlers: Output

        public static HttpHandler text(string s)
        {
            return HttpHandler.FromFunc(async ctx =>
            {
                SetContentType(ctx, "text/plain; charset=utf-8");
                await WriteUtf8StringToResponse(s, ctx);
                return ValueOption.Some(ctx);
            });
        }

        public static HttpHandler json(string s)
        {
            return HttpHandler.FromFunc(async ctx =>
            {
                SetContentType(ctx, "application/json; charset=utf-8"); // note: the charset is theoretically a spec violation
                await WriteUtf8StringToResponse(s, ctx);
                return ValueOption.Some(ctx);
            });
        }

        public static HttpHandler serialize_json(object o)
        {
            return HttpHandler.FromFunc(async ctx =>
            {
                // REVIEW: do we need error handling? What happens if an exception bubbles up to asp.net core?
                var s = Newtonsoft.Json.JsonConvert.SerializeObject(o);
                return await json(s).Invoke(ctx);
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


        // --------------------------------------------------------------------------------------
        // Handlers: Interop

        public static HttpHandler to_view_controller<TController>()
        {
            throw new NotImplementedException();
        }

        public static HttpHandler to_api_controller<TController>(string prefix = "", RouteValueDictionary defaultRouteValues = null)
        {
            return HttpHandler.FromFunc(async ctx =>
            {
                var currentPath = prefix + GetPath(ctx);

                // REVIEW: cache all this?
                var appModelProvider = ctx.RequestServices.GetServices<IApplicationModelProvider>().OfType<DefaultApplicationModelProvider>().Single();
                ApplicationModelProviderContext appModelProviderCtx = new ApplicationModelProviderContext(new[] { typeof(TController).GetTypeInfo() });
                appModelProvider.OnProvidersExecuting(appModelProviderCtx);

                var descriptors = ControllerActionDescriptorBuilder.Build(appModelProviderCtx.Result);
                var matchedDescriptors = new List<ActionDescriptor>();
                var parsedValues = new Dictionary<ActionDescriptor, RouteValueDictionary>();

                foreach (var d in descriptors)
                {
                    var templateMatcher = new TemplateMatcher(TemplateParser.Parse(d.AttributeRouteInfo.Template), defaultRouteValues ?? new RouteValueDictionary());
                    var parsedRouteValues = new RouteValueDictionary();
                    if (templateMatcher.TryMatch(new PathString(currentPath), parsedRouteValues))
                    {
                        matchedDescriptors.Add(d);
                        parsedValues[d] = parsedRouteValues;
                    }
                }

                var actionSelector = ctx.RequestServices.GetRequiredService<IActionSelector>();
                var best = actionSelector.SelectBestCandidate(new RouteContext(ctx), matchedDescriptors);

                var routeCtx = new RouteContext(ctx);
                routeCtx.RouteData.Values.Clear();
                foreach (var kv in parsedValues[best])
                    routeCtx.RouteData.Values.Add(kv.Key, kv.Value);

                var actx = new ActionContext(ctx, routeCtx.RouteData, best);
                var actionInvokerProvider = ctx.RequestServices.GetServices<IActionInvokerProvider>().OfType<ControllerActionInvokerProvider>().Single();
                var fac = new ActionInvokerFactory(new[] { actionInvokerProvider });
                IActionInvoker invoker = fac.CreateInvoker(actx);
                await invoker.InvokeAsync();
                return ValueOption.Some(ctx);
            });
        }


        // --------------------------------------------------------------------------------------
        // Handlers: Routing

        public static PartialHttpHandler GET() =>
            PartialHttpHandler.FromFunc(async (next, ctx) =>
            {
                if (ctx.Request.Method == HttpMethods.Get)
                    return await next.Invoke(ctx);
                return ValueOption<HttpContext>.None;
            });

        public static HttpHandler GET(HttpHandler next)
        {
            return GET().WithNext(next);
        }
        public static HttpHandler POST(HttpHandler next)
        {
            return HttpHandler.FromFunc(async ctx =>
            {
                if (ctx.Request.Method == HttpMethods.Post)
                    return await next.Invoke(ctx);
                return ValueOption<HttpContext>.None;
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

        public static PartialHttpHandler routeStartsWith(string path)
        {
            return PartialHttpHandler.FromFunc(async (next, ctx) =>
            {
                var currentPath = GetPath(ctx);
                if (currentPath.StartsWith(path))
                    return await next.Invoke(ctx);
                return ValueOption.None;
            });
        }

        public static HttpHandler routeStartsWith(string path, HttpHandler next)
        {
            return routeStartsWith(path).WithNext(next);
        }

        public static HttpHandler subRoute(string path, HttpHandler next)
        {
            return routeStartsWith(path, handlerWithRootedPath(path).WithNext(next));
        }


        // --------------------------------------------------------------------------------------
        // Handlers: Authentication

        public static PartialHttpHandler basicAuth(Func<(string user, string pw), bool> auth)
        {
            return PartialHttpHandler.FromFunc(async (next, ctx) =>
            {
                var credentials = ReadBasicAuth(ctx);
                if (credentials != null && auth(credentials.Value))
                {
                    // user supplied credentials, and the callback said they were valid
                    ctx.User = new ClaimsPrincipal(new GenericIdentity(credentials.Value.user, "Basic"));
                    return await next.Invoke(ctx);
                }

                // the user didn't specify credentials, OR they were invalid
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.Headers.Add("WWW-Authenticate", "Basic");
                // DON'T return None, we did handle the request and it failed!
                return ValueOption.Some(ctx);
            });
        }

        public static PartialHttpHandler basicAuth(Func<(string user, string pw), ClaimsPrincipal> auth)
        {
            return PartialHttpHandler.FromFunc(async (next, ctx) =>
            {
                var credentials = ReadBasicAuth(ctx);
                if (credentials != null)
                {
                    var principal = auth(credentials.Value);
                    if (principal != null)
                    {
                        // user supplied credentials, and the callback said they were valid
                        ctx.User = principal;
                        return await next.Invoke(ctx);
                    }
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

        public static HttpHandler basicAuth(Func<(string user, string pw), ClaimsPrincipal> auth, HttpHandler next)
        {
            return basicAuth(auth).WithNext(next);
        }

    }
}
