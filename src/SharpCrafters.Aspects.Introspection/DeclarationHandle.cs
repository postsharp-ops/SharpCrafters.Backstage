// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Runtime.CompilerServices;

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// Identifies a declaration in a weaving symbol file. A writer returns it from <see cref="IntrospectionV1Writer.GetDeclarationHandle{TKey}"/>,
/// and a reader returns it in the data structures, where <see cref="IntrospectionV1Reader.GetIdentifier"/> gives its identifier.
/// The default value, <see cref="IsNil"/>, stands for no declaration. A handle belongs to the reader or writer that returned it: the
/// handles of two instances are different even when their indexes are the same.
/// </summary>
public readonly struct DeclarationHandle : IEquatable<DeclarationHandle>
{
    internal DeclarationHandle( object owner, int index )
    {
        this.Owner = owner;
        this.Index = index;
    }

    // The reader or writer that returned the handle, or null for the nil handle.
    internal object? Owner { get; }

    // One-based, so that the default value is nil.
    internal int Index { get; }

    /// <summary>
    /// Gets a value indicating whether the handle stands for no declaration.
    /// </summary>
    public bool IsNil => this.Index == 0;

    /// <inheritdoc />
    public bool Equals( DeclarationHandle other ) => this.Index == other.Index && ReferenceEquals( this.Owner, other.Owner );

    /// <inheritdoc />
    public override bool Equals( object? obj ) => obj is DeclarationHandle other && this.Equals( other );

    /// <inheritdoc />
    public override int GetHashCode() => unchecked((this.Index * 397) ^ (this.Owner == null ? 0 : RuntimeHelpers.GetHashCode( this.Owner )));

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
