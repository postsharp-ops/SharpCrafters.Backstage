// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.ProcessClassification;

namespace SharpCrafters.Backstage.Application
{
    internal sealed class ApplicationInfoProvider : IApplicationInfoProvider
    {
        public IApplicationInfo Application { get; }

        public ProcessKind ProcessKind => this.Application.ProcessKind ?? ProcessKindDetector.GetCurrentProcessKind();

        public ApplicationInfoProvider( IApplicationInfo initialApplication )
        {
            this.Application = initialApplication;
        }
    }
}