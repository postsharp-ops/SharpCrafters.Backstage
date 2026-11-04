// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// The text of a query option, such as <c>--where</c>, and the errors about it: prefixed with the option, followed by the
/// text and, when the position is known, a caret under it.
/// </summary>
internal sealed class QueryText
{
    public const string ShellHint =
        "If the path ended with $count inside double quotes, the shell removed it: quote the condition with single quotes, or escape the $ "
        + "(\\$count in bash, `$count in PowerShell).";

    public QueryText( string option, string text )
    {
        this.Option = option;
        this.Text = text;
    }

    // The option, such as --where.
    public string Option { get; }

    public string Text { get; }

    public IntrospectionCommandException Error( string message, int? position = null )
        => new(
            $"{this.Option}: {message}" + Environment.NewLine + "  " + this.Text
            + (position == null ? "" : Environment.NewLine + "  " + new string( ' ', Math.Clamp( position.Value, 0, this.Text.Length ) ) + "^"),
            IntrospectionExitCodes.InvalidQuery );
}
