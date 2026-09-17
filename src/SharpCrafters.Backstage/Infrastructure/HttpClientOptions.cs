// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;

namespace SharpCrafters.Backstage.Infrastructure;

/// <summary>
/// The options of an <see cref="System.Net.Http.HttpClient"/> created by an <see cref="IHttpClientFactory"/>.
/// </summary>
[PublicAPI]
public sealed record HttpClientOptions
{
    /// <summary>
    /// Gets the options of a client that authenticates with nothing and uses the default timeout.
    /// </summary>
    public static HttpClientOptions Default { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the requests authenticate with the credentials of the user who runs the
    /// current process, that is, with Windows integrated authentication.
    /// </summary>
    /// <remarks>
    /// An on-premises license server is typically published by IIS with Windows authentication, and answers 401 to
    /// an anonymous request. PostSharp used <c>CredentialCache.DefaultNetworkCredentials</c> for that reason. The
    /// option has no effect on an operating system that has no such mechanism, where the request is anonymous.
    /// </remarks>
    public bool UseDefaultCredentials { get; init; }

    /// <summary>
    /// Gets the time after which a request is abandoned, or <see langword="null"/> to use the default of
    /// <see cref="System.Net.Http.HttpClient"/>, which is 100 seconds.
    /// </summary>
    public TimeSpan? Timeout { get; init; }
}
