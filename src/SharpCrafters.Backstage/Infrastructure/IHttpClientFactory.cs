// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using System.Net.Http;

namespace SharpCrafters.Backstage.Infrastructure;

/// <summary>
/// Creates instances of <see cref="HttpClient"/> class.
/// </summary>
[PublicAPI]
public interface IHttpClientFactory : IBackstageService
{
    /// <summary>
    /// Creates a new instance of <see cref="HttpClient"/> with <see cref="HttpClientOptions.Default"/>.
    /// </summary>
    /// <returns>The new object of <see cref="HttpClient"/>.</returns>
    HttpClient Create();

    /// <summary>
    /// Creates a new instance of <see cref="HttpClient"/> with the given options.
    /// </summary>
    /// <param name="options">The options of the client, such as whether it authenticates with the credentials of the current user.</param>
    /// <returns>The new object of <see cref="HttpClient"/>.</returns>
    HttpClient Create( HttpClientOptions options );
}
