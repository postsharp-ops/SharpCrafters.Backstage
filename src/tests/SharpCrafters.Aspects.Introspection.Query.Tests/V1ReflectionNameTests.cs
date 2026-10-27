// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.V1;
using System;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Query.Tests;

/// <summary>
/// The conversion of the reflection names of version 1 to documentation comment identifiers. The expected identifiers are
/// those that Roslyn 5.0 gives the same declarations (<c>ISymbol.GetDocumentationCommentId</c>), measured on 2026-10-23.
/// </summary>
public sealed class V1ReflectionNameTests
{
    [Theory]
    [InlineData( "T:[App]App.Customer", "T:App.Customer", IntrospectionDeclarationKind.Type, "Customer" )]
    [InlineData( "T:[App]App.Box{T}", "T:App.Box`1", IntrospectionDeclarationKind.Type, "Box" )]
    [InlineData( "T:[App]App.Box{T}+Inner{U}", "T:App.Box`1.Inner`1", IntrospectionDeclarationKind.Type, "Inner" )]
    [InlineData( "T:[App]Global", "T:Global", IntrospectionDeclarationKind.Type, "Global" )]
    [InlineData( "M:[App]App.Box{T}::Set(T)", "M:App.Box`1.Set(`0)", IntrospectionDeclarationKind.Method, "Set" )]
    [InlineData( "M:[App]App.Box{T}+Inner{U}::M(T, U)", "M:App.Box`1.Inner`1.M(`0,`1)", IntrospectionDeclarationKind.Method, "M" )]
    [InlineData( "M:[App]App.C::M{T,U,V}(T, U, V)", "M:App.C.M``3(``0,``1,``2)", IntrospectionDeclarationKind.Method, "M" )]
    [InlineData( "M:[App]App.Box{T}::M{T}(T)", "M:App.Box`1.M``1(``0)", IntrospectionDeclarationKind.Method, "M" )]
    [InlineData( "M:[App]App.C::A(System.Int32[,])", "M:App.C.A(System.Int32[0:,0:])", IntrospectionDeclarationKind.Method, "A" )]
    [InlineData( "M:[App]App.C::B(System.Int32[][])", "M:App.C.B(System.Int32[][])", IntrospectionDeclarationKind.Method, "B" )]
    [InlineData( "M:[App]App.C::N(System.Nullable{[System.Runtime]System.Decimal})", "M:App.C.N(System.Nullable{System.Decimal})", IntrospectionDeclarationKind.Method, "N" )]
    [InlineData( "M:[App]App.C::D(System.Collections.Generic.Dictionary{System.String, System.Int32})", "M:App.C.D(System.Collections.Generic.Dictionary{System.String,System.Int32})", IntrospectionDeclarationKind.Method, "D" )]
    [InlineData( "M:[App]App.C::R(System.Int32&)", "M:App.C.R(System.Int32@)", IntrospectionDeclarationKind.Method, "R" )]
    [InlineData( "M:[App]App.C::Out([App]App.Outer+Inner)", "M:App.C.Out(App.Outer.Inner)", IntrospectionDeclarationKind.Method, "Out" )]
    [InlineData( "M:[App]App.C::#ctor()", "M:App.C.#ctor", IntrospectionDeclarationKind.Constructor, "#ctor" )]
    [InlineData( "M:[App]App.C::#cctor()", "M:App.C.#cctor", IntrospectionDeclarationKind.Constructor, "#cctor" )]
    [InlineData( "M:[App]App.C::App#IService#Run()", "M:App.C.App#IService#Run", IntrospectionDeclarationKind.Method, "App#IService#Run" )]
    [InlineData( "M:[App]App.C::op_Addition([App]App.C, [App]App.C)", "M:App.C.op_Addition(App.C,App.C)", IntrospectionDeclarationKind.Method, "op_Addition" )]
    [InlineData( "M:[App]App.C::op_Implicit([App]App.C)~System.Int32", "M:App.C.op_Implicit(App.C)~System.Int32", IntrospectionDeclarationKind.Method, "op_Implicit" )]
    [InlineData( "M:[App]App.C::op_Explicit(System.Int32)~[App]App.C", "M:App.C.op_Explicit(System.Int32)~App.C", IntrospectionDeclarationKind.Method, "op_Explicit" )]
    [InlineData( "M:[App]App.C::M(System.Collections.Generic.List{[mscorlib]System.Collections.Generic.List{[mscorlib]System.Nullable{System.Int32}}})", "M:App.C.M(System.Collections.Generic.List{System.Collections.Generic.List{System.Nullable{System.Int32}}})", IntrospectionDeclarationKind.Method, "M" )]
    [InlineData( "M:[App]App.C::add_E([App]App.C+Handler)", "M:App.C.add_E(App.C.Handler)", IntrospectionDeclarationKind.Method, "add_E" )]
    [InlineData( "M:[App]App.C::get_Item(System.Int32, System.Int32)", "M:App.C.get_Item(System.Int32,System.Int32)", IntrospectionDeclarationKind.Method, "get_Item" )]
    [InlineData( "P:[App]App.C::Item(System.Int32, System.Int32)", "P:App.C.Item(System.Int32,System.Int32)", IntrospectionDeclarationKind.Property, "Item" )]
    [InlineData( "P:[App]App.C::Name()", "P:App.C.Name", IntrospectionDeclarationKind.Property, "Name" )]
    [InlineData( "P:[App]App.C::{Name}k__BackingField()", "P:App.C.{Name}k__BackingField", IntrospectionDeclarationKind.Property, "{Name}k__BackingField" )]
    [InlineData( "F:[App]App.C::<Name>k__BackingField", "F:App.C.<Name>k__BackingField", IntrospectionDeclarationKind.Field, "<Name>k__BackingField" )]
    [InlineData( "E:[App]App.C::Changed", "E:App.C.Changed", IntrospectionDeclarationKind.Event, "Changed" )]
    [InlineData( "R:[App]App.C::M(System.String)@value", "M:App.C.M(System.String);Parameter=value", IntrospectionDeclarationKind.Parameter, "value" )]
    [InlineData( "R:[App]App.C::M(System.String)@return", "M:App.C.M(System.String);Return", IntrospectionDeclarationKind.ReturnParameter, "return" )]
    [InlineData( "A:App", "A:App", IntrospectionDeclarationKind.Assembly, "App" )]

    // The parameters and the return value of a conversion operator, whose signature ends with its return type.
    [InlineData( "R:[App]App.C::op_Implicit([App]App.C)~System.Int32@value", "M:App.C.op_Implicit(App.C)~System.Int32;Parameter=value", IntrospectionDeclarationKind.Parameter, "value" )]
    [InlineData( "R:[App]App.C::op_Implicit([App]App.C)~System.Int32@return", "M:App.C.op_Implicit(App.C)~System.Int32;Return", IntrospectionDeclarationKind.ReturnParameter, "return" )]

    // An assembly whose name starts with a digit or with a letter that is not ASCII.
    [InlineData( "M:[App]App.C::M([123Lib]Lib.Value)", "M:App.C.M(Lib.Value)", IntrospectionDeclarationKind.Method, "M" )]
    [InlineData( "M:[App]App.C::M([Ünicode.Lib]Lib.Value, System.Int32[,])", "M:App.C.M(Lib.Value,System.Int32[0:,0:])", IntrospectionDeclarationKind.Method, "M" )]

    // Generic parameters whose names are not ASCII.
    [InlineData( "M:[App]App.C::M{Δ}(Δ)", "M:App.C.M``1(``0)", IntrospectionDeclarationKind.Method, "M" )]
    [InlineData( "M:[App]App.Box{Ω}::Get(Ω)", "M:App.Box`1.Get(`0)", IntrospectionDeclarationKind.Method, "Get" )]

    // A nested type that reuses the name of a generic parameter of its declaring type hides it.
    [InlineData( "M:[App]App.Outer{T}+Inner{T}::M(T)", "M:App.Outer`1.Inner`1.M(`1)", IntrospectionDeclarationKind.Method, "M" )]
    public void TheReflectionNameIsConvertedToTheDocumentationCommentIdentifier( string reflectionName, string id, IntrospectionDeclarationKind kind, string name )
    {
        var parsed = V1ReflectionName.Parse( reflectionName );

        Assert.Equal( id, parsed.Id );
        Assert.Equal( kind, parsed.Kind );
        Assert.Equal( name, parsed.Name );
        Assert.Equal( "App", parsed.Assembly );
    }

    [Fact]
    public void TheDeclaringTypeAndNamespaceAreGiven()
    {
        var nested = V1ReflectionName.Parse( "T:[App]App.Models.Box{T}+Inner{U}" );
        Assert.Equal( "T:[App]App.Models.Box{T}", nested.DeclaringTypeName );

        var outer = V1ReflectionName.Parse( "T:[App]App.Models.Box{T}" );
        Assert.Null( outer.DeclaringTypeName );
        Assert.Equal( "App.Models", outer.Namespace );

        var member = V1ReflectionName.Parse( "M:[App]App.Models.Box{T}+Inner{U}::M(T, U)" );
        Assert.Equal( "T:[App]App.Models.Box{T}+Inner{U}", member.DeclaringTypeName );

        var parameter = V1ReflectionName.Parse( "R:[App]App.C::get_Item(System.Int32, System.String)@index" );
        Assert.Equal( "M:[App]App.C::get_Item(System.Int32, System.String)", parameter.ContainingMethodName );
    }

    [Fact]
    public void ANameOfVisualBasicHasTheSameGrammar()
    {
        // From When_PropertyWithAspect.pssym of the Visual Basic tests of the Visual Studio extension.
        var property = V1ReflectionName.Parse( "P:[ConsoleApplication1]ConsoleApplication1.When_PropertyWithAspect.ClassWithProperties::AutoProperty1()" );

        Assert.Equal( "P:ConsoleApplication1.When_PropertyWithAspect.ClassWithProperties.AutoProperty1", property.Id );
    }

    [Theory]
    [InlineData( "X" )]
    [InlineData( "Q:[App]App.C" )]
    [InlineData( "M:[App]App.C" )]
    [InlineData( "T:[App" )]
    [InlineData( "R:[App]App.C::M(System.String)" )]
    public void AMalformedNameThrows( string reflectionName )
        => Assert.Throws<FormatException>( () => V1ReflectionName.Parse( reflectionName ) );
}
