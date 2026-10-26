// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Projects;
using System.Collections.Generic;

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// The provenance of the objects of a merged model: the targets, each a project and a target framework, whose files hold them.
/// </summary>
internal sealed class ModelProvenance
{
    private readonly Dictionary<object, List<IntrospectionTarget>> _targets = new( ReferenceEqualityComparer.Instance );

    public ModelProvenance( IReadOnlyList<IntrospectionTarget> allTargets )
    {
        this.AllTargets = allTargets;
    }

    public IReadOnlyList<IntrospectionTarget> AllTargets { get; }

    public IReadOnlyList<IntrospectionTarget> GetTargets( object value ) => this._targets.TryGetValue( value, out var targets ) ? targets : this.AllTargets;

    public void Add( object value, IntrospectionTarget target )
    {
        if ( !this._targets.TryGetValue( value, out var targets ) )
        {
            targets = [];
            this._targets.Add( value, targets );
        }

        if ( !targets.Contains( target ) )
        {
            targets.Add( target );
        }
    }
}
