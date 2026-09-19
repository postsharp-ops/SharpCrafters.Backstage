// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;

namespace SharpCrafters.Backstage.Licensing.Registration;

/// <summary>
/// What kind of offer a <see cref="SelfRegisteredEdition"/> is.
/// </summary>
/// <remarks>
/// <para>
/// A caller that looks for one of them — the free edition to name in a message, the trial to give a second paragraph
/// in the setup pages — asks for the kind rather than matching an alias, which is the verb of a command line and
/// differs from one family to the next: PostSharp calls its free edition <c>essentials</c> and Metalama calls its own
/// <c>community</c>.
/// </para>
/// <para>
/// A name in a wrapper rather than an enumeration, so that a product can offer a kind that this package has never
/// heard of without the enumeration having to be edited to admit it. The ones named here are the kinds that the two
/// families offer today; equality is of the name, so a kind is whatever a product says it is.
/// </para>
/// </remarks>
[PublicAPI]
public readonly record struct SelfRegisteredEditionKind( string Name )
{
    /// <summary>
    /// An edition given away, which the family still issues.
    /// </summary>
    public static SelfRegisteredEditionKind Free { get; } = new( nameof(Free) );

    /// <summary>
    /// An edition given away that only an earlier version of the product can consume.
    /// </summary>
    public static SelfRegisteredEditionKind LegacyFree { get; } = new( nameof(LegacyFree) );

    /// <summary>
    /// The period during which the whole product is granted so that the user can judge it.
    /// </summary>
    public static SelfRegisteredEditionKind Trial { get; } = new( nameof(Trial) );

    /// <summary>
    /// Returns the name, which is what a message or a failed assertion shows.
    /// </summary>
    public override string ToString() => this.Name ?? "";
}
