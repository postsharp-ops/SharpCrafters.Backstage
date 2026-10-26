// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Commands;
using SharpCrafters.Aspects.Introspection.Query.Projects;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpCrafters.Aspects.Introspection.Query.Tests.Commands;

/// <summary>
/// A provider of the tests: the file of a project is given by <c>TestSymbolFile</c>, and is a file of version 1. It records no
/// diagnostic and no skipped aspect instance, as PostSharp.
/// </summary>
internal sealed class TestProvider : IntrospectionProvider
{
    public Dictionary<string, IntrospectionSourceLocation> Locations { get; } = new( StringComparer.Ordinal );

    public bool HasLocations { get; init; }

    public override string ProductName => "TestProduct";

    public override string FileProperty => "TestSymbolFile";

    public override string FileExtension => ".pssym";

    public override IntrospectionModel Load( IntrospectionTarget target )
    {
        using var stream = File.OpenRead( target.FilePath );

        return IntrospectionV1Loader.Load( stream, target.AssemblyName, target.Project.Directory );
    }

    public override bool Provides( string typeName, string? fieldName ) => typeName != "diagnostic" && fieldName != "isSkipped";

    public override IIntrospectionSourceLocations? GetSourceLocations( IntrospectionTarget target ) => this.HasLocations ? new SourceLocations( this ) : null;

    public override void AddCommands( IConfigurator<CommandSettings> branch, IntrospectionCommandOptions options )
        => branch.AddCommand<FilesCommand>( "files" ).WithData( options );

    /// <summary>
    /// A command of the product, which reads the files of the scope itself.
    /// </summary>
    private sealed class FilesCommand : IntrospectionCommand<IntrospectionCommandSettings>
    {
        protected override int Execute( IntrospectionCommandOptions options, IntrospectionCommandSettings settings )
        {
            var targets = GetTargets( options, settings );
            WriteJson( options, settings, "[" + string.Join( ",", targets.Select( t => $"\"{t.Project.Name} {t.TargetFramework}\"" ) ) + "]" );

            return 0;
        }
    }

    private sealed class SourceLocations( TestProvider provider ) : IIntrospectionSourceLocations
    {
        public IntrospectionSourceLocation? GetLocation( IntrospectionDeclaration declaration ) => provider.Locations.GetValueOrDefault( declaration.Id );

        public IReadOnlyList<string> FindDeclarations( string file, int line )
            => provider.Locations.Where( l => string.Equals( l.Value.File, file, StringComparison.OrdinalIgnoreCase ) && l.Value.Line == line )
                .Select( l => l.Key )
                .ToList();
    }
}
