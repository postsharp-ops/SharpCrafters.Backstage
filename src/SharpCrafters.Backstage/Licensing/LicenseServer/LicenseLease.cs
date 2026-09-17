// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Xml;

namespace SharpCrafters.Backstage.Licensing.LicenseServer;

/// <summary>
/// A licence leased from a license server for a limited period: the licence key that the server allocated, and the
/// three instants that bound the lease.
/// </summary>
/// <param name="LicenseKey">The licence key allocated by the server. Unlike a registered licence key, it is temporary.</param>
/// <param name="StartTime">The instant at which the lease began, in UTC.</param>
/// <param name="EndTime">The instant after which the lease is void, in UTC.</param>
/// <param name="RenewTime">The instant from which the client should renew the lease, in UTC. It precedes <paramref name="EndTime"/>, so that a client whose server is temporarily unreachable keeps working.</param>
/// <remarks>
/// This is a <c>record</c> so that a renewal which returns an identical lease compares equal to the stored one, and
/// the configuration manager skips the write.
/// </remarks>
internal sealed record LicenseLease( string LicenseKey, DateTime StartTime, DateTime EndTime, DateTime RenewTime )
{
    /// <summary>
    /// The lifetime given to a lease whose response carries no <c>EndTime</c>.
    /// </summary>
    private static readonly TimeSpan _defaultDuration = TimeSpan.FromDays( 1 );

    /// <summary>
    /// Parses the body that a license server returns from its <c>Lease.ashx</c> handler.
    /// </summary>
    /// <param name="serializedLease">The body of the response, of the form <c>License: KEY; StartTime: …; EndTime: …; RenewTime: …</c>.</param>
    /// <param name="now">The current instant in UTC, used as the default <see cref="StartTime"/>.</param>
    /// <param name="lease">The parsed lease.</param>
    /// <returns><see langword="true"/> if the body carries a licence key, which is the only mandatory part.</returns>
    /// <remarks>
    /// <para>
    /// The parsing is deliberately lenient, as PostSharp's was: the parts are separated by <c>;</c>, each part is
    /// split at its <b>first</b> <c>:</c> so that a timestamp keeps its own colons, the name of a part is matched
    /// without regard to case, and a part that is not understood is ignored rather than rejected, so that a server of
    /// a later version can add one. Splitting on <c>;</c> is safe because a licence key is made of letters, digits
    /// and hyphens and can never contain one.
    /// </para>
    /// <para>
    /// The instants are read as UTC and kept as UTC. PostSharp converted them to local time here and compared them to
    /// a local clock, which cancels out only when the client and the server share a time zone.
    /// </para>
    /// </remarks>
    public static bool TryDeserialize( string? serializedLease, DateTime now, [MaybeNullWhen( false )] out LicenseLease lease )
    {
        lease = null;

        if ( string.IsNullOrWhiteSpace( serializedLease ) )
        {
            return false;
        }

        string? licenseKey = null;
        DateTime? startTime = null, endTime = null, renewTime = null;

        try
        {
            // ReSharper disable once RedundantSuppressNullableWarningExpression
            foreach ( var part in serializedLease!.Split( ';' ) )
            {
                var separator = part.IndexOf( ":", StringComparison.Ordinal );

                if ( separator < 0 || separator == part.Length - 1 )
                {
                    continue;
                }

                var name = part.Substring( 0, separator ).Trim();
                var value = part.Substring( separator + 1 ).Trim();

                switch ( name.ToLowerInvariant() )
                {
                    case "license":
                        licenseKey = value;

                        break;

                    case "starttime":
                        startTime = XmlConvert.ToDateTime( value, XmlDateTimeSerializationMode.Utc );

                        break;

                    case "endtime":
                        endTime = XmlConvert.ToDateTime( value, XmlDateTimeSerializationMode.Utc );

                        break;

                    case "renewtime":
                        renewTime = XmlConvert.ToDateTime( value, XmlDateTimeSerializationMode.Utc );

                        break;
                }
            }
        }
        catch ( Exception e ) when ( e is FormatException or ArgumentException or OverflowException )
        {
            // XmlConvert.ToDateTime throws FormatException for a malformed value, but also ArgumentOutOfRangeException
            // for one that is out of the range of DateTime. PostSharp caught only the former, so a crafted response
            // escaped its handler.
            return false;
        }

        if ( string.IsNullOrEmpty( licenseKey ) )
        {
            return false;
        }

        var start = startTime ?? now;
        var end = endTime ?? (start + _defaultDuration);

        // ReSharper disable once RedundantSuppressNullableWarningExpression
        lease = new LicenseLease( licenseKey!, start, end, renewTime ?? end );

        return true;
    }
}
