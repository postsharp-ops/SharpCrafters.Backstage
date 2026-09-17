// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpCrafters.Backstage.Testing;

/// <summary>
/// One request that a <see cref="LicenseServerSimulator"/> received, with its query string already parsed, so that a
/// test asserts on values rather than on a URL.
/// </summary>
[PublicAPI]
public sealed record LeaseRequest
{
    /// <summary>
    /// The suffix that the client appends to the machine name, which a real server strips before it compares the name
    /// to its list of build servers.
    /// </summary>
    private static readonly Regex _machineHashSuffix = new( "-[0-9a-fA-F]+$", RegexOptions.Compiled );

    public Uri RequestUri { get; init; } = null!;

    /// <summary>
    /// Gets the value of the <c>user</c> argument, which a real server requires.
    /// </summary>
    public string? User { get; init; }

    /// <summary>
    /// Gets the value of the <c>machine</c> argument, which the client composes as the machine name followed by a
    /// hyphen and the hexadecimal hash of the machine identifier.
    /// </summary>
    public string? Machine { get; init; }

    /// <summary>
    /// Gets <see cref="Machine"/> without its hash suffix, which is the name that a real server compares to its list
    /// of build servers.
    /// </summary>
    public string? MachineName => this.Machine == null ? null : _machineHashSuffix.Replace( this.Machine, "" );

    /// <summary>
    /// Gets the hexadecimal hash at the end of <see cref="Machine"/>, or <c>null</c> when there is none.
    /// </summary>
    public string? MachineHash
    {
        get
        {
            if ( this.Machine == null )
            {
                return null;
            }

            var match = _machineHashSuffix.Match( this.Machine );

            return match.Success ? match.Value.Substring( 1 ) : null;
        }
    }

    /// <summary>
    /// Gets the value of the <c>version</c> argument. A real server reads an absent one as 4.9.9, that is, as a client
    /// older than PostSharp 5.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Gets the value of the <c>buildDate</c> argument, as it was sent.
    /// </summary>
    public string? BuildDate { get; init; }

    /// <summary>
    /// Gets <see cref="BuildDate"/> parsed with the round-trip format, or <c>null</c> when it is absent or does not
    /// parse.
    /// </summary>
    public DateTime? BuildDateValue
        => DateTime.TryParseExact( this.BuildDate, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value )
            ? value
            : null;

    /// <summary>
    /// Gets the value of the <c>product</c> argument, which selects the pool of licences the server allocates from. A
    /// real server reads an absent one as "any product".
    /// </summary>
    public string? Product { get; init; }

    /// <summary>
    /// Gets every argument of the query string, including the ones this record does not name, so that a test can
    /// assert that the client sends nothing else.
    /// </summary>
    public IReadOnlyDictionary<string, string> QueryArguments { get; init; } = null!;

    /// <summary>
    /// Gets the instant at which the request was received, read from the clock of the test.
    /// </summary>
    public DateTime ReceivedAt { get; init; }

    public override string ToString() => this.RequestUri.ToString();
}
