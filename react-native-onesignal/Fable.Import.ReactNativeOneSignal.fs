namespace Fable.Import

open System
open Fable.Core
open Fable.Import
open Fable.Import.JS
open Fable.Import.Browser

module ReactNativeOneSignal =

    type OneSignal =
        abstract member configure:OneSignalOptions -> unit

    and OneSignalOptions =
        abstract onIdsAvailable: obj -> unit

    and OneSignalID =
        abstract userId: string      
        abstract pushToken: string      

    type Globals =
        [<Import("default", from="react-native-onesignal")>]
        static member OneSignal with get(): OneSignal = failwith "JS only" and set(v: OneSignal): unit = failwith "JS only"