// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Hashing;
using System.Linq;
using System.Text;

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// Computes the structural keys and the identifiers of the aspect instances and the predecessors of a model
/// (Documentation/future/Introspection-Cli.md of PostSharp, section 5.5). A key is built from the content, not from the order
/// of the build, so the identifier stays the same across builds as long as the source does not change.
/// </summary>
/// <remarks>
/// <list type="table">
/// <listheader><term>Object</term><description>Structural key</description></listheader>
/// <item><term>Aspect instance</term><description><c>&lt;aspect class&gt;@&lt;target declaration&gt;[#n]</c></description></item>
/// <item><term>Attribute, multicast attribute, inherited attribute</term><description><c>attribute:</c>, <c>multicast:</c> or
/// <c>inherited:</c>, then <c>&lt;declaration&gt;:&lt;attribute class&gt;[#n]</c></description></item>
/// <item><term>XML element</term><description><c>xml:&lt;file&gt;(&lt;line&gt;,&lt;column&gt;)</c>, with the path of the file
/// relative to the directory of the project when the loader is given it, and forward slashes</description></item>
/// <item><term>Fabric</term><description><c>fabric:&lt;fabric type&gt;</c></description></item>
/// <item><term>Aspect instance as predecessor</term><description><c>aspect:&lt;key of the aspect instance&gt;</c></description></item>
/// </list>
/// <para>
/// A declaration is qualified with its assembly (<see cref="IntrospectionDeclaration.QualifiedId"/>), and a class is its full
/// name (<see cref="IntrospectionAspectClass.FullName"/>), not its short name, so that two aspect classes of the same short
/// name do not share keys. <c>#n</c>, counted from 1, distinguishes the keys that would be equal, in the order of the file,
/// which is the order of the source for the custom attributes of a declaration: the C# compiler keeps it in metadata (check
/// S2, measured with Roslyn 5.0, Debug and Release). It is omitted when the key is unique.
/// </para>
/// <para>
/// The identifier is the first 48 bits of the XxHash64 of the key, prefixed with the version of the format of the keys, as 12
/// lowercase hexadecimal characters. A collision is not handled.
/// </para>
/// </remarks>
internal static class StructuralKeys
{
    // The version of the format of the keys, hashed with each key, so that a change of the format changes the identifiers
    // instead of making them collide.
    private const string _formatVersion = "1:";

    public static void Assign( IntrospectionModel model )
    {
        // The keys of the aspect instances do not depend on the predecessors, and those of the predecessors of child aspect
        // instances depend on the aspect instances that provide them.
        AssignUnique(
            model.AspectInstancesList,
            i => i.AspectClass.FullName + "@" + i.TargetDeclaration.QualifiedId,
            ( i, key ) =>
            {
                i.StructuralKey = key;
                i.Id = GetIdentifier( key );
            } );

        AssignUnique(
            model.PredecessorsList,
            GetPredecessorKey,
            ( p, key ) =>
            {
                p.StructuralKey = key;
                p.Id = GetIdentifier( key );
            } );
    }

    /// <summary>
    /// Gets the identifier of a structural key.
    /// </summary>
    public static string GetIdentifier( string structuralKey )
    {
        var hash = XxHash64.Hash( Encoding.UTF8.GetBytes( _formatVersion + structuralKey ) );

        // The hash is big-endian: its first 6 bytes are its first 48 bits.
        return Convert.ToHexString( hash, 0, 6 ).ToLowerInvariant();
    }

    private static string GetPredecessorKey( IntrospectionPredecessor predecessor )
        => predecessor.Kind switch
        {
            AspectPredecessorKind.Attribute => "attribute:" + GetAttributeKey( predecessor ),
            AspectPredecessorKind.Multicast => "multicast:" + GetAttributeKey( predecessor ),
            AspectPredecessorKind.Inherited => "inherited:" + GetAttributeKey( predecessor ),
            AspectPredecessorKind.Xml => string.Format(
                CultureInfo.InvariantCulture,
                "xml:{0}({1},{2})",
                predecessor.KeyFile,
                predecessor.Line,
                predecessor.Column ),
            AspectPredecessorKind.Fabric => "fabric:" + predecessor.Declaration?.QualifiedId,
            _ => "aspect:" + predecessor.AspectInstance?.StructuralKey
        };

    private static string GetAttributeKey( IntrospectionPredecessor predecessor )
        => predecessor.Declaration?.QualifiedId + ":" + predecessor.AttributeClass?.FullName;

    private static void AssignUnique<T>( IReadOnlyList<T> items, Func<T, string> getKey, Action<T, string> assign )
    {
        var keys = items.Select( getKey ).ToList();
        var counts = keys.GroupBy( k => k, StringComparer.Ordinal ).ToDictionary( g => g.Key, g => g.Count(), StringComparer.Ordinal );
        var ordinals = new Dictionary<string, int>( StringComparer.Ordinal );

        for ( var i = 0; i < items.Count; i++ )
        {
            var key = keys[i];

            if ( counts[key] > 1 )
            {
                ordinals[key] = ordinals.GetValueOrDefault( key ) + 1;
                key += "#" + ordinals[key].ToString( CultureInfo.InvariantCulture );
            }

            assign( items[i], key );
        }
    }
}
