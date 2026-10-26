// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// The static type of an expression: a kind, and the type of the schema of an object or a list of objects.
/// </summary>
internal readonly record struct ExpressionType( ValueKind Kind, string? TypeName = null )
{
    public static readonly ExpressionType Boolean = new( ValueKind.Boolean );
    public static readonly ExpressionType Integer = new( ValueKind.Integer );
    public static readonly ExpressionType String = new( ValueKind.String );

    // The type of the literal null, which compares with anything.
    public static readonly ExpressionType Null = new( ValueKind.Object, "null" );

    public bool IsNull => this.TypeName == "null";

    public bool IsCollection => this.Kind is ValueKind.ObjectList or ValueKind.StringList;

    public bool IsTextual => this.Kind is ValueKind.String or ValueKind.Enum;

    public override string ToString()
        => this.Kind switch
        {
            ValueKind.Object when this.IsNull => "null",
            ValueKind.Object => this.TypeName!,
            ValueKind.ObjectList => "collection of " + this.TypeName,
            ValueKind.StringList => "collection of string",
            ValueKind.Enum => "string",
            _ => this.Kind.ToString().ToLowerInvariant()
        };
}
