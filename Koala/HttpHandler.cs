using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;

namespace Koala
{
    public abstract class PartialHttpHandler
    {
        public abstract Task<bool> Invoke(HttpHandler next, HttpContext ctx);
        public abstract HttpHandler WithNext(HttpHandler next);

        private sealed class FuncPartialHttpHandler : PartialHttpHandler
        {
            private readonly Func<HttpHandler, HttpContext, Task<bool>> _f;

            public FuncPartialHttpHandler(Func<HttpHandler, HttpContext, Task<bool>> f)
            {
                _f = f;
            }

            public override Task<bool> Invoke(HttpHandler next, HttpContext ctx)
            {
                return _f(next, ctx);
            }

            public override HttpHandler WithNext(HttpHandler next)
            {
                return HttpHandler.FromFunc(ctx => this.Invoke(next, ctx));
            }
        }

        public static PartialHttpHandler FromFunc(Func<HttpHandler, HttpContext, Task<bool>> f)
        {
            return new FuncPartialHttpHandler(f);
        }

        // overload operator +
        public static HttpHandler operator +(PartialHttpHandler self, HttpHandler next)
        {
            return self.WithNext(next);
        }

        // overload operator +
        public static PartialHttpHandler operator +(PartialHttpHandler self, PartialHttpHandler nextPartial)
        {
            return PartialHttpHandler.FromFunc((next,ctx) => self.Invoke(nextPartial.WithNext(next), ctx));
        }
    }

    public abstract class HttpHandler
    {
        public abstract Task<bool> Invoke(HttpContext ctx);

        private sealed class FuncHttpHandler : HttpHandler
        {
            private readonly Func<HttpContext, Task<bool>> _f;

            public FuncHttpHandler(Func<HttpContext, Task<bool>> f)
            {
                _f = f;
            }

            public override Task<bool> Invoke(HttpContext ctx)
            {
                return _f(ctx);
            }
        }

        public static HttpHandler FromFunc(Func<HttpContext, Task<bool>> f)
        {
            return new FuncHttpHandler(f);
        }

        public static HttpHandler Wrap(Func<HttpContext, HttpHandler> f)
        {
            return HttpHandler.FromFunc(async ctx => await f(ctx).Invoke(ctx));
        }
    }
}
