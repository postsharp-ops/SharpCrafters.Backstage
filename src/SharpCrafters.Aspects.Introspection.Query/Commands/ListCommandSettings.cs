// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpCrafters.Aspects.Introspection.Query.Commands;

/// <summary>
/// The settings of <c>introspect list</c>.
/// </summary>
public sealed class ListCommandSettings : IntrospectionCommandSettings
{
    /// <summary>
    /// Gets the collection, such as <c>aspect-instances</c>.
    /// </summary>
    [Description( "The collection: aspect-classes, aspect-instances, predecessors, transformations, annotations, declaration-mappings, declarations or diagnostics." )]
    [CommandArgument( 0, "<COLLECTION>" )]
    public string Collection { get; init; } = "";

    /// <summary>
    /// Gets the conditions, which are combined with <c>and</c>.
    /// </summary>
    [Description(
        "A condition in the subset of the OData $filter syntax, such as \"aspectClass/shortName eq 'Log'\". Several are combined with 'and'. "
        + "Quote it with single quotes in a shell that expands $ inside double quotes, or escape the $ of $count. See 'introspect schema' for the fields." )]
    [CommandOption( "--where <CONDITION>" )]
    public string[] Where { get; init; } = [];

    /// <summary>
    /// Gets the selected paths, separated by commas.
    /// </summary>
    [Description( "The fields to write, separated by commas, such as id,aspectClass/shortName,transformations/$count." )]
    [CommandOption( "--select <PATHS>" )]
    public string? Select { get; init; }

    /// <summary>
    /// Gets the ordering.
    /// </summary>
    [Description( "The order, as fields separated by commas, each followed by asc or desc, such as \"targetDeclaration/id desc\"." )]
    [CommandOption( "--order-by <ORDERING>" )]
    public string? OrderBy { get; init; }

    /// <summary>
    /// Gets the number of objects to skip.
    /// </summary>
    [Description( "Skips the first objects of the result." )]
    [CommandOption( "--skip <N>" )]
    public int Skip { get; init; }

    /// <summary>
    /// Gets the maximal number of objects, or <c>null</c> for the default limit.
    /// </summary>
    [Description( "Writes at most this number of objects. By default 100, except with --format jsonl. The output says when the result is truncated." )]
    [CommandOption( "--limit <N>" )]
    public int? Limit { get; init; }

    /// <summary>
    /// Gets a value indicating whether every object is written, without the default limit.
    /// </summary>
    [Description( "Writes every object, without the default limit." )]
    [CommandOption( "--all" )]
    public bool All { get; init; }

    /// <summary>
    /// Gets a value indicating whether only the number of objects is written.
    /// </summary>
    [Description( "Writes only the number of objects that satisfy the conditions." )]
    [CommandOption( "--count" )]
    public bool Count { get; init; }

    /// <summary>
    /// The number of objects that list writes by default.
    /// </summary>
    public const int DefaultLimit = 100;

    /// <inheritdoc />
    public override Spectre.Console.ValidationResult Validate()
        => this.All && this.Limit != null
            ? Spectre.Console.ValidationResult.Error( "--all and --limit cannot both be given." )
            : this.Limit < 0
                ? Spectre.Console.ValidationResult.Error( "--limit cannot be negative." )
                : base.Validate();

    /// <summary>
    /// Gets the limit of the query: the one given, none with --all, --count or the format jsonl, or the default one.
    /// </summary>
    internal int? GetEffectiveLimit() => this.All || this.Count || (this.Limit == null && this.Format == "jsonl") ? this.Limit : this.Limit ?? DefaultLimit;
}
