// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.UserInterface;
using System.Linq;

namespace SharpCrafters.Backstage.Application;

/// <summary>
/// The <see cref="IIdeExtensionStatusService"/> of a composite product. It reads the status of the primary product and
/// writes it to every product, so that no product recommends installing the extension that hosts it.
/// </summary>
internal sealed class CompositeIdeExtensionStatusService : IIdeExtensionStatusService
{
    private readonly CompositeProductServices _products;

    public CompositeIdeExtensionStatusService( CompositeProductServices products )
    {
        this._products = products;
    }

    private IIdeExtensionStatusService Primary => this._products.Primary.IdeExtensionStatusService;

    public bool ShouldRecommendToInstallVisualStudioExtension => this.Primary.ShouldRecommendToInstallVisualStudioExtension;

    public bool IsVisualStudioExtensionInstalled
    {
        get => this.Primary.IsVisualStudioExtensionInstalled;
        set => CompositeBackstageProduct.ForEach( this._products.Products.Select( p => p.IdeExtensionStatusService ), s => s.IsVisualStudioExtensionInstalled = value );
    }
}
