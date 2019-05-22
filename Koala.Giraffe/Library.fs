namespace Koala.Giraffe

[<AutoOpen>]
module Interop =
    open Giraffe
    open Koala

    let wrap_koala_as_func(handler:Koala.HttpHandler) : Giraffe.Core.HttpFunc =
        fun ctx -> task {
            let! result = handler.Invoke(ctx)
            if not result then
                return None
            else
                return Some ctx
        }

    let wrap_koala(handler:Koala.HttpHandler) : Giraffe.Core.HttpHandler =
        fun next ctx -> task {
            let! result = handler.Invoke(ctx)
            if not result then
                return! next ctx
            else
                return Some ctx
        }

    let wrap_giraffe(handler:Giraffe.Core.HttpFunc, next:Koala.HttpHandler) : Koala.HttpHandler =
        { new Koala.HttpHandler() with
            member __.Invoke(ctx) = task {
                let! result = handler ctx
                return
                    match result with
                    | Some v -> true
                    | None -> false
            }
        }
