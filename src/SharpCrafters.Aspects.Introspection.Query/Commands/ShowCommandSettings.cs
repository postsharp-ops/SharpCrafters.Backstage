// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpCrafters.Aspects.Introspection.Query.Commands;

/// <summary>
/// The settings of <c>introspect show</c>.
/// </summary>
public sealed class ShowCommandSettings : IntrospectionCommandSettings
{
    /// <summary>
    /// Gets the kind of object, such as <c>declaration</c>.
    /// </summary>
    [Description( "The kind of object: declaration, aspect-instance, predecessor or aspect-class." )]
    [CommandArgument( 0, "<COLLECTION>" )]
    public string Collection { get; init; } = "";

    /// <summary>
    /// Gets the selector of the object.
    /// </summary>
    [Description(
        "The object: a source position (src/Customer.cs:42), a C# name without parameters, possibly partial (Customer.FullName), a short aspect class name (Log), "
        + "an identifier or a unique prefix of it, or an exact documentation comment identifier ('M:App.Repository.Get(System.Int32)'). Quote it when it is not plain." )]
    [CommandArgument( 1, "<SELECTOR>" )]
    public string Selector { get; init; } = "";

    /// <summary>
    /// Gets a value indicating whether the members of a type are included.
    /// </summary>
    [Description( "Includes the members of a type." )]
    [CommandOption( "--members" )]
    public bool Members { get; init; }
}
