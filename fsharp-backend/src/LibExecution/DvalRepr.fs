module LibExecution.DvalRepr

// Printing Dvals is more complicated than you'd expect. Different situations
// have different constaints, such as develop-focused representation showing
// explicitly what the value is, vs an API-based representation which does
// something clever with Option/Result types. There is also versioning, as not
// all changes are going to be backward compatible.

// Note: we inline a lot of code which could be reused. This is deliberate: it
// allows us reason more easily about what changes are going to be safe. In
// general, we should avoid general purpose or reusable functions in this file.

open Prelude
open VendoredTablecloth

open RuntimeTypes

// FSTODO: move everything in this file into where it's used

// I tried System.Text.Json but ran into a number of problems:
//
// - Infinity/Nan: The original OCaml Yojson converter represented these
// special floats as invalid JSON, which System.Text.Json threw an exception
// over.
//
// - 82.0: It was possible to workaround this using incredibly ugly hacks, but
// by default 82.0 would be printed as `82` and there was no way to change
// that.

open Newtonsoft.Json
open Newtonsoft.Json.Linq

// TODO CLEANUP - remove all the unsafeDval by inlining them into the named
// functions that use them, such as toQueryable or toRoundtrippable

let writeJson (f : JsonWriter -> unit) : string =
  let stream = new System.IO.StringWriter()
  let w = new JsonTextWriter(stream)
  // Match yojson
  w.FloatFormatHandling <- FloatFormatHandling.Symbol
  f w
  string stream

let writePrettyJson (f : JsonWriter -> unit) : string =
  let stream = new System.IO.StringWriter()
  let w = new JsonTextWriter(stream)
  // Match yojson
  w.FloatFormatHandling <- FloatFormatHandling.Symbol
  w.Formatting <- Formatting.Indented
  f w
  string stream



let parseJson (s : string) : JToken =
  let reader = new JsonTextReader(new System.IO.StringReader(s))
  let jls = JsonLoadSettings()
  jls.CommentHandling <- CommentHandling.Load // Load them so we can error later
  jls.DuplicatePropertyNameHandling <- DuplicatePropertyNameHandling.Error
  jls.CommentHandling <- CommentHandling.Ignore

  reader.DateParseHandling <- DateParseHandling.None
  JToken.ReadFrom(reader)

type JsonWriter with

  member this.writeObject(f : unit -> unit) =
    this.WriteStartObject()
    f ()
    this.WriteEnd()

  member this.writeArray(f : unit -> unit) =
    this.WriteStartArray()
    f ()
    this.WriteEnd()

let (|JString|_|) (j : JToken) : Option<string> =
  match j.Type with
  | JTokenType.String -> Some(JString(j.Value<string>()))
  | _ -> None

let (|JNull|_|) (j : JToken) : Option<unit> =
  match j.Type with
  | JTokenType.Null -> Some(JNull)
  | _ -> None

let (|JInteger|_|) (j : JToken) : Option<int64> =
  match j.Type with
  | JTokenType.Integer -> Some(JInteger(j.Value<int64>()))
  | _ -> None

let (|JFloat|_|) (j : JToken) : Option<float> =
  match j.Type with
  | JTokenType.Float -> Some(JFloat(j.Value<float>()))
  | _ -> None

let (|JBoolean|_|) (j : JToken) : Option<bool> =
  match j.Type with
  | JTokenType.Boolean -> Some(JBoolean(j.Value<bool>()))
  | _ -> None

let (|JList|_|) (j : JToken) : Option<List<JToken>> =
  match j.Type with
  | JTokenType.Array -> Some(JList(j.Values<JToken>() |> Seq.toList))
  | _ -> None

let (|JObject|_|) (j : JToken) : Option<List<string * JToken>> =
  match j.Type with
  | JTokenType.Object ->
    let list =
      j.Values()
      |> seq
      |> Seq.toList
      |> List.map (fun (jp : JProperty) -> (jp.Name, jp.Value))

    Some(JObject list)
  | _ -> None


let (|JNonStandard|_|) (j : JToken) : Option<unit> =
  match j.Type with
  | JTokenType.None
  | JTokenType.Undefined
  | JTokenType.Constructor
  | JTokenType.Property
  | JTokenType.Guid
  | JTokenType.Raw
  | JTokenType.Bytes
  | JTokenType.TimeSpan
  | JTokenType.Uri
  | JTokenType.Comment
  | JTokenType.Date -> Some()
  | _ -> None

let ocamlStringOfFloat (f : float) : string =
  // We used OCaml's string_of_float in lots of different places and now we're
  // reliant on it. Ugh.  string_of_float in OCaml is C's sprintf with the
  // format "%.12g".
  // https://github.com/ocaml/ocaml/blob/4.07/stdlib/stdlib.ml#L274

  // CLEANUP We should move on to a nicer format. See DvalRepr.tests for edge cases. See:
  if System.Double.IsPositiveInfinity f then
    "inf"
  else if System.Double.IsNegativeInfinity f then
    "-inf"
  else if System.Double.IsNaN f then
    "nan"
  else
    let result = sprintf "%.12g" f
    if result.Contains "." then result else $"{result}."


let ocamlBytesToString (bytes : byte []) =
  // CLEANUP: dumping these as ASCII isn't a great look
  System.Text.Encoding.UTF8.GetString bytes

// -------------------------
// Runtime Types
// -------------------------


// As of Wed Apr 21, 2021, this fn is only used for things that are shown to
// developers, and not for storage or any other thing that needs to be kept
// backwards-compatible.
// CLEANUP: once we no longer support compatibility with OCaml, these messages can get much better.
let rec typeToDeveloperReprV0 (t : DType) : string =
  match t with
  | TInt -> "Int"
  | TFloat -> "Float"
  | TBool -> "Bool"
  | TNull -> "Null"
  | TChar -> "Character"
  | TStr -> "String"
  | TList _ -> "List"
  | TDict _ -> "Dict"
  | TRecord _ -> "Dict"
  | TFn _ -> "Block"
  | TVariable varname -> "Any"
  | TIncomplete -> "Incomplete"
  | TError -> "Error"
  | THttpResponse _ -> "Response"
  | TDB _ -> "Datastore"
  | TDate -> "Date"
  | TPassword -> "Password"
  | TUuid -> "UUID"
  | TOption _ -> "Option"
  | TErrorRail -> "ErrorRail"
  | TResult _ -> "Result"
  | TUserType (name, _) -> name
  | TBytes -> "Bytes"

let prettyTypename (dv : Dval) : string = dv |> Dval.toType |> typeToDeveloperReprV0

// Backwards compatible version of `typeToDeveloperRepr`, should not be visible to
// users but used by things like HttpClient (transitively)
let rec typeToBCTypeName (t : DType) : string =
  match t with
  | TInt -> "int"
  | TFloat -> "float"
  | TBool -> "bool"
  | TNull -> "null"
  | TChar -> "character"
  | TStr -> "string"
  | TList _ -> "list"
  | TDict _ -> "dict"
  | TRecord _ -> "dict"
  | TFn _ -> "block"
  | TVariable varname -> "any"
  | TIncomplete -> "incomplete"
  | TError -> "error"
  | THttpResponse _ -> "response"
  | TDB _ -> "datastore"
  | TDate -> "date"
  | TPassword -> "password"
  | TUuid -> "uuid"
  | TOption _ -> "option"
  | TErrorRail -> "errorrail"
  | TResult _ -> "result"
  | TUserType (name, _) -> String.toLowercase name
  | TBytes -> "bytes"

let rec toNestedString (reprfn : Dval -> string) (dv : Dval) : string =
  let rec inner (indent : int) (dv : Dval) : string =
    let nl = "\n" + String.replicate indent " "
    let inl = "\n" + String.replicate (indent + 2) " "
    let indent = indent + 2
    let recurse = inner indent

    match dv with
    | DList l ->
      if l = [] then
        "[]"
      else
        "[ " + inl + String.concat ", " (List.map recurse l) + nl + "]"
    | DObj o ->
      if o = Map.empty then
        "{}"
      else
        let strs =
          Map.fold [] (fun l key value -> (key + ": " + recurse value) :: l) o

        "{ " + inl + String.concat ("," + inl) strs + nl + "}"
    | _ -> reprfn dv

  inner 0 dv

let toEnduserReadableTextV0 (dval : Dval) : string =
  let rec nestedreprfn dv =
    (* If nesting inside an object or a list, wrap strings in quotes *)
    match dv with
    | DStr _
    | DUuid _
    | DChar _ -> "\"" + reprfn dv + "\""
    | _ -> reprfn dv

  and reprfn dv =
    match dv with
    | DInt i -> string i
    | DBool true -> "true"
    | DBool false -> "false"
    | DStr s -> s
    | DFloat f -> ocamlStringOfFloat f

    | DChar c -> c
    | DNull -> "null"
    | DDate d -> d.toIsoString ()
    | DUuid uuid -> string uuid
    | DDB dbname -> $"<DB: {dbname}>"
    | DError (_, msg) ->
      // FSTODO make this a string again
      $"Error: {msg}"
    | DIncomplete _ -> "<Incomplete>"
    | DFnVal _ ->
      // See docs/dblock-serialization.ml
      "<Block>"
    | DPassword _ ->
      // redacting, do not unredact
      "<Password>"
    | DObj _
    | DList _ -> toNestedString nestedreprfn dv
    | DErrorRail d ->
      // We don't print error here, because the errorrail value will know
      // whether it's an error or not.
      reprfn d
    | DHttpResponse (Redirect url) -> $"302 {url}\n" + nestedreprfn DNull
    | DHttpResponse (Response (code, headers, body)) ->
      let headerString =
        headers
        |> List.map (fun (k, v) -> k + ": " + v)
        |> String.concat ","
        |> fun s -> "{ " + s + " }"

      $"{code} {headerString}" + "\n" + nestedreprfn body
    | DResult (Ok d) -> reprfn d
    | DResult (Error d) -> "Error: " + reprfn d
    | DOption (Some d) -> reprfn d
    | DOption None -> "Nothing"
    | DBytes bytes -> ocamlBytesToString bytes

  reprfn dval

let rec toPrettyMachineJsonV1 (w : JsonWriter) (dv : Dval) : unit =
  let writeDval = toPrettyMachineJsonV1 w
  // utf8jsonwriter has different methods for writing into objects vs arrays.
  // Dark doesn't assume the outermost value is an array or object, so try
  // writing the array version to start, then recurse between the two as
  // appropriate based on the content.
  match dv with
  (* basic types *)
  | DInt i -> w.WriteValue i
  | DFloat f -> w.WriteValue f
  | DBool b -> w.WriteValue b
  | DNull -> w.WriteNull()
  | DStr s -> w.WriteValue s
  | DList l -> w.writeArray (fun () -> List.iter writeDval l)
  | DObj o ->
    w.writeObject (fun () ->
      Map.iter
        (fun k v ->
          w.WritePropertyName k
          writeDval v)
        o)
  | DFnVal _ ->
    (* See docs/dblock-serialization.ml *)
    w.WriteNull()
  | DIncomplete _ -> w.WriteNull()
  | DChar c -> w.WriteValue c
  | DError (_, msg) ->
    w.writeObject (fun () ->
      w.WritePropertyName "Error"
      w.WriteValue msg)
  | DHttpResponse (Redirect _) -> writeDval DNull
  | DHttpResponse (Response (_, _, response)) -> writeDval response
  | DDB dbName -> w.WriteValue dbName
  | DDate date -> w.WriteValue(date.toIsoString ())
  | DPassword hashed ->
    w.writeObject (fun () ->
      w.WritePropertyName "Error"
      w.WriteValue "Password is redacted")
  | DUuid uuid -> w.WriteValue uuid
  | DOption opt ->
    match opt with
    | None -> w.WriteNull()
    | Some v -> writeDval v
  | DErrorRail dv -> writeDval dv
  | DResult res ->
    (match res with
     | Ok dv -> writeDval dv
     | Error dv ->
       w.writeObject (fun () ->
         w.WritePropertyName "Error"
         writeDval dv))
  | DBytes bytes ->
    // CLEANUP: rather than using a mutable byte array, should this be a readonly span?
    w.WriteValue(System.Convert.ToBase64String bytes)


let toPrettyMachineJsonStringV1 (dval : Dval) : string =
  writePrettyJson (fun w -> toPrettyMachineJsonV1 w dval)

// This special format was originally the default OCaml (yojson-derived) format
// for this.
let responseOfJson (dv : Dval) (j : JToken) : DHTTP =
  match j with
  | JList [ JString "Redirect"; JString url ] -> Redirect url
  | JList [ JString "Response"; JInteger code; JList headers ] ->
    let headers =
      headers
      |> List.map (function
        | JList [ JString k; JString v ] -> (k, v)
        | _ -> failwith "Invalid DHttpResponse headers")

    Response(code, headers, dv)
  | _ -> failwith "invalid response json"

#nowarn "104" // ignore warnings about enums out of range

// The "unsafe" variations here are bad. They encode data ambiguously, and
// though we mostly have the decoding right, it's brittle and unsafe.  This
// should be considered append only. There's a ton of dangerous things in this,
// and we really need to move off it, but for now we're here. Do not change
// existing encodings - this will break everything.
let rec unsafeDvalOfJsonV0 (json : JToken) : Dval =
  let convert = unsafeDvalOfJsonV0

  match json with
  | JInteger i -> DInt i
  | JFloat f -> DFloat f
  | JBoolean b -> DBool b
  | JNull -> DNull
  | JString s -> DStr s
  | JList l ->
    // We shouldnt have saved dlist that have incompletes or error rails but we might have
    l |> List.map convert |> Dval.list

  | JObject fields ->
    let fields = fields |> List.sortBy (fun (k, _) -> k)
    // These are the only types that are allowed in the queryable
    // representation. We may allow more in the future, but the real thing to
    // do is to use the DB's type and version to encode/decode them correctly
    match fields with
    // DResp (Result.ok_or_failwith (dhttp_of_yojson a), unsafe_dval_of_yojson_v0 b)
    | [ ("type", JString "response"); ("value", JList [ a; b ]) ] ->
      DHttpResponse(responseOfJson (convert b) a)
    | [ ("type", JString "date"); ("value", JString v) ] ->
      DDate(System.DateTime.ofIsoString v)
    | [ ("type", JString "password"); ("value", JString v) ] ->
      v |> Base64.fromDefaultEncoded |> Base64.decode |> Password |> DPassword
    | [ ("type", JString "error"); ("value", JString v) ] -> DError(SourceNone, v)
    | [ ("type", JString "bytes"); ("value", JString v) ] ->
      // Note that the OCaml version uses the non-url-safe b64 encoding here
      v |> System.Convert.FromBase64String |> DBytes
    | [ ("type", JString "char"); ("value", JString v) ] -> DChar v
    | [ ("type", JString "character"); ("value", JString v) ] -> DChar v
    | [ ("type", JString "datastore"); ("value", JString v) ] -> DDB v
    | [ ("type", JString "incomplete"); ("value", JNull) ] -> DIncomplete SourceNone
    | [ ("type", JString "errorrail"); ("value", dv) ] -> DErrorRail(convert dv)
    | [ ("type", JString "option"); ("value", JNull) ] -> DOption None
    | [ ("type", JString "option"); ("value", dv) ] -> DOption(Some(convert dv))
    | [ ("type", JString "block"); ("value", JNull) ] ->
      // See docs/dblock-serialization.ml
      DFnVal(
        Lambda { body = EBlank(id 56789); symtable = Map.empty; parameters = [] }
      )
    | [ ("type", JString "uuid"); ("value", JString v) ] -> DUuid(System.Guid v)
    | [ ("constructor", JString "Ok")
        ("type", JString "result")
        ("values", JList [ dv ]) ] -> DResult(Ok(convert dv))
    | [ ("constructor", JString "Error")
        ("type", JString "result")
        ("values", JList [ dv ]) ] -> DResult(Error(convert dv))
    | _ -> fields |> List.map (fun (k, v) -> (k, convert v)) |> Map.ofList |> DObj
  // Json.NET does a bunch of magic based on the contents of various types.
  // For example, it has tokens for Dates, constructors, etc. We've tried to
  // disable all those so we fail if we see them. However, we might need to
  // just convert some of these into strings.
  | JNonStandard
  | _ -> failwith $"Invalid type in json: {json}"



// and unsafe_dvalmap_of_yojson_v0 (json : Yojson.Safe.t) : dval_map =
//   match json with
//   | `Assoc alist ->
//       List.fold_left
//         alist
//         ~f:(fun m (k, v) ->
//           DvalMap.insert m ~key:k ~value:(unsafe_dval_of_yojson_v0 v))
//         ~init:DvalMap.empty
//   | _ ->
//       Exception.internal "Not a json object"

// Convert a dval (already converted from json) into
let rec unsafeDvalOfJsonV1 (json : JToken) : Dval =
  let convert = unsafeDvalOfJsonV1

  match json with
  | JInteger i -> DInt i
  | JFloat f -> DFloat f
  | JBoolean b -> DBool b
  | JNull -> DNull
  | JString s -> DStr s
  | JList l ->
    // We shouldnt have saved dlist that have incompletes or error rails but we might have
    l |> List.map convert |> Dval.list
  | JObject fields ->
    let fields = fields |> List.sortBy (fun (k, _) -> k)
    // These are the only types that are allowed in the queryable
    // representation. We may allow more in the future, but the real thing to
    // do is to use the DB's type and version to encode/decode them correctly
    match fields with
    // DResp (Result.ok_or_failwith (dhttp_of_yojson a), unsafe_dval_of_yojson_v0 b)
    | [ ("type", JString "response"); ("value", JList [ a; b ]) ] ->
      DHttpResponse(responseOfJson (convert b) a)
    | [ ("type", JString "date"); ("value", JString v) ] ->
      DDate(System.DateTime.ofIsoString v)
    | [ ("type", JString "password"); ("value", JString v) ] ->
      v |> Base64.fromEncoded |> Base64.decode |> Password |> DPassword
    | [ ("type", JString "error"); ("value", JString v) ] -> DError(SourceNone, v)
    | [ ("type", JString "bytes"); ("value", JString v) ] ->
      // Note that the OCaml version uses the non-url-safe b64 encoding here
      v |> System.Convert.FromBase64String |> DBytes
    | [ ("type", JString "char"); ("value", JString v) ] ->
      v |> String.toEgcSeq |> Seq.head |> DChar
    | [ ("type", JString "character"); ("value", JString v) ] ->
      v |> String.toEgcSeq |> Seq.head |> DChar
    | [ ("type", JString "datastore"); ("value", JString v) ] -> DDB v
    | [ ("type", JString "incomplete"); ("value", JNull) ] -> DIncomplete SourceNone
    | [ ("type", JString "errorrail"); ("value", dv) ] -> DErrorRail(convert dv)
    | [ ("type", JString "option"); ("value", JNull) ] -> DOption None
    | [ ("type", JString "option"); ("value", dv) ] -> DOption(Some(convert dv))
    | [ ("type", JString "block"); ("value", JNull) ] ->
      // See docs/dblock-serialization.ml
      DFnVal(
        Lambda { body = EBlank(id 23456); symtable = Map.empty; parameters = [] }
      )
    | [ ("type", JString "uuid"); ("value", JString v) ] -> DUuid(System.Guid v)
    | [ ("constructor", JString "Ok")
        ("type", JString "result")
        ("values", JList [ dv ]) ] -> DResult(Ok(convert dv))
    | [ ("constructor", JString "Error")
        ("type", JString "result")
        ("values", JList [ dv ]) ] -> DResult(Error(convert dv))
    | _ -> fields |> List.map (fun (k, v) -> (k, convert v)) |> Map.ofList |> DObj
  // Json.NET does a bunch of magic based on the contents of various types.
  // For example, it has tokens for Dates, constructors, etc. We've tried to
  // disable all those so we fail if we see them. However, we might need to
  // just convert some of these into strings.
  | JNonStandard
  | _ -> failwith $"Invalid type in json: {json}"


// and unsafeDvalmapOfJsonV1 (j : J.JsonValue) : DvalMap =
//   match j with
//   | J.JsonValue.Record records ->
//       Array.fold
//         Map.empty
//         (fun m (k, v) -> Map.add k (unsafeDvalOfJsonV1 v) m)
//         records
//   | _ -> failwith "Not a json object"

let rec unsafeDvalToJsonValueV0 (w : JsonWriter) (redact : bool) (dv : Dval) : unit =
  let writeDval = unsafeDvalToJsonValueV0 w redact

  let wrapStringValue (typ : string) (str : string) =
    w.writeObject (fun () ->
      w.WritePropertyName "type"
      w.WriteValue(typ)
      w.WritePropertyName "value"
      w.WriteValue(str))

  let wrapNullValue (typ : string) =
    w.writeObject (fun () ->
      w.WritePropertyName "type"
      w.WriteValue(typ)
      w.WritePropertyName "value"
      w.WriteNull())

  let wrapNestedDvalValue (typ : string) (dv : Dval) =
    w.writeObject (fun () ->
      w.WritePropertyName "type"
      w.WriteValue(typ)
      w.WritePropertyName "value"
      writeDval dv)

  match dv with
  (* basic types *)
  | DInt i -> w.WriteValue i
  | DFloat f -> w.WriteValue f
  | DBool b -> w.WriteValue b
  | DNull -> w.WriteNull()
  | DStr s -> w.WriteValue s
  | DList l -> w.writeArray (fun () -> List.iter writeDval l)
  | DObj o ->
    w.writeObject (fun () ->
      Map.iter
        (fun k v ->
          w.WritePropertyName k
          writeDval v)
        o)
  | DFnVal _ ->
    // See docs/dblock-serialization.md
    wrapNullValue "block"
  | DIncomplete _ -> wrapNullValue "incomplete"
  | DChar c -> wrapStringValue "character" c
  | DError (_, msg) -> wrapStringValue "error" msg
  | DHttpResponse (h) ->
    w.writeObject (fun () ->
      w.WritePropertyName "type"
      w.WriteValue "response"
      w.WritePropertyName "value"

      w.writeArray (fun () ->
        match h with
        | Redirect str ->
          w.writeArray (fun () ->
            w.WriteValue "Redirect"
            w.WriteValue str)

          writeDval DNull
        | Response (code, headers, hdv) ->
          w.writeArray (fun () ->
            w.WriteValue "Response"
            w.WriteValue code

            w.writeArray (fun () ->
              List.iter
                (fun (k : string, v : string) ->
                  w.writeArray (fun () ->
                    w.WriteValue k
                    w.WriteValue v))
                headers))

          writeDval hdv))
  | DDB dbname -> wrapStringValue "datastore" dbname
  | DDate date -> wrapStringValue "date" (date.toIsoString ())
  | DPassword (Password hashed) ->
    if redact then
      wrapNullValue "password"
    else
      hashed |> Base64.defaultEncodeToString |> wrapStringValue "password"
  | DUuid uuid -> wrapStringValue "uuid" (string uuid)
  | DOption opt ->
    (match opt with
     | None -> wrapNullValue "option"
     | Some ndv -> wrapNestedDvalValue "option" ndv)
  | DErrorRail erdv -> wrapNestedDvalValue "errorrail" erdv
  | DResult res ->
    (match res with
     | Ok rdv ->
       w.writeObject (fun () ->
         w.WritePropertyName "type"
         w.WriteValue("result")
         w.WritePropertyName "constructor"
         w.WriteValue("Ok")
         w.WritePropertyName "values"
         w.writeArray (fun () -> writeDval rdv))
     | Error rdv ->
       w.writeObject (fun () ->
         w.WritePropertyName "type"
         w.WriteValue("result")
         w.WritePropertyName "constructor"
         w.WriteValue("Error")
         w.WritePropertyName "values"
         w.writeArray (fun () -> writeDval rdv)))
  | DBytes bytes ->
    // Note that the OCaml version uses the non-url-safe b64 encoding here
    bytes |> System.Convert.ToBase64String |> wrapStringValue "bytes"


let unsafeDvalToJsonValueV1 (w : JsonWriter) (redact : bool) (dv : Dval) : unit =
  unsafeDvalToJsonValueV0 w redact dv

(* ------------------------- *)
(* Roundtrippable - for events and traces *)
(* ------------------------- *)
let toInternalRoundtrippableV0 (dval : Dval) : string =
  writeJson (fun w -> unsafeDvalToJsonValueV1 w false dval)

// Used for fuzzing and to document what's supported. There are a number of
// known bugs in our roundtripping in OCaml - we actually want to reproduce
// these in the F# implementation to make sure nothing changes. We return false
// if any of these appear unless "allowKnownBuggyValues" is true.
let isRoundtrippableDval (allowKnownBuggyValues : bool) (dval : Dval) : bool =
  match dval with
  | DChar c when c.Length = 1 -> true
  | DChar _ -> false // invalid
  | DStr _ -> true
  | DInt _ -> true
  | DNull _ -> true
  | DBool _ -> true
  | DFloat _ -> true
  | DList ls when not allowKnownBuggyValues ->
    // CLEANUP: Bug where Lists containing fake dvals will be replaced with
    // the fakeval
    not (List.any Dval.isFake ls)
  | DList _ -> true
  | DObj _ -> true
  | DDate _ -> true
  | DPassword _ -> true
  | DUuid _ -> true
  | DBytes _ -> true
  | DHttpResponse _ -> true
  | DOption (Some DNull) when not allowKnownBuggyValues ->
    // CLEANUP: Bug where Lists containing fake dvals will be replaced with
    // the fakeval
    false
  | DOption _ -> true
  | DResult _ -> true
  | DDB _ -> true
  | DError _ -> true
  | DIncomplete _ -> true
  | DErrorRail _ -> true
  | DFnVal _ -> false // not supported

let ofInternalRoundtrippableJsonV0 (j : JToken) : Result<Dval, string> =
  (* Switched to v1 cause it was a bug fix *)
  try
    unsafeDvalOfJsonV1 j |> Ok
  with
  | e -> Error(string e)

let ofInternalRoundtrippableV0 (str : string) : Dval =
  // cleanup: we know the types here, so we should probably do type directed parsing and simplify what's stored
  str |> parseJson |> unsafeDvalOfJsonV1

// -------------------------
// Queryable - for the DB *)
// -------------------------

let toInternalQueryableV1 (dvalMap : DvalMap) : string =
  writeJson (fun w ->
    w.WriteStartObject()

    dvalMap
    |> Map.toList
    |> List.iter (fun (k, dval) ->
      (w.WritePropertyName k
       unsafeDvalToJsonValueV0 w false dval))

    w.WriteEnd())

let isQueryableDval (dval : Dval) : bool =
  match dval with
  | DStr _ -> true
  | DInt _ -> true
  | DNull _ -> true
  | DBool _ -> true
  | DFloat _ -> true
  | DList _ -> true
  | DObj _ -> true
  | DDate _ -> true
  | DPassword _ -> true
  | DUuid _ -> true
  // TODO support
  | DChar _ -> false
  | DBytes _ -> false
  | DHttpResponse _ -> false
  | DOption _ -> false
  | DResult _ -> false
  // Not supportable I think
  | DDB _ -> false
  | DFnVal _ -> false // not supported
  | DError _ -> false
  | DIncomplete _ -> false
  | DErrorRail _ -> false

let ofInternalQueryableV1 (str : string) : Dval =
  // The first level _must_ be an object at the moment
  let rec convertTopLevel (json : JToken) : Dval =
    match json with
    | JObject _ -> convert json
    | _ -> failwith "Value that isn't an object"

  and convert (json : JToken) : Dval =
    match json with
    | JInteger i -> DInt i
    | JFloat f -> DFloat f
    | JBoolean b -> DBool b
    | JNull -> DNull
    | JString s -> DStr s
    | JList l ->
      // We shouldnt have saved dlist that have incompletes or error rails but we might have
      l |> List.map convert |> Dval.list
    | JObject fields ->
      let fields = fields |> List.sortBy (fun (k, _) -> k)
      // These are the only types that are allowed in the queryable
      // representation. We may allow more in the future, but the real thing to
      // do is to use the DB's type and version to encode/decode them correctly
      match fields with
      | [ ("type", JString "date"); ("value", JString v) ] ->
        DDate(System.DateTime.ofIsoString v)
      | [ ("type", JString "password"); ("value", JString v) ] ->
        v |> Base64.decodeFromString |> Password |> DPassword
      | [ ("type", JString "uuid"); ("value", JString v) ] -> DUuid(System.Guid v)
      | _ -> fields |> List.map (fun (k, v) -> (k, convert v)) |> Map.ofList |> DObj
    // Json.NET does a bunch of magic based on the contents of various types.
    // For example, it has tokens for Dates, constructors, etc. We've tried to
    // disable all those so we fail if we see them. However, we might need to
    // just convert some of these into strings.
    | JNonStandard _
    | _ -> failwith $"Invalid type in json: {json}"

  str |> parseJson |> convertTopLevel

// -------------------------
// Other formats
// -------------------------
let rec toDeveloperReprV0 (dv : Dval) : string =
  let rec toRepr_ (indent : int) (dv : Dval) : string =
    let makeSpaces len = "".PadRight(len, ' ')
    let nl = "\n" + makeSpaces indent
    let inl = "\n" + makeSpaces (indent + 2)
    let indent = indent + 2
    let typename = prettyTypename dv
    let wrap str = $"<{typename}: {str}>"
    let justtipe = $"<{typename}>"

    match dv with
    | DPassword _ -> "<password>"
    | DStr s -> $"\"{s}\""
    | DChar c -> $"'{c}'"
    | DInt i -> string i
    | DBool true -> "true"
    | DBool false -> "false"
    | DFloat f -> ocamlStringOfFloat f
    | DNull -> "null"
    | DFnVal _ ->
      (* See docs/dblock-serialization.ml *)
      justtipe
    | DIncomplete _ -> justtipe
    | DError (_, msg) -> wrap msg
    | DDate d -> wrap (d.toIsoString ())
    | DDB name -> wrap name
    | DUuid uuid -> wrap (string uuid)
    | DHttpResponse h ->
      match h with
      | Redirect url -> $"302 {url}" + nl + toRepr_ indent DNull
      | Response (code, headers, hdv) ->
        let headerString =
          headers
          |> List.map (fun (k, v) -> k + ": " + v)
          |> String.concat ","
          |> fun s -> "{ " + s + " }"

        $"{code} {headerString}" + nl + toRepr_ indent hdv
    | DList l ->
      if List.isEmpty l then
        "[]"
      else
        let elems = String.concat ", " (List.map (toRepr_ indent) l)
        // CLEANUP: this space makes no sense
        $"[ {inl}{elems}{nl}]"
    | DObj o ->
      if Map.isEmpty o then
        "{}"
      else
        let strs =
          Map.fold [] (fun l key value -> ($"{key}: {toRepr_ indent value}") :: l) o

        let elems = String.concat $",{inl}" strs
        // CLEANUP: this space makes no sense
        "{ " + $"{inl}{elems}{nl}" + "}"
    | DOption None -> "Nothing"
    | DOption (Some dv) -> "Just " + toRepr_ indent dv
    | DResult (Ok dv) -> "Ok " + toRepr_ indent dv
    | DResult (Error dv) -> "Error " + toRepr_ indent dv
    | DErrorRail dv -> "ErrorRail: " + toRepr_ indent dv
    | DBytes bytes -> bytes |> System.Convert.ToBase64String

  toRepr_ 0 dv


let ofUnknownJsonV0 str =
  try
    str |> parseJson |> unsafeDvalOfJsonV0
  with
  | _ -> failwith "Invalid json"


let ofUnknownJsonV1 str =
  // FSTODO: there doesn't seem to be a good reason that we use JSON.NET here,
  // might be better to switch to STJ
  let rec convert json =
    match json with
    | JInteger i -> DInt i
    | JFloat f -> DFloat f
    | JBoolean b -> DBool b
    | JNull -> DNull
    | JString s -> DStr s
    | JList l -> l |> List.map convert |> Dval.list
    | JObject fields ->
      fields |> List.fold Map.empty (fun m (k, v) -> Map.add k (convert v) m) |> DObj

    // Json.NET does a bunch of magic based on the contents of various types.
    // For example, it has tokens for Dates, constructors, etc. We've tried to
    // disable all those so we fail if we see them. However, we might need to
    // just convert some of these into strings.
    | JNonStandard
    | _ -> failwith $"Invalid type in json: {json}"

  try
    str |> parseJson |> convert
  with
  | :? JsonReaderException as e ->
    let msg = if str = "" then "JSON string was empty" else e.Message
    failwith msg


// let rec show dv =
//   match dv with
//   | DInt i ->
//       Dint.to_string i
//   | DBool true ->
//       "true"
//   | DBool false ->
//       "false"
//   | DStr s ->
//       Unicode_string.to_string s
//   | DFloat f ->
//       string_of_float f
//   | DCharacter c ->
//       Unicode_string.Character.to_string c
//   | DNull ->
//       "null"
//   | DDate d ->
//       Util.isostring_of_date d
//   | DUuid uuid ->
//       Uuidm.to_string uuid
//   | DDB dbname ->
//       "<DB: " ^ dbname ^ ">"
//   | DError (_, msg) ->
//       "<Error: " ^ msg ^ ">"
//   | DIncomplete SourceNone ->
//       "<Incomplete>"
//   | DIncomplete (SourceId (tlid, id)) ->
//       Printf.sprintf "<Incomplete[%s,%s]>" (string_of_id tlid) (string_of_id id)
//   | DBlock _ ->
//       (* See docs/dblock-serialization.ml *)
//       "<Block>"
//   | DPassword _ ->
//       (* redacting, do not unredact *)
//       "<Password>"
//   | DObj o ->
//       to_nested_string ~reprfn:show dv
//   | DList l ->
//       to_nested_string ~reprfn:show dv
//   | DErrorRail d ->
//       (* We don't print error here, because the errorrail value will know
//           * whether it's an error or not. *)
//       "<ErrorRail: " ^ show d ^ ">"
//   | DResp (dh, dv) ->
//       dhttp_to_formatted_string dh ^ "\n" ^ show dv ^ ""
//   | DResult (ResOk d) ->
//       "Ok " ^ show d
//   | DResult (ResError d) ->
//       "Error " ^ show d
//   | DOption (OptJust d) ->
//       "Just " ^ show d
//   | DOption OptNothing ->
//       "Nothing"
//   | DBytes bytes ->
//       "<Bytes: length=" ^ string_of_int (RawBytes.length bytes) ^ ">"
//
//
// let parse_literal (str : string) : dval option =
//   let len = String.length str in
//   (* Character *)
//   if len > 2 && str.[0] = '\'' && str.[len - 1] = '\''
//   then
//     Some
//       (DCharacter
//          (Unicode_string.Character.unsafe_of_string
//             (String.sub ~pos:1 ~len:(len - 2) str)))
//     (* String *)
//   else if len > 1 && str.[0] = '"' && str.[len - 1] = '"'
//   then
//     (* It might have \n characters in it (as well as probably other codes like
//      * \r or some shit that we haven't taken into account), which need to be
//      * converted manually to appropriate string chars. *)
//     str
//     |> String.sub ~pos:1 ~len:(len - 2)
//     |> Util.string_replace "\\\"" "\""
//     |> fun s -> Some (dstr_of_string_exn s)
//   else if str = "null"
//   then Some DNull
//   else if str = "true"
//   then Some (DBool true)
//   else if str = "false"
//   then Some (DBool false)
//   else
//     try Some (DInt (Dint.of_string_exn str))
//     with _ ->
//       ( match float_of_string_opt str with
//       | Some v ->
//           Some (DFloat v)
//       | None ->
//           None )
//



let toStringPairsExn (dv : Dval) : (string * string) list =
  match dv with
  | DObj obj ->
    obj
    |> Map.toList
    |> List.map (function
      | (k, DStr v) -> (k, v)
      | (k, v) ->
        // CLEANUP: this is just to keep the error messages the same with OCaml. It's safe to change the error message
        // failwith $"Expected a string, but got: {toDeveloperReprV0 v}"
        failwith "expecting str")
  | _ ->
    // CLEANUP As above
    // $"Expected a string, but got: {toDeveloperReprV0 dv}"
    failwith "expecting str"

// -------------------------
// URLs and queryStrings
// -------------------------

// For putting into URLs as query params
let rec toUrlStringExn (dv : Dval) : string =
  let r = toUrlStringExn
  match dv with
  | DFnVal _ ->
    (* See docs/dblock-serialization.ml *)
    "<block>"
  | DIncomplete _ -> "<incomplete>"
  | DPassword _ -> "<password>"
  | DInt i -> string i
  | DBool true -> "true"
  | DBool false -> "false"
  | DStr s -> s
  | DFloat f -> ocamlStringOfFloat f
  | DChar c -> c
  | DNull -> "null"
  | DDate d -> d.toIsoString ()
  | DDB dbname -> dbname
  | DErrorRail d -> r d
  | DError (_, msg : string) -> $"error={msg}"
  | DUuid uuid -> string uuid
  | DHttpResponse (Redirect _) -> "null"
  | DHttpResponse (Response (_, _, hdv)) -> r hdv
  | DList l -> "[ " + String.concat ", " (List.map r l) + " ]"
  | DObj o ->
    let strs = Map.fold [] (fun l key value -> (key + ": " + r value) :: l) o
    "{ " + (String.concat ", " strs) + " }"
  | DOption None -> "none"
  | DOption (Some v) -> r v
  | DResult (Error v) -> "error=" + r v
  | DResult (Ok v) -> r v
  | DBytes bytes -> Base64.defaultEncodeToString bytes

// Convert strings into queryParams. This matches the OCaml Uri.query function. Note that keys and values use slightly different encodings
let queryToEncodedString (queryParams : (List<string * List<string>>)) : string =
  match queryParams with
  | [ key, [] ] -> urlEncodeKey key
  | _ ->
    queryParams
    |> List.map (fun (k, vs) ->
      let k = k |> urlEncodeKey
      vs
      |> List.map urlEncodeValue
      |> fun vs ->
           if vs = [] then
             k
           else
             let vs = String.concat "," vs
             $"{k}={vs}")
    |> String.concat "&"

let toQuery (dv : Dval) : List<string * List<string>> =
  match dv with
  | DObj kvs ->
    kvs
    |> Map.toList
    |> List.map (fun (k, value) ->
      match value with
      | DNull -> (k, [])
      | DList l -> (k, List.map toUrlStringExn l)
      | _ -> (k, [ toUrlStringExn value ]))
  | _ -> failwith "attempting to use non-object as query param" // CODE exception


// The queryString passed in should not include the leading '?' from the URL
let parseQueryString (queryString : string) : List<string * List<string>> =
  // This will eat any intended question mark, so add one
  let nvc = System.Web.HttpUtility.ParseQueryString("?" + queryString)
  nvc.AllKeys
  |> Array.map (fun key ->
    let values = nvc.GetValues key
    let split =
      values[values.Length - 1] |> FSharpPlus.String.split [| "," |] |> Seq.toList

    if isNull key then
      // All the values with no key are by GetValues, so make each one a value
      values |> Array.toList |> List.map (fun k -> (k, []))
    else
      [ (key, split) ])
  |> List.concat

let ofQuery (query : List<string * List<string>>) : Dval =
  query
  |> List.map (fun (k, v) ->
    match v with
    | [] -> k, DNull
    | [ "" ] -> k, DNull // CLEANUP this should be a string
    | [ v ] -> k, DStr v
    | list -> k, DList(List.map DStr list))
  |> Map
  |> DObj


let ofQueryString (queryString : string) : Dval =
  queryString |> parseQueryString |> ofQuery

// -------------------------
// Forms
// -------------------------

let toFormEncoding (dv : Dval) : string = toQuery dv |> queryToEncodedString

let ofFormEncoding (f : string) : Dval = f |> parseQueryString |> ofQuery


// -------------------------
// Hashes
// -------------------------

// This has been used to save millions of values in our DB, so the format isn't
// amenable to change without a migration. Don't change ANYTHING for existing
// values, but continue to add representations for new values. Also, inline
// everything!
let rec toHashableRepr (indent : int) (oldBytes : bool) (dv : Dval) : byte [] =
  let makeSpaces len = "".PadRight(len, ' ')
  let nl = "\n" + makeSpaces indent
  let inl = "\n" + makeSpaces (indent + 2)
  let indent = indent + 2 in

  match dv with
  | DDB dbname -> ("<db: " + dbname + ">") |> UTF8.toBytes
  | DInt i -> string i |> UTF8.toBytes
  | DBool true -> "true" |> UTF8.toBytes
  | DBool false -> "false" |> UTF8.toBytes
  | DFloat f -> ocamlStringOfFloat f |> UTF8.toBytes
  | DNull -> "null" |> UTF8.toBytes
  | DStr s -> "\"" + string s + "\"" |> UTF8.toBytes
  | DChar c -> "'" + string c + "'" |> UTF8.toBytes
  | DIncomplete _ ->
    "<incomplete: <incomplete>>" |> UTF8.toBytes (* Can't be used anyway *)
  | DFnVal _ ->
    (* See docs/dblock-serialization.ml *)
    "<block: <block>>" |> UTF8.toBytes
  | DError (_, msg) -> "<error: " + msg + ">" |> UTF8.toBytes
  | DDate d -> "<date: " + d.toIsoString () + ">" |> UTF8.toBytes
  | DPassword _ -> "<password: <password>>" |> UTF8.toBytes
  | DUuid id -> "<uuid: " + string id + ">" |> UTF8.toBytes
  | DHttpResponse d ->
    let formatted, hdv =
      match d with
      | Redirect url -> ("302 " + url, DNull)
      | Response (c, hs, hdv) ->
        let stringOfHeaders hs =
          hs
          |> List.map (fun (k, v) -> k + ": " + v)
          |> String.concat ","
          |> fun s -> "{ " + s + " }"

        (string c + " " + stringOfHeaders hs, hdv)

    [ (formatted + nl) |> UTF8.toBytes; toHashableRepr indent false hdv ]
    |> Array.concat
  | DList l ->
    if List.isEmpty l then
      "[]" |> UTF8.toBytes
    else
      let body =
        l
        |> List.map (toHashableRepr indent false)
        |> List.intersperse (UTF8.toBytes ", ")
        |> Array.concat

      Array.concat [ "[ " |> UTF8.toBytes
                     inl |> UTF8.toBytes
                     body
                     nl |> UTF8.toBytes
                     "]" |> UTF8.toBytes ]
  | DObj o ->
    if Map.isEmpty o then
      "{}" |> UTF8.toBytes
    else
      let rows =
        o
        |> Map.fold [] (fun l key value ->
          (Array.concat [ UTF8.toBytes (key + ": ")
                          toHashableRepr indent false value ]
           :: l))
        |> List.intersperse (UTF8.toBytes ("," + inl))

      Array.concat (
        [ UTF8.toBytes "{ "; UTF8.toBytes inl ]
        @ rows @ [ UTF8.toBytes nl; UTF8.toBytes "}" ]
      )
  | DOption None -> "Nothing" |> UTF8.toBytes
  | DOption (Some dv) ->
    Array.concat [ "Just " |> UTF8.toBytes; toHashableRepr indent false dv ]
  | DErrorRail dv ->
    Array.concat [ "ErrorRail: " |> UTF8.toBytes; toHashableRepr indent false dv ]
  | DResult (Ok dv) ->
    Array.concat [ "ResultOk " |> UTF8.toBytes; toHashableRepr indent false dv ]
  | DResult (Error dv) ->
    Array.concat [ "ResultError " |> UTF8.toBytes; toHashableRepr indent false dv ]
  | DBytes bytes ->
    if oldBytes then
      bytes
    else
      bytes
      |> System.Security.Cryptography.SHA384.HashData
      |> Base64.urlEncodeToString
      |> UTF8.toBytes


let supportedHashVersions : int list = [ 0; 1 ]

let currentHashVersion : int = 1

// Originally to prevent storing sensitive data to disk, this also reduces the
// size of the data stored by only storing a hash
let hash (version : int) (arglist : List<Dval>) : string =
  let hashStr (bytes : byte []) : string =
    bytes
    |> System.Security.Cryptography.SHA384.HashData
    |> Base64.urlEncodeToString

  // Version 0 deprecated because it has a collision between [b"a"; b"bc"] and
  // [b"ab"; b"c"]
  match version with
  | 0 -> arglist |> List.map (toHashableRepr 0 true) |> Array.concat |> hashStr
  | 1 -> DList arglist |> toHashableRepr 0 false |> hashStr
  | _ -> failwith $"Invalid Dval.hash version: {version}"
