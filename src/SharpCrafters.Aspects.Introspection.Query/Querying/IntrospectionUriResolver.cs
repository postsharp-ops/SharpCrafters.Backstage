// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;
using System;
using System.Linq;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// Resolves the names of a query against <see cref="IntrospectionEdmModel"/>, and raises the diagnostics on which agents act: a
/// field name in the wrong case suggests the right one and lists the fields of the type, a field that the product does not
/// record names the product, and an enumeration value that does not exist lists the values.
/// </summary>
internal sealed class IntrospectionUriResolver : ODataUriResolver
{
    private readonly QueryText _query;
    private readonly IntrospectionProvider _provider;

    public IntrospectionUriResolver( QueryText query, IntrospectionProvider provider )
    {
        this._query = query;
        this._provider = provider;
    }

    /// <inheritdoc />
    public override IEdmProperty ResolveProperty( IEdmStructuredType type, string propertyName )
    {
        var model = IntrospectionEdmModel.Instance;
        var typeDefinition = model.GetType( type );

        return model.GetProperty( typeDefinition, this.ResolveField( typeDefinition, propertyName ) );
    }

    /// <summary>
    /// Gets a field of a type of the schema, or throws an <see cref="IntrospectionCommandException"/> that says why it cannot be
    /// used. The position is known for the paths of <c>--select</c>, not for the names that Microsoft.OData.Core resolves.
    /// </summary>
    public FieldDefinition ResolveField( TypeDefinition type, string name, int? position = null )
    {
        var field = type.GetField( name );

        if ( field == null )
        {
            var suggestion = type.Fields.FirstOrDefault( f => string.Equals( f.Name, name, StringComparison.OrdinalIgnoreCase ) );

            throw this._query.Error(
                $"'{name}' is not a field of {type.Name}."
                + (suggestion != null ? $" Field names are case-sensitive: did you mean '{suggestion.Name}'?" : "")
                + $" The fields of {type.Name} are: {string.Join( ", ", type.Fields.Select( f => f.Name ) )}.",
                position );
        }

        if ( !this._provider.Provides( type.Name, field.Name ) )
        {
            throw this._query.Error( $"The files of {this._provider.ProductName} do not record '{field.Name}' of {type.Name}.", position );
        }

        return field;
    }

    /// <inheritdoc />
    public override void PromoteBinaryOperandTypes(
        BinaryOperatorKind binaryOperatorKind,
        ref SingleValueNode leftNode,
        ref SingleValueNode rightNode,
        out IEdmTypeReference typeReference )
    {
        this.CheckEnumValue( leftNode, rightNode );
        this.CheckEnumValue( rightNode, leftNode );

        base.PromoteBinaryOperandTypes( binaryOperatorKind, ref leftNode, ref rightNode, out typeReference );
    }

    /// <summary>
    /// Throws when <paramref name="value"/> is a string literal that is not a value of the enumeration field that
    /// <paramref name="operand"/> reads.
    /// </summary>
    public void CheckEnumValue( SingleValueNode operand, SingleValueNode value )
    {
        // An enumeration value that does not exist is a typo, not a condition that is never true.
        if ( QueryNodes.StripConvert( operand ) is SingleValuePropertyAccessNode property
             && IntrospectionEdmModel.Instance.GetField( property.Property ).EnumValues is { } values
             && QueryNodes.StripConvert( value ) is ConstantNode { Value: string text }
             && !values.Contains( text, StringComparer.Ordinal ) )
        {
            throw this._query.Error( $"'{text}' is not a value of '{QueryNodes.Describe( operand )}'. The values are: {string.Join( ", ", values )}." );
        }
    }
}
