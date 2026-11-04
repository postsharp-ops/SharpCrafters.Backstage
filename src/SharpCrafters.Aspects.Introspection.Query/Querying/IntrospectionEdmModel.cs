// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.OData.Edm;
using System;
using System.Collections.Generic;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// The EDM model that Microsoft.OData.Core parses and binds the queries against, built from <see cref="IntrospectionSchema"/>:
/// an entity type per type of the schema, a structural property per scalar or string list field, and a navigation property per
/// object or object list field. The names are those of the schema, case-sensitive.
/// </summary>
/// <remarks>
/// An enumeration field is an <c>Edm.String</c>, not an enumeration type: the queries compare it with a string, such as
/// <c>kind eq 'Multicast'</c>, and pass it to the string functions, as the JSON output writes it; and
/// <see cref="IntrospectionUriResolver"/> checks the values, which Microsoft.OData.Core would check for <c>in</c> without listing
/// them.
/// </remarks>
internal sealed class IntrospectionEdmModel
{
    private const string _namespace = "Introspection";

    private readonly Dictionary<IEdmStructuredType, TypeDefinition> _types = new();
    private readonly Dictionary<TypeDefinition, IEdmEntityType> _edmTypes = new();
    private readonly Dictionary<IEdmProperty, FieldDefinition> _fields = new();
    private readonly Dictionary<(TypeDefinition, FieldDefinition), IEdmProperty> _properties = new();

    public static IntrospectionEdmModel Instance { get; } = new();

    private IntrospectionEdmModel()
    {
        var model = new EdmModel();

        foreach ( var type in IntrospectionSchema.Types )
        {
            var entityType = new EdmEntityType( _namespace, type.Name );
            model.AddElement( entityType );
            this._types.Add( entityType, type );
            this._edmTypes.Add( type, entityType );
        }

        foreach ( var type in IntrospectionSchema.Types )
        {
            var entityType = (EdmEntityType) this._edmTypes[type];

            foreach ( var field in type.Fields )
            {
                IEdmProperty property = field.Kind switch
                {
                    ValueKind.String or ValueKind.Enum => entityType.AddStructuralProperty( field.Name, EdmPrimitiveTypeKind.String, true ),
                    ValueKind.Integer => entityType.AddStructuralProperty( field.Name, EdmPrimitiveTypeKind.Int64, true ),
                    ValueKind.Boolean => entityType.AddStructuralProperty( field.Name, EdmPrimitiveTypeKind.Boolean, true ),
                    ValueKind.StringList => entityType.AddStructuralProperty(
                        field.Name,
                        new EdmCollectionTypeReference( new EdmCollectionType( EdmCoreModel.Instance.GetString( true ) ) ) ),
                    ValueKind.Object => entityType.AddUnidirectionalNavigation(
                        new EdmNavigationPropertyInfo
                        {
                            Name = field.Name, Target = this._edmTypes[IntrospectionSchema.GetType( field.TypeName! )], TargetMultiplicity = EdmMultiplicity.ZeroOrOne
                        } ),
                    ValueKind.ObjectList => entityType.AddUnidirectionalNavigation(
                        new EdmNavigationPropertyInfo
                        {
                            Name = field.Name, Target = this._edmTypes[IntrospectionSchema.GetType( field.TypeName! )], TargetMultiplicity = EdmMultiplicity.Many
                        } ),
                    _ => throw new InvalidOperationException( $"Unexpected kind {field.Kind}." )
                };

                this._fields.Add( property, field );
                this._properties.Add( (type, field), property );
            }
        }

        this.Model = model;
    }

    public IEdmModel Model { get; }

    public IEdmEntityType GetEdmType( TypeDefinition type ) => this._edmTypes[type];

    public TypeDefinition GetType( IEdmStructuredType type ) => this._types[type];

    public IEdmProperty GetProperty( TypeDefinition type, FieldDefinition field ) => this._properties[(type, field)];

    public FieldDefinition GetField( IEdmProperty property ) => this._fields[property];
}
