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

    private readonly Dictionary<IntrospectionTarget, IIntrospectionSourceLocations?> _locations = new();

    public QueryContext(
        IntrospectionModel model,
        ModelProvenance provenance,
        IntrospectionProvider provider,
        string workingDirectory )
    {
        this.Model = model;
        this.Provenance = provenance;
        this.Provider = provider;
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

    public ModelProvenance Provenance { get; }

    // The targets whose files the query read, which the collection files lists.
    public IReadOnlyList<IntrospectionTarget> Targets => this.Provenance.AllTargets;

    public IntrospectionProvider Provider { get; }

    public string WorkingDirectory { get; }

    /// <summary>
    /// Gets the target frameworks of the files that hold an object.
    /// </summary>
    public IReadOnlyList<string> GetTargetFrameworks( object value )
        => this.Provenance.GetTargets( value ).Select( t => t.TargetFramework ).Distinct( System.StringComparer.OrdinalIgnoreCase ).ToList();

    /// <summary>
    /// Gets the project of an object: for a declaration, the project that builds its assembly when it is in the scope; otherwise
    /// the first project whose file holds it.
    /// </summary>
    public string GetProject( object value )
    {
        var targets = this.Provenance.GetTargets( value );

        if ( value is IntrospectionDeclaration declaration && this.GetTarget( declaration ) is { } own )
        {
            return own.Project.Name;
        }

        return targets.Count == 0 ? "" : targets[0].Project.Name;
    }

    /// <summary>
    /// Gets the targets whose assembly declares a declaration, which are those whose source locations give its location.
    /// </summary>
    public IntrospectionTarget? GetTarget( IntrospectionDeclaration declaration )
        => this.Targets.FirstOrDefault( t => string.Equals( t.AssemblyName, declaration.Assembly, System.StringComparison.Ordinal ) );

    /// <summary>
    /// Gets the source locations of a target, which the provider reads once.
    /// </summary>
    public IIntrospectionSourceLocations? GetSourceLocations( IntrospectionTarget target )
    {
        if ( !this._locations.TryGetValue( target, out var locations ) )
        {
            locations = this.Provider.GetSourceLocations( target );
            this._locations.Add( target, locations );
        }

        return locations;
    }

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
        // Each target framework of the assembly in turn: a declaration under a preprocessor symbol is compiled into some of them only.
        foreach ( var target in this.Targets.Where( t => string.Equals( t.AssemblyName, declaration.Assembly, System.StringComparison.Ordinal ) ) )
        {
            if ( this.GetSourceLocations( target )?.GetLocation( declaration ) is { } location )
            {
                return this.FormatLocation( location.File, location.Line, location.Column );
            }
        }

        return null;
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
