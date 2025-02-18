// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Metalama.Backstage.Configuration;
using Metalama.Backstage.Extensibility;
using Metalama.Backstage.Infrastructure;
using Metalama.Backstage.Licensing;
using Metalama.Backstage.Testing;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Metalama.Backstage.Tests.Licensing;

internal static class TestLicensingConfigurationHelpers
{
    public static string? ReadStoredGen0LicenseString( IServiceProvider serviceProvider )
        => serviceProvider.GetRequiredBackstageService<IConfigurationManager>().Get<LicensingConfiguration>().License;

    public static string? ReadStoredGen1LicenseString( IServiceProvider serviceProvider )
        => serviceProvider.GetRequiredBackstageService<IConfigurationManager>().Get<LicensingConfiguration>().Licenses.SingleOrDefault();

    public static void SetStoredGen0LicenseString( IServiceProvider serviceProvider, string licenseString )
    {
        var configuration = new LicensingConfiguration { License = licenseString };

        ((TestFileSystem) serviceProvider.GetRequiredBackstageService<IFileSystem>()).Mock.AddFile(
            serviceProvider.GetRequiredBackstageService<IConfigurationManager>().GetFilePath<LicensingConfiguration>(),
            new MockFileDataEx( configuration.ToJson() ) );
    }

    public static void SetStoredGen1LicenseString( IServiceProvider serviceProvider, string licenseString )
    {
        var configuration = new LicensingConfiguration { Licenses = ImmutableArray.Create( licenseString ) };

        ((TestFileSystem) serviceProvider.GetRequiredBackstageService<IFileSystem>()).Mock.AddFile(
            serviceProvider.GetRequiredBackstageService<IConfigurationManager>().GetFilePath<LicensingConfiguration>(),
            new MockFileDataEx( configuration.ToJson() ) );
    }
}