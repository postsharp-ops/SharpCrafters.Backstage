// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;

namespace SharpCrafters.Backstage.Licensing.Consumption;

/// <summary>
/// A message that licensing reports to the application, which typically reports it as a diagnostic.
/// </summary>
/// <param name="Text">The text of the message.</param>
/// <param name="Kind">
/// What the message is about. See <see cref="LicensingMessageKind"/> for what an application does with it.
/// </param>
[PublicAPI]
public sealed record LicensingMessage( string Text, LicensingMessageKind Kind )
{
    public bool IsError { get; init; }

    public override string ToString() => $"{(this.IsError ? "Error" : "Warning")}: {this.Text}";
}
