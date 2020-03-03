﻿namespace FSharpLint.Framework

/// Used to walk the FSharp Compiler's abstract syntax tree,
/// so that each node can be visited by a list of visitors.
module Ast =

    open FSharp.Compiler.Ast

    /// Nodes in the AST to be visited.
    [<NoEquality; NoComparison>]
    type AstNode =
        | Expression of SynExpr
        | Pattern of SynPat
        | SimplePattern of SynSimplePat
        | SimplePatterns of SynSimplePats
        | ModuleOrNamespace of SynModuleOrNamespace
        | ModuleDeclaration of SynModuleDecl
        | Binding of SynBinding
        | TypeDefinition of SynTypeDefn
        | MemberDefinition of SynMemberDefn
        | ComponentInfo of SynComponentInfo
        | ExceptionRepresentation of SynExceptionDefnRepr
        | UnionCase of SynUnionCase
        | EnumCase of SynEnumCase
        | TypeRepresentation of SynTypeDefnRepr
        | TypeSimpleRepresentation of SynTypeDefnSimpleRepr
        | Type of SynType
        | Field of SynField
        | Match of SynMatchClause
        | ConstructorArguments of SynConstructorArgs
        | TypeParameter of SynTypar
        | InterfaceImplementation of SynInterfaceImpl
        | Identifier of string list
        | File of ParsedInput

    /// Concatenates the nested-list structure of `SynAttributes` into a `SynAttribute list` to keep other code
    /// mostly unchanged.
    let extractAttributes (attrs:SynAttributes) =
        attrs
        |> List.collect (fun attrList -> attrList.Attributes)

    /// Extracts an expression from parentheses e.g. ((x + 4)) -> x + 4
    let rec removeParens = function
        | SynExpr.Paren(x, _, _, _) -> removeParens x
        | x -> x

    /// Inlines pipe operators to give a flat function application expression
    /// e.g. `x |> List.map id` to `List.map id x`.
    let (|FuncApp|_|) functionApplication =
        let rec flatten flattened exprToFlatten =
            match exprToFlatten with
            | SynExpr.App(_, _, x, y, _) ->
                match x with
                | SynExpr.App(_, true, SynExpr.Ident(op), rhs, _) as app ->
                    let lhs = y

                    match op.idText with
                    | "op_PipeRight" | "op_PipeRight2" | "op_PipeRight3" ->
                        flatten [rhs] lhs
                    | "op_PipeLeft" | "op_PipeLeft2" | "op_PipeLeft3" ->
                        flatten (lhs::flattened) rhs
                    | _ -> flatten (lhs::flattened) app
                | x ->
                    let leftExpr, rightExpr = (x, y)
                    flatten (rightExpr::flattened) leftExpr
            | expr -> expr::flattened

        match functionApplication with
        | AstNode.Expression(SynExpr.App(_, _, _, _, range) as functionApplication) ->
            Some(flatten [] functionApplication, range)
        | _ -> None

    [<NoEquality; NoComparison>]
    type Lambda = { Arguments: SynSimplePats list; Body: SynExpr }

    let (|Lambda|_|) lambda =
        /// A match clause is generated by the compiler for each wildcard argument,
        /// this function extracts the body expression of the lambda from those statements.
        let rec removeAutoGeneratedMatchesFromLambda = function
            | SynExpr.Match(SequencePointInfoForBinding.NoSequencePointAtInvisibleBinding,
                            _,
                            [SynMatchClause.Clause(SynPat.Wild(_), _, expr, _, _)], _) ->
                removeAutoGeneratedMatchesFromLambda expr
            | x -> x

        let (|IsCurriedLambda|_|) = function
            | SynExpr.Lambda(_, _, parameter, (SynExpr.Lambda(_) as inner), _) as outer
                    when outer.Range = inner.Range ->
                Some(parameter, inner)
            | _ -> None

        let rec getLambdaParametersAndExpression parameters = function
            | IsCurriedLambda(parameter, curriedLambda) ->
                getLambdaParametersAndExpression (parameter::parameters) curriedLambda
            | SynExpr.Lambda(_, _, parameter, body, _) ->
                { Arguments = parameter::parameters |> List.rev
                  Body = removeAutoGeneratedMatchesFromLambda body } |> Some
            | _ -> None

        match lambda with
        | AstNode.Expression(SynExpr.Lambda(_, _, _, _, range) as lambda) ->
            getLambdaParametersAndExpression [] lambda
            |> Option.map (fun x -> (x, range))
        | _ -> None

    let (|Cons|_|) pattern =
        match pattern with
        | SynPat.LongIdent(LongIdentWithDots([identifier], _),
                           _, _,
                           Pats([SynPat.Tuple(_, [lhs; rhs], _)]), _, _)
                when identifier.idText = "op_ColonColon" ->
            Some(lhs, rhs)
        | _ -> None

    type ExtraSyntaxInfo =
        | LambdaArg = 1uy
        | LambdaBody = 2uy
        | Else = 3uy
        | None = 255uy

    [<Struct; NoEquality; NoComparison>]
    type Node(extraInfo:ExtraSyntaxInfo, astNode:AstNode) =
        member __.ExtraSyntaxInfo = extraInfo
        member __.AstNode = astNode

    let inline private expressionNode x = Node(ExtraSyntaxInfo.None, Expression x)
    let inline private patternNode x = Node(ExtraSyntaxInfo.None, Pattern x)
    let inline private simplePatternNode x = Node(ExtraSyntaxInfo.None, SimplePattern x)
    let inline private simplePatternsNode x = Node(ExtraSyntaxInfo.None, SimplePatterns x)
    let inline private moduleOrNamespaceNode x = Node(ExtraSyntaxInfo.None, ModuleOrNamespace x)
    let inline private moduleDeclarationNode x = Node(ExtraSyntaxInfo.None, ModuleDeclaration x)
    let inline private bindingNode x = Node(ExtraSyntaxInfo.None, Binding x)
    let inline private typeDefinitionNode x = Node(ExtraSyntaxInfo.None, TypeDefinition x)
    let inline private memberDefinitionNode x = Node(ExtraSyntaxInfo.None, MemberDefinition x)
    let inline private componentInfoNode x = Node(ExtraSyntaxInfo.None, ComponentInfo x)
    let inline private exceptionRepresentationNode x = Node(ExtraSyntaxInfo.None, ExceptionRepresentation x)
    let inline private unionCaseNode x = Node(ExtraSyntaxInfo.None, UnionCase x)
    let inline private enumCaseNode x = Node(ExtraSyntaxInfo.None, EnumCase x)
    let inline private typeRepresentationNode x = Node(ExtraSyntaxInfo.None, TypeRepresentation x)
    let inline private typeSimpleRepresentationNode x = Node(ExtraSyntaxInfo.None, TypeSimpleRepresentation x)
    let inline private typeNode x = Node(ExtraSyntaxInfo.None, Type x)
    let inline private fieldNode x = Node(ExtraSyntaxInfo.None, Field x)
    let inline private matchNode x = Node(ExtraSyntaxInfo.None, Match x)
    let inline private constructorArgumentsNode x = Node(ExtraSyntaxInfo.None, ConstructorArguments x)
    let inline private identifierNode x = Node(ExtraSyntaxInfo.None, Identifier x)

    /// Gets a string literal from the AST.
    let (|StringLiteral|_|) node =
        match node with
        | Expression(SynExpr.Const(SynConst.String(value, _), range)) -> Some(value, range)
        | _ -> None

    module List =
        let inline revIter f items =
            items |> List.rev |> List.iter f

    let inline private moduleDeclarationChildren node add =
        match node with
        | SynModuleDecl.NestedModule(componentInfo, _, moduleDeclarations, _, _) ->
            moduleDeclarations |> List.revIter (moduleDeclarationNode >> add)
            add <| componentInfoNode componentInfo
        | SynModuleDecl.Let(_, bindings, _) -> bindings |> List.revIter (bindingNode >> add)
        | SynModuleDecl.DoExpr(_, expression, _) -> add <| expressionNode expression
        | SynModuleDecl.Types(typeDefinitions, _) -> typeDefinitions |> List.revIter (typeDefinitionNode >> add)
        | SynModuleDecl.Exception(SynExceptionDefn.SynExceptionDefn(repr, members, _), _) ->
            members |> List.revIter (memberDefinitionNode >> add)
            add <| exceptionRepresentationNode repr
        | SynModuleDecl.NamespaceFragment(moduleOrNamespace) -> add <| moduleOrNamespaceNode moduleOrNamespace
        | SynModuleDecl.Open(_)
        | SynModuleDecl.Attributes(_)
        | SynModuleDecl.HashDirective(_)
        | SynModuleDecl.ModuleAbbrev(_) -> ()

    let inline private typeChildren node add =
        match node with
        | SynType.LongIdentApp(synType, _, _, types, _, _, _)
        | SynType.App(synType, _, types, _, _, _, _) ->
            types |> List.revIter (typeNode >> add)
            add <| typeNode synType
        | SynType.Tuple(_, types, _) ->
            types |> List.revIter (snd >> typeNode >> add)
        | SynType.Fun(synType, synType1, _)
        | SynType.StaticConstantNamed(synType, synType1, _)
        | SynType.MeasureDivide(synType, synType1, _) ->
            add <| typeNode synType1
            add <| typeNode synType
        | SynType.Var(_)
        | SynType.Anon(_)
        | SynType.LongIdent(_)
        | SynType.StaticConstant(_) -> ()
        | SynType.WithGlobalConstraints(synType, _, _)
        | SynType.HashConstraint(synType, _)
        | SynType.MeasurePower(synType, _, _)
        | SynType.Array(_, synType, _) -> add <| typeNode synType
        | SynType.StaticConstantExpr(expression, _) -> add <| expressionNode expression
        | SynType.AnonRecd (_, typeNames, _) ->
            typeNames |> List.revIter (snd >> typeNode >> add)

    /// Concatenates the typed-or-untyped structure of `SynSimplePats` into a `SynSimplePat list` to keep other code
    /// mostly unchanged.
    let inline extractPatterns (simplePats:SynSimplePats) =
        let rec loop pat acc =
            match pat with
            | SynSimplePats.SimplePats(patterns, _range) -> patterns @ acc
            | SynSimplePats.Typed(patterns, _type, _range) -> loop patterns acc
        loop simplePats []

    let inline private memberDefinitionChildren node add =
        match node with
        | SynMemberDefn.Member(binding, _) -> add <| bindingNode binding
        | SynMemberDefn.ImplicitCtor(_, _, patterns, _, _) ->
            let combinedPatterns = extractPatterns patterns
            combinedPatterns |> List.revIter (simplePatternNode >> add)
        | SynMemberDefn.ImplicitInherit(synType, expression, _, _) ->
            add <| expressionNode expression
            add <| typeNode synType
        | SynMemberDefn.LetBindings(bindings, _, _, _) -> bindings |> List.revIter (bindingNode >> add)
        | SynMemberDefn.Interface(synType, Some(members), _) ->
            members |> List.revIter (memberDefinitionNode >> add)
            add <| typeNode synType
        | SynMemberDefn.Interface(synType, None, _)
        | SynMemberDefn.Inherit(synType, _, _) -> add <| typeNode synType
        | SynMemberDefn.Open(_)
        | SynMemberDefn.AbstractSlot(_) -> ()
        | SynMemberDefn.ValField(field, _) -> add <| fieldNode field
        | SynMemberDefn.NestedType(typeDefinition, _, _) -> add <| typeDefinitionNode typeDefinition
        | SynMemberDefn.AutoProperty(_, _, _, Some(synType), _, _, _, _, expression, _, _) ->
            add <| expressionNode expression
            add <| typeNode synType
        | SynMemberDefn.AutoProperty(_, _, _, None, _, _, _, _, expression, _, _) ->
            add <| expressionNode expression

    let inline private patternChildren node add =
        match node with
        | SynPat.IsInst(synType, _) -> add <| typeNode synType
        | SynPat.QuoteExpr(expression, _) -> add <| expressionNode expression
        | SynPat.Typed(pattern, synType, _) ->
            add <| typeNode synType
            add <| patternNode pattern
        | SynPat.Or(pattern, pattern1, _) ->
            add <| patternNode pattern1
            add <| patternNode pattern
        | SynPat.ArrayOrList(_, patterns, _)
        | SynPat.Tuple(_, patterns, _)
        | SynPat.Ands(patterns, _) -> patterns |> List.revIter (patternNode >> add)
        | SynPat.Attrib(pattern, _, _)
        | SynPat.Named(pattern, _, _, _, _)
        | SynPat.Paren(pattern, _) -> add <| patternNode pattern
        | SynPat.Record(patternsAndIdentifier, _) -> patternsAndIdentifier |> List.revIter (snd >> patternNode >> add)
        | SynPat.Const(_)
        | SynPat.Wild(_)
        | SynPat.FromParseError(_)
        | SynPat.InstanceMember(_)
        | SynPat.DeprecatedCharRange(_)
        | SynPat.Null(_)
        | SynPat.OptionalVal(_) -> ()
        | Cons(lhs, rhs) ->
            add <| patternNode rhs
            add <| patternNode lhs
        | SynPat.LongIdent(_, _, _, constructorArguments, _, _) ->
            add <| constructorArgumentsNode constructorArguments

    let inline private expressionChildren node add =
        match node with
        | SynExpr.Paren(expression, _, _, _)
        | SynExpr.DotGet(expression, _, _, _)
        | SynExpr.DotIndexedGet(expression, _, _, _)
        | SynExpr.LongIdentSet(_, expression, _)
        | SynExpr.Do(expression, _)
        | SynExpr.Assert(expression, _)
        | SynExpr.CompExpr(_, _, expression, _)
        | SynExpr.ArrayOrListOfSeqExpr(_, expression, _)
        | SynExpr.AddressOf(_, expression, _, _)
        | SynExpr.InferredDowncast(expression, _)
        | SynExpr.InferredUpcast(expression, _)
        | SynExpr.DoBang(expression, _)
        | SynExpr.Lazy(expression, _)
        | SynExpr.TraitCall(_, _, expression, _)
        | SynExpr.YieldOrReturn(_, expression, _)
        | SynExpr.YieldOrReturnFrom(_, expression, _) -> add <| expressionNode expression
        | SynExpr.SequentialOrImplicitYield(_, expression1, expression2, ifNotExpression, _) ->
            add <| expressionNode expression1
            add <| expressionNode expression2
            add <| expressionNode ifNotExpression
        | SynExpr.Quote(expression, _, expression1, _, _)
        | SynExpr.Sequential(_, _, expression, expression1, _)
        | SynExpr.NamedIndexedPropertySet(_, expression, expression1, _)
        | SynExpr.DotIndexedSet(expression, _, expression1, _, _, _)
        | SynExpr.JoinIn(expression, _, expression1, _)
        | SynExpr.While(_, expression, expression1, _)
        | SynExpr.TryFinally(expression, expression1, _, _, _)
        | SynExpr.Set(expression, expression1, _)
        | SynExpr.DotSet(expression, _, expression1, _) ->
            add <| expressionNode expression1
            add <| expressionNode expression
        | SynExpr.Typed(expression, synType, _) ->
            add <| typeNode synType
            add <| expressionNode expression
        | SynExpr.Tuple(_, expressions, _, _)
        | SynExpr.ArrayOrList(_, expressions, _) -> expressions |> List.revIter (expressionNode >> add)
        | SynExpr.Record(_, Some(expr, _), _, _) -> add <| expressionNode expr
        | SynExpr.Record(_, None, _, _) -> ()
        | SynExpr.AnonRecd(_, Some (expr,_), _, _) ->
            add <| expressionNode expr
        | SynExpr.AnonRecd(_, None, _, _) -> ()
        | SynExpr.ObjExpr(synType, _, bindings, _, _, _) ->
            bindings |> List.revIter (bindingNode >> add)
            add <| typeNode synType
        | SynExpr.ImplicitZero(_)
        | SynExpr.Null(_)
        | SynExpr.Const(_)
        | SynExpr.DiscardAfterMissingQualificationAfterDot(_)
        | SynExpr.FromParseError(_)
        | SynExpr.LibraryOnlyILAssembly(_)
        | SynExpr.LibraryOnlyStaticOptimization(_)
        | SynExpr.LibraryOnlyUnionCaseFieldGet(_)
        | SynExpr.LibraryOnlyUnionCaseFieldSet(_)
        | SynExpr.ArbitraryAfterError(_) -> ()
        | SynExpr.DotNamedIndexedPropertySet(expression, _, expression1, expression2, _)
        | SynExpr.For(_, _, expression, _, expression1, expression2, _) ->
            add <| expressionNode expression2
            add <| expressionNode expression1
            add <| expressionNode expression
        | SynExpr.LetOrUseBang(_, _, _, pattern, rightHandSide, andBangs, leftHandSide, _) ->
            add <| expressionNode rightHandSide
            add <| expressionNode leftHandSide
            // TODO: is the the correct way to handle the new `and!` syntax?
            andBangs |> List.iter (fun (_, _, _, pattern, body, _) ->
                add <| expressionNode body
                add <| patternNode pattern
            )
            add <| patternNode pattern
        | SynExpr.ForEach(_, _, _, pattern, expression, expression1, _) ->
            add <| expressionNode expression1
            add <| expressionNode expression
            add <| patternNode pattern
        | SynExpr.MatchLambda(_, _, matchClauses, _, _) ->
            matchClauses |> List.revIter (matchNode >> add)
        | SynExpr.TryWith(expression, _, matchClauses, _, _, _, _)
        | SynExpr.MatchBang(_, expression, matchClauses, _)
        | SynExpr.Match(_, expression, matchClauses, _) ->
            matchClauses |> List.revIter (matchNode >> add)
            add <| expressionNode expression
        | SynExpr.TypeApp(expression, _, types, _, _, _, _) ->
            types |> List.revIter (typeNode >> add)
            add <| expressionNode expression
        | SynExpr.New(_, synType, expression, _)
        | SynExpr.TypeTest(expression, synType, _)
        | SynExpr.Upcast(expression, synType, _)
        | SynExpr.Downcast(expression, synType, _) ->
            add <| typeNode synType
            add <| expressionNode expression
        | SynExpr.LetOrUse(_, _, bindings, expression, _) ->
            add <| expressionNode expression
            bindings |> List.revIter (bindingNode >> add)
        | SynExpr.Ident(ident) -> add <| identifierNode([ident.idText])
        | SynExpr.LongIdent(_, LongIdentWithDots(ident, _), _, _) ->
            add <| identifierNode(ident |> List.map (fun x -> x.idText))
        | SynExpr.IfThenElse(cond, body, Some(elseExpr), _, _, _, _) ->
            add <| Node(ExtraSyntaxInfo.Else, AstNode.Expression elseExpr)
            add <| expressionNode body
            add <| expressionNode cond
        | SynExpr.IfThenElse(cond, body, None, _, _, _, _) ->
            add <| expressionNode body
            add <| expressionNode cond
        | SynExpr.Lambda(_)
        | SynExpr.App(_)
        | SynExpr.Fixed(_) -> ()

    let inline private typeSimpleRepresentationChildren node add =
        match node with
        | SynTypeDefnSimpleRepr.Union(_, unionCases, _) -> unionCases |> List.revIter (unionCaseNode >> add)
        | SynTypeDefnSimpleRepr.Enum(enumCases, _) -> enumCases |> List.revIter (enumCaseNode >> add)
        | SynTypeDefnSimpleRepr.Record(_, fields, _) -> fields |> List.revIter (fieldNode >> add)
        | SynTypeDefnSimpleRepr.TypeAbbrev(_, synType, _) -> add <| typeNode synType
        | SynTypeDefnSimpleRepr.Exception(exceptionRepr) -> add <| exceptionRepresentationNode exceptionRepr
        | SynTypeDefnSimpleRepr.General(_)
        | SynTypeDefnSimpleRepr.LibraryOnlyILAssembly(_)
        | SynTypeDefnSimpleRepr.None(_) -> ()

    let inline private simplePatternsChildren node add =
        match node with
        | SynSimplePats.SimplePats(simplePatterns, _) ->
            simplePatterns |> List.revIter (simplePatternNode >> add)
        | SynSimplePats.Typed(simplePatterns, synType, _) ->
            add <| typeNode synType
            add <| simplePatternsNode simplePatterns

    let inline private simplePatternChildren node add =
        match node with
        | SynSimplePat.Typed(simplePattern, synType, _) ->
            add <| typeNode synType
            add <| simplePatternNode simplePattern
        | SynSimplePat.Attrib(simplePattern, _, _) -> add <| simplePatternNode simplePattern
        | SynSimplePat.Id(identifier, _, _, _, _, _) -> add <| identifierNode([identifier.idText])

    let inline private matchChildren node add =
        match node with
        | Clause(pattern, Some(expression), expression1, _, _) ->
            add <| expressionNode expression1
            add <| expressionNode expression
            add <| patternNode pattern
        | Clause(pattern, None, expression1, _, _) ->
            add <| expressionNode expression1
            add <| patternNode pattern

    let inline private constructorArgumentsChildren node add =
        match node with
        | SynConstructorArgs.Pats(patterns) ->
            patterns |> List.revIter (patternNode >> add)
        | SynConstructorArgs.NamePatPairs(namePatterns, _) ->
            namePatterns |> List.revIter (snd >> patternNode >> add)

    let inline private typeRepresentationChildren node add =
        match node with
        | SynTypeDefnRepr.ObjectModel(_, members, _) ->
            members |> List.revIter (memberDefinitionNode >> add)
        | SynTypeDefnRepr.Simple(typeSimpleRepresentation, _) ->
            add <| typeSimpleRepresentationNode typeSimpleRepresentation
        | SynTypeDefnRepr.Exception(exceptionRepr) ->
            add <| exceptionRepresentationNode exceptionRepr

    /// Extracts the child nodes to be visited from a given node.
    let traverseNode node add =
        match node with
        | ModuleDeclaration(x) -> moduleDeclarationChildren x add
        | ModuleOrNamespace(SynModuleOrNamespace(_, _, _, moduleDeclarations, _, _, _, _)) ->
            moduleDeclarations |> List.revIter (moduleDeclarationNode >> add)
        | Binding(SynBinding.Binding(_, _, _, _, _, _, _, pattern, _, expression, _, _)) ->
            add <| expressionNode expression
            add <| patternNode pattern
        | ExceptionRepresentation(SynExceptionDefnRepr.SynExceptionDefnRepr(_, unionCase, _, _, _, _)) ->
            add <| unionCaseNode unionCase
        | TypeDefinition(TypeDefn(componentInfo, typeRepresentation, members, _)) ->
            members |> List.revIter (memberDefinitionNode >> add)
            add <| typeRepresentationNode typeRepresentation
            add <| componentInfoNode componentInfo
        | TypeSimpleRepresentation(x) -> typeSimpleRepresentationChildren x add
        | Type(x) -> typeChildren x add
        | Match(x) -> matchChildren x add
        | MemberDefinition(x) -> memberDefinitionChildren x add
        | Field(SynField.Field(_, _, _, synType, _, _, _, _)) -> add <| typeNode synType
        | Pattern(x) -> patternChildren x add
        | ConstructorArguments(x) -> constructorArgumentsChildren x add
        | SimplePattern(x) -> simplePatternChildren x add
        | SimplePatterns(x) -> simplePatternsChildren x add
        | InterfaceImplementation(InterfaceImpl(synType, bindings, _)) ->
            bindings |> List.revIter (bindingNode >> add)
            add <| typeNode synType
        | TypeRepresentation(x) -> typeRepresentationChildren x add
        | FuncApp(exprs, _) -> exprs |> List.revIter (expressionNode >> add)
        | Lambda({ Arguments = args; Body = body }, _) ->
            add <| Node(ExtraSyntaxInfo.LambdaBody, AstNode.Expression(body))
            args |> List.revIter (fun arg -> add <| Node(ExtraSyntaxInfo.LambdaArg, AstNode.SimplePatterns arg))
        | Expression(x) -> expressionChildren x add
        | File(ParsedInput.ImplFile(ParsedImplFileInput(_, _, _, _, _, moduleOrNamespaces, _))) ->
            moduleOrNamespaces |> List.revIter (moduleOrNamespaceNode >> add)

        | File(ParsedInput.SigFile(_))
        | ComponentInfo(_)
        | EnumCase(_)
        | UnionCase(_)
        | Identifier(_)
        | TypeParameter(_) -> ()
