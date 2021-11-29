module LibService.Rollbar

open FSharp.Control.Tasks
open System.Threading.Tasks
open FSharp.Control.Tasks

open Microsoft.AspNetCore.Http

type ExecutionID = Telemetry.ExecutionID


open Prelude
open Tablecloth

let mutable initialized = false

let init (serviceName : string) : unit =
  print "Configuring rollbar"
  // FSTODO: include host, ip address, serviceName
  let config = Rollbar.RollbarConfig(Config.rollbarServerAccessToken)
  config.Environment <- Config.rollbarEnvironment
  config.Enabled <- Config.rollbarEnabled
  config.LogLevel <- Rollbar.ErrorLevel.Error
  // FSTODO add username

  let (_ : Rollbar.IRollbar) =
    Rollbar.RollbarLocator.RollbarInstance.Configure config

  initialized <- true
  ()

// "https://ui.honeycomb.io/dark/datasets/kubernetes-bwd-ocaml?query={\"filters\":[{\"column\":\"rollbar\",\"op\":\"exists\"},{\"column\":\"execution_id\",\"op\":\"=\",\"value\":\"44602511168214071\"}],\"limit\":100,\"time_range\":604800}"
type HoneycombFilter = { column : string; op : string; value : string }

type HoneycombJson =
  { filters : List<HoneycombFilter>
    limit : int
    time_range : int }

let honeycombLinkOfExecutionID (executionID : ExecutionID) : string =
  let query =
    { filters = [ { column = "execution_id"; op = "="; value = string executionID } ]
      limit = 100
      // 604800 is 7 days
      time_range = 604800 }

  let queryStr = Json.Vanilla.serialize query

  let uri =
    System.Uri(
      $"https://ui.honeycomb.io/dark/datasets/kubernetes-bwd-ocaml?query={queryStr}"
    )

  string uri

let send
  (executionID : ExecutionID)
  (metadata : List<string * string>)
  (e : exn)
  : unit =
  assert initialized

  try
    print "sending exception to rollbar"
    let (state : Dictionary.T<string, obj>) = Dictionary.empty ()
    state["message.honeycomb"] <- honeycombLinkOfExecutionID executionID
    state["execution_id"] <- string executionID
    List.iter
      (fun (k, v) ->
        Dictionary.add k (v :> obj) state |> ignore<Dictionary.T<string, obj>>)
      metadata

    let (_ : Rollbar.ILogger) =
      Rollbar.RollbarLocator.RollbarInstance.Error(e, state)

    ()
  with
  | e ->
    // FSTODO: log failure
    print "Exception when calling rollbar"

module AspNet =
  open Microsoft.Extensions.DependencyInjection
  open Rollbar.NetCore.AspNet
  open Microsoft.AspNetCore.Builder
  open Microsoft.AspNetCore.Http.Abstractions

  // Rollbar's ASP.NET core middleware requires an IHttpContextAccessor, which
  // supposedly costs significant performance (couldn't see a cost in practice
  // though). AFAICT, this allows HTTP vars to be shared across the Task using an
  // AsyncContext. This would make sense for a lot of ways to use Rollbar, but we use
  // telemetry for our context and only want to use rollbar for exception tracking.
  type DarkRollbarMiddleware(nextRequestProcessor : RequestDelegate) =
    member this._nextRequestProcessor : RequestDelegate = nextRequestProcessor
    member this.Invoke(ctx : HttpContext) : Task =
      task {
        try
          do! this._nextRequestProcessor.Invoke(ctx)
        with
        | e ->
          send (Telemetry.executionID ()) [] e
          print e.Message
          print e.StackTrace
          raise e
      }



  let addRollbarToServices (services : IServiceCollection) : IServiceCollection =
    // Nothing to do here, as rollbar is initialized above
    services

  let addRollbarToApp (app : IApplicationBuilder) : IApplicationBuilder =
    app.UseMiddleware<DarkRollbarMiddleware>()



// FSTODO enrich this
// let error_to_payload =
//   let context =
//     match ctx with
//     | Remote _ ->
//         `String "server"
//     | EventQueue ->
//         `String "event queue worker"
//     | CronChecker ->
//         `String "cron event emitter"
//     | GarbageCollector ->
//         `String "garbage collector worker"
//     | Push _ ->
//         `String "server push"
//     | Other str ->
//         `String str
//     | Heapio event ->
//         `String (sprintf "heapio: %s" event)
//   in
//   let env = `String Config.rollbar_environment in
//   let language = `String "OCaml" in
//   let framework = `String "Cohttp" in
//   let level = if pageable then `String "critical" else `String "error" in
//   let payload =
//     match ctx with
//     | Remote request_data ->
//         let request =
//           let headers =
//             request_data.headers |> List.Assoc.map ~f:(fun v -> `String v)
//           in
//           [ ("url", `String ("https:" ^ request_data.url))
//           ; ("method", `String request_data.http_method)
//           ; ("headers", `Assoc headers)
//           ; ("execution_id", `String execution_id)
//           ; ("body", `String request_data.body) ]
//           |> fun r -> `Assoc r
//         in
//         [ ("body", message)
//         ; ("level", level)
//         ; ("environment", env)
//         ; ("language", language)
//         ; ("framework", framework)
//         ; ("context", context)
//         ; ("execution_id", `String execution_id)
//         ; ("request", request) ]
//     | EventQueue | CronChecker | GarbageCollector ->
//         [ ("body", message)
//         ; ("level", level)
//         ; ("environment", env)
//         ; ("language", language)
//         ; ("framework", framework)
//         ; ("execution_id", `String execution_id)
//         ; ("context", context) ]
//     | Push event | Heapio event ->
//         [ ("body", message)
//         ; ("level", level)
//         ; ("environment", env)
//         ; ("language", language)
//         ; ("framework", framework)
//         ; ("execution_id", `String execution_id)
//         ; ("context", context)
//         ; ("push_event", `String event) ]
//     | Other str ->
//         [ ("body", message)
//         ; ("level", level)
//         ; ("environment", env)
//         ; ("language", language)
//         ; ("framework", framework)
//         ; ("execution_id", `String execution_id)
//         ; ("context", context) ]
//   in
//   payload |> fun p -> `Assoc p
