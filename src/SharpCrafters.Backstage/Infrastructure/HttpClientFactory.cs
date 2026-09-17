// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Net.Http;

namespace SharpCrafters.Backstage.Infrastructure;

internal sealed class HttpClientFactory : IHttpClientFactory
{
    public HttpClient Create() => this.Create( HttpClientOptions.Default );

    public HttpClient Create( HttpClientOptions options )
    {
        // A handler is only built when an option requires one, so that the common case keeps the default handler,
        // which HttpClient disposes of with itself.
        var client = options.UseDefaultCredentials
            ? new HttpClient( new HttpClientHandler { UseDefaultCredentials = true } )
            : new HttpClient();

        if ( options.Timeout != null )
        {
            client.Timeout = options.Timeout.Value;
        }

        return client;
    }
}
