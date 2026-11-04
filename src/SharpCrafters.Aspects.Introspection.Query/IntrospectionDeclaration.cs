// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Collections.Generic;

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// A declaration that a file of the introspection of aspects mentions: the target of an aspect instance or of a transformation,
/// an aspect class, an advice, the declaration of a predecessor, or a declaration that one of them is a member of.
/// </summary>
public sealed class IntrospectionDeclaration
{
    internal IntrospectionDeclaration( string assembly, string id, IntrospectionDeclarationKind kind, string name, string? sourceIdentifier )
    {
        this.Assembly = assembly;
        this.Id = id;
        this.Kind = kind;
        this.Name = name;
        this.SourceIdentifier = sourceIdentifier;
    }

    /// <summary>
    /// Gets the documentation comment identifier of the declaration, as Roslyn writes it, such as
    /// <c>M:App.Repository.Get(System.Int32)</c>, with the extensions of <c>SerializableDeclarationId</c> for parameters
    /// (<c>;Parameter=</c>, <c>;Return</c>), and <c>A:</c> followed by the name for an assembly.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the name of the assembly that declares the declaration.
    /// </summary>
    public string Assembly { get; }

    /// <summary>
    /// Gets <see cref="Id"/> qualified with <see cref="Assembly"/>, such as <c>[App]M:App.Repository.Get(System.Int32)</c>, which
    /// identifies the declaration in a solution.
    /// </summary>
    public string QualifiedId => "[" + this.Assembly + "]" + this.Id;

    /// <summary>
    /// Gets the kind of the declaration.
    /// </summary>
    public IntrospectionDeclarationKind Kind { get; }

    /// <summary>
    /// Gets the name of the declaration, without its declaring type or namespace, and without the generic arity of a type.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the namespace of a type, or of the outermost type that declares a nested type, or <c>null</c>.
    /// </summary>
    public string? Namespace { get; internal set; }

    /// <summary>
    /// Gets the type that declares a member or a nested type, or <c>null</c>.
    /// </summary>
    public IntrospectionDeclaration? DeclaringType { get; internal set; }

    /// <summary>
    /// Gets the declaration that this one belongs to: the method of a parameter, the property or event of an accessor, the
    /// property of a backing field, or <c>null</c>.
    /// </summary>
    public IntrospectionDeclaration? ContainingDeclaration { get; internal set; }

    /// <summary>
    /// Gets the members of a type that the file mentions.
    /// </summary>
    public IReadOnlyList<IntrospectionDeclaration> Members => this.MembersList;

    /// <summary>
    /// Gets the accessors of a property or an event that the file mentions.
    /// </summary>
    public IReadOnlyList<IntrospectionDeclaration> Accessors => this.AccessorsList;

    /// <summary>
    /// Gets the backing field of a property, when the file mentions it, or <c>null</c>.
    /// </summary>
    public IntrospectionDeclaration? BackingField { get; internal set; }

    /// <summary>
    /// Gets the identifier of the declaration in the file it was read from, such as a reflection name of version 1, or <c>null</c>
    /// when it is the same as <see cref="Id"/>.
    /// </summary>
    public string? SourceIdentifier { get; }

    internal List<IntrospectionDeclaration> MembersList { get; } = [];

    internal List<IntrospectionDeclaration> AccessorsList { get; } = [];

    /// <inheritdoc />
    public override string ToString() => this.QualifiedId;
}
