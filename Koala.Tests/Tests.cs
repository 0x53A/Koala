using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore;
using System;
using static Koala.Routing;
using System.Net.Http;
using System.Threading.Tasks;
using Expecto.CSharp;
using Expecto;
using Microsoft.AspNetCore.Mvc;
using ExpressionToCodeLib;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Linq;

namespace Koala.Tests
{
    public class TestClass
    {
        [Expecto.XUnit.ExpectoBridge]
        public void __dummy() { }

        private static IWebHost WebHostFromKoalaHandler(string[] args, int port, HttpHandler handler)
        {
            var webHost =
            WebHost.CreateDefaultBuilder(args)
                   .ConfigureServices(services =>
                   {
                       services.AddMvc(mvcOpt =>
                       {
                           mvcOpt.RespectBrowserAcceptHeader = true;
                       })
                       .SetCompatibilityVersion(CompatibilityVersion.Version_2_1)
                       .AddXmlSerializerFormatters();
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

        public static void Main(string[] args)
        {
            Expecto.CSharp.Runner.RunTestsInAssembly(Expecto.Impl.ExpectoConfig.defaultConfig, args);
        }


        [Tests]
        public static Test Tests =
            Runner.TestList("general groupings", new Test[] {
                Runner.TestCase("basic auth", () => TestBasicAuth()),
                Runner.TestCase("content negotiation - default", () => ConNeg_default()),
                Runner.TestCase("content negotiation - json", () => ConNeg_json()),
                Runner.TestCase("content negotiation - xml", () => ConNeg_xml()),
});

        public static void TestBasicAuth()
        {
            // The function to validate credentials.
            // Normally you would hash the password and compare it against a database or whatever.
            Func<(string user, string pw), bool> authenticator = (credentials) =>
            {
                return credentials == ("Bond", "007");
            };

            var handler =
                GET(
                    choose(new[]
                    {
                        route("/", text("Hello World!")),

                        // show how handlers can be combined, and how easily a new handler can be created from these building blocks.
                        route("/protected", basicAuth(authenticator, HttpHandler.Wrap(ctx => text($"Welcome Agent {ctx.User.Identity.Name}!"))))
                    }));


            using (var webHost = WebHostFromKoalaHandler(Array.Empty<string>(), 0, handler))
            {
                webHost.Start();
                var port = new Uri(webHost.ServerFeatures.Get<IServerAddressesFeature>().Addresses.First()).Port;

                // without header it should fail
                var client = new HttpClient();
                client.BaseAddress = new Uri($"http://localhost:{port}/");
            }
        }

        public static void GiraffeSample()
        {
            // The function to validate credentials.
            bool authenticator((string user, string pw) credentials)
            {
                // Normally you would hash the password and compare it against a database or whatever.
                // Here it is just checked against a hardcoded value.
                return credentials == ("Bond", "007");
            }

            var handler =
                choose(new[]
                {
                  GET(
                    choose(new[]
                    {
                        route("/", text("Hello World!")),

                        // show how handlers can be combined, and how easily a new handler can be created from these building blocks.
                        route("/protected", basicAuth(authenticator, HttpHandler.Wrap(ctx => text($"Welcome Agent {ctx.User.Identity.Name}!"))))

                    })),

                  route("/api",
                    choose(new[]{
                        GET(route("/time", HttpHandler.Wrap(ctx => text($"{DateTime.Now}")))),
                        POST(route("/ping", text("pong")))
                  }))
                });

        }

        public static async Task ConNeg_default()
        {
            var obj = new { X = 1, Y = "2", Z = 3.3 };
            var handler = GET(route("/api/value", serialize_conneg(obj)));

            using (var webHost = WebHostFromKoalaHandler(Array.Empty<string>(), 12345, handler))
            {
                webHost.Start();

                var httpClient = new HttpClient();
                var x = await httpClient.GetAsync("http://localhost:12345/api/value");
                PAssert.That(() => x.IsSuccessStatusCode);
                PAssert.That(() => x.Content.Headers.ContentType.MediaType == "application/json");
                var json = await x.Content.ReadAsStringAsync();
                PAssert.That(() => JsonConvert.DeserializeAnonymousType(json, obj).Equals(obj));
            }
        }

        public static async Task ConNeg_json()
        {
            var obj = new { X = 1, Y = "2", Z = 3.3 };
            var handler = GET(route("/api/value", serialize_conneg(obj)));

            using (var webHost = WebHostFromKoalaHandler(Array.Empty<string>(), 12345, handler))
            {
                webHost.Start();

                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                var x = await httpClient.GetAsync("http://localhost:12345/api/value");
                PAssert.That(() => x.IsSuccessStatusCode);
                PAssert.That(() => x.Content.Headers.ContentType.MediaType == "application/json");
                var json = await x.Content.ReadAsStringAsync();
                PAssert.That(() => JsonConvert.DeserializeAnonymousType(json, obj).Equals(obj));
            }
        }

        public class DataObject
        {
            public int X { get; set; }
            public string Y { get; set; }
            public double Z { get; set; }
        }

        public static async Task ConNeg_xml()
        {
            var obj = new DataObject { X = 1, Y = "2", Z = 3.3 };
            var handler = GET(route("/api/value", serialize_conneg(obj)));

            using (var webHost = WebHostFromKoalaHandler(Array.Empty<string>(), 0, handler))
            {
                webHost.Start();
                var port = new Uri(webHost.ServerFeatures.Get<IServerAddressesFeature>().Addresses.First()).Port;

                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Accept", "application/xml");
                var x = await httpClient.GetAsync($"http://localhost:{port}/api/value");
                PAssert.That(() => x.IsSuccessStatusCode);
                PAssert.That(() => x.Content.Headers.ContentType.MediaType == "application/xml");

                var xml = await x.Content.ReadAsStringAsync();
                PAssert.That(() => xml == "<DataObject xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><X>1</X><Y>2</Y><Z>3.3</Z></DataObject>");
            }
        }
    }
}
