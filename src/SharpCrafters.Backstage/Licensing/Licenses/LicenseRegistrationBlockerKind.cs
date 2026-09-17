// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Backstage.Licensing.Licenses;

/// <summary>
/// The reasons why a licence cannot be registered, as a value a test can assert instead of a message it would have to
/// match.
/// </summary>
internal enum LicenseRegistrationBlockerKind
{
    /// <summary>
    /// Nothing prevents the licence from being registered.
    /// </summary>
    None,

    /// <summary>
    /// The licence cannot be used at all: it does not parse, it is not signed by an authority, it has expired, or a
    /// license server refused to lease it. The message carries which of those it is.
    /// </summary>
    Unusable,

    /// <summary>
    /// The licence key is a redistribution key, which licenses the code a customer ships rather than the machine of a
    /// user, and which is therefore never registered in a user profile.
    /// </summary>
    Redistribution
}
