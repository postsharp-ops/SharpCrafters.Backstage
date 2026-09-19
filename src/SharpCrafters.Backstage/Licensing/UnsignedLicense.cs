// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;

namespace SharpCrafters.Backstage.Licensing;

/// <summary>
/// A license that the product issues to itself, described by what goes into the key: the trial, and the free edition
/// that a user registers without buying anything.
/// </summary>
/// <remarks>
/// <para>
/// It is a description and not a key, because the product family decides what the license says and the licensing
/// services decide how a key is built and signed. The family is the only one that can say that the free edition of
/// PostSharp is a PostSharp Ultimate key carrying the Community type, and it has no business serializing one.
/// </para>
/// <para>
/// A family that does not offer one of these editions says so by not describing it, which is what the setup pages
/// and the command line read to decide what to offer.
/// </para>
/// </remarks>
/// <param name="Product">The product that the key names.</param>
/// <param name="LicenseType">The type that the key carries, which for a free edition is what makes it free.</param>
/// <param name="ValidFrom">The moment from which the key is valid.</param>
[PublicAPI]
public sealed record UnsignedLicense( LicenseProduct Product, LicenseType LicenseType, DateTime ValidFrom )
{
    /// <summary>
    /// Gets the moment at which the key stops being valid, or <see langword="null"/> when it never does.
    /// </summary>
    public DateTime? ValidTo { get; init; }

    /// <summary>
    /// Gets the moment at which the subscription ends, or <see langword="null"/> when the key carries none. A trial
    /// carries one, so that a build made with a later version of the product is not covered by it.
    /// </summary>
    public DateTime? SubscriptionEndDate { get; init; }
}
