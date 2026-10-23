// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SharpCrafters.Aspects.Introspection.Query.Output;

/// <summary>
/// How <c>show</c> writes the lists of the objects related to the object it shows: at most <see cref="Limit"/> objects per list, or
/// all of them when it is <c>null</c>.
/// </summary>
/// <remarks>
/// A list is written as <c>{ "count", "truncated", "items" }</c>, with <c>byNamespace</c> and <c>next</c> when it is truncated, so
/// that its shape does not depend on its length. An aspect class can have thousands of aspect instances, and an aspect instance of a
/// type-level aspect thousands of transformations: written in full, they make an output too large for an agent to read.
/// </remarks>
internal sealed record RelatedListOptions( int? Limit )
{
    /// <summary>
    /// The number of objects of a list that <c>show</c> writes by default.
    /// </summary>
    public const int DefaultLimit = 20;
}

/// <summary>
/// What the output says about a truncated list: how its objects are distributed by namespace, and which command lists all of them.
/// </summary>
internal static class RelatedLists
{
    /// <summary>
    /// Gets the namespace by which an object is counted: that of its target declaration, or of its own declaration.
    /// </summary>
    public static string GetNamespace( object item )
    {
        var declaration = item switch
        {
            IntrospectionAspectInstance i => i.TargetDeclaration,
            IntrospectionTransformation t => t.TargetDeclaration,
            IntrospectionPredecessor p => p.Declaration,
            IntrospectionAnnotation a => a.TargetDeclaration,
            IntrospectionDeclarationMapping m => m.Source,
            IntrospectionDeclaration d => d,
            _ => null
        };

        // An assembly, or a type of the global namespace.
        return declaration?.Namespace ?? "";
    }

    /// <summary>
    /// Gets the number of objects of each namespace, the largest first.
    /// </summary>
    public static IReadOnlyList<(string Namespace, int Count)> ByNamespace( IEnumerable<object> items )
        => items.GroupBy( GetNamespace, StringComparer.Ordinal )
            .Select( g => (g.Key, g.Count()) )
            .OrderByDescending( g => g.Item2 )
            .ThenBy( g => g.Key, StringComparer.Ordinal )
            .ToList();

    /// <summary>
    /// Gets the command, without <c>introspect</c>, that lists all the objects of a list of an object, or <c>null</c>.
    /// </summary>
    public static string? GetNext( string ownerType, string field, object owner )
        => (owner, field) switch
        {
            (IntrospectionAspectClass c, "aspectInstances") => List( "aspect-instances", $"aspectClass/name eq {Literal( c.FullName )}" ),
            (IntrospectionAspectInstance i, "predecessors") => List( "predecessors", $"aspectInstances/any(a: a/id eq {Literal( i.Id )})" ),
            (IntrospectionAspectInstance i, "transformations") => List( "transformations", $"aspectInstance/id eq {Literal( i.Id )}" ),
            (IntrospectionPredecessor p, "aspectInstances") => List( "aspect-instances", $"predecessors/any(p: p/id eq {Literal( p.Id )})" ),
            (IntrospectionDeclaration d, "aspectInstances") => List( "aspect-instances", $"targetDeclaration/qualifiedId eq {Literal( d.QualifiedId )}" ),
            (IntrospectionDeclaration d, "transformations") => List( "transformations", $"targetDeclaration/qualifiedId eq {Literal( d.QualifiedId )}" ),
            (IntrospectionDeclaration d, "annotations") => List( "annotations", $"targetDeclaration/qualifiedId eq {Literal( d.QualifiedId )}" ),
            (IntrospectionDeclaration d, "members") => List( "declarations", $"declaringType/qualifiedId eq {Literal( d.QualifiedId )}" ),
            _ => null
        };

    /// <summary>
    /// Gets the sentence of the text output that ends a truncated list.
    /// </summary>
    public static string GetNote( string what, IReadOnlyList<object> all, int shown, string? next )
    {
        var namespaces = ByNamespace( all );
        var largest = string.Join( ", ", namespaces.Take( 3 ).Select( n => $"{(n.Namespace.Length == 0 ? "(no namespace)" : n.Namespace)} ({n.Count})" ) );
        var more = namespaces.Count > 3 ? ", ..." : "";
        var how = next != null ? $"run 'introspect {next}', or add --all" : "add --all";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"... {all.Count - shown} more {what}, {all.Count} in all, in {namespaces.Count} namespace(s): {largest}{more}. To see them, {how}." );
    }

    private static string List( string collection, string condition ) => $"list {collection} --where \"{condition}\"";

    // A string literal of the conditions, whose quotes are doubled.
    private static string Literal( string value ) => "'" + value.Replace( "'", "''", StringComparison.Ordinal ) + "'";
}
