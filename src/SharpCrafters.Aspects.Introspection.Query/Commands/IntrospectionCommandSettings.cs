// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

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
    /// Gets the format of the output: <c>json</c>, the default, which is colored on a terminal, <c>text</c> or <c>jsonl</c>.
    /// </summary>
    [Description( "The format of the output: json (the default, colored on a terminal), text for a reader, or jsonl, one object per line." )]
    [CommandOption( "--format <FORMAT>" )]
    [DefaultValue( "json" )]
    public string Format { get; init; } = "json";

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
