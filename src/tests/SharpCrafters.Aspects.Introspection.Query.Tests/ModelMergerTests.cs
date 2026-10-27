// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Projects;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Query.Tests;

/// <summary>
/// The merge of the models of several files.
/// </summary>
public sealed class ModelMergerTests
{
    private static readonly string _root = Path.Combine( Path.GetTempPath(), "introspection-merge" );

    private static (IntrospectionTarget Target, IntrospectionModel Model) Load( string project, string framework, string assembly, string body )
    {
        var directory = Path.Combine( _root, project );
        var target = new IntrospectionTarget(
            new IntrospectionProject( Path.Combine( directory, project + ".csproj" ), true ),
            framework,
            "Debug",
            assembly,
            Path.Combine( directory, "bin", assembly + ".pssym" ),
            new Dictionary<string, string>(),
            null );

        var model = IntrospectionV1Loader.Load(
            new MemoryStream( Encoding.UTF8.GetBytes( $"""<Symbols xmlns="http://schemas.postsharp.org/2.0/symbols" PostSharpVersion="2027.0.6">{body}</Symbols>""" ) ),
            assembly,
            directory );

        return (target, model);
    }

    [Fact]
    public void EveryAspectClassAndItsLicenseFlagAreMerged()
    {
        // The second framework has the same aspect instance, a limited license, and a class without instance.
        const string instance = """<Instance Declaration="M:[App]App.C::Run()"><Target><JoinPoint /></Target></Instance>""";

        var (model, provenance) = ModelMerger.Merge(
        [
            Load( "App", "net10.0", "App", $"""<Class Class="T:[App]App.LogAttribute">{instance}</Class>""" ),
            Load( "App", "net48", "App", $"""<Class Class="T:[App]App.LogAttribute" LimitedLicense="true">{instance}</Class><Class Class="T:[App]App.CacheAttribute" />""" )
        ] );

        var log = Assert.Single( model.AspectClasses, c => c.FullName == "App.LogAttribute" );
        Assert.True( log.HasLimitedLicense );
        Assert.Equal( 2, provenance.GetTargets( log ).Count );

        var cache = Assert.Single( model.AspectClasses, c => c.FullName == "App.CacheAttribute" );
        Assert.Equal( "net48", Assert.Single( provenance.GetTargets( cache ) ).TargetFramework );
    }

    [Fact]
    public void TheXmlPredecessorsOfTwoProjectsAreDistinct()
    {
        // Each project has its own postsharp.config, at the same position.
        static string Body( string project ) => $"""
                                                 <Class Class="T:[{project}]{project}.LogAttribute">
                                                   <Instance File="{Path.Combine( _root, project, "postsharp.config" )}" Line="5" Column="6"><Target Target="M:[{project}]{project}.F::Run()" /></Instance>
                                                 </Class>
                                                 """;

        var (model, _) = ModelMerger.Merge(
        [
            Load( "App", "net10.0", "App", Body( "App" ) ),
            Load( "App", "net48", "App", Body( "App" ) ),
            Load( "Lib", "net10.0", "Lib", Body( "Lib" ) )
        ] );

        Assert.Equal( ["xml:[App]postsharp.config(5,6)", "xml:[Lib]postsharp.config(5,6)"], model.Predecessors.Select( p => p.StructuralKey ).Order().ToArray() );
        Assert.Equal( 2, model.Predecessors.Select( p => p.Id ).Distinct().Count() );
        Assert.All( model.Predecessors, p => Assert.Single( p.AspectInstances ) );
    }
}
