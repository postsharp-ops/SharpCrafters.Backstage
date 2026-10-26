// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

internal sealed class FunctionExpression : QueryExpression
{
    private static readonly TimeSpan _matchTimeout = TimeSpan.FromSeconds( 1 );

    private readonly string _name;
    private readonly IReadOnlyList<QueryExpression> _arguments;
    private readonly Regex? _pattern;

    public FunctionExpression( string name, IReadOnlyList<QueryExpression> arguments, ExpressionType type, Regex? pattern ) : base( type )
    {
        this._name = name;
        this._arguments = arguments;
        this._pattern = pattern;
    }

    public override object? Evaluate( QueryContext context, object current, IReadOnlyDictionary<string, object?> variables )
    {
        var values = this._arguments.Select( a => a.Evaluate( context, current, variables ) as string ).ToList();

        if ( values[0] == null )
        {
            return this.Type.Kind == ValueKind.Boolean ? false : null;
        }

        return this._name switch
        {
            "contains" => values[1] != null && values[0]!.Contains( values[1]!, StringComparison.Ordinal ),
            "startswith" => values[1] != null && values[0]!.StartsWith( values[1]!, StringComparison.Ordinal ),
            "endswith" => values[1] != null && values[0]!.EndsWith( values[1]!, StringComparison.Ordinal ),
            "matchesPattern" => Match( this._pattern!, values[0]! ),
            "tolower" => values[0]!.ToLowerInvariant(),
            _ => values[0]!.ToUpperInvariant()
        };

        static bool Match( Regex pattern, string value )
        {
            try
            {
                return pattern.IsMatch( value );
            }
            catch ( RegexMatchTimeoutException )
            {
                throw new IntrospectionCommandException( $"The regular expression '{pattern}' takes too long to match.", IntrospectionExitCodes.InvalidQuery );
            }
        }
    }

    public static Regex CreatePattern( string pattern )
    {
        try
        {
            return new Regex( pattern, RegexOptions.CultureInvariant, _matchTimeout );
        }
        catch ( ArgumentException e )
        {
            throw new IntrospectionCommandException( $"'{pattern}' is not a valid regular expression: {e.Message}", IntrospectionExitCodes.InvalidQuery );
        }
    }

    public static string FormatValue( object? value )
        => value switch
        {
            null => "null",
            string s => s,
            bool b => b ? "true" : "false",
            long l => l.ToString( CultureInfo.InvariantCulture ),
            _ => value.ToString() ?? ""
        };
}
