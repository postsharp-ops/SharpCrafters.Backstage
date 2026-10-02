// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System.Collections.Immutable;

namespace SharpCrafters.Backstage.Repositories;

/// <summary>
/// The result of <see cref="IRepositoryConfigurationReader.Read"/>: the repository settings that a file declares, or
/// the reasons why the file is ignored.
/// </summary>
[PublicAPI]
public sealed record RepositoryConfigurationReadResult
{
    private readonly ImmutableArray<string> _errors = ImmutableArray<string>.Empty;

    /// <summary>
    /// Gets a value indicating whether telemetry is enabled for the repository: <c>false</c> opts the repository out,
    /// <c>true</c> opts it in, still subject to the gates of the process, and <c>null</c> leaves the global default.
    /// </summary>
    public bool? TelemetryEnabled { get; init; }

    /// <summary>
    /// Gets a value indicating whether the file declares a repository setting, including one whose value is invalid.
    /// A file below the repository root is reported when it declares one.
    /// </summary>
    public bool DeclaresSettings { get; init; }

    /// <summary>
    /// Gets the reasons why the file is ignored, each one a clause that completes "The file is ignored because". When
    /// there is any, no setting of the file applies, and each reason is reported as a warning.
    /// </summary>
    public ImmutableArray<string> Errors
    {
        get => this._errors;
        init => this._errors = value.IsDefault ? ImmutableArray<string>.Empty : value;
    }
}
