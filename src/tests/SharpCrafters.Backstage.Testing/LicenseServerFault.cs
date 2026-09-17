// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;

namespace SharpCrafters.Backstage.Testing;

/// <summary>
/// The abnormal behaviours that a <see cref="LicenseServerSimulator"/> can be asked to exhibit, so that the handling
/// of each of them is covered by a test instead of being reasoned about.
/// </summary>
[PublicAPI]
public enum LicenseServerFault
{
    /// <summary>
    /// The server answers normally.
    /// </summary>
    None,

    /// <summary>
    /// The request fails before any response, as it does when the host does not resolve or refuses the connection.
    /// The client sees an <see cref="System.Net.Http.HttpRequestException"/>.
    /// </summary>
    Unreachable,

    /// <summary>
    /// The request is abandoned, exactly as <see cref="System.Net.Http.HttpClient"/> abandons one that exceeds its own
    /// timeout. The client meets the same exception as in production, and the test waits for nothing. Use
    /// <see cref="Slow"/> only to verify that a timeout is applied at all.
    /// </summary>
    Timeout,

    /// <summary>
    /// The server answers HTTP 400, which it does when a required query-string argument is missing or unparsable.
    /// </summary>
    BadRequest,

    /// <summary>
    /// The server answers HTTP 403, whose body is the explanation. This is how a real server denies a lease, so the
    /// body must reach the user. See <see cref="LicenseServerSimulator.DenialMessage"/>.
    /// </summary>
    Forbidden,

    /// <summary>
    /// The server answers HTTP 404, as a server whose lease handler is not deployed does.
    /// </summary>
    NotFound,

    /// <summary>
    /// The server answers HTTP 500 with an error page.
    /// </summary>
    InternalServerError,

    /// <summary>
    /// The server answers HTTP 503, which it does when its global lock times out under load.
    /// </summary>
    ServiceUnavailable,

    /// <summary>
    /// The server answers HTTP 200 with a body that is not a lease at all, as a captive portal or an authenticating
    /// proxy does.
    /// </summary>
    GarbageResponse,

    /// <summary>
    /// The server answers HTTP 200 with an empty body.
    /// </summary>
    EmptyResponse,

    /// <summary>
    /// The server answers HTTP 200 with a body that carries the instants but no licence key, which is the only
    /// mandatory part.
    /// </summary>
    MissingLicenseKey,

    /// <summary>
    /// The server answers HTTP 200 with a lease that has already expired, which a server whose clock is behind the
    /// client's produces.
    /// </summary>
    ExpiredLease,

    /// <summary>
    /// The server takes <see cref="LicenseServerSimulator.ResponseDelay"/> before answering. Only the test that
    /// verifies that a timeout is applied at all needs this; every other one uses <see cref="Timeout"/>, which waits
    /// for nothing.
    /// </summary>
    Slow
}
