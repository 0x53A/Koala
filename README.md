# Koala


## What?

A functional routing library for ASP.NET Core, ~~stolen from~~ inspired by [Giraffe](https://github.com/giraffe-fsharp/Giraffe) and [Suave](https://github.com/SuaveIO/suave).

## Why

ASP.NET Core is a fast, modern web framework.

This library provides a different routing layer over the core.


## Advantages

### just functions and variables

Instead of a jungle of controllers with a maze of attributes, which get discovered through reflection, you just have one object (the root handler that gets passed to ``AddKoala()``) which itself gets composed from smaller objects.

### Easy overview

In most cases, your routing definition is a block less than one page long, which you can read top to bottom, left to right.

### Composition, re-use and parametrization

* Your api got so big that you have one large block? Just split it into multiple logical units and compose it at the root.
* You have an api that you want to re-use in two projects? Just put it into a lib and reference it.
* You need to parametrize a route based on the customer name? ``route`` is just a function, so you can pass it a variable instead of a constant. You can't do the same with a ``[Route("...")]`` Attribute ;-)



## Hello World

Add the NuGet package [Koala](https://www.nuget.org/packages/Koala/) to a new Console Application and copy-paste this into your Program.cs:

```C#
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore;
using static Koala.Routing;

namespace Koala.HelloWorld
{
    public class KoalaHelloWorld
    {
        private static IWebHost ConfigureWebHostFromKoalaHandler(string[] args, int port, HttpHandler handler)
        {
            var webHost =
                WebHost.CreateDefaultBuilder(args)
                   .Configure(app =>
                   {
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
            var handler = route("/", text("Hello World!"));

            using (var webHost = ConfigureWebHostFromKoalaHandler(args, port: 5000, handler: handler))
            {
                // .Run() is blocking, to start the webserver non-blocking, call .Start()
                webHost.Run();
            }
        }
    }
}

```

The two important parts are:

1) ``app.AddKoala(handler);`` in your ``Configure`` callback.
This integrates Koala into your pipeline.

2) The routing definition:
``var handler = route("/", text("Hello World!"));``

The rest is just standard ASP.NET Core.




## Basic building block: the ``Koala.HttpHandler``

### Slightly more complex example

```c#
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

                  subRoute("/api",
                    choose(new[]{
                        GET(route("/time", HttpHandler.Wrap(ctx => text($"{DateTime.Now}")))),
                        POST(route("/ping", text("pong")))
                  }))
                });
```

(Almost) Everything in Koala is a HttpHandler.

A HttpHandler can

* filter requests (``GET``, which checks the Http Verb, or ``route``, which checks that the route matches)  
 => if the filter doesn't match, it will no-op and (if inside a ``choose``) give the next handler a chance to run. They run in the order they are defined from top to bottom.
 
* modify and/or reject requests
 => ``basicAuth`` will check the credentials, and either set ``ctx.User`` (if successfull), or send back 401 UNAUTHORIZED with a ``WWW-Authenticate: Basic`` challenge.
 
 * terminate the request
  => ``text`` will set the header ``Content-Type: text/plain`` and send the specified text back in the response body.
  
  
