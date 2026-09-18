// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Xml;

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
public sealed record LeaseConfiguration : ConfigurationObject
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

    /// <summary>
    /// Writes the lease in the textual form that a license server speaks, which is also the form in which PostSharp
    /// 2026.0 keeps a lease in the registry.
    /// </summary>
    /// <remarks>
    /// The instants are written as <c>xs:dateTime</c> in universal time, which is what
    /// <see cref="TryParse"/> and the readers of every version accept.
    /// </remarks>
    public string Serialize()
        => $"License: {this.LicenseKey}"
           + $"; StartTime: {FormatInstant( this.StartTime )}"
           + $"; EndTime: {FormatInstant( this.EndTime )}"
           + $"; RenewTime: {FormatInstant( this.RenewTime )}";

    private static string FormatInstant( DateTime value ) => XmlConvert.ToString( value, XmlDateTimeSerializationMode.Utc );

    /// <summary>
    /// Reads a lease from the textual form that <see cref="Serialize"/> writes.
    /// </summary>
    /// <param name="text">The text, which may be null or blank.</param>
    /// <param name="now">The current instant in UTC, used when the text carries no start time.</param>
    /// <param name="lease">The lease that the text carries.</param>
    /// <returns><see langword="false"/> when the text is not a lease, which is not an error: a lease is derived state
    /// that can be acquired again.</returns>
    /// <remarks>
    /// Public because a product whose earlier versions keep a lease somewhere other than a file supplies the schema
    /// that maps it, and the parsing of the form is the same wherever the text comes from. The parsing itself is
    /// <see cref="LicenseLease.TryDeserialize"/>, which is what reads the response of a license server, so a lease
    /// read from a store and a lease read from a server are understood identically.
    /// </remarks>
    public static bool TryParse( string? text, DateTime now, [NotNullWhen( true )] out LeaseConfiguration? lease )
    {
        if ( !LicenseLease.TryDeserialize( text, now, out var licenseLease ) )
        {
            lease = null;

            return false;
        }

        lease = FromLicenseLease( licenseLease );

        return true;
    }

    // Internal, unlike the members above: a schema that maps this object onto another store needs its data, not the
    // wire type that a license server speaks, which stays an implementation detail.
    internal LicenseLease ToLicenseLease() => new( this.LicenseKey, this.StartTime, this.EndTime, this.RenewTime );

    internal static LeaseConfiguration FromLicenseLease( LicenseLease lease )
        => new()
        {
            LicenseKey = lease.LicenseKey, StartTime = lease.StartTime, EndTime = lease.EndTime, RenewTime = lease.RenewTime
        };
}
