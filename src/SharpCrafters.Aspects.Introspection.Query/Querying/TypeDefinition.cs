// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// A type of the schema, and the collection of its objects.
/// </summary>
internal sealed class TypeDefinition
{
    private readonly Dictionary<string, FieldDefinition> _fields;

    public TypeDefinition(
        string name,
        string collectionName,
        string itemName,
        string description,
        IReadOnlyList<FieldDefinition> fields,
        IReadOnlyList<string> defaultColumns,
        Func<QueryContext, IEnumerable<object>> getItems )
    {
        this.Name = name;
        this.CollectionName = collectionName;
        this.ItemName = itemName;
        this.Description = description;
        this.Fields = fields;
        this.DefaultColumns = defaultColumns;
        this.GetItems = getItems;
        this._fields = fields.ToDictionary( f => f.Name, StringComparer.Ordinal );
    }

    // The name of the type in the schema, such as aspectInstance.
    public string Name { get; }

    // The name of the collection for list, such as aspect-instances.
    public string CollectionName { get; }

    // The name of one object for show, such as aspect-instance.
    public string ItemName { get; }

    // Whether show takes an object of the type, which has an identifier or a name.
    public bool HasIdentifier { get; init; }

    public string Description { get; }

    public IReadOnlyList<FieldDefinition> Fields { get; }

    // The fields that the text output of list shows when --select is not given.
    public IReadOnlyList<string> DefaultColumns { get; }

    public Func<QueryContext, IEnumerable<object>> GetItems { get; }

    public FieldDefinition? GetField( string name ) => this._fields.GetValueOrDefault( name );
}
