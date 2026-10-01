// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage;
using PostSharp.Backstage.Configuration;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Repositories;
using SharpCrafters.Backstage.Testing;
using System;
using System.IO;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Telemetry;

/// <summary>
/// Tests the repository settings of PostSharp, which are in the <c>postsharp.config</c> file at the root of a repository.
/// </summary>
/// <remarks>
/// The file is also the configuration of the compiler, in every directory above a project, and the compiler evaluates
/// expressions and conditions in it. The reader does not, so it refuses what it cannot read with the meaning the user
/// intended, and says why.
/// </remarks>
public sealed class PostSharpRepositoryConfigurationTests : TestsBase
{
    private const string _repoRoot = @"C:\repo";
    private const string _projectDirectory = @"C:\repo\src\project";

    public PostSharpRepositoryConfigurationTests( ITestOutputHelper logger ) : base( logger ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services )
    {
        services.AddService( typeof(SharpCrafters.Backstage.Application.ProductProfile), PostSharpProduct.Profile );
        services.AddService( typeof(IRepositoryConfigurationReader), PostSharpRepositoryConfigurationReader.Instance );
    }

    private static string Project( string content )
        => $"""<Project xmlns="http://schemas.postsharp.org/1.0/configuration">{content}</Project>""";

    private static RepositoryConfigurationReadResult Read( string text ) => PostSharpRepositoryConfigurationReader.Instance.Read( "postsharp.config", text );

    private RepositoryConfigurationResult Resolve() => new RepositoryConfigurationService( this.ServiceProvider ).GetRepositoryConfiguration( _projectDirectory );

    private void CreateGitRoot() => this.FileSystem.CreateDirectory( Path.Combine( _repoRoot, ".git" ) );

    private void WriteFile( string directory, string content )
    {
        this.FileSystem.CreateDirectory( directory );
        this.FileSystem.WriteAllText( Path.Combine( directory, "postsharp.config" ), content );
    }

    [Theory]
    [InlineData( "False", false )]
    [InlineData( "false", false )]
    [InlineData( "True", true )]
    [InlineData( "TRUE", true )]
    public void ALiteralValueIsRead( string value, bool expected )
    {
        var result = Read( Project( $"""<Property Name="TelemetryEnabled" Value="{value}" />""" ) );

        Assert.Equal( expected, result.TelemetryEnabled );
        Assert.True( result.DeclaresSettings );
        Assert.Empty( result.Errors );
    }

    [Fact]
    public void AFileWithoutThePropertyDeclaresNothing()
    {
        var result = Read( Project( """<Property Name="LoggingEnabled" Value="{$Configuration}" /><Multicast />""" ) );

        Assert.Null( result.TelemetryEnabled );
        Assert.False( result.DeclaresSettings );
        Assert.Empty( result.Errors );
    }

    [Theory]
    [InlineData( "{$Configuration}" )]
    [InlineData( "{@Telemetry}" )]
    [InlineData( "{has-plugin('Foo')}" )]
    public void AnExpressionIsRefusedAsAnExpression( string value )
    {
        var result = Read( Project( $"""<Property Name="TelemetryEnabled" Value="{value}" />""" ) );

        Assert.Null( result.TelemetryEnabled );
        Assert.True( result.DeclaresSettings );
        Assert.Contains( "Expressions are not supported", Assert.Single( result.Errors ), StringComparison.Ordinal );
    }

    [Theory]
    [InlineData( "" )]
    [InlineData( "0" )]
    [InlineData( "no" )]
    [InlineData( " false" )]
    public void AnotherValueIsRefused( string value )
    {
        var result = Read( Project( $"""<Property Name="TelemetryEnabled" Value="{value}" />""" ) );

        Assert.Null( result.TelemetryEnabled );
        Assert.Contains( "the value must be 'True' or 'False'", Assert.Single( result.Errors ), StringComparison.Ordinal );
    }

    [Theory]
    [InlineData( "Condition=\"'$(Configuration)'=='Release'\"" )]
    [InlineData( "Overwrite=\"false\"" )]
    [InlineData( "Deferred=\"true\"" )]
    public void AnotherAttributeIsRefused( string attribute )
    {
        var result = Read( Project( $"""<Property Name="TelemetryEnabled" Value="False" {attribute} />""" ) );

        Assert.Null( result.TelemetryEnabled );
        Assert.Contains( "only the 'Name' and 'Value' attributes are supported", Assert.Single( result.Errors ), StringComparison.Ordinal );
    }

    [Fact]
    public void ASecondDefinitionIsRefused()
    {
        var result = Read( Project( """<Property Name="TelemetryEnabled" Value="False" /><Property Name="TelemetryEnabled" Value="True" />""" ) );

        Assert.Null( result.TelemetryEnabled );
        Assert.Contains( "2 times", Assert.Single( result.Errors ), StringComparison.Ordinal );
    }

    [Fact]
    public void AFileThatIsNotXmlIsRefused()
    {
        var result = Read( "<Project" );

        Assert.False( result.DeclaresSettings );
        Assert.Contains( "not well-formed XML", Assert.Single( result.Errors ), StringComparison.Ordinal );
    }

    [Fact]
    public void AFileOfAnotherNamespaceIsRefused()
    {
        var result = Read( """<Project><Property Name="TelemetryEnabled" Value="False" /></Project>""" );

        Assert.Contains( "http://schemas.postsharp.org/1.0/configuration", Assert.Single( result.Errors ), StringComparison.Ordinal );
    }

    [Fact]
    public void TheFileAtTheRootOptsTheRepositoryOut()
    {
        this.CreateGitRoot();
        this.WriteFile( _repoRoot, Project( """<Property Name="TelemetryEnabled" Value="False" />""" ) );

        var result = this.Resolve();

        Assert.False( result.Configuration!.Telemetry!.Enabled );
        Assert.Empty( result.Warnings );
    }

    [Fact]
    public void AnInvalidFileAtTheRootIsIgnoredAndWarns()
    {
        this.CreateGitRoot();
        this.WriteFile( _repoRoot, Project( """<Property Name="TelemetryEnabled" Value="{$NoTelemetry}" />""" ) );

        var result = this.Resolve();

        Assert.Null( result.Configuration!.Telemetry );
        var warning = Assert.Single( result.Warnings );
        Assert.Equal( RepositoryConfigurationWarningKind.MalformedFile, warning.Kind );
        Assert.Equal( Path.Combine( _repoRoot, "postsharp.config" ), warning.FilePath );
    }

    [Fact]
    public void AFileBelowTheRootWithoutThePropertyIsNotReported()
    {
        this.CreateGitRoot();
        this.WriteFile( Path.Combine( _repoRoot, "src" ), Project( """<Property Name="LoggingEnabled" Value="True" />""" ) );

        var result = this.Resolve();

        Assert.Null( result.Configuration!.Telemetry );
        Assert.Empty( result.Warnings );
    }

    [Fact]
    public void ThePropertyBelowTheRootIsIgnoredAndWarns()
    {
        this.CreateGitRoot();
        this.WriteFile( Path.Combine( _repoRoot, "src" ), Project( """<Property Name="TelemetryEnabled" Value="False" />""" ) );

        var result = this.Resolve();

        Assert.Null( result.Configuration!.Telemetry );
        var warning = Assert.Single( result.Warnings );
        Assert.Equal( RepositoryConfigurationWarningKind.MisplacedFile, warning.Kind );
        Assert.Equal( Path.Combine( _repoRoot, "src", "postsharp.config" ), warning.FilePath );
    }

    [Fact]
    public void ThePostSharpProductReadsPostSharpConfig()
    {
        Assert.Equal( "postsharp.config", PostSharpProduct.Profile.RepositoryConfigurationFileName );
    }
}
