using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore;
using System;
using static Koala.Routing;

namespace Koala.Tests
{
    public class Tests
    {
        private static IWebHost WebHostFromKoalaHandler(string[] args, int port, HttpHandler handler)
        {
            var webHost =
            WebHost.CreateDefaultBuilder(args)
                   .ConfigureServices(services =>
                   {
                       //services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1);
                   })
                   .Configure(app =>
                   {
                       //var env = app.ApplicationServices.GetRequiredService<IHostingEnvironment>();
                       //if (env.IsDevelopment())
                       //{
                       //    app.UseDeveloperExceptionPage();
                       //}
                       //else
                       //{
                       //    app.UseHsts();
                       //}

                       app.AddKoala(handler);
                   })
                   .UseKestrel(c =>
                   {
                       c.ListenAnyIP(port);
                   })
                   .Build();
            return webHost;
        }

        //public class MyInputModel
        //{
        //    public string Name { get; set; }
        //}

        //public class MyResultModel
        //{
        //    public string Text { get; set; }
        //}

        public static void Main(string[] args)
        {
            //HttpHandler bindBody<T>(Func<T, HttpHandler> f)
            //{
            //    return HttpHandler.FromFunc(async ctx =>
            //    {
            //        var svc = ctx.RequestServices.GetRequiredService<IKoalaModelBinder>();
            //        var t = await svc.TryBind<T>(ctx);
            //        var x = await f(t.Value).Invoke(ctx);
            //        return x;
            //    });
            //}

            // the function to validate credentials.
            // Normally you would hash the password and compare it against a database or whatever.
            Func<(string user, string pw), bool> authenticator = (credentials) => {
                return credentials == ("Bond", "007");
            };

            var handler =
                GET() +
                    choose(new[] // note: choose uses a params array, so you could
                    {
                        // Dialect A: combine handlers with +
                        route("/") + text("Hello World!"),

                        // Dialect B: pass 'next' handlers as parameter
                        route("/test", text("Test OK!")),

                        // show how handlers can be combined, and how easily a new handler can be created from building blocks.
                        route("/protected") + basicAuth(authenticator) + HttpHandler.Wrap(ctx => text($"Super Secret!\nWelcome Agent {ctx.User.Identity.Name}!")),

                        //route("/model") + bindBody<MyInputModel>(m => text(m.Name))

                    });


            using (var webHost = WebHostFromKoalaHandler(args, 12345, handler))
            {
                webHost.Start();
                Console.WriteLine("Started!");
                Console.ReadLine();
            }
        }
    }
}
