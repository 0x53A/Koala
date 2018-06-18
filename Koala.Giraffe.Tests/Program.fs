// Learn more about F# at http://fsharp.org

open System
open Expecto
open Giraffe





module Tests =
    open Microsoft.AspNetCore
    open Microsoft.AspNetCore.Hosting
    open Microsoft.AspNetCore.Hosting.Server.Features
    open System.Net.Http
    open Microsoft.Extensions.DependencyInjection

    let run cb f =
        let builder = WebHost.CreateDefaultBuilder([||])
        cb(builder)
        use webHost = builder.UseKestrel(fun c -> c.ListenAnyIP(0)).Build()
        webHost.Start()
        let port = (new Uri(Seq.head <| webHost.ServerFeatures.Get<IServerAddressesFeature>().Addresses)).Port
        f port

    [<Tests>]
    let tests =
      test "A simple test" {
        let giraffe = (Koala.Giraffe.Interop.wrap_koala(Koala.Routing.text("hello"))) // (Giraffe.ResponseWriters.text "hello") 
        run (fun b ->
              b
                .ConfigureServices(fun (s:IServiceCollection) -> s.AddGiraffe()|>ignore)
                .Configure(fun app -> app.UseGiraffe giraffe)
                |> ignore
            )
            (fun port ->
                let client = new HttpClient()
                client.BaseAddress <- new Uri(sprintf "http://localhost:%i/" port)
                Swensen.Unquote.Assertions.test (<@ client.GetStringAsync("api/").Result = "hello"  @>))
      }



[<EntryPoint>]
let main argv =
    runTestsInAssembly defaultConfig argv
