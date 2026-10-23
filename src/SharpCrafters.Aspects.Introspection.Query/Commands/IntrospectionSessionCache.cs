// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace SharpCrafters.Aspects.Introspection.Query.Commands;

/// <summary>
/// The sessions of one <c>introspect batch</c> call, so that its queries evaluate the projects and load the files once. A batch
/// creates it and shares it with the options of each query; nothing outlives the call.
/// </summary>
/// <remarks>
/// A scope that failed to open, for instance because no file exists, fails again with the same exception, without evaluating the
/// projects again. The queries run one after another, and a session is not modified after it is opened.
/// </remarks>
internal sealed class IntrospectionSessionCache
{
    private readonly Dictionary<(string?, string?, string?, string?), object> _sessions = new();

    /// <summary>
    /// Gets the session of the scope of a command, opening it on the first call.
    /// </summary>
    public IntrospectionSession GetOrOpen( IntrospectionCommandSettings settings, Func<IntrospectionSession> open )
    {
        var key = (settings.Project, settings.Solution, settings.Framework, settings.Configuration);

        if ( !this._sessions.TryGetValue( key, out var entry ) )
        {
            try
            {
                entry = open();
            }
            catch ( IntrospectionCommandException e )
            {
                entry = ExceptionDispatchInfo.Capture( e );
            }

            this._sessions.Add( key, entry );
        }

        if ( entry is ExceptionDispatchInfo failure )
        {
            failure.Throw();
        }

        return (IntrospectionSession) entry;
    }
}
