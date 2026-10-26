// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpCrafters.Aspects.Introspection.Query.Commands;

/// <summary>
/// The settings of <c>introspect schema</c>.
/// </summary>
public sealed class SchemaCommandSettings : IntrospectionCommandSettings
{
    /// <summary>
    /// Gets the collection to describe, or <c>null</c> for all.
    /// </summary>
    [Description( "The collection to describe. By default, all of them." )]
    [CommandArgument( 0, "[COLLECTION]" )]
    public string? Collection { get; init; }
}
