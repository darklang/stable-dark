module Tests.TypeChecker

// Test the type checker

open Expecto

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Prelude.Tablecloth
open Tablecloth

module RT = LibExecution.RuntimeTypes
module PT = LibBackend.ProgramTypes
module S = LibExecution.Shortcuts
module Exe = LibExecution.Execution
module TypeChecker = LibExecution.TypeChecker


open TestUtils


let testBasicTypecheckWorks : Test =
  let t
    ((fn, args) : string * List<string * RT.Dval>)
    : Result<unit, TypeChecker.Error.T list> =
    let args = Map.ofList args in

    let fn =
      fns.Force()
      |> Map.get (PT.FQFnName.parse fn)
      |> Option.unwrapUnsafe
      |> RT.builtInFnToFn

    TypeChecker.checkFunctionCall Map.empty fn args

  testMany
    "basic type checking"
    t
    [ ("Int::add_v0", [ ("a", RT.DInt 5I); ("b", RT.DInt 4I) ]), Ok()
      (("Int::add_v0", [ ("a", RT.DInt 5I); ("b", RT.DBool true) ]),
       Error(
         [ TypeChecker.Error.TypeUnificationFailure
             { expectedType = RT.TInt; actualValue = RT.DBool true } ]
       ))

      ("toString_v0", [ ("a", RT.DInt 5I) ]), Ok() ]

let testErrorNotWrappedByErrorRail =
  testTask "error not wrapped by errorRail" {
    let expr = FSharpToExpr.parseRTExpr "Dict.get_v1 (List.empty_v0 []) \"hello\""

    let! state = executionStateFor "error" Map.empty Map.empty

    let! result = Exe.run state Map.empty expr

    Expect.isTrue
      (match result with
       | RT.DError _ -> true
       | _ -> false)
      ""
  }

let testArguments : Test =
  let t (name, returnType, body) =
    task {

      let userFn : RT.UserFunction.T =
        { tlid = id 7
          name = name
          parameters = []
          returnType = returnType
          description = ""
          infix = false
          body = body }

      let expr = S.eApply (S.eUserFnVal name) []
      let fns = Map.ofList [ name, userFn ]
      let! state = executionStateFor "error" Map.empty fns
      let! result = Exe.run state Map.empty expr
      return normalizeDvalResult result
    }

  testManyTask
    "type check arguments"
    t
    [ (("myBadFn", RT.TStr, S.eInt 7),
       RT.DError(
         RT.SourceNone,
         "Type error(s) in return type: Expected to see a value of type String but found a Int"
       ))
      (("myGoodFn", RT.TStr, S.eStr "test"), RT.DStr "test")
      (("myAnyFn", RT.TVariable "a", S.eInt 5), RT.DInt 5I) ]




let tests =
  testList
    "typeChecker"
    [ testBasicTypecheckWorks; testErrorNotWrappedByErrorRail; testArguments ]


// let t_dark_internal_fns_are_internal () =
//   let ast = fn "DarkInternal::checkAccess" [] in
//   let check_access canvas_name =
//     match exec_ast ~canvas_name ast with DError _ -> None | dval -> Some dval
//   in
//   AT.check
//     (AT.list (AT.option at_dval))
//     "DarkInternal:: functions are internal."
//     [check_access "test"; check_access "test_admin"]
//     [None; Some DNull]
//
//
// (* ---------------- *)
// (* Dval hashing *)
// (* ---------------- *)
// let t_dval_hash_differs_for_version_0_and_1 () =
//   let arglist =
//     [ DBytes ("ab" |> Libtarget.bytes_from_base64url)
//     ; DBytes ("c" |> Libtarget.bytes_from_base64url) ]
//   in
//   AT.check
//     AT.bool
//     "DVal.hash differs for version 0 and 1"
//     false
//     (Dval.hash 0 arglist = Dval.hash 1 arglist)
//
