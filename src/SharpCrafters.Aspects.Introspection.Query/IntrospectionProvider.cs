// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Commands;
using SharpCrafters.Aspects.Introspection.Query.Projects;
using Spectre.Console.Cli;
using System.Collections.Generic;

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// What a product gives the <c>introspect</c> commands: the property that locates its file, the loader of the file, the source
/// locations of the declarations, and the commands of its own (Documentation/future/Introspection-Cli.md of PostSharp,
/// sections 5.1 and 7.1). The tool of the product registers the commands with
/// <see cref="IntrospectionCommandFactory.AddIntrospectBranch"/>.
/// </summary>
public abstract class IntrospectionProvider
{
    /// <summary>
    /// Gets the name of the product, such as <c>PostSharp</c>, which the messages name.
    /// </summary>
    public abstract string ProductName { get; }

    /// <summary>
    /// Gets the MSBuild property that gives the path of the file, such as <c>PostSharpSymbolFile</c>. A project without it is not
    /// of the product, and is skipped.
    /// </summary>
    public abstract string FileProperty { get; }

    /// <summary>
    /// Gets the MSBuild properties of the other files that the provider reads, such as <c>PostSharpSymbolLocationFile</c>.
    /// </summary>
    public virtual IReadOnlyList<string> AdditionalFileProperties => [];

    /// <summary>
    /// Gets the extension of the file, such as <c>.pssym</c>, with which the files are searched when no project can be
    /// evaluated.
    /// </summary>
    public abstract string FileExtension { get; }

    /// <summary>
    /// Gets what the message about a missing file tells the user to do, such as <c>Build the project.</c>.
    /// </summary>
    public virtual string MissingFileHint => "Build the project; the command never builds.";

    /// <summary>
    /// Loads the file of a target into the common model.
    /// </summary>
    public abstract IntrospectionModel Load( IntrospectionTarget target );

    /// <summary>
    /// Gets the source locations of the declarations of a target, or <c>null</c> when the product records none.
    /// </summary>
    public virtual IIntrospectionSourceLocations? GetSourceLocations( IntrospectionTarget target ) => null;

    /// <summary>
    /// Gets a value indicating whether the files of the product provide a collection, or a field of a type, such as the
    /// <c>diagnostics</c> collection and the <c>isSkipped</c> field, which only Metalama records. A condition or a selection that
    /// uses data that is not provided is an error that names the product, never an empty result.
    /// </summary>
    /// <param name="typeName">The name of the type in the schema, such as <c>aspectInstance</c>.</param>
    /// <param name="fieldName">The name of the field, or <c>null</c> for the collection of the type.</param>
    public virtual bool Provides( string typeName, string? fieldName ) => true;

    /// <summary>
    /// Adds the commands of the product to the <c>introspect</c> branch, such as <c>metrics</c> for PostSharp.
    /// </summary>
    public virtual void AddCommands( IConfigurator<CommandSettings> branch, IntrospectionCommandOptions options ) { }
}
