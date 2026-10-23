// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// Identifies a declaration in a weaving symbol file. A writer returns it from <see cref="IntrospectionV1Writer.DefineDeclaration"/>,
/// and a reader returns it in the data structures, where <see cref="IntrospectionV1Reader.GetIdentifier"/> gives its identifier.
/// The default value, <see cref="IsNil"/>, stands for no declaration.
/// </summary>
public readonly struct DeclarationHandle : IEquatable<DeclarationHandle>
{
    internal DeclarationHandle( int index )
    {
        this.Index = index;
    }

    // One-based, so that the default value is nil.
    internal int Index { get; }

    /// <summary>
    /// Gets a value indicating whether the handle stands for no declaration.
    /// </summary>
    public bool IsNil => this.Index == 0;

    /// <inheritdoc />
    public bool Equals( DeclarationHandle other ) => this.Index == other.Index;

    /// <inheritdoc />
    public override bool Equals( object? obj ) => obj is DeclarationHandle other && this.Equals( other );

    /// <inheritdoc />
    public override int GetHashCode() => this.Index;

    /// <summary>
    /// Determines whether two handles identify the same declaration.
    /// </summary>
    public static bool operator ==( DeclarationHandle left, DeclarationHandle right ) => left.Equals( right );

    /// <summary>
    /// Determines whether two handles identify different declarations.
    /// </summary>
    public static bool operator !=( DeclarationHandle left, DeclarationHandle right ) => !left.Equals( right );

    /// <inheritdoc />
    public override string ToString() => this.IsNil ? "(nil)" : "#" + this.Index;
}
