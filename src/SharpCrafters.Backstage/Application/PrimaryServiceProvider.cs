// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing;
using System;

namespace SharpCrafters.Backstage.Application;

/// <summary>
/// The outer service provider of a composite product as its primary product sees it: the license product catalog is
/// the catalog of the primary product and not the union catalog, so that the licensing services of the primary product
/// read and write the license keys of that product only.
/// </summary>
internal sealed class PrimaryServiceProvider : IServiceProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILicenseProductCatalog _catalog;

    public PrimaryServiceProvider( IServiceProvider serviceProvider, ILicenseProductCatalog catalog )
    {
        this._serviceProvider = serviceProvider;
        this._catalog = catalog;
    }

    public object? GetService( Type serviceType )
        => serviceType == typeof(ILicenseProductCatalog) ? this._catalog : this._serviceProvider.GetService( serviceType );
}
