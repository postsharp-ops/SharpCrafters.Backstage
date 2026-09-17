// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Infrastructure;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Testing
{
    public sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly ILogger _logger;

        public TestHttpClientFactory( IServiceProvider serviceProvider )
        {
            this._logger = serviceProvider.GetLoggerFactory().GetLogger( nameof(TestHttpClientFactory) );
        }

        /// <summary>
        /// The registered hooks, first match wins. The list is immutable and replaced atomically, because a test may
        /// register a hook while a request started by another thread is enumerating it, which throws on a
        /// <see cref="List{T}"/>.
        /// </summary>
        private ImmutableList<Hook> _hooks = ImmutableList<Hook>.Empty;

        public ConcurrentBag<(HttpRequestMessage Request, HttpResponseMessage Response)> ProcessedRequests { get; private set; } = [];

        /// <summary>
        /// Gets the options passed to the last call to <see cref="Create(HttpClientOptions)"/>, so that a test can
        /// assert how the code under test asked for its client without observing a real connection.
        /// </summary>
        public HttpClientOptions? LastOptions { get; private set; }

        public void InsertHook( Predicate<HttpRequestMessage> filter, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> hook )
        {
            var item = new Hook( filter, hook );

            ImmutableList<Hook> initial, updated;

            do
            {
                initial = this._hooks;
                updated = initial.Insert( 0, item );
            }
            while ( Interlocked.CompareExchange( ref this._hooks, updated, initial ) != initial );
        }

        /// <summary>
        /// Removes a hook registered by <see cref="InsertHook"/>, so that one component can stop answering without
        /// removing the hooks of the others, which <see cref="ClearHooks"/> does.
        /// </summary>
        /// <param name="hook">The delegate passed to <see cref="InsertHook"/>.</param>
        /// <returns><see langword="true"/> if the hook was registered.</returns>
        public bool RemoveHook( Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> hook )
        {
            ImmutableList<Hook> initial, updated;

            do
            {
                initial = this._hooks;
                var index = initial.FindIndex( h => h.Handler == hook );

                if ( index < 0 )
                {
                    return false;
                }

                updated = initial.RemoveAt( index );
            }
            while ( Interlocked.CompareExchange( ref this._hooks, updated, initial ) != initial );

            return true;
        }

        public void ClearProcessedRequests() => this.ProcessedRequests = [];

        public void ClearHooks() => this._hooks = ImmutableList<Hook>.Empty;

        public HttpClient Create() => this.Create( HttpClientOptions.Default );

        public HttpClient Create( HttpClientOptions options )
        {
            this.LastOptions = options;

            var client = new HttpClient( new Handler( this ) );

            if ( options.Timeout != null )
            {
                client.Timeout = options.Timeout.Value;
            }

            return client;
        }

        private sealed record Hook( Predicate<HttpRequestMessage> Filter, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Handler );

        private sealed class Handler : HttpMessageHandler
        {
            private readonly TestHttpClientFactory _parent;

            public Handler( TestHttpClientFactory parent )
            {
                this._parent = parent;
            }

            protected override async Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
            {
                var hook = this._parent._hooks.FirstOrDefault( h => h.Filter( request ) )?.Handler;

                HttpResponseMessage response;

                if ( hook != null )
                {
                    this._parent._logger.Trace?.Log( $"Hooking '{request.RequestUri}'." );
                    response = await hook( request, cancellationToken );
                }
                else
                {
                    this._parent._logger.Trace?.Log( $"No hook was registered for '{request.RequestUri}'. Returning dummy response." );

                    response = new HttpResponseMessage( HttpStatusCode.OK )
                    {
                        Content = new StringContent( $"<error>No hook was registered for '{request.RequestUri}'.</error>" )
                    };
                }

                this._parent.ProcessedRequests.Add( (request, response) );

                return response;
            }
        }
    }
}
