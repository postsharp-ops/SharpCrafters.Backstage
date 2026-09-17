// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using System;
using System.Text.Json.Serialization;

namespace SharpCrafters.Backstage.Licensing.LicenseServer;

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
