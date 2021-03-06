namespace Fable.React

open Fable.Core
open Fable.Core.JsInterop
open Browser
open Props

#if !FABLE_COMPILER
type HTMLNode =
    | Text of string
    | RawText of string
    | Node of string * IProp seq * ReactElement seq
    | List of ReactElement seq
    | Empty
with interface ReactElement

type ServerElementType =
    | Tag
    | Fragment
    | Component

type ReactElementTypeWrapper<'P> =
    | Comp of obj
    | Fn of ('P -> ReactElement)
    | HtmlTag of string
    interface ReactElementType<'P>

[<RequireQualifiedAccess>]
module ServerRendering =
    let [<Literal>] private ChildrenName = "children"

    let private createServerElementPrivate(tag: obj, props: obj, children: ReactElement seq, elementType: ServerElementType) =
        match elementType with
        | ServerElementType.Tag ->
            HTMLNode.Node (string tag, props :?> IProp seq, children) :> ReactElement
        | ServerElementType.Fragment ->
            HTMLNode.List children :> ReactElement
        | ServerElementType.Component ->
            let tag = tag :?> System.Type
            let comp = System.Activator.CreateInstance(tag, props)
            let childrenProp = tag.GetProperty(ChildrenName)
            childrenProp.SetValue(comp, children |> Seq.toArray)
            let render = tag.GetMethod("render")
            render.Invoke(comp, null) :?> ReactElement

    let private createServerElementByFnPrivate(f, props, children) =
        let propsType = props.GetType()
        let props =
            if propsType.GetProperty (ChildrenName) |> isNull then
                props
            else
                let values = ResizeArray<obj> ()
                let properties = propsType.GetProperties()
                for p in properties do
                    if p.Name = ChildrenName then
                        values.Add (children |> Seq.toArray)
                    else
                        values.Add (FSharp.Reflection.FSharpValue.GetRecordField(props, p))
                FSharp.Reflection.FSharpValue.MakeRecord(propsType, values.ToArray()) :?> 'P
        f props

    // In most cases these functions are inlined (mainly for Fable optimizations)
    // so we create a proxy to avoid inlining big functions every time

    let createServerElement(tag: obj, props: obj, children: ReactElement seq, elementType: ServerElementType) =
        createServerElementPrivate(tag, props, children, elementType)

    let createServerElementByFn(f, props, children) =
        createServerElementByFnPrivate(f, props, children)
#endif

[<RequireQualifiedAccess>]
module ReactElementType =
    let inline ofComponent<'comp, 'props, 'state when 'comp :> Component<'props, 'state>> : ReactElementType<'props> =
#if FABLE_REPL_LIB
        failwith "Cannot create React components from types in Fable REPL"
#else
#if FABLE_COMPILER
        jsConstructor<'comp> |> unbox
#else
        Comp (typeof<'comp>) :> _
#endif
#endif

    let inline ofFunction<'props> (f: 'props -> ReactElement): ReactElementType<'props> =
#if FABLE_COMPILER
        f |> unbox
#else
        Fn f :> _
#endif

    let inline ofHtmlElement<'props> (name: string): ReactElementType<'props> =
#if FABLE_COMPILER
        unbox name
#else
        HtmlTag name :> ReactElementType<'props>
#endif

    /// Create a ReactElement to be rendered from an element type, props and children
    let inline create<'props> (comp: ReactElementType<'props>) (props: 'props) (children: ReactElement seq): ReactElement =
#if FABLE_COMPILER
        ReactBindings.React.createElement(comp, props, children)
#else
        match (comp :?> ReactElementTypeWrapper<'props>) with
        | Comp obj -> ServerRendering.createServerElement(obj, props, children, ServerElementType.Component)
        | Fn f -> ServerRendering.createServerElementByFn(f, props, children)
        | HtmlTag obj -> ServerRendering.createServerElement(obj, props, children, ServerElementType.Tag)
#endif

    /// React.memo is a higher order component. It’s similar to React.PureComponent but for function components instead of classes.
    /// If your function component renders the same result given the same props, you can wrap it in a call to React.memo.
    /// React will skip rendering the component, and reuse the last rendered result.
    /// By default it will only shallowly compare complex objects in the props object. If you want control over the comparison, you can use `memoWith`.
    let memo<'props> (render: 'props -> ReactElement) =
#if FABLE_COMPILER
        ReactBindings.React.memo(render, unbox null)
#else
        ofFunction render
#endif

    /// React.memo is a higher order component. It’s similar to React.PureComponent but for function components instead of classes.
    /// If your function renders the same result given the "same" props (according to `areEqual`), you can wrap it in a call to React.memo.
    /// React will skip rendering the component, and reuse the last rendered result.
    /// By default it will only shallowly compare complex objects in the props object. If you want control over the comparison, you can use `memoWith`.
    /// This version allow you to control the comparison used instead of the default shallow one by provide a custom comparison function.
    let memoWith<'props> (areEqual: 'props -> 'props -> bool) (render: 'props -> ReactElement) =
#if FABLE_COMPILER
        ReactBindings.React.memo(render, areEqual)
#else
        ofFunction render
#endif


[<AutoOpen>]
module Helpers =
    [<System.Obsolete("Use ReactBindings.React.createElement")>]
    let inline createElement(comp: obj, props: obj, [<ParamList>] children: ReactElement seq): ReactElement =
#if FABLE_COMPILER
        ReactBindings.React.createElement(comp, props, children)
#else
        HTMLNode.Empty :> _
#endif

    /// Instantiate a component from a type inheriting React.Component
    /// Example: `ofType<MyComponent,_,_> { myProps = 5 } []`
    let inline ofType<'T,'P,'S when 'T :> Component<'P,'S>> (props: 'P) (children: ReactElement seq): ReactElement =
        ReactElementType.create ReactElementType.ofComponent<'T,_,_> props children

    [<System.Obsolete("Use ofType")>]
    let inline com<'T,'P,'S when 'T :> Component<'P,'S>> (props: 'P) (children: ReactElement seq): ReactElement =
        ofType<'T, 'P, 'S> props children

    [<System.Obsolete("Use FunctionComponent.Of to build a function component")>]
    let inline ofFunction<'P> (f: 'P -> ReactElement) (props: 'P) (children: ReactElement seq): ReactElement =
        ReactElementType.create (ReactElementType.ofFunction f) props children

    /// Instantiate an imported React component. The first two arguments must be string literals, "default" can be used for the first one.
    /// Example: `ofImport "Map" "leaflet" { x = 10; y = 50 } []`
    let inline ofImport<'P> (importMember: string) (importPath: string) (props: 'P) (children: ReactElement seq): ReactElement =
#if FABLE_REPL_LIB
        failwith "Cannot import React components in Fable REPL"
#else
#if FABLE_COMPILER
        ReactBindings.React.createElement(import importMember importPath, props, children)
#else
        failwith "Cannot import React components in .NET"
#endif
#endif

#if FABLE_COMPILER
    [<Emit("typeof $0 === 'function'")>]
    let private isFunction (x: obj): bool = jsNative

    [<Emit("typeof $0 === 'object' && !$0[Symbol.iterator]")>]
    let private isNonEnumerableObject (x: obj): bool = jsNative
#endif

    /// Same as F# equality but ignores functions in the first level of an object
    /// Useful in combination with memoBuilderWith for most cases (ignore Elmish dispatch, etc)
    let equalsButFunctions (x: 'a) (y: 'a) =
#if FABLE_COMPILER
        if obj.ReferenceEquals(x, y) then
            true
        elif isNonEnumerableObject x && not(isNull(box y)) then
            (true, JS.Object.keys (x)) ||> Seq.fold (fun eq k ->
                eq && (isFunction x?(k) || x?(k) = y?(k)))
        else (box x) = (box y)
#else
        // Server rendering, won't be actually used
        // Avoid `x = y` because it will force 'a to implement structural equality
        false
#endif

    [<System.Obsolete("Use FunctionComponent.Of with memoizedWith")>]
    let memoBuilder<'props> (name: string) (render: 'props -> ReactElement) : 'props -> ReactElement =
#if FABLE_COMPILER
        render?displayName <- name
#endif
        let memoType = ReactElementType.memo render
        fun props ->
            ReactElementType.create memoType props []

    [<System.Obsolete("Use FunctionComponent.Of with memoizedWith")>]
    let memoBuilderWith<'props> (name: string) (areEqual: 'props -> 'props -> bool) (render: 'props -> ReactElement) : 'props -> ReactElement =
#if FABLE_COMPILER
        render?displayName <- name
#endif
        let memoType = ReactElementType.memoWith areEqual render
        fun props ->
            ReactElementType.create memoType props []

    [<System.Obsolete("Use ReactElementType.create")>]
    let inline from<'P> (com: ReactElementType<'P>) (props: 'P) (children: ReactElement seq): ReactElement =
        ReactElementType.create com props children

    /// Alias of `ofString`
    let inline str (s: string): ReactElement =
#if FABLE_COMPILER
        unbox s
#else
        HTMLNode.Text s :> ReactElement
#endif

    /// Cast a string to a React element (erased in runtime)
    let inline ofString (s: string): ReactElement =
        str s

    /// Cast an option value to a React element (erased in runtime)
    let inline ofOption (o: ReactElement option): ReactElement =
        match o with Some o -> o | None -> null // Option.toObj(o)

    [<System.Obsolete("Use ofOption")>]
    let opt (o: ReactElement option): ReactElement =
        ofOption o

    /// Cast an int to a React element (erased in runtime)
    let inline ofInt (i: int): ReactElement =
#if FABLE_COMPILER
        unbox i
#else
        HTMLNode.RawText (string i) :> ReactElement
#endif

    /// Cast a float to a React element (erased in runtime)
    let inline ofFloat (f: float): ReactElement =
#if FABLE_COMPILER
        unbox f
#else
        HTMLNode.RawText (string f) :> ReactElement
#endif

    /// Returns a list **from .render() method**
    let inline ofList (els: ReactElement list): ReactElement =
#if FABLE_COMPILER
        unbox(List.toArray els)
#else
        HTMLNode.List els :> ReactElement
#endif

    /// Returns an array **from .render() method**
    let inline ofArray (els: ReactElement array): ReactElement =
#if FABLE_COMPILER
        unbox els
#else
        HTMLNode.List els :> ReactElement
#endif

    /// A ReactElement when you don't want to render anything (null in javascript)
    let nothing: ReactElement =
#if FABLE_COMPILER
        null
#else
        HTMLNode.Empty :> ReactElement
#endif

    /// Instantiate a DOM React element
    let inline domEl (tag: string) (props: IHTMLProp seq) (children: ReactElement seq): ReactElement =
#if FABLE_COMPILER
        ReactBindings.React.createElement(tag, keyValueList CaseRules.LowerFirst props, children)
#else
        ServerRendering.createServerElement(tag, (props |> Seq.cast<IProp>), children, ServerElementType.Tag)
#endif

    /// Instantiate a DOM React element (void)
    let inline voidEl (tag: string) (props: IHTMLProp seq) : ReactElement =
#if FABLE_COMPILER
        ReactBindings.React.createElement(tag, keyValueList CaseRules.LowerFirst props, [])
#else
        ServerRendering.createServerElement(tag, (props |> Seq.cast<IProp>), [], ServerElementType.Tag)
#endif

    /// Instantiate an SVG React element
    let inline svgEl (tag: string) (props: IProp seq) (children: ReactElement seq): ReactElement =
#if FABLE_COMPILER
        ReactBindings.React.createElement(tag, keyValueList CaseRules.LowerFirst props, children)
#else
        ServerRendering.createServerElement(tag, (props |> Seq.cast<IProp>), children, ServerElementType.Tag)
#endif

    /// Instantiate a React fragment
    let inline fragment (props: IFragmentProp seq) (children: ReactElement seq): ReactElement =
#if FABLE_COMPILER
        ReactBindings.React.createElement(ReactBindings.React.Fragment, keyValueList CaseRules.LowerFirst props, children)
#else
        ServerRendering.createServerElement(typeof<Fragment>, (props |> Seq.cast<IProp>), children, ServerElementType.Fragment)
#endif

    // Class list helpers
    let classBaseList baseClass classes =
        classes
        |> Seq.choose (fun (name, condition) ->
            if condition && not(System.String.IsNullOrEmpty(name)) then Some name
            else None)
        |> Seq.fold (fun state name -> state + " " + name) baseClass
        |> ClassName

    let classList classes = classBaseList "" classes

#if FABLE_COMPILER
    /// Finds a DOM element by its ID and mounts the React element there
    let inline mountById (domElId: string) (reactEl: ReactElement): unit =
        ReactDom.render(reactEl, document.getElementById(domElId))

    /// Finds the first DOM element matching a CSS selector and mounts the React element there
    let inline mountBySelector (domElSelector: string) (reactEl: ReactElement): unit =
        ReactDom.render(reactEl, document.querySelector(domElSelector))
#endif