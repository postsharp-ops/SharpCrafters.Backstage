// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;

namespace SharpCrafters.Backstage.Licensing.Registration;

/// <summary>
/// What kind of offer a <see cref="SelfRegisteredEdition"/> is.
/// </summary>
/// <remarks>
/// A caller that looks for one of them — the free edition to name in a message, the trial to give a second paragraph
/// in the setup pages — asks for the kind rather than matching an alias, which differs from one family to the next.
/// </remarks>
[PublicAPI]
public enum SelfRegisteredEditionKind
{
    /// <summary>
    /// An edition given away, which the family still issues.
    /// </summary>
    Free,

    /// <summary>
    /// An edition given away that only an earlier version of the product can consume.
    /// </summary>
    LegacyFree,

    /// <summary>
    /// The period during which the whole product is granted so that the user can judge it.
    /// </summary>
    Trial
}
