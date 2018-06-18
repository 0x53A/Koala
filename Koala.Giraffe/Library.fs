namespace Koala.Giraffe

[<AutoOpen>]
module Interop =
    open Giraffe
    open Koala

    let wrap_koala_as_func(handler:Koala.HttpHandler) : Giraffe.Core.HttpFunc =
        fun ctx -> task {
            let! result = handler.Invoke(ctx)
            if result.IsNone then
                return None
            else
                return Some result.Value
        }

    let wrap_koala(handler:Koala.HttpHandler) : Giraffe.Core.HttpHandler =
        fun next ctx -> task {
            let! result = handler.Invoke(ctx)
            if result.IsNone then
                return! next ctx
            else
                return Some result.Value
        }

    let wrap_giraffe(handler:Giraffe.Core.HttpFunc, next:Koala.HttpHandler) : Koala.HttpHandler =
        { new Koala.HttpHandler() with
            member __.Invoke(ctx) = task {
                let! result = handler ctx
                return
                    match result with
                    | Some v -> ValueOption.Some v
                    | None -> ValueOption.op_Implicit ValueOption.None
            }
        }
