// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;

namespace SharpCrafters.Backstage.Licensing.Consumption;

/// <summary>
/// What a <see cref="LicensingMessage"/> is about, so that an application can report it under a diagnostic of its
/// own rather than as one message that says everything.
/// </summary>
/// <remarks>
/// <para>
/// An application that has one diagnostic for licensing ignores this and reports
/// <see cref="LicensingMessage.Text"/>. An application that has several, as PostSharp does, maps the kind to the
/// diagnostic, so that a user can suppress or escalate one situation without touching the others, and so that the
/// documentation of a diagnostic describes one situation.
/// </para>
/// <para>
/// The kind says what happened and not how severe it is. Whether a message stops the build is
/// <see cref="LicensingMessage.IsError"/>, and an application may decide otherwise.
/// </para>
/// </remarks>
[PublicAPI]
public enum LicensingMessageKind
{
    /// <summary>
    /// Anything that the other members do not name. An application maps this to its general licensing diagnostic.
    /// </summary>
    Default,

    /// <summary>
    /// A license key could not be read, or carries something that makes it unusable whatever it is used for: it is
    /// not a license key at all, its signature does not verify, it is of a form that is no longer supported, or it
    /// is not yet valid.
    /// </summary>
    InvalidLicenseKey,

    /// <summary>
    /// A license key is no longer valid, or its subscription ended before the build date of the version being used.
    /// The key was valid and the user is expected to renew it, which is what distinguishes this from
    /// <see cref="InvalidLicenseKey"/>.
    /// </summary>
    Expired,

    /// <summary>
    /// A license key has been revoked.
    /// </summary>
    Revoked,

    /// <summary>
    /// A license key is for another family of products. It is a valid key and the user holds it legitimately; it
    /// licenses something else.
    /// </summary>
    WrongProductFamily,

    /// <summary>
    /// A license key is constrained to a namespace, and the project is not inside it under any of its names.
    /// </summary>
    NamespaceMismatch,

    /// <summary>
    /// A license server did not lease a license, or leased one that may not be leased.
    /// </summary>
    LicenseServerLeaseFailed,

    /// <summary>
    /// A license server is addressed over a protocol that does not protect the license key in transit.
    /// </summary>
    InsecureLicenseServer,

    /// <summary>
    /// A feature requires a license that none of the licenses found grants. The user holds at least one license,
    /// which is what distinguishes this from <see cref="NoLicense"/>.
    /// </summary>
    RequirementNotSatisfied,

    /// <summary>
    /// A feature requires a license and no usable license was found at all, so the user is asked to register one
    /// rather than told that the one they hold is not enough.
    /// </summary>
    NoLicense
}
