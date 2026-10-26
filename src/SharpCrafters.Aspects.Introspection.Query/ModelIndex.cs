// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Projects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// The index of a file: its model, as the loader built it, in a binary form that is read without parsing XML, decoding the
/// name table or converting identifiers (Documentation/future/Introspection-Cli.md of PostSharp, section 7.3).
/// </summary>
/// <remarks>
/// <para>
/// An index is kept per file in the cache directory, keyed by the path of the file, and is valid while the write time and the
/// size of the file, the directory of the project and the version of the format are those it records. A query on a solution
/// therefore loads only the files that changed since the previous command.
/// </para>
/// <para>
/// The format is a table of strings, then the objects of each collection by index, in the order of the model. A reference is
/// the index of an object, or -1 for <c>null</c>.
/// </para>
/// </remarks>
internal static class ModelIndex
{
    // Changed whenever the format or what the loader builds changes.
    private const int _formatVersion = 2;
    private const int _magic = 0x58494353; // "SCIX"

    /// <summary>
    /// Loads the model of a target from its index when it is valid, or with the provider, then writes the index.
    /// </summary>
    public static IntrospectionModel Load( IntrospectionTarget target, IntrospectionProvider provider, string? cacheDirectory )
    {
        if ( cacheDirectory == null )
        {
            return provider.Load( target );
        }

        var file = new FileInfo( target.FilePath );
        var stamp = string.Join( "|", _formatVersion, provider.ProductName, file.FullName, file.LastWriteTimeUtc.Ticks, file.Length, target.Project.Directory, target.AssemblyName );
        var path = Path.Combine( cacheDirectory, "index", EvaluationCache.Hash( file.FullName + "|" + provider.ProductName ) + ".bin" );

        try
        {
            if ( File.Exists( path ) )
            {
                using var stream = File.OpenRead( path );
                using var reader = new BinaryReader( stream, Encoding.UTF8 );

                if ( reader.ReadInt32() == _magic && reader.ReadString() == stamp )
                {
                    return Read( reader );
                }
            }
        }
        catch ( Exception e ) when ( e is IOException or EndOfStreamException or UnauthorizedAccessException or InvalidDataException )
        {
            // A damaged or concurrently replaced index is rebuilt.
        }

        var model = provider.Load( target );

        try
        {
            Directory.CreateDirectory( Path.GetDirectoryName( path )! );
            var temporary = path + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";

            using ( var stream = File.Create( temporary ) )
            using ( var writer = new BinaryWriter( stream, Encoding.UTF8 ) )
            {
                writer.Write( _magic );
                writer.Write( stamp );
                Write( writer, model );
            }

            File.Move( temporary, path, true );
        }
        catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
        {
            // The index is an optimization: a command that cannot write it still runs.
        }

        return model;
    }

    internal static void Write( BinaryWriter writer, IntrospectionModel model )
    {
        var strings = new Dictionary<string, int>( StringComparer.Ordinal );
        var table = new List<string>();

        int S( string? value )
        {
            if ( value == null )
            {
                return -1;
            }

            if ( !strings.TryGetValue( value, out var index ) )
            {
                index = table.Count;
                strings.Add( value, index );
                table.Add( value );
            }

            return index;
        }

        var declarations = Index( model.Declarations );
        var classes = Index( model.AspectClasses );
        var instances = Index( model.AspectInstances );

        int D( IntrospectionDeclaration? d ) => d == null ? -1 : declarations[d];

        // The objects are written to a buffer first, because the table of strings comes before them.
        using var body = new MemoryStream();
        using var w = new BinaryWriter( body, Encoding.UTF8 );

        w.Write( S( model.Assembly ) );
        w.Write( model.SchemaVersion );
        w.Write( S( model.ProductVersion ) );
        w.Write( model.LinesOfCode );
        w.Write( model.EnhancedTypesCount );

        w.Write( model.Declarations.Count );

        foreach ( var d in model.Declarations )
        {
            w.Write( S( d.Assembly ) );
            w.Write( S( d.Id ) );
            w.Write( (byte) d.Kind );
            w.Write( S( d.Name ) );
            w.Write( S( d.SourceIdentifier ) );
            w.Write( S( d.Namespace ) );
        }

        foreach ( var d in model.Declarations )
        {
            w.Write( D( d.DeclaringType ) );
            w.Write( D( d.ContainingDeclaration ) );
            w.Write( D( d.BackingField ) );
            WriteList( w, d.Members, D );
            WriteList( w, d.Accessors, D );
        }

        w.Write( model.AspectClasses.Count );

        foreach ( var c in model.AspectClasses )
        {
            w.Write( D( c.Declaration ) );
            w.Write( c.HasLimitedLicense );
        }

        w.Write( model.AspectInstances.Count );

        foreach ( var i in model.AspectInstances )
        {
            w.Write( classes[i.AspectClass] );
            w.Write( D( i.TargetDeclaration ) );
            w.Write( S( i.Id ) );
            w.Write( S( i.StructuralKey ) );
            w.Write( i.LinesOfCodeAvoided );
            w.Write( S( i.Tags ) );
            w.Write( i.IsSkipped );
            w.Write( S( i.FileId ) );
            w.Write( i.Transformations.Count );

            foreach ( var t in i.Transformations )
            {
                w.Write( D( t.TargetDeclaration ) );
                w.Write( D( t.AdviceDeclaration ) );
                w.Write( S( t.Description ) );
                w.Write( S( t.Semantic ) );
                w.Write( t.Order );
                w.Write( t.LinesOfCodeAvoided ?? int.MinValue );
            }
        }

        w.Write( model.Predecessors.Count );

        foreach ( var p in model.Predecessors )
        {
            w.Write( (byte) p.Kind );
            w.Write( p.IsKindInferred );
            w.Write( S( p.Id ) );
            w.Write( S( p.StructuralKey ) );
            w.Write( D( p.Declaration ) );
            w.Write( p.AttributeClass == null ? -1 : classes[p.AttributeClass] );
            w.Write( S( p.File ) );
            w.Write( S( p.KeyFile ) );
            w.Write( p.Line ?? int.MinValue );
            w.Write( p.Column ?? int.MinValue );
            w.Write( p.AspectInstance == null ? -1 : instances[p.AspectInstance] );
            WriteList( w, p.AspectInstances, i => instances[i] );
        }

        w.Write( model.Annotations.Count );

        foreach ( var a in model.Annotations )
        {
            w.Write( D( a.TargetDeclaration ) );
            w.Write( D( a.AnnotationClass ) );
            w.Write( S( a.Arguments ) );
            w.Write( S( a.Description ) );
            w.Write( a.LinesOfCodeAvoided );
        }

        w.Write( model.DeclarationMappings.Count );

        foreach ( var m in model.DeclarationMappings )
        {
            w.Write( D( m.Source ) );
            w.Write( D( m.Target ) );
            w.Write( m.Kind == null ? -1 : (int) m.Kind.Value );
        }

        w.Flush();

        writer.Write( table.Count );

        foreach ( var s in table )
        {
            writer.Write( s );
        }

        writer.Write( body.ToArray() );

        static Dictionary<T, int> Index<T>( IReadOnlyList<T> items )
            where T : notnull
        {
            var index = new Dictionary<T, int>( ReferenceEqualityComparer.Instance as IEqualityComparer<T> ?? EqualityComparer<T>.Default );

            for ( var i = 0; i < items.Count; i++ )
            {
                index.Add( items[i], i );
            }

            return index;
        }

        static void WriteList<T>( BinaryWriter w, IReadOnlyList<T> items, Func<T, int> getIndex )
        {
            w.Write( items.Count );

            foreach ( var item in items )
            {
                w.Write( getIndex( item ) );
            }
        }
    }

    /// <summary>
    /// Reads a model from an index.
    /// </summary>
    /// <exception cref="InvalidDataException">The index is damaged: a count or a reference is out of range.</exception>
    /// <exception cref="EndOfStreamException">The index is truncated.</exception>
    internal static IntrospectionModel Read( BinaryReader reader )
    {
        try
        {
            return ReadModel( reader );
        }
        catch ( Exception e ) when ( e is IndexOutOfRangeException or ArgumentException or NullReferenceException )
        {
            throw new InvalidDataException( "The index is damaged.", e );
        }
    }

    private static IntrospectionModel ReadModel( BinaryReader reader )
    {
        // Each element takes at least one byte, so a count larger than what remains is damaged; checking it before allocating
        // keeps a damaged count from allocating an array of gigabytes.
        int Count()
        {
            var count = reader.ReadInt32();

            if ( count < 0 || count > reader.BaseStream.Length - reader.BaseStream.Position )
            {
                throw new InvalidDataException( "A count of the index is out of range." );
            }

            return count;
        }

        var table = new string[Count()];

        for ( var i = 0; i < table.Length; i++ )
        {
            table[i] = reader.ReadString();
        }

        string? S() => reader.ReadInt32() is var index and >= 0 ? table[index] : null;
        string R() => S() ?? throw new InvalidDataException( "A required string of the index is missing." );
        int? N() => reader.ReadInt32() is var value and not int.MinValue ? value : null;

        var model = new IntrospectionModel( R() ) { SchemaVersion = reader.ReadInt32(), ProductVersion = S(), LinesOfCode = reader.ReadInt32(), EnhancedTypesCount = reader.ReadInt32() };

        var declarations = new IntrospectionDeclaration[Count()];

        for ( var i = 0; i < declarations.Length; i++ )
        {
            var assembly = R();
            var id = R();
            var kind = (IntrospectionDeclarationKind) reader.ReadByte();
            var name = R();
            var declaration = new IntrospectionDeclaration( assembly, id, kind, name, S() ) { Namespace = S() };
            declarations[i] = declaration;
            model.DeclarationsList.Add( declaration );
            model.DeclarationsByQualifiedId[declaration.QualifiedId] = declaration;
        }

        IntrospectionDeclaration? D() => reader.ReadInt32() is var index and >= 0 ? declarations[index] : null;

        foreach ( var d in declarations )
        {
            d.DeclaringType = D();
            d.ContainingDeclaration = D();
            d.BackingField = D();
            ReadList( d.MembersList, () => D()! );
            ReadList( d.AccessorsList, () => D()! );
        }

        var classes = new IntrospectionAspectClass[Count()];

        for ( var i = 0; i < classes.Length; i++ )
        {
            classes[i] = new IntrospectionAspectClass( D()! ) { HasLimitedLicense = reader.ReadBoolean() };
            model.AspectClassesList.Add( classes[i] );
        }

        var instances = new IntrospectionAspectInstance[Count()];

        for ( var i = 0; i < instances.Length; i++ )
        {
            var aspectClass = classes[reader.ReadInt32()];

            var instance = new IntrospectionAspectInstance( aspectClass, D()! )
            {
                Id = R(),
                StructuralKey = R(),
                LinesOfCodeAvoided = reader.ReadInt32(),
                Tags = S(),
                IsSkipped = reader.ReadBoolean(),
                FileId = S()
            };

            var count = Count();

            for ( var t = 0; t < count; t++ )
            {
                var transformation = new IntrospectionTransformation( instance, D()! )
                {
                    AdviceDeclaration = D(), Description = S(), Semantic = S(), Order = reader.ReadInt32(), LinesOfCodeAvoided = N()
                };

                instance.TransformationsList.Add( transformation );
                model.TransformationsList.Add( transformation );
            }

            instances[i] = instance;
            aspectClass.AspectInstancesList.Add( instance );
            model.AspectInstancesList.Add( instance );
        }

        var predecessorCount = Count();

        for ( var i = 0; i < predecessorCount; i++ )
        {
            var predecessor = new IntrospectionPredecessor( (AspectPredecessorKind) reader.ReadByte(), reader.ReadBoolean() )
            {
                Id = R(),
                StructuralKey = R(),
                Declaration = D(),
                AttributeClass = reader.ReadInt32() is var c and >= 0 ? classes[c] : null,
                File = S(),
                KeyFile = S(),
                Line = N(),
                Column = N(),
                AspectInstance = reader.ReadInt32() is var a and >= 0 ? instances[a] : null
            };

            ReadList( predecessor.AspectInstancesList, () => instances[reader.ReadInt32()] );

            foreach ( var instance in predecessor.AspectInstancesList )
            {
                instance.PredecessorsList.Add( predecessor );
            }

            model.PredecessorsList.Add( predecessor );
        }

        var annotationCount = Count();

        for ( var i = 0; i < annotationCount; i++ )
        {
            model.AnnotationsList.Add(
                new IntrospectionAnnotation( D()! ) { AnnotationClass = D(), Arguments = S(), Description = S(), LinesOfCodeAvoided = reader.ReadInt32() } );
        }

        var mappingCount = Count();

        for ( var i = 0; i < mappingCount; i++ )
        {
            var source = D()!;
            var target = D()!;
            var kind = reader.ReadInt32();
            model.DeclarationMappingsList.Add( new IntrospectionDeclarationMapping( source, target, kind < 0 ? null : (DeclarationMappingKind) kind ) );
        }

        return model;

        void ReadList<T>( List<T> list, Func<T> read )
        {
            var count = Count();

            for ( var i = 0; i < count; i++ )
            {
                list.Add( read() );
            }
        }
    }
}
