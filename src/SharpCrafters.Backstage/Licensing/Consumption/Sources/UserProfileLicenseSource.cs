// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Extensibility;
using System;
using System.Collections.Generic;

namespace SharpCrafters.Backstage.Licensing.Consumption.Sources;

/// <summary>
/// License source providing licenses from a license file.
/// </summary>
internal sealed class UserProfileLicenseSource : LicenseSourceBase
{
    private readonly Version _currentVersion;

    private LicensingConfiguration _licensingConfiguration;

    public override string Description => "user profile";

    public override LicenseSourceKind Kind => LicenseSourceKind.UserProfile;

    protected override IEnumerable<string> GetLicenseStrings( Action<LicensingMessage> reportMessage )
        => this._licensingConfiguration.GetRegisteredLicenseStrings( this._currentVersion, reportMessage );

    public UserProfileLicenseSource( IServiceProvider services )
        : base( services )
    {
        this._currentVersion = services.GetRequiredBackstageService<IApplicationInfoProvider>().Application.GetLicensingVersion();

        var configurationManager = services.GetRequiredBackstageService<IConfigurationManager>();
        this._licensingConfiguration = configurationManager.Get<LicensingConfiguration>();
        configurationManager.ConfigurationFileChanged += this.OnConfigurationFileChanged;
    }

    private void OnConfigurationFileChanged( ConfigurationFile file )
    {
        if ( file is LicensingConfiguration licensingConfiguration )
        {
            this._licensingConfiguration = licensingConfiguration;
            this.OnChanged();
        }
    }

    public override LicenseSourcePriority Priority => LicenseSourcePriority.UserProfile;
}