// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Query.Tests.Commands;

/// <summary>
/// The scope of a solution, the merge of the files of several projects and target frameworks, and the caches.
/// </summary>
public sealed class SolutionTests : IDisposable
{
    private const string _libSymbols = $"""
                                        <Symbols xmlns="{TestProject.SymbolsNamespace}">
                                          <Class Class="T:[Lib]Lib.LogAttribute">
                                            <Instance Declaration="M:[Lib]Lib.Helper::Run()"><Target><JoinPoint Description="Wrapped" /></Target></Instance>
                                          </Class>
                                        </Symbols>
                                        """;

    // The aspect class of the library, applied by an attribute of the library inherited by a class of the application, and by
    // an attribute of the application.
    private const string _appSymbols = $"""
                                        <Symbols xmlns="{TestProject.SymbolsNamespace}">
                                          <Class Class="T:[Lib]Lib.LogAttribute">
                                            <Instance Declaration="A:Lib" ForwardRef="true"><Target Target="M:[App]App.Derived::Run()"><JoinPoint /></Target></Instance>
                                            <Instance Declaration="M:[App]App.Program::Main()"><Target><JoinPoint /></Target></Instance>
                                          </Class>
                                        </Symbols>
                                        """;

    private readonly string _root = Path.Combine( Path.GetTempPath(), "introspection-tests", Guid.NewGuid().ToString( "N" ) );
    private readonly TestProvider _provider = new();
    private readonly TestProject _app;
    private readonly TestProject _lib;

    public SolutionTests()
    {
        Directory.CreateDirectory( this._root );
        this._app = new TestProject( _appSymbols, directory: Path.Combine( this._root, "App" ) );
        this._lib = new TestProject( _libSymbols, projectName: "Lib", directory: Path.Combine( this._root, "Lib" ), assemblyName: "Lib" );

        File.WriteAllText(
            Path.Combine( this._root, "All.slnx" ),
            """
            <Solution>
              <Project Path="App/App.csproj" />
              <Project Path="Lib/Lib.csproj" />
            </Solution>
            """ );
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete( this._root, true );
        }
        catch ( IOException )
        {
            // Released later.
        }
    }

    private (int ExitCode, string Output, string Error) Run( params string[] arguments ) => CommandRunner.Run( this._root, this._provider, arguments );

    private JsonElement RunJson( params string[] arguments )
    {
        var (exitCode, output, error) = this.Run( arguments.Concat( ["--format", "json"] ).ToArray() );
        Assert.True( exitCode == 0, error );

        return JsonDocument.Parse( output ).RootElement;
    }

    [Fact]
    public void TheFilesOfTheProjectsOfTheSolutionAreMerged()
    {
        // The solution is the only file of the current directory, so it is the scope.
        var result = this.RunJson( "list", "aspect-instances", "--select", "project,targetDeclaration/id" );

        Assert.Equal( ["App", "Lib"], result.GetProperty( "files" ).EnumerateArray().Select( f => f.GetProperty( "project" ).GetString() ) );

        var items = result.GetProperty( "items" ).EnumerateArray().Select( i => (i.GetProperty( "project" ).GetString(), i.GetProperty( "targetDeclaration" ).GetProperty( "id" ).GetString()) ).ToList();
        Assert.Equal( [("App", "M:App.Derived.Run"), ("App", "M:App.Program.Main"), ("Lib", "M:Lib.Helper.Run")], items );

        // One aspect class, which both files name.
        var aspectClass = Assert.Single( this.RunJson( "list", "aspect-classes" ).GetProperty( "items" ).EnumerateArray() );
        Assert.Equal( "Lib.LogAttribute", aspectClass.GetProperty( "name" ).GetString() );
        Assert.Equal( 3, this.RunJson( "show", "aspect-class", "Log" ).GetProperty( "aspectClass" ).GetProperty( "aspectInstances" ).GetProperty( "count" ).GetInt32() );

        // A declaration is of the project that builds its assembly.
        Assert.Equal(
            "Lib",
            this.RunJson( "list", "declarations", "--where", "id eq 'T:Lib.LogAttribute'", "--select", "project" ).GetProperty( "items" )[0].GetProperty( "project" ).GetString() );

        Assert.Equal(
            "Inherited",
            this.RunJson( "list", "predecessors", "--where", "aspectInstances/any(i: i/targetDeclaration/id eq 'M:App.Derived.Run')" ).GetProperty( "items" )[0].GetProperty( "kind" ).GetString() );
    }

    [Fact]
    public void TheTargetFrameworksOfAProjectAreMerged()
    {
        using var project = new TestProject( targetFrameworks: "<TargetFrameworks>net10.0;net48</TargetFrameworks>" );

        // The file of net48 has no caching aspect.
        project.WriteSymbols(
            "net48",
            TestProject.DefaultSymbols.Replace(
                """<Class Class="T:[App]App.CacheAttribute">""",
                """<Class Class="T:[App]App.CacheAttributeOfNet48Only">""",
                StringComparison.Ordinal ) );

        var (exitCode, output, error) = CommandRunner.Run( project.Directory, this._provider, ["list", "aspect-instances", "--select", "aspectClass/shortName,targetFrameworks", "--format", "json"] );
        Assert.True( exitCode == 0, error );

        var result = JsonDocument.Parse( output ).RootElement;
        Assert.Equal( 2, result.GetProperty( "files" ).GetArrayLength() );

        var frameworks = result.GetProperty( "items" ).EnumerateArray()
            .GroupBy( i => i.GetProperty( "aspectClass" ).GetProperty( "shortName" ).GetString()! )
            .ToDictionary(
                g => g.Key,
                g => string.Join( " ", g.Select( i => string.Join( ",", i.GetProperty( "targetFrameworks" ).EnumerateArray().Select( f => f.GetString() ) ) ) ) );

        Assert.Equal( "net10.0,net48", frameworks["NotifyPropertyChanged"] );
        Assert.Equal( "net10.0,net48 net10.0,net48 net10.0,net48", frameworks["Log"] );
        Assert.Equal( "net10.0", frameworks["Cache"] );
        Assert.Equal( "net48", frameworks["CacheAttributeOfNet48Only"] );

        // An aspect instance of both files is one object, and so are its transformations.
        Assert.Equal( 6, result.GetProperty( "count" ).GetInt32() );
        (exitCode, output, _) = CommandRunner.Run( project.Directory, this._provider, ["list", "transformations", "--count", "--format", "text"] );
        Assert.Equal( "7", output.Trim() );
    }

    [Fact]
    public void TheSolutionIsGivenByItsPathOrItsDirectory()
    {
        var parent = Path.GetDirectoryName( this._root )!;

        Assert.Equal( 0, CommandRunner.Run( parent, this._provider, ["list", "aspect-classes", "--solution", Path.Combine( this._root, "All.slnx" )] ).ExitCode );
        Assert.Equal( 0, CommandRunner.Run( parent, this._provider, ["list", "aspect-classes", "--solution", Path.GetFileName( this._root )] ).ExitCode );

        // A project of the solution can still be the scope.
        var result = JsonDocument.Parse( CommandRunner.Run( this._root, this._provider, ["list", "aspect-instances", "--project", "Lib", "--format", "json"] ).Output ).RootElement;
        Assert.Equal( 1, result.GetProperty( "count" ).GetInt32() );
    }

    [Fact]
    public void AProjectAndASolutionInTheDirectoryAreAmbiguous()
    {
        File.WriteAllText( Path.Combine( this._root, "Root.csproj" ), "<Project />" );

        var (exitCode, _, error) = this.Run( "list", "aspect-classes" );

        Assert.Equal( IntrospectionExitCodes.Error, exitCode );
        Assert.Contains( "All.slnx, Root.csproj", error, StringComparison.Ordinal );
    }

    [Fact]
    public void AProjectWithoutFileIsReportedAndLeftOut()
    {
        File.Delete( Path.Combine( this._lib.Directory, "bin", "Debug", "net10.0", "Lib.pssym" ) );

        var (exitCode, output, error) = this.Run( "list", "aspect-instances", "--count", "--format", "text" );

        Assert.Equal( 0, exitCode );
        Assert.Equal( "2", output.Trim() );
        Assert.Contains( "the file of 'Lib' (Debug, net10.0) does not exist, so the result leaves it out", error, StringComparison.Ordinal );
    }

    [Fact]
    public void AProjectOfAnotherProductIsSkipped()
    {
        using var other = new TestProject( setProperty: false, projectName: "Other", directory: Path.Combine( this._root, "Other" ) );
        File.WriteAllText(
            Path.Combine( this._root, "All.slnx" ),
            """<Solution><Project Path="App/App.csproj" /><Project Path="Other/Other.csproj" /></Solution>""" );

        var (exitCode, output, error) = this.Run( "list", "aspect-instances", "--count", "--format", "text" );

        Assert.Equal( 0, exitCode );
        Assert.Equal( "2", output.Trim() );
        Assert.DoesNotContain( "Other", error, StringComparison.Ordinal );
    }

    [Fact]
    public void ADeclarationAtAPositionReadsOnlyItsProject()
    {
        var provider = new TestProvider { HasLocations = true };
        provider.Locations["M:App.Program.Main"] = new IntrospectionSourceLocation( this._app.SourceFile, 3, 5 );

        // The file of the library is malformed, which a query that reads it reports.
        File.WriteAllText( Path.Combine( this._lib.Directory, "bin", "Debug", "net10.0", "Lib.pssym" ), "not xml" );
        Assert.Equal( IntrospectionExitCodes.Error, CommandRunner.Run( this._root, provider, ["list", "aspect-instances"] ).ExitCode );

        var (exitCode, output, error) = CommandRunner.Run( this._root, provider, ["show", "declaration", "App/Customer.cs:3", "--format", "text"] );

        Assert.True( exitCode == 0, error );
        Assert.StartsWith( "Method App.Program.Main at App/Customer.cs:3:5 (App).", output, StringComparison.Ordinal );
    }

    [Fact]
    public void TheEvaluationOfAProjectIsCachedUntilAnInputChanges()
    {
        var cache = Path.Combine( this._root, ".cache" );
        var projectFile = Path.Combine( this._app.Directory, "App.csproj" );

        (int, string, string) Run() => CommandRunner.Run( this._app.Directory, this._provider, ["list", "aspect-instances", "--count"], cacheDirectory: cache );

        Assert.Equal( 0, Run().Item1 );
        Assert.NotEmpty( Directory.GetFiles( Path.Combine( cache, "evaluations" ) ) );

        // The project now points to a file that does not exist, but its write time is unchanged: the cached evaluation is used.
        var writeTime = File.GetLastWriteTimeUtc( projectFile );
        File.WriteAllText( projectFile, File.ReadAllText( projectFile ).Replace( "$(AssemblyName).pssym", "Missing.pssym", StringComparison.Ordinal ) );
        File.SetLastWriteTimeUtc( projectFile, writeTime );
        Assert.Equal( 0, Run().Item1 );

        // A change of the write time invalidates it.
        File.SetLastWriteTimeUtc( projectFile, writeTime.AddSeconds( 1 ) );
        Assert.Equal( IntrospectionExitCodes.NoFile, Run().Item1 );

        // So does the appearance of a file that the SDK imports, while the project file keeps its write time.
        File.WriteAllText(
            Path.Combine( this._app.Directory, "Directory.Build.targets" ),
            "<Project><PropertyGroup><TestSymbolFile>bin/$(Configuration)/$(TargetFramework)/$(AssemblyName).pssym</TestSymbolFile></PropertyGroup></Project>" );

        Assert.Equal( 0, Run().Item1 );
    }

    [Fact]
    public void ThePackageThatARestoreBringsBackInvalidatesTheCachedEvaluation()
    {
        // The targets of a package set the property of the file, and are imported only when they exist, as obj/*.nuget.g.targets
        // imports them; the package is not in the packages folder yet.
        using var project = new TestProject(
            setProperty: false,
            extraProperties: "</PropertyGroup><Import Project=\"packages/pkg/build/Pkg.targets\" Condition=\"Exists('packages/pkg/build/Pkg.targets')\" /><PropertyGroup>" );

        var cache = Path.Combine( project.Directory, ".cache" );

        int Run() => CommandRunner.Run( project.Directory, this._provider, ["list", "aspect-classes", "--count"], cacheDirectory: cache ).ExitCode;

        Assert.Equal( IntrospectionExitCodes.NoFile, Run() );

        // The package appears, and no file that the evaluation read changes.
        var targets = Path.Combine( project.Directory, "packages", "pkg", "build", "Pkg.targets" );
        Directory.CreateDirectory( Path.GetDirectoryName( targets )! );
        File.WriteAllText(
            targets,
            "<Project><PropertyGroup><TestSymbolFile>bin/$(Configuration)/$(TargetFramework)/$(AssemblyName).pssym</TestSymbolFile></PropertyGroup></Project>" );

        Assert.Equal( 0, Run() );
    }

    [Fact]
    public void AnAddedSourceFileInvalidatesTheCachedEvaluation()
    {
        var cache = Path.Combine( this._root, ".cache" );

        JsonElement FileOfApp() => JsonDocument.Parse( CommandRunner.Run( this._app.Directory, this._provider, ["list", "aspect-classes", "--format", "json"], cacheDirectory: cache ).Output )
            .RootElement.GetProperty( "files" )[0];

        Assert.False( FileOfApp().GetProperty( "stale" ).GetBoolean() );

        // A new source file, which the globs include, is newer than the file: the project file is unchanged.
        var added = Path.Combine( this._app.Directory, "Added.cs" );
        File.WriteAllText( added, "namespace App { class Added { } }" );
        File.SetLastWriteTimeUtc( added, DateTime.UtcNow.AddMinutes( 10 ) );
        Assert.True( FileOfApp().GetProperty( "stale" ).GetBoolean() );
    }

    [Fact]
    public void ADeclarationAtAPositionReadsTheProjectOfTheDeepestDirectory()
    {
        // A project with a long name in the root, and one with a short name in a subdirectory, which holds the source file.
        using var outer = new TestProject( _appSymbols, projectName: "AVeryLongNameOfTheOuterProject", directory: Path.Combine( this._root, "Outer" ) );
        using var inner = new TestProject( _libSymbols, projectName: "L", directory: Path.Combine( outer.Directory, "L" ), assemblyName: "Lib" );

        File.WriteAllText(
            Path.Combine( this._root, "Nested.slnx" ),
            """
            <Solution>
              <Project Path="Outer/AVeryLongNameOfTheOuterProject.csproj" />
              <Project Path="Outer/L/L.csproj" />
            </Solution>
            """ );

        var provider = new TestProvider { HasLocations = true };
        provider.Locations["M:Lib.Helper.Run"] = new IntrospectionSourceLocation( inner.SourceFile, 3, 5 );

        var (exitCode, output, error) = CommandRunner.Run( this._root, provider, ["show", "declaration", "Outer/L/Customer.cs:3", "--solution", "Nested.slnx", "--format", "text"] );

        Assert.True( exitCode == 0, error );
        Assert.StartsWith( "Method Lib.Helper.Run at Outer/L/Customer.cs:3:5 ", output, StringComparison.Ordinal );
    }

    [Fact]
    public void TheLocationOfADeclarationIsTakenFromTheTargetFrameworkThatGivesIt()
    {
        using var project = new TestProject( targetFrameworks: "<TargetFrameworks>net10.0;net48</TargetFrameworks>" );
        project.WriteSymbols( "net48", TestProject.DefaultSymbols );

        // Only the file of net48, the second target framework, gives locations.
        var provider = new TestProvider { HasLocations = true, LocationFramework = "net48" };
        provider.Locations["P:App.Customer.FullName"] = new IntrospectionSourceLocation( project.SourceFile, 18, 19 );

        var (exitCode, output, error) = CommandRunner.Run( project.Directory, provider, ["show", "declaration", "Customer.FullName", "--format", "json"] );

        Assert.True( exitCode == 0, error );
        Assert.Equal( "Customer.cs:18:19", JsonDocument.Parse( output ).RootElement.GetProperty( "declaration" ).GetProperty( "location" ).GetString() );
    }

    [Fact]
    public void TheModelOfAFileIsIndexedUntilTheFileChanges()
    {
        var cache = Path.Combine( this._root, ".cache" );
        var file = Path.Combine( this._app.Directory, "bin", "Debug", "net10.0", "App.pssym" );

        string AspectClass() => JsonDocument.Parse( CommandRunner.Run( this._app.Directory, this._provider, ["list", "aspect-classes", "--format", "json"], cacheDirectory: cache ).Output )
            .RootElement.GetProperty( "items" )[0]
            .GetProperty( "name" )
            .GetString()!;

        Assert.Equal( "Lib.LogAttribute", AspectClass() );
        Assert.NotEmpty( Directory.GetFiles( Path.Combine( cache, "index" ) ) );

        // A file of the same size and write time is served from the index.
        var writeTime = File.GetLastWriteTimeUtc( file );
        File.WriteAllText( file, _appSymbols.Replace( "LogAttribute", "BugAttribute", StringComparison.Ordinal ) );
        File.SetLastWriteTimeUtc( file, writeTime );
        Assert.Equal( "Lib.LogAttribute", AspectClass() );

        File.SetLastWriteTimeUtc( file, writeTime.AddSeconds( 1 ) );
        Assert.Equal( "Lib.BugAttribute", AspectClass() );
    }
}
