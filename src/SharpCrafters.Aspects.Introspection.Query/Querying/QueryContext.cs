// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Projects;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// The model that a command queries, with the target it was loaded from, its source locations, and the reverse relations from
/// the declarations to what applies to them.
/// </summary>
internal sealed class QueryContext
{
    private readonly Dictionary<IntrospectionDeclaration, List<IntrospectionAspectInstance>> _aspectInstancesByTarget = new();
    private readonly Dictionary<IntrospectionDeclaration, List<IntrospectionTransformation>> _transformationsByTarget = new();
    private readonly Dictionary<IntrospectionDeclaration, List<IntrospectionAnnotation>> _annotationsByTarget = new();
    private readonly Dictionary<IntrospectionDeclaration, List<IntrospectionDeclarationMapping>> _mappingsByDeclaration = new();

    public QueryContext(
        IntrospectionModel model,
        IntrospectionTarget target,
        IntrospectionProvider provider,
        IIntrospectionSourceLocations? locations,
        string workingDirectory )
    {
        this.Model = model;
        this.Target = target;
        this.Provider = provider;
        this.Locations = locations;
        this.WorkingDirectory = workingDirectory;

        foreach ( var instance in model.AspectInstances )
        {
            Add( this._aspectInstancesByTarget, instance.TargetDeclaration, instance );
        }

        foreach ( var transformation in model.Transformations )
        {
            Add( this._transformationsByTarget, transformation.TargetDeclaration, transformation );
        }

        foreach ( var annotation in model.Annotations )
        {
            Add( this._annotationsByTarget, annotation.TargetDeclaration, annotation );
        }

        foreach ( var mapping in model.DeclarationMappings )
        {
            Add( this._mappingsByDeclaration, mapping.Source, mapping );

            if ( mapping.Target != mapping.Source )
            {
                Add( this._mappingsByDeclaration, mapping.Target, mapping );
            }
        }

        static void Add<T>( Dictionary<IntrospectionDeclaration, List<T>> index, IntrospectionDeclaration key, T value )
        {
            if ( !index.TryGetValue( key, out var list ) )
            {
                list = [];
                index.Add( key, list );
            }

            list.Add( value );
        }
    }

    public IntrospectionModel Model { get; }

    public IntrospectionTarget Target { get; }

    public IntrospectionProvider Provider { get; }

    public IIntrospectionSourceLocations? Locations { get; }

    public string WorkingDirectory { get; }

    public IReadOnlyList<string> TargetFrameworks => [this.Target.TargetFramework];

    public string Project => this.Target.Project.Name;

    public IReadOnlyList<IntrospectionAspectInstance> GetAspectInstances( IntrospectionDeclaration declaration )
        => this._aspectInstancesByTarget.GetValueOrDefault( declaration ) ?? [];

    public IReadOnlyList<IntrospectionTransformation> GetTransformations( IntrospectionDeclaration declaration )
        => this._transformationsByTarget.GetValueOrDefault( declaration ) ?? [];

    public IReadOnlyList<IntrospectionAnnotation> GetAnnotations( IntrospectionDeclaration declaration )
        => this._annotationsByTarget.GetValueOrDefault( declaration ) ?? [];

    public IReadOnlyList<IntrospectionDeclarationMapping> GetDeclarationMappings( IntrospectionDeclaration declaration )
        => this._mappingsByDeclaration.GetValueOrDefault( declaration ) ?? [];

    /// <summary>
    /// Gets the location of a declaration as <c>path:line:column</c>, the path relative to the working directory, or <c>null</c>.
    /// </summary>
    public string? GetLocation( IntrospectionDeclaration declaration )
    {
        var location = this.Locations?.GetLocation( declaration );

        return location == null ? null : this.FormatLocation( location.File, location.Line, location.Column );
    }

    /// <summary>
    /// Gets the location of a predecessor: the position of its XML element, or, for an attribute, the location that the file
    /// gives it, or <c>null</c>.
    /// </summary>
    public string? GetLocation( IntrospectionPredecessor predecessor )
        => predecessor.File == null ? null : this.FormatLocation( predecessor.File, predecessor.Line, predecessor.Column );

    public string GetRelativePath( string path )
    {
        if ( !Path.IsPathFullyQualified( path ) )
        {
            return path.Replace( '\\', '/' );
        }

        // A path on another drive stays fully qualified.
        return Path.GetRelativePath( this.WorkingDirectory, path ).Replace( '\\', '/' );
    }

    private string FormatLocation( string file, int? line, int? column )
    {
        var path = this.GetRelativePath( file );

        return line == null ? path
            : column == null ? string.Create( CultureInfo.InvariantCulture, $"{path}:{line}" )
            : string.Create( CultureInfo.InvariantCulture, $"{path}:{line}:{column}" );
    }

    /// <summary>
    /// Splits the tags of an aspect instance, which version 1 writes as one string.
    /// </summary>
    public static IReadOnlyList<string> SplitTags( string? tags )
        => tags == null ? [] : tags.Split( [',', ' ', ';'], System.StringSplitOptions.RemoveEmptyEntries ).ToList();
}
