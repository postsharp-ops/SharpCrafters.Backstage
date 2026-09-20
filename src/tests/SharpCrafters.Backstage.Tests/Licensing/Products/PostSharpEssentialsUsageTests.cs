// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage;
using PostSharp.Backstage.Serialization;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Testing;
using System;
using System.Linq;
using System.Text.Json.Serialization.Metadata;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Products;

/// <summary>
/// The record of how many lines of code each assembly has had enhanced under the free edition of PostSharp, which is
/// what the cap of that edition is applied to across the projects of a solution.
/// </summary>
/// <remarks>
/// A compilation sees one project, so the number for the solution is accumulated through this file. What the tests
/// here assert is that the file survives a round trip and that an assembly leaves the record when it stops using the
/// free edition, because a row that stayed would charge a project for lines of code it no longer enhances.
/// </remarks>
public sealed class PostSharpEssentialsUsageTests : TestsBase
{
    public PostSharpEssentialsUsageTests( ITestOutputHelper logger ) : base( logger )
    {
        this.InitializationOptions = this.InitializationOptions with
        {
            AdditionalJsonTypeInfoResolvers = new IJsonTypeInfoResolver[] { PostSharpBackstageJsonContext.Default }
        };
    }

    /// <summary>
    /// The file-based manager and not the in-memory one of the test base, because what has to be exercised is the
    /// serializer: a type the context does not declare is read and written by the in-memory manager and fails only
    /// once a file is involved.
    /// </summary>
    private IConfigurationManager FileConfigurationManager => new SharpCrafters.Backstage.Configuration.ConfigurationManager( this.ServiceProvider );

    /// <summary>
    /// An assembly nothing is recorded for counts for nothing, rather than being absent in a way the caller has to
    /// handle.
    /// </summary>
    [Fact]
    public void AnUnknownAssemblyCountsForNothing()
        => Assert.Equal( 0, new PostSharpEssentialsUsageConfiguration().GetLinesOfCode( "A, Version=1.0.0.0" ) );

    /// <summary>
    /// What one compilation writes is what the compilation of a project referencing that assembly reads. This is the
    /// whole purpose of the file, and it goes through the configuration manager and the serializer, which is where a
    /// type the serializer does not know would fail.
    /// </summary>
    [Fact]
    public void WhatIsWrittenIsRead()
    {
        this.FileConfigurationManager.Update<PostSharpEssentialsUsageConfiguration>( c => c.SetLinesOfCode( "A, Version=1.0.0.0", 700 ) );

        Assert.Equal( 700, this.FileConfigurationManager.Get<PostSharpEssentialsUsageConfiguration>().GetLinesOfCode( "A, Version=1.0.0.0" ) );
    }

    /// <summary>
    /// Two assemblies are counted apart, so the compilation that reads them adds up the closure it references and
    /// nothing else.
    /// </summary>
    [Fact]
    public void AssembliesAreCountedApart()
    {
        this.FileConfigurationManager.Update<PostSharpEssentialsUsageConfiguration>(
            c => c.SetLinesOfCode( "A, Version=1.0.0.0", 700 ).SetLinesOfCode( "B, Version=1.0.0.0", 200 ) );

        var configuration = this.FileConfigurationManager.Get<PostSharpEssentialsUsageConfiguration>();

        Assert.Equal( 700, configuration.GetLinesOfCode( "A, Version=1.0.0.0" ) );
        Assert.Equal( 200, configuration.GetLinesOfCode( "B, Version=1.0.0.0" ) );
    }

    /// <summary>
    /// Two versions of one assembly are two assemblies, because they are two assemblies to the compiler that reads
    /// the references of a project.
    /// </summary>
    [Fact]
    public void TwoVersionsOfAnAssemblyAreCountedApart()
    {
        var configuration = new PostSharpEssentialsUsageConfiguration()
            .SetLinesOfCode( "A, Version=1.0.0.0", 700 )
            .SetLinesOfCode( "A, Version=2.0.0.0", 200 );

        Assert.Equal( 700, configuration.GetLinesOfCode( "A, Version=1.0.0.0" ) );
        Assert.Equal( 200, configuration.GetLinesOfCode( "A, Version=2.0.0.0" ) );
    }

    /// <summary>
    /// An assembly that no longer uses the free edition leaves the record, instead of counting for ever towards the
    /// cap of every solution that references it.
    /// </summary>
    [Fact]
    public void ZeroRemovesTheRecordOfAnAssembly()
    {
        this.FileConfigurationManager.Update<PostSharpEssentialsUsageConfiguration>( c => c.SetLinesOfCode( "A, Version=1.0.0.0", 700 ) );
        this.FileConfigurationManager.Update<PostSharpEssentialsUsageConfiguration>( c => c.SetLinesOfCode( "A, Version=1.0.0.0", 0 ) );

        var configuration = this.FileConfigurationManager.Get<PostSharpEssentialsUsageConfiguration>();

        Assert.Equal( 0, configuration.GetLinesOfCode( "A, Version=1.0.0.0" ) );
        Assert.Empty( configuration.LinesOfCodeByAssembly );
    }

    /// <summary>
    /// Every type of the PostSharp context keeps the members of the file that this version does not declare, as every
    /// type of the product-neutral context does. Several versions of PostSharp read one file of the user profile, and
    /// a version that removed what it does not know would destroy what a later version wrote.
    /// </summary>
    [Fact]
    public void EveryTypeOfThePostSharpJsonContextCarriesExtensionData()
    {
        var types = JsonExtensionDataVerifier.GetSerializedObjectTypes( typeof(PostSharpBackstageJsonContext) );

        Assert.NotEmpty( types );

        var typesWithoutExtensionData = JsonExtensionDataVerifier.GetTypesWithoutExtensionData( typeof(PostSharpBackstageJsonContext) );

        Assert.True(
            typesWithoutExtensionData.Count == 0,
            "The following types are serialized into a configuration file and declare no member annotated with "
            + "JsonExtensionDataAttribute, so a version that does not know a member of the file removes it:"
            + Environment.NewLine
            + string.Join( Environment.NewLine, typesWithoutExtensionData.Select( t => "  " + t.FullName ) ) );
    }
}
