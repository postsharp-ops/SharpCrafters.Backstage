// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpCrafters.Aspects.Introspection.Query.Commands;

/// <summary>
/// The settings of <c>introspect batch</c>.
/// </summary>
public sealed class BatchCommandSettings : IntrospectionCommandSettings
{
    /// <summary>
    /// Gets the file that holds the queries, or <c>null</c> or <c>-</c> to read them from the standard input.
    /// </summary>
    [Description( "The file that holds the queries, one per line. By default, or with -, the standard input." )]
    [CommandOption( "--file <PATH>" )]
    public string? File { get; init; }
}
