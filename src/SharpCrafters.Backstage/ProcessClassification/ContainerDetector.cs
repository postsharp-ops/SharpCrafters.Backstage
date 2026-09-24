// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using System;

namespace SharpCrafters.Backstage.ProcessClassification;

internal class ContainerDetector : IContainerDetector
{
    private readonly ILogger _logger;
    private readonly Lazy<bool> _isRunningInContainerLazy;

    public ContainerDetector( IServiceProvider serviceProvider )
    {
        this._logger = serviceProvider.GetLoggerFactory().GetLogger( nameof(ContainerDetector) );
        this._isRunningInContainerLazy = new Lazy<bool>( this.IsRunningInContainerCore );
    }

    public bool IsRunningInContainer => this._isRunningInContainerLazy.Value;

    private bool IsRunningInContainerCore() => ContainerDetection.IsRunningInContainer( this._logger );
}