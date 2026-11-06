// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCrafters.Backstage.UserInterface;

/// <summary>
/// Remembers whether the Visual Studio extension is installed.
/// </summary>
/// <remarks>
/// <para>
/// One extension serves every product, so the status is kept once for the user, in the registry under
/// <see cref="KeyPath"/>, where the build of every product reads it.
/// </para>
/// <para>
/// Earlier versions kept the status in the configuration store of each product, as
/// <see cref="IdeExtensionsStatusConfiguration"/>, and a build that uses an earlier version of a product still reads it
/// there. So the status is also written to these legacy stores, and it is read from them when the registry holds no
/// status, for instance because an earlier version of the extension wrote it.
/// </para>
/// </remarks>
internal sealed class IdeExtensionStatusService : IIdeExtensionStatusService
{
    internal const string KeyPath = @"Software\SharpCrafters\IdeExtensions";
    internal const string VisualStudioValueName = "VisualStudio";

    private readonly IUserDeviceDetectionService _userDeviceDetectionService;
    private readonly IRegistryService? _registryService;
    private readonly Func<IEnumerable<IConfigurationManager>> _getLegacyStores;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdeExtensionStatusService"/> class whose legacy store is the
    /// configuration store of the product.
    /// </summary>
    public IdeExtensionStatusService( IServiceProvider serviceProvider )
        : this( serviceProvider, () => [serviceProvider.GetRequiredBackstageService<IConfigurationManager>()] ) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdeExtensionStatusService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="getLegacyStores">Gets the configuration stores where earlier versions of the products read the
    /// status. It is called each time the stores are needed, so that a store is created only when it is needed.</param>
    public IdeExtensionStatusService( IServiceProvider serviceProvider, Func<IEnumerable<IConfigurationManager>> getLegacyStores )
    {
        this._userDeviceDetectionService = serviceProvider.GetRequiredBackstageService<IUserDeviceDetectionService>();
        this._registryService = serviceProvider.GetBackstageService<IRegistryService>() is { IsSupported: true } registryService ? registryService : null;
        this._getLegacyStores = getLegacyStores;
    }

    public bool ShouldRecommendToInstallVisualStudioExtension
        => this._userDeviceDetectionService is { IsInteractiveDevice: true, IsVisualStudioInstalled: true }
           && !this.IsVisualStudioExtensionInstalled;

    public bool IsVisualStudioExtensionInstalled
    {
        get
            => this.GetSharedStatus()
               ?? this._getLegacyStores().Any( s => s.Get<IdeExtensionsStatusConfiguration>().IsVisualStudioExtensionInstalled );

        set
        {
            // A store that cannot be written does not prevent writing the others.
            var writes = new List<Action> { () => this.SetSharedStatus( value ) };

            writes.AddRange(
                this._getLegacyStores()
                    .Select( s => (Action) (() => s.Update<IdeExtensionsStatusConfiguration>( c => c with { IsVisualStudioExtensionInstalled = value } )) ) );

            writes.ForEachContinueOnException( write => write() );
        }
    }

    private bool? GetSharedStatus()
    {
        using var key = this._registryService?.OpenKey( RegistryHiveKind.CurrentUser, KeyPath );

        return key?.GetValue( VisualStudioValueName ) is int value ? value != 0 : null;
    }

    private void SetSharedStatus( bool value )
    {
        if ( this._registryService == null )
        {
            return;
        }

        using var key = this._registryService.CreateKey( RegistryHiveKind.CurrentUser, KeyPath )
                        ?? throw new InvalidOperationException(
                            $"Cannot create the registry key '{this._registryService.GetDisplayPath( RegistryHiveKind.CurrentUser, KeyPath )}'." );

        key.SetDWordValue( VisualStudioValueName, value ? 1 : 0 );
    }
}
