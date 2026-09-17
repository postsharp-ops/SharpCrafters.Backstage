// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Utilities;
using System;
using System.Collections.Immutable;

namespace SharpCrafters.Backstage.Testing
{
    // This object must be immutable because implementations except any IApplicationInfo to be immutable.
    [PublicAPI]
    public class TestApplicationInfo : IApplicationInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestApplicationInfo"/> class.
        /// </summary>
        /// <param name="isPrerelease">A value indicating whether the application is a pre-release.</param>
        /// <param name="version">The version of the application.</param>
        /// <param name="buildDate">The date of build of the application.</param>
        public TestApplicationInfo(
            string name,
            bool isPrerelease,
            string version,
            DateTime buildDate )
        {
            this.Name = name;
            this.IsPrerelease = isPrerelease;
            this.PackageVersion = version;
            this.BuildDate = buildDate;
            this.Company = AssemblyMetadataReader.GetInstance( typeof(TestApplicationInfo).Assembly ).Company;
        }

        public TestApplicationInfo() : this( "test", false, "0.0", DateTime.Now ) { }

        /// <summary>
        /// Gets the package version of the product under test, read from its own assembly. A test whose subject
        /// depends on the version -- licensing does, because a license key and a license server both state the
        /// version they require -- takes it from here. A literal would go on saying "the current version" long after
        /// it had stopped being one, and would pass for years before failing on the release that moved past it.
        /// </summary>
        public static string CurrentPackageVersion { get; } =
            AssemblyMetadataReader.GetInstance( typeof(IApplicationInfo).Assembly ).PackageVersion
            ?? throw new InvalidOperationException( "The assembly of the product under test declares no package version." );

        public string? Company { get; init; }

        /// <inheritdoc />
        public string Name { get; }

        public Version? AssemblyVersion => TestVersionHelper.GetAssemblyVersionFromPackageVersion( this.PackageVersion );

        /// <inheritdoc />
        public bool? IsPrerelease { get; init; }

        /// <inheritdoc />
        public string? PackageVersion { get; }

        /// <inheritdoc />
        public DateTime? BuildDate { get; set; }

        /// <inheritdoc />
        public ProcessKind ProcessKind => ProcessKind.Other;

        public bool IsUnattendedProcess { get; init; }

        /// <inheritdoc />
        bool IApplicationInfo.IsUnattendedProcess( ILoggerFactory loggerFactory ) => this.IsUnattendedProcess;

        /// <inheritdoc />
        public bool IsLongRunningProcess { get; init; }

        /// <inheritdoc />
        public bool IsTelemetryEnabled { get; init; }

        /// <inheritdoc />
        public bool IsLicenseAuditEnabled { get; init; } = true;

        /// <inheritdoc />
        public bool ShouldCreateLocalCrashReports => this.IsTelemetryEnabled;

        /// <inheritdoc />
        public ImmutableArray<IComponentInfo> Components { get; init; } = ImmutableArray<IComponentInfo>.Empty;
    }
}