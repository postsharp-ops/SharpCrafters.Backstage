// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Query.Tests;

/// <summary>
/// The index of a file holds its model as the loader built it.
/// </summary>
public sealed class ModelIndexTests
{
    public static IEnumerable<object[]> Fixtures
        => Directory.GetFiles( Path.Combine( AppContext.BaseDirectory, "Fixtures" ), "*.pssym", SearchOption.AllDirectories ).Select( f => new object[] { Path.GetFileName( f ) } );

    [Theory]
    [MemberData( nameof(Fixtures) )]
    public void AModelIsReadFromItsIndexAsItWasLoaded( string fileName )
    {
        var file = Directory.GetFiles( Path.Combine( AppContext.BaseDirectory, "Fixtures" ), fileName, SearchOption.AllDirectories ).First();
        using var stream = File.OpenRead( file );
        var model = IntrospectionV1Loader.Load( stream );

        var written = Write( model );
        var read = ModelIndex.Read( new BinaryReader( new MemoryStream( written ), Encoding.UTF8 ) );

        // Written again, the model read from the index gives the same bytes, and the same objects in the same order.
        Assert.Equal( written, Write( read ) );
        Assert.Equal( Describe( model ), Describe( read ) );
    }

    private static byte[] Write( IntrospectionModel model )
    {
        using var stream = new MemoryStream();

        using ( var writer = new BinaryWriter( stream, Encoding.UTF8 ) )
        {
            ModelIndex.Write( writer, model );
        }

        return stream.ToArray();
    }

    private static List<string> Describe( IntrospectionModel model )
        => model.AspectInstances.Select(
                i => $"{i.Id} {i.StructuralKey} {i.AspectClass.FullName} {i.TargetDeclaration.QualifiedId} "
                     + string.Join( ";", i.Predecessors.Select( p => p.StructuralKey + " " + p.Kind + " " + p.AspectInstance?.Id ) ) + " "
                     + string.Join( ";", i.Transformations.Select( t => t.TargetDeclaration.QualifiedId + " " + t.Order + " " + t.Description ) ) )
            .Concat( model.Declarations.Select( d => $"{d.QualifiedId} {d.Kind} {d.DeclaringType?.QualifiedId} {d.BackingField?.QualifiedId} {string.Join( ",", d.Accessors.Select( a => a.Name ) )}" ) )
            .Concat( model.Annotations.Select( a => $"{a.TargetDeclaration.QualifiedId} {a.Description}" ) )
            .Concat( model.DeclarationMappings.Select( m => $"{m.Source.QualifiedId} {m.Target.QualifiedId}" ) )
            .ToList();
}
