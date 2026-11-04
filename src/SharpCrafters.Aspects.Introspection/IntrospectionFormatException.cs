// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// The exception that a reader throws when a weaving symbol file is malformed: XML that is not well formed, a reference to a
/// name that the name table does not define, a missing required attribute, or an invalid number.
/// </summary>
public sealed class IntrospectionFormatException : FormatException
{
    internal IntrospectionFormatException( string message, int lineNumber, int linePosition, Exception? innerException = null )
        : base( lineNumber > 0 ? $"{message} (line {lineNumber}, position {linePosition})" : message, innerException )
    {
        this.LineNumber = lineNumber;
        this.LinePosition = linePosition;
    }

    /// <summary>
    /// Gets the line of the file where the error is, or <c>0</c> when the reader gives no line information.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// Gets the position in the line where the error is, or <c>0</c> when the reader gives no line information.
    /// </summary>
    public int LinePosition { get; }
}
