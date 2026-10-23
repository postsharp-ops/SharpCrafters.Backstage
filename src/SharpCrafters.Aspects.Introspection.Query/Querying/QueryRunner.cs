// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Collections.Generic;
using System.Linq;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// The parsed options of <c>list</c>.
/// </summary>
internal sealed class ListQuery
{
    public ListQuery(
        TypeDefinition type,
        IReadOnlyList<QueryExpression> conditions,
        IReadOnlyList<PathExpression>? selection,
        IReadOnlyList<(PathExpression Path, bool IsDescending)> orderings,
        int skip,
        int? limit )
    {
        this.Type = type;
        this.Conditions = conditions;
        this.Selection = selection;
        this.Orderings = orderings;
        this.Skip = skip;
        this.Limit = limit;
    }

    public TypeDefinition Type { get; }

    public IReadOnlyList<QueryExpression> Conditions { get; }

    // The paths of --select, or null for every field.
    public IReadOnlyList<PathExpression>? Selection { get; }

    public IReadOnlyList<(PathExpression Path, bool IsDescending)> Orderings { get; }

    public int Skip { get; }

    public int? Limit { get; }

    /// <summary>
    /// Gets the objects that satisfy every condition, in the requested order, after <see cref="Skip"/> and <see cref="Limit"/>,
    /// and all the objects that satisfy every condition, before them. Without an ordering, the order is that of the file.
    /// </summary>
    public (IReadOnlyList<object> Items, IReadOnlyList<object> Matching) Run( QueryContext context )
    {
        var items = this.Type.GetItems( context ).Where( item => this.Conditions.All( c => c.IsTrue( context, item ) ) );

        if ( this.Orderings.Count > 0 )
        {
            var comparer = Comparer<object?>.Create( CompareValues );
            IOrderedEnumerable<object>? ordered = null;

            foreach ( var (path, isDescending) in this.Orderings )
            {
                object? Key( object item ) => path.Evaluate( context, item, _empty );

                ordered = ordered == null
                    ? isDescending ? items.OrderByDescending( Key, comparer ) : items.OrderBy( Key, comparer )
                    : isDescending ? ordered.ThenByDescending( Key, comparer ) : ordered.ThenBy( Key, comparer );
            }

            items = ordered!;
        }

        var all = items.ToList();
        IEnumerable<object> page = all.Skip( this.Skip );

        if ( this.Limit != null )
        {
            page = page.Take( this.Limit.Value );
        }

        return (page.ToList(), all);
    }

    private static readonly IReadOnlyDictionary<string, object?> _empty = new Dictionary<string, object?>();

    // null first, then strings by ordinal, numbers, and false before true.
    private static int CompareValues( object? x, object? y )
        => (x, y) switch
        {
            (null, null) => 0,
            (null, _) => -1,
            (_, null) => 1,
            (string a, string b) => string.CompareOrdinal( a, b ),
            _ => Comparer<object>.Default.Compare( x, y )
        };
}
