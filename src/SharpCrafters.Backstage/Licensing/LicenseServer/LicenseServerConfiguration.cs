// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Serialization;
using System;
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
    [JsonPropertyName( "leases" )]
    [JsonConverter( typeof(CaseInsensitiveImmutableDictionaryConverterFactory<LeaseConfiguration>) )]
    public ImmutableDictionary<string, LeaseConfiguration> Leases { get; init; } =
        ImmutableDictionary<string, LeaseConfiguration>.Empty.WithComparers( StringComparer.OrdinalIgnoreCase );
}

/// <summary>
/// One lease, as it is stored between two runs of the product.
/// </summary>
/// <remarks>
/// This is the stored form of <see cref="LicenseLease"/> and not the type itself, because the two have different
/// obligations: the wire format is fixed by the servers that customers deploy, whereas this one is ours and derives
/// from <see cref="ConfigurationObject"/> so that a member written by a later version of the product survives a
/// rewrite by this one.
/// </remarks>
internal sealed record LeaseConfiguration : ConfigurationObject
{
    /// <summary>
    /// Gets the licence key that the server allocated. It is temporary and is never presented to the user as a
    /// registered licence key.
    /// </summary>
    [JsonPropertyName( "licenseKey" )]
    public string LicenseKey { get; init; } = "";

    [JsonPropertyName( "startTime" )]
    public DateTime StartTime { get; init; }

    [JsonPropertyName( "endTime" )]
    public DateTime EndTime { get; init; }

    [JsonPropertyName( "renewTime" )]
    public DateTime RenewTime { get; init; }

    public LicenseLease ToLicenseLease() => new( this.LicenseKey, this.StartTime, this.EndTime, this.RenewTime );

    public static LeaseConfiguration FromLicenseLease( LicenseLease lease )
        => new()
        {
            LicenseKey = lease.LicenseKey, StartTime = lease.StartTime, EndTime = lease.EndTime, RenewTime = lease.RenewTime
        };
}
