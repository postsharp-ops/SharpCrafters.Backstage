// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Infrastructure.ProcessClassification;
using SharpCrafters.Backstage.Utilities;
using System;
using System.Collections.Immutable;
using System.Reflection;
using ILoggerFactory = SharpCrafters.Backstage.Diagnostics.ILoggerFactory;

namespace SharpCrafters.Backstage.Application
{
    /// <summary>
    /// Implementation of <see cref="IApplicationInfo" /> interface with build information
    /// initialized from assembly metadata using <see cref="AssemblyMetadataReader" />.
    /// </summary>
    public abstract class ApplicationInfoBase : ComponentInfoBase, IApplicationInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationInfoBase"/> class.
        /// </summary>
        /// <param name="metadataAssembly">The assembly whose metadata describes the application.</param>
        /// <param name="productProfile">The profile of the product family that the application belongs to.</param>
        private readonly ProductProfile _productProfile;

        protected ApplicationInfoBase( Assembly metadataAssembly, ProductProfile productProfile ) : base( metadataAssembly, productProfile )
        {
            this._productProfile = productProfile;
        }

        /// <summary>
        /// The environment variable, after the prefix of the product, that forces the attended answer. It is
        /// <c>METALAMA_FORCE_ATTENDED</c> for Metalama.
        /// </summary>
        public const string ForceAttendedVariableName = "FORCE_ATTENDED";

        /// <inheritdoc />
        public virtual ProcessKind ProcessKind => ProcessUtilities.ProcessKind;

        /// <inheritdoc />
        public virtual bool IsLongRunningProcess => false;

        /// <inheritdoc />
        /// <remarks>
        /// <para>
        /// The environment variable named by <see cref="ForceAttendedVariableName"/>, after the prefix of the product,
        /// forces the attended answer. It exists for the tests of the products built on this package, which run in a
        /// container and are therefore detected as unattended, and which would otherwise be unable to exercise
        /// anything that a developer machine does and a build server does not.
        /// </para>
        /// <para>
        /// The override goes one way only. Attended is the stricter of the two answers: it withholds the unattended
        /// licence source and it lets the licence audit run, so forcing it can take a licence away and never grant
        /// one. Forcing the other direction would hand the unattended licence to whoever set the variable, so the
        /// variable does not do it, whatever its value. This is the difference between this override and the ones in
        /// <see cref="ComponentInfoBase"/>, which are still to be reviewed for the same abuse. See #2018.
        /// </para>
        /// </remarks>
        public virtual bool IsUnattendedProcess( ILoggerFactory loggerFactory )
        {
            var variableName = this._productProfile.GetEnvironmentVariableName( ForceAttendedVariableName );

            if ( bool.TryParse( Environment.GetEnvironmentVariable( variableName ), out var forceAttended ) && forceAttended )
            {
                loggerFactory.GetLogger( nameof(ApplicationInfoBase) ).Trace?.Log(
                    $"Attended mode forced by the '{variableName}' environment variable." );

                return false;
            }

            return ProcessUtilities.IsCurrentProcessUnattended( loggerFactory );
        }

        /// <inheritdoc />
        public virtual bool IsTelemetryEnabled => true;

        /// <inheritdoc />
        public virtual bool IsLicenseAuditEnabled => false;

        /// <inheritdoc />
        public virtual bool ShouldCreateLocalCrashReports => true;

        public virtual ImmutableArray<IComponentInfo> Components => ImmutableArray<IComponentInfo>.Empty;
    }
}