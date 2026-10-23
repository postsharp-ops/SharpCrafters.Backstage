// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Output;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpCrafters.Aspects.Introspection.Query.Commands;

/// <summary>
/// The options common to the <c>introspect</c> commands (Documentation/future/Introspection-Cli.md of PostSharp, section 5.8).
/// </summary>
public class IntrospectionCommandSettings : CommandSettings
{
    /// <summary>
    /// Gets the project: a project file, or a directory that holds exactly one.
    /// </summary>
    [Description( "The project: a project file, or a directory that holds one. By default, the only project or solution of the current directory." )]
    [CommandOption( "--project <PATH>" )]
    public string? Project { get; init; }

    /// <summary>
    /// Gets the solution.
    /// </summary>
    [Description( "The solution: a .sln or .slnx file, or a directory that holds one." )]
    [CommandOption( "--solution <PATH>" )]
    public string? Solution { get; init; }

    /// <summary>
    /// Gets the target framework that restricts the query.
    /// </summary>
    [Description( "Restricts the query to one target framework, such as net10.0." )]
    [CommandOption( "-f|--framework <TFM>" )]
    public string? Framework { get; init; }

    /// <summary>
    /// Gets the configuration of the build output, or <c>null</c> for the default configuration of each project.
    /// </summary>
    [Description( "The configuration of the build output. By default, the default configuration of each project, usually Debug." )]
    [CommandOption( "-c|--configuration <CONFIGURATION>" )]
    public string? Configuration { get; init; }

    /// <summary>
    /// Gets the format of the output: <c>text</c>, <c>json</c> or <c>jsonl</c>.
    /// </summary>
    [Description( "The format of the output: text (the default), json, or jsonl, one object per line." )]
    [CommandOption( "--format <FORMAT>" )]
    [DefaultValue( "text" )]
    public string Format { get; init; } = "text";

    /// <summary>
    /// Gets when the JSON output is colored: <c>auto</c>, <c>always</c> or <c>never</c>.
    /// </summary>
    [Description( "Colors the JSON output: auto (when the output is a terminal and NO_COLOR is not set, the default), always, or never." )]
    [CommandOption( "--color <WHEN>" )]
    [DefaultValue( "auto" )]
    public string Color { get; init; } = "auto";

    /// <inheritdoc />
    public override Spectre.Console.ValidationResult Validate()
    {
        if ( this.Format is not ("text" or "json" or "jsonl") )
        {
            return Spectre.Console.ValidationResult.Error( $"--format is text, json or jsonl, not '{this.Format}'." );
        }

        if ( this.Color is not ("auto" or "always" or "never") )
        {
            return Spectre.Console.ValidationResult.Error( $"--color is auto, always or never, not '{this.Color}'." );
        }

        if ( this.Project != null && this.Solution != null )
        {
            return Spectre.Console.ValidationResult.Error( "--project and --solution cannot both be given." );
        }

        return base.Validate();
    }
}

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

    /// <summary>
    /// Gets the maximal number of objects of each list of related objects, or <c>null</c> for the default limit.
    /// </summary>
    [Description( "Writes at most this number of objects in each list of related objects, by default 20. A truncated list says how many objects it has, in which namespaces, and which command lists them all." )]
    [CommandOption( "--limit <N>" )]
    public int? Limit { get; init; }

    /// <summary>
    /// Gets a value indicating whether every related object is written.
    /// </summary>
    [Description( "Writes every related object, without the default limit." )]
    [CommandOption( "--all" )]
    public bool All { get; init; }

    /// <inheritdoc />
    public override Spectre.Console.ValidationResult Validate()
        => this.All && this.Limit != null
            ? Spectre.Console.ValidationResult.Error( "--all and --limit cannot both be given." )
            : this.Limit < 0
                ? Spectre.Console.ValidationResult.Error( "--limit cannot be negative." )
                : base.Validate();

    /// <summary>
    /// Gets how the lists of related objects are written.
    /// </summary>
    internal RelatedListOptions GetListOptions() => new( this.All ? null : this.Limit ?? RelatedListOptions.DefaultLimit );
}

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
