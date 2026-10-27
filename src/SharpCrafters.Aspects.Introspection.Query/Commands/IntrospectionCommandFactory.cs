// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Spectre.Console.Cli;

namespace SharpCrafters.Aspects.Introspection.Query.Commands;

/// <summary>
/// Registers the <c>introspect</c> branch in the command line of a tool, with the commands of both products and those that the
/// provider of the product adds (Documentation/future/Introspection-Cli.md of PostSharp, section 5.1). The tool calls it from
/// the <c>configureMoreCommands</c> callback of <c>BackstageCommandFactory.ConfigureCommandApp</c>.
/// </summary>
public static class IntrospectionCommandFactory
{
    /// <summary>
    /// Adds the <c>introspect</c> branch.
    /// </summary>
    public static void AddIntrospectBranch( IConfigurator configurator, IntrospectionCommandOptions options )
        => configurator.AddBranch(
            "introspect",
            branch =>
            {
                var product = options.Provider.ProductName;
                branch.SetDescription( $"Queries how {product} applied aspects to a project, from the files of its last build. Never builds." );

                branch.AddCommand<ListCommand>( "list" )
                    .WithData( options )
                    .WithDescription( "Lists the objects of a collection that satisfy conditions." )
                    .WithExample( "introspect", "list", "aspect-instances", "--where", "aspectClass/shortName eq 'Log'" );

                branch.AddCommand<ShowCommand>( "show" )
                    .WithData( options )
                    .WithDescription( "Shows one object with what relates to it, such as a declaration with its aspects and their transformations in order." )
                    .WithExample( "introspect", "show", "declaration", "src/Customer.cs:18" );

                branch.AddCommand<SchemaCommand>( "schema" )
                    .WithData( options )
                    .WithDescription( "Describes the collections and their fields, which --where, --select and --order-by use." );

                branch.AddCommand<BatchCommand>( "batch" )
                    .WithData( options )
                    .WithDescription(
                        "Runs several queries, one per line of the standard input or of --file, over one load of the files. Each line is a command without 'introspect', such as 'show declaration src/Customer.cs:18'." )
                    .WithExample( "introspect", "batch", "--file", "queries.txt" );

                options.Provider.AddCommands( branch, options );
            } );
}
