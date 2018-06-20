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
using System.Collections.Generic;
using System.Reflection;
using System.Net.Http.Headers;
using System.Text;
using System.Net;
using Microsoft.AspNetCore.Builder;

namespace Koala.Tests
{
    public class TestClass
    {
        public static void Main(string[] args)
        {
            var tests = Runner.TestList("TestClass", DiscoverTestMethods<TestClass>());
            Runner.RunTestsWithArgs(Impl.ExpectoConfig.defaultConfig, args, tests);
        }

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
                       //app.UseMvc(route =>
                       //{

                       //});
                   })
                   .UseKestrel(c =>
                   {
                       c.ListenAnyIP(port);
                   })
                   .Build();
            return webHost;
        }

        private static IEnumerable<Test> DiscoverTestMethods<T>()
        {
            var t = typeof(T);
            foreach (var m in t.GetMethods())
            {
                var isTaskReturning = typeof(Task).IsAssignableFrom(m.ReturnType);
                if (m.GetCustomAttribute<FTestsAttribute>() != null)
                {
                    if (isTaskReturning)
                        yield return Runner.FocusedTestCase(m.Name, () => (Task)m.Invoke(null, Array.Empty<object>()));
                    else
                        yield return Runner.FocusedTestCase(m.Name, () => m.Invoke(null, Array.Empty<object>()));
                }
                else if (m.GetCustomAttribute<PTestsAttribute>() != null)
                {
                    if (isTaskReturning)
                        yield return Runner.PendingTestCase(m.Name, () => (Task)m.Invoke(null, Array.Empty<object>()));
                    else
                        yield return Runner.PendingTestCase(m.Name, () => m.Invoke(null, Array.Empty<object>()));
                }
                else if (m.GetCustomAttribute<TestsAttribute>() != null)
                {
                    if (isTaskReturning)
                        yield return Runner.TestCase(m.Name, () => (Task)m.Invoke(null, Array.Empty<object>()));
                    else
                        yield return Runner.TestCase(m.Name, () => m.Invoke(null, Array.Empty<object>()));
                }
            }
        }

        [Tests]
        public static async Task TestBasicAuth()
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

                var result = await client.GetAsync("/protected");
                PAssert.That(() => result.StatusCode == HttpStatusCode.Unauthorized);
                PAssert.That(() => result.Headers.WwwAuthenticate.Any(w => w.Scheme == "Basic"));

                // with the wrong credentials, it should also 403
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("Goldeneye:big-laser")));
                result = await client.GetAsync("/protected");
                PAssert.That(() => result.StatusCode == HttpStatusCode.Unauthorized);
                PAssert.That(() => result.Headers.WwwAuthenticate.Any(w => w.Scheme == "Basic"));

                // with the correct credentials, it should return the value
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("Bond:007")));
                result = await client.GetAsync("/protected");
                PAssert.That(() => result.StatusCode == HttpStatusCode.OK);
                var content = await result.Content.ReadAsStringAsync();
                PAssert.That(() => content == "Welcome Agent Bond!");
            }
        }

        [Tests]
        public static async Task ConNeg_default()
        {
            var obj = new { X = 1, Y = "2", Z = 3.3 };
            var handler = GET(route("/api/value", serialize_conneg(obj)));

            using (var webHost = WebHostFromKoalaHandler(Array.Empty<string>(), 0, handler))
            {
                webHost.Start();
                var port = new Uri(webHost.ServerFeatures.Get<IServerAddressesFeature>().Addresses.First()).Port;

                var httpClient = new HttpClient();
                var x = await httpClient.GetAsync($"http://localhost:{port}/api/value");
                PAssert.That(() => x.IsSuccessStatusCode);
                PAssert.That(() => x.Content.Headers.ContentType.MediaType == "application/json");
                var json = await x.Content.ReadAsStringAsync();
                PAssert.That(() => JsonConvert.DeserializeAnonymousType(json, obj).Equals(obj));
            }
        }

        [Tests]
        public static async Task ConNeg_json()
        {
            var obj = new { X = 1, Y = "2", Z = 3.3 };
            var handler = GET(route("/api/value", serialize_conneg(obj)));

            using (var webHost = WebHostFromKoalaHandler(Array.Empty<string>(), 0, handler))
            {
                webHost.Start();
                var port = new Uri(webHost.ServerFeatures.Get<IServerAddressesFeature>().Addresses.First()).Port;

                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                var x = await httpClient.GetAsync($"http://localhost:{port}/api/value");
                PAssert.That(() => x.IsSuccessStatusCode);
                PAssert.That(() => x.Content.Headers.ContentType.MediaType == "application/json");
                var json = await x.Content.ReadAsStringAsync();
                PAssert.That(() => JsonConvert.DeserializeAnonymousType(json, obj).Equals(obj));
            }
        }

        // xml can't (de)serialize anonymous objects
        public class DataObject
        {
            public int X { get; set; }
            public string Y { get; set; }
            public double Z { get; set; }
        }

        [Tests]
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

        [Tests]
        public static async Task Conneg_null_default()
        {
            var handler = GET(route("/api/value", serialize_conneg(null)));

            using (var webHost = WebHostFromKoalaHandler(Array.Empty<string>(), 0, handler))
            {
                webHost.Start();
                var port = new Uri(webHost.ServerFeatures.Get<IServerAddressesFeature>().Addresses.First()).Port;

                var httpClient = new HttpClient();
                var x = await httpClient.GetAsync($"http://localhost:{port}/api/value");
                PAssert.That(() => x.StatusCode == HttpStatusCode.NoContent);
            }
        }

        [Tests]
        public static async Task Conneg_null_json()
        {
            var handler = GET(route("/api/value", serialize_conneg(null)));

            using (var webHost = WebHostFromKoalaHandler(Array.Empty<string>(), 0, handler))
            {
                webHost.Start();
                var port = new Uri(webHost.ServerFeatures.Get<IServerAddressesFeature>().Addresses.First()).Port;

                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                var x = await httpClient.GetAsync($"http://localhost:{port}/api/value");
                PAssert.That(() => x.StatusCode == HttpStatusCode.NoContent);
            }
        }

        [Tests]
        public static async Task ConNeg_null_xml()
        {
            var handler = GET(route("/api/value", serialize_conneg(null)));

            using (var webHost = WebHostFromKoalaHandler(Array.Empty<string>(), 0, handler))
            {
                webHost.Start();
                var port = new Uri(webHost.ServerFeatures.Get<IServerAddressesFeature>().Addresses.First()).Port;

                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Accept", "application/xml");
                var x = await httpClient.GetAsync($"http://localhost:{port}/api/value");
                PAssert.That(() => x.StatusCode == HttpStatusCode.NoContent);
            }
        }

        [Tests]
        public static async Task json_null()
        {
            var handler = GET(route("/api/value", serialize_json(null)));

            using (var webHost = WebHostFromKoalaHandler(Array.Empty<string>(), 0, handler))
            {
                webHost.Start();
                var port = new Uri(webHost.ServerFeatures.Get<IServerAddressesFeature>().Addresses.First()).Port;

                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Accept", "application/xml");
                var x = await httpClient.GetAsync($"http://localhost:{port}/api/value");
                PAssert.That(() => x.StatusCode == HttpStatusCode.OK);
                var content = await x.Content.ReadAsStringAsync();
                PAssert.That(() => JsonConvert.DeserializeObject(content) == null);
            }
        }

        [Tests]
        public static async Task json_string()
        {
            var handler = GET(route("/api/value", serialize_json("hello world")));

            using (var webHost = WebHostFromKoalaHandler(Array.Empty<string>(), 0, handler))
            {
                webHost.Start();
                var port = new Uri(webHost.ServerFeatures.Get<IServerAddressesFeature>().Addresses.First()).Port;

                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Accept", "application/xml");
                var x = await httpClient.GetAsync($"http://localhost:{port}/api/value");
                PAssert.That(() => x.StatusCode == HttpStatusCode.OK);
                var content = await x.Content.ReadAsStringAsync();
                PAssert.That(() => "hello world".Equals(JsonConvert.DeserializeObject(content)));
            }
        }

        [Tests]
        public static async Task json_anonymous()
        {
            var obj = new { a = 1, b = "2", c = DateTime.Now, d = new { e = 0.3, f = 0.2 } };
            var handler = GET(route("/api/value", serialize_json(obj)));

            using (var webHost = WebHostFromKoalaHandler(Array.Empty<string>(), 0, handler))
            {
                webHost.Start();
                var port = new Uri(webHost.ServerFeatures.Get<IServerAddressesFeature>().Addresses.First()).Port;

                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Accept", "application/xml");
                var x = await httpClient.GetAsync($"http://localhost:{port}/api/value");
                PAssert.That(() => x.StatusCode == HttpStatusCode.OK);
                var content = await x.Content.ReadAsStringAsync();
                PAssert.That(() => obj.Equals(JsonConvert.DeserializeAnonymousType(content, obj)));
            }
        }

        [Tests]
        public static async Task SubRoute()
        {
            var handler =
                GET(
                    choose(
                        route("/", text("root")),
                        subRoute("/api",
                            choose(
                                route("/a", text("a")),
                                route("/b", text("b")))),
                        route("/api/c", text("c"))
                        ));

            using (var webHost = WebHostFromKoalaHandler(Array.Empty<string>(), 0, handler))
            {
                webHost.Start();
                var port = new Uri(webHost.ServerFeatures.Get<IServerAddressesFeature>().Addresses.First()).Port;

                var httpClient = new HttpClient();

                {
                    var x = await httpClient.GetAsync($"http://localhost:{port}/");
                    PAssert.That(() => x.StatusCode == HttpStatusCode.OK);
                    var content = await x.Content.ReadAsStringAsync();
                    PAssert.That(() => content == "root");
                }

                {
                    var x = await httpClient.GetAsync($"http://localhost:{port}/api/a");
                    PAssert.That(() => x.StatusCode == HttpStatusCode.OK);
                    var content = await x.Content.ReadAsStringAsync();
                    PAssert.That(() => content == "a");
                }

                {
                    var x = await httpClient.GetAsync($"http://localhost:{port}/api/b");
                    PAssert.That(() => x.StatusCode == HttpStatusCode.OK);
                    var content = await x.Content.ReadAsStringAsync();
                    PAssert.That(() => content == "b");
                }

                {
                    var x = await httpClient.GetAsync($"http://localhost:{port}/api/c");
                    PAssert.That(() => x.StatusCode == HttpStatusCode.OK);
                    var content = await x.Content.ReadAsStringAsync();
                    PAssert.That(() => content == "c");
                }
            }
        }

        // https://docs.microsoft.com/en-us/aspnet/core/tutorials/first-mvc-app/adding-controller?view=aspnetcore-2.1
        public class HelloWorldController : Controller
        {
            // 
            // GET: /HelloWorld/

            public string Index()
            {
                return "This is my default action...";
            }

            // 
            // GET: /HelloWorld/Welcome/ 

            public string Welcome()
            {
                return "This is the Welcome action method...";
            }
        }

        [Route("api/[controller]")]
        [ApiController]
        public class TodoController : ControllerBase
        {
            public string Get()
            {
                return "Get";
            }

            [HttpGet("Item1")]
            public ActionResult<string> Asd()
            {
                return "Item1";
            }

            [HttpGet("Item2")]
            public ActionResult<string> Xyz()
            {
                return "Item2";
            }

            [HttpGet("Item3/{i}")]
            public string Foo(int i)
            {
                return $"Foo{i}";
            }
        }

        [Tests]
        public static async Task RouteToController()
        {
            var handler = GET(subRoute("/Myapi/MyTodo", to_api_controller<TodoController>("/api/ToDo")));

            using (var webHost = WebHostFromKoalaHandler(Array.Empty<string>(), 0, handler))
            {
                webHost.Start();
                var port = new Uri(webHost.ServerFeatures.Get<IServerAddressesFeature>().Addresses.First()).Port;

                var httpClient = new HttpClient();

                {
                    var x = await httpClient.GetAsync($"http://localhost:{port}/Myapi/MyTodo/Item1");
                    PAssert.That(() => x.StatusCode == HttpStatusCode.OK);
                    var content = await x.Content.ReadAsStringAsync();
                    PAssert.That(() => content == "Item1");
                }

                {
                    var x = await httpClient.GetAsync($"http://localhost:{port}/Myapi/MyTodo/Item2");
                    PAssert.That(() => x.StatusCode == HttpStatusCode.OK);
                    var content = await x.Content.ReadAsStringAsync();
                    PAssert.That(() => content == "Item2");
                }

                {
                    var x = await httpClient.GetAsync($"http://localhost:{port}/Myapi/MyTodo/Item3/5");
                    PAssert.That(() => x.StatusCode == HttpStatusCode.OK);
                    var content = await x.Content.ReadAsStringAsync();
                    PAssert.That(() => content == "Foo5");
                }
            }
        }
    }
}
