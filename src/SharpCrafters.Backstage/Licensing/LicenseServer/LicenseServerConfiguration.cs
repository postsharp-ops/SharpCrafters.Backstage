// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace SharpCrafters.Backstage.Licensing.LicenseServer;

/// <summary>
/// The leases currently held from the license servers that the current user profile is configured to use, keyed by
/// the URL of the server as <see cref="LicenseServerUrl.GetStoreKey"/> normalizes it.
/// </summary>
/// <remarks>
/// <para>
/// The leases live in a file of their own rather than in <c>licensing.json</c> for two reasons. The configuration
/// manager takes one lock per file, and a lease is written whenever a build renews one, whereas the registered
/// license keys are written only when the user registers one; sharing a file would make every build queue behind an
/// occasional command. And the two have different standing: <c>licensing.json</c> records what the user decided,
/// while a lease is derived state that can be acquired again at any moment.
/// </para>
/// <para>
/// A lease of a server that is no longer registered is simply ignored, and is removed the next time the licenses are
/// unregistered. It is not worth a pass over the file to collect it.
/// </para>
/// </remarks>
[ConfigurationFile( "licenseServer.json" )]
[Description( "Licenses leased from license servers." )]
internal sealed record LicenseServerConfiguration : ConfigurationFile
{
    /// <remarks>
    /// The keys are compared ordinally, and <see cref="LicenseServerUrl.GetStoreKey"/> is what makes two spellings of
    /// one server into one key. The comparison cannot be case-insensitive, because the path of a URL is not: two
    /// servers published under paths that differ only by case are two servers.
    /// </remarks>
    [JsonPropertyName( "leases" )]
    public ImmutableDictionary<string, LeaseConfiguration> Leases { get; init; } = ImmutableDictionary<string, LeaseConfiguration>.Empty;
}
