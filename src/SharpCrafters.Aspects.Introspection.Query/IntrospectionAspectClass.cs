// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Collections.Generic;

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// The class of an aspect.
/// </summary>
public sealed class IntrospectionAspectClass
{
    private const string _attributeSuffix = "Attribute";

    internal IntrospectionAspectClass( IntrospectionDeclaration declaration )
    {
        this.Declaration = declaration;
        this.FullName = declaration.Id.Substring( 2 );

        var name = declaration.Name;

        this.ShortName = name.Length > _attributeSuffix.Length && name.EndsWith( _attributeSuffix, System.StringComparison.Ordinal )
            ? name.Substring( 0, name.Length - _attributeSuffix.Length )
            : name;
    }

    /// <summary>
    /// Gets the type of the aspect class.
    /// </summary>
    public IntrospectionDeclaration Declaration { get; }

    /// <summary>
    /// Gets the full name of the type, with its namespace and declaring types, as in its documentation comment identifier, such
    /// as <c>App.Aspects.LogAttribute</c>.
    /// </summary>
    public string FullName { get; }

    /// <summary>
    /// Gets the name of the type without its namespace and without the <c>Attribute</c> suffix, such as <c>Log</c>.
    /// </summary>
    public string ShortName { get; }

    /// <summary>
    /// Gets a value indicating whether the licence covers the aspect class only in part.
    /// </summary>
    public bool HasLimitedLicense { get; internal set; }

    /// <summary>
    /// Gets the aspect instances of the aspect class.
    /// </summary>
    public IReadOnlyList<IntrospectionAspectInstance> AspectInstances => this.AspectInstancesList;

    internal List<IntrospectionAspectInstance> AspectInstancesList { get; } = [];

    /// <inheritdoc />
    public override string ToString() => this.FullName;
}
