// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Common.Testing.Hooks;
using System.Globalization;

namespace SharpCrafters.Backstage.VersionControl;

/// <summary>
/// The synchronization points of <see cref="GitStatusService"/> and <see cref="VcsStatusCache"/>, and the names by
/// which a test refers to them.
/// </summary>
/// <remarks>
/// <para>
/// They are gathered here rather than declared beside the code that reaches them, so that the classes of this
/// namespace hold what the product does and this one holds what the tests need. Every name is composed by the methods
/// below, so that a test and the code under test cannot disagree about it.
/// </para>
/// <para>
/// A synchronization point costs a null check when no test has enabled it. See
/// <see cref="ITestSynchronizationProvider"/> for what one is and when adding one is justified.
/// </para>
/// </remarks>
internal static class TestSynchronizationPoints
{
    /// <summary>
    /// Reached after the query of a repository has found no run in progress and before it registers its own, so that
    /// a test can hold one caller here while another reaches the same point and exercise the registration race.
    /// </summary>
    public const string BeforeRegisteringQuery = "BeforeRegisteringQuery";

    /// <summary>
    /// Reached inside the run of the command, so that a test can hold the caller that started it while other callers
    /// arrive and verify that they join the run instead of starting one of their own.
    /// </summary>
    public const string InsideCommand = "InsideCommand";

    /// <summary>
    /// Reached by a caller that has found a run in progress and is about to join it, so that a test can establish
    /// that the caller joined rather than infer it from the number of commands.
    /// </summary>
    public const string JoinedQuery = "JoinedQuery";

    /// <summary>
    /// Reached after the memory layer of the cache has missed and before the file layer is read, so that a test can
    /// let a record be stored in the middle of a read.
    /// </summary>
    public const string BeforeReadingFile = "BeforeReadingFile";

    /// <summary>
    /// Reached after a record has been placed in the memory layer of the cache and before it is written to the file
    /// layer, so that a test can let another instance run while the file is not yet there.
    /// </summary>
    public const string BeforeWritingFile = "BeforeWritingFile";

    /// <summary>
    /// Composes the name of a synchronization point of <see cref="GitStatusService"/>, following the
    /// <c>{ClassName}.{Location}:{Context}</c> convention.
    /// </summary>
    /// <param name="location">One of the constants of this class.</param>
    /// <param name="repositoryRoot">The repository being queried, so that a test can pin one repository without
    /// pinning every other repository that the process queries.</param>
    public static string ForService( string location, string repositoryRoot ) => Compose( nameof(GitStatusService), location, repositoryRoot );

    /// <summary>
    /// Composes the name of a synchronization point of <see cref="VcsStatusCache"/>, following the
    /// <c>{ClassName}.{Location}:{Context}</c> convention.
    /// </summary>
    /// <param name="location">One of the constants of this class.</param>
    /// <param name="repositoryRoot">The repository whose record is being read or written.</param>
    public static string ForCache( string location, string repositoryRoot ) => Compose( nameof(VcsStatusCache), location, repositoryRoot );

    private static string Compose( string className, string location, string repositoryRoot )
        => string.Format( CultureInfo.InvariantCulture, "{0}.{1}:{2}", className, location, repositoryRoot );
}