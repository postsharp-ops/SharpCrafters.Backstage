// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;

namespace SharpCrafters.Backstage.FileLocks;

/// <summary>
/// Replaces the time limit and the warning threshold that a caller gives to a <c>RetryWithLockDetection</c> method of
/// <see cref="RetryHelper"/>. The user sets them in environment variables, so that a machine where a file stays locked
/// for longer can wait for longer without a new version of the product.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RetryHelper"/> resolves this service from the service provider given to a <c>RetryWithLockDetection</c>
/// method. The core services register an implementation that reads the <c>FILE_LOCK_TIMEOUT</c> and
/// <c>FILE_LOCK_WARNING</c> environment variables of the product, for instance <c>POSTSHARP_FILE_LOCK_TIMEOUT</c>. The
/// <c>Retry</c> methods take no service provider and are not affected.
/// </para>
/// </remarks>
public interface IFileLockRetrySettings : IBackstageService
{
    /// <summary>
    /// Gets the time limit that replaces the <see cref="RetryBudget.Duration"/> of the budget given by the caller, or
    /// <c>null</c> to keep it. A budget that limits the number of attempts only is not changed.
    /// </summary>
    TimeSpan? Timeout { get; }

    /// <summary>
    /// Gets the time of failures from which the <see cref="RetryWarning"/> given by the caller is reported, instead of
    /// its own threshold, or <c>null</c> to keep it. Zero reports the warning at the first failed attempt. A value that
    /// is not below the time limit of the budget means no warning. When the caller gives no warning, there is none.
    /// </summary>
    TimeSpan? WarningThreshold { get; }
}
