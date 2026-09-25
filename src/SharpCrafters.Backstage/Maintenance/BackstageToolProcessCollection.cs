// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;
using System.Collections;
using System.Collections.Generic;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// The running processes of the tools of the product, returned by <see cref="IProcessManager.GetToolProcesses"/>.
/// </summary>
/// <remarks>
/// The caller owns the collection and disposes it. Disposing the collection disposes every
/// <see cref="BackstageToolProcess"/> in it, and therefore every <see cref="System.Diagnostics.Process"/>.
/// </remarks>
[PublicAPI]
public sealed class BackstageToolProcessCollection : IReadOnlyList<BackstageToolProcess>, IDisposable
{
    private readonly IReadOnlyList<BackstageToolProcess> _items;

    internal BackstageToolProcessCollection( IReadOnlyList<BackstageToolProcess> items )
    {
        this._items = items;
    }

    public BackstageToolProcess this[ int index ] => this._items[index];

    public int Count => this._items.Count;

    public IEnumerator<BackstageToolProcess> GetEnumerator() => this._items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    public void Dispose()
    {
        foreach ( var item in this._items )
        {
            item.Dispose();
        }
    }
}
