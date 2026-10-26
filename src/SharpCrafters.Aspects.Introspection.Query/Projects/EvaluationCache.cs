// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SharpCrafters.Aspects.Introspection.Query.Projects;

/// <summary>
/// The cache of the evaluations of the projects (Documentation/future/Introspection-Cli.md of PostSharp, section 5.7). An entry
/// is valid while none of its inputs changed: the write times of the project file and of every file that its evaluation
/// imported, of the solution file and of the <c>global.json</c> that applies, the version of the selected .NET SDK, and the
/// global properties of the evaluation (<c>Configuration</c>, <c>TargetFramework</c>) and the properties that the provider reads.
/// The appearance of a file that the evaluation would import, such as a new <c>Directory.Build.props</c> or the
/// <c>obj/*.nuget.g.props</c> of the first restore, also invalidates it, and so does a source file that is added, removed or
/// renamed in the directory of the project, which the globs of <c>Compile</c> would include.
/// </summary>
/// <remarks>
/// An entry is a JSON file named after the hash of its key, written to a temporary file and moved, so that two commands that run
/// at the same time never read half an entry. An entry that cannot be read is ignored.
/// </remarks>
internal sealed class EvaluationCache
{
    // Changed when the format of an entry or the content of an evaluation changes.
    private const int _formatVersion = 2;

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    private readonly string? _directory;

    /// <param name="directory">The directory of the cache, or <c>null</c> to cache nothing.</param>
    public EvaluationCache( string? directory )
    {
        this._directory = directory == null ? null : Path.Combine( directory, "evaluations" );
    }

    public ProjectEvaluation Evaluate(
        string projectPath,
        IntrospectionProvider provider,
        string? configuration,
        string? targetFramework,
        string sdkVersion,
        string? solutionPath,
        Func<ProjectEvaluation> evaluate )
    {
        var key = string.Join(
            "|",
            _formatVersion,
            Path.GetFullPath( projectPath ),
            configuration,
            targetFramework,
            provider.FileProperty,
            string.Join( ",", provider.AdditionalFileProperties ),
            sdkVersion,
            solutionPath );

        var extraInputs = new List<string>();

        if ( solutionPath != null )
        {
            extraInputs.Add( solutionPath );
        }

        if ( FindGlobalJson( Path.GetDirectoryName( Path.GetFullPath( projectPath ) )! ) is { } globalJson )
        {
            extraInputs.Add( globalJson );
        }

        var path = this._directory == null ? null : Path.Combine( this._directory, Hash( key ) + ".json" );

        if ( path != null && TryRead( path, key ) is { } cached )
        {
            return cached;
        }

        var evaluation = evaluate();

        if ( path != null )
        {
            Write( path, key, evaluation, evaluation.Inputs.Concat( extraInputs ), GetSourceTreeStamp( evaluation ) );
        }

        return evaluation;
    }

    private static ProjectEvaluation? TryRead( string path, string key )
    {
        try
        {
            if ( !File.Exists( path ) )
            {
                return null;
            }

            var entry = JsonSerializer.Deserialize<Entry>( File.ReadAllText( path ), _jsonOptions );

            if ( entry == null || entry.Key != key || entry.Evaluation == null )
            {
                return null;
            }

            // An input that changed, appeared or disappeared invalidates the entry.
            foreach ( var input in entry.Inputs )
            {
                var exists = File.Exists( input.Path );

                if ( exists != (input.WriteTimeTicks != 0) || (exists && File.GetLastWriteTimeUtc( input.Path ).Ticks != input.WriteTimeTicks) )
                {
                    return null;
                }
            }

            // A source file that the globs would include or no longer include.
            if ( entry.SourceTree != GetSourceTreeStamp( entry.Evaluation ) )
            {
                return null;
            }

            return entry.Evaluation;
        }
        catch ( Exception e ) when ( e is IOException or JsonException or UnauthorizedAccessException )
        {
            return null;
        }
    }

    private static void Write( string path, string key, ProjectEvaluation evaluation, IEnumerable<string> inputs, string sourceTree )
    {
        try
        {
            var entry = new Entry
            {
                Key = key,
                Evaluation = evaluation,
                SourceTree = sourceTree,
                Inputs = inputs.Distinct( StringComparer.OrdinalIgnoreCase )
                    .Select( i => new Input { Path = i, WriteTimeTicks = File.Exists( i ) ? File.GetLastWriteTimeUtc( i ).Ticks : 0 } )
                    .ToList()
            };

            Directory.CreateDirectory( Path.GetDirectoryName( path )! );
            var temporary = path + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
            File.WriteAllText( temporary, JsonSerializer.Serialize( entry, _jsonOptions ) );
            File.Move( temporary, path, true );
        }
        catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
        {
            // The cache is an optimization: a command that cannot write it still runs.
        }
    }

    // The hash of the paths of the source files under the directory of the project, outside of the directories that the default
    // items of the .NET SDK exclude (bin, obj and those whose name starts with a period). The write time of a file is not part of
    // it: a change of content leaves the evaluation valid, and the staleness is computed from the write times at every command.
    private static string GetSourceTreeStamp( ProjectEvaluation evaluation )
    {
        var directory = Path.GetDirectoryName( evaluation.ProjectPath )!;

        var extensions = evaluation.Targets.SelectMany( t => t.SourceFiles )
            .Select( Path.GetExtension )
            .Append( ".cs" )
            .Append( ".vb" )
            .Where( e => !string.IsNullOrEmpty( e ) )
            .ToHashSet( StringComparer.OrdinalIgnoreCase );

        var files = new List<string>();
        AddSourceFiles( directory, true );
        files.Sort( StringComparer.Ordinal );

        return Hash( string.Join( "|", files ) );

        void AddSourceFiles( string current, bool isProjectDirectory )
        {
            if ( !Directory.Exists( current ) )
            {
                return;
            }

            foreach ( var file in Directory.EnumerateFiles( current ) )
            {
                if ( extensions.Contains( Path.GetExtension( file ) ) )
                {
                    files.Add( Path.GetRelativePath( directory, file ) );
                }
            }

            foreach ( var child in Directory.EnumerateDirectories( current ) )
            {
                var name = Path.GetFileName( child );

                if ( name.StartsWith( '.' )
                     || (isProjectDirectory && (name.Equals( "bin", StringComparison.OrdinalIgnoreCase ) || name.Equals( "obj", StringComparison.OrdinalIgnoreCase ))) )
                {
                    continue;
                }

                AddSourceFiles( child, false );
            }
        }
    }

    // The global.json that applies to a directory: the nearest one in it or above it, as the dotnet command line finds it.
    private static string? FindGlobalJson( string directory )
    {
        for ( var current = new DirectoryInfo( directory ); current != null; current = current.Parent )
        {
            var candidate = Path.Combine( current.FullName, "global.json" );

            if ( File.Exists( candidate ) )
            {
                return candidate;
            }
        }

        return null;
    }

    internal static string Hash( string key ) => Convert.ToHexString( XxHash64.Hash( Encoding.UTF8.GetBytes( key ) ) ).ToLowerInvariant();

    private sealed class Entry
    {
        public string Key { get; set; } = "";

        public List<Input> Inputs { get; set; } = [];

        public string SourceTree { get; set; } = "";

        public ProjectEvaluation? Evaluation { get; set; }
    }

    private sealed class Input
    {
        public string Path { get; set; } = "";

        // Zero for a file that did not exist.
        public long WriteTimeTicks { get; set; }
    }
}
