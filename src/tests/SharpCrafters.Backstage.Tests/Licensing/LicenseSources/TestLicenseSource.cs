// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using SharpCrafters.Backstage.Licensing.Licenses;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SharpCrafters.Backstage.Tests.Licensing.LicenseSources
{
    internal sealed class TestLicenseSource : ILicenseSource, IUsable
    {
        private readonly ILicense? _license;

        public string Description => "test license source";

        public LicenseSourceKind Kind => LicenseSourceKind.Test;

        [UsedImplicitly]
        public string Id { get; }

        public int NumberOfAuditReports { get; private set; }

        public TestLicenseSource( string id, ILicense? license )
        {
            this.Id = id;
            this._license = license;
        }

#pragma warning disable CS1998 // The source holds its license in a field, so nothing is awaited.
        public async IAsyncEnumerable<ILicense> GetLicensesAsync(
            Action<LicensingMessage> reportMessage,
            [EnumeratorCancellation] CancellationToken cancellationToken = default )
#pragma warning restore CS1998
        {
            this.NumberOfAuditReports++;

            if ( this._license != null )
            {
                yield return this._license;
            }
        }

        event Action? ILicenseSource.Changed { add { } remove { } }

        public LicenseSourcePriority Priority => LicenseSourcePriority.UserProfile;

        public bool SupportsRegistration => true;
    }
}