// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.FileLocks;
using System;
using System.Globalization;

namespace SharpCrafters.Backstage.Infrastructure;

/// <summary>
/// The <see cref="IFileLockRetrySettings"/> read from the <c>FILE_LOCK_TIMEOUT</c> and <c>FILE_LOCK_WARNING</c>
/// environment variables of the product, for instance <c>POSTSHARP_FILE_LOCK_TIMEOUT</c>. Both are numbers of
/// milliseconds. The variables are read once, when the service is created.
/// </summary>
internal sealed class FileLockRetrySettings : IFileLockRetrySettings
{
    /// <summary>
    /// The name, without the prefix of the product, of the environment variable that sets <see cref="Timeout"/>.
    /// </summary>
    public const string TimeoutVariableName = "FILE_LOCK_TIMEOUT";

    /// <summary>
    /// The name, without the prefix of the product, of the environment variable that sets <see cref="WarningThreshold"/>.
    /// </summary>
    public const string WarningThresholdVariableName = "FILE_LOCK_WARNING";

    public FileLockRetrySettings( IServiceProvider serviceProvider )
    {
        var productProfile = serviceProvider.GetRequiredBackstageService<ProductProfile>();
        var environmentVariableProvider = serviceProvider.GetRequiredBackstageService<IEnvironmentVariableProvider>();

        this.Timeout = ParseMilliseconds(
            environmentVariableProvider.GetEnvironmentVariable( productProfile.GetEnvironmentVariableName( TimeoutVariableName ) ),
            1 );

        this.WarningThreshold = ParseMilliseconds(
            environmentVariableProvider.GetEnvironmentVariable( productProfile.GetEnvironmentVariableName( WarningThresholdVariableName ) ),
            0 );
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A value that is not a whole number of at least one millisecond is ignored.
    /// </remarks>
    public TimeSpan? Timeout { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// A value that is not a whole number of at least zero milliseconds is ignored.
    /// </remarks>
    public TimeSpan? WarningThreshold { get; }

    /// <summary>
    /// Parses a number of milliseconds from the value of an environment variable. An empty or invalid value, or one
    /// below <paramref name="minimum"/>, gives <c>null</c>.
    /// </summary>
    internal static TimeSpan? ParseMilliseconds( string? value, int minimum )
        => int.TryParse( value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds ) && milliseconds >= minimum
            ? TimeSpan.FromMilliseconds( milliseconds )
            : null;
}
