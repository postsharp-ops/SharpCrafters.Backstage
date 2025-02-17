// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Metalama.Backstage.Licensing.Consumption.Sources;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Metalama.Backstage.Licensing.Consumption;

/// <inheritdoc />
internal sealed class LicenseConsumptionService : ILicenseConsumptionService
{
    private readonly IServiceProvider _services;
    private readonly IReadOnlyList<ILicenseSource> _sources;

    public LicenseConsumptionService( IServiceProvider services, IReadOnlyList<ILicenseSource> licenseSources )
    {
        this._services = services;
        this._sources = licenseSources;

        foreach ( var source in this._sources )
        {
            source.Changed += this.OnSourceChanged;
        }
    }

    private void OnSourceChanged()
    {
        this.Changed?.Invoke();
    }

    public ILicenseConsumer CreateConsumer(
        string? projectLicenseKey,
        LicenseSourceKind ignoredLicenseKinds,
        out ImmutableArray<LicensingMessage> messages )
    {
        var sources = new List<ILicenseSource>( this._sources.Count + 1 );

        sources.AddRange( this._sources.Where( s => (s.Kind & ignoredLicenseKinds) == 0 ) );

        if ( !string.IsNullOrEmpty( projectLicenseKey ) )
        {
            // ReSharper disable once RedundantSuppressNullableWarningExpression
            sources.Add( new ExplicitLicenseSource( projectLicenseKey!, this._services ) );
        }

        return LicenseConsumer.Create( this._services, sources, out messages );
    }

    public ILicenseConsumer CreateConsumer( string? projectLicenseKey = null, LicenseSourceKind ignoredLicenseKinds = LicenseSourceKind.None )
        => this.CreateConsumer( projectLicenseKey, ignoredLicenseKinds, out _ );

    public event Action? Changed;
}