// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.ProcessClassification;
using System;

namespace SharpCrafters.Backstage.Application
{
    internal sealed class ApplicationInfoProvider : IApplicationInfoProvider
    {
        private readonly Lazy<ProcessKind> _processKind;

        public IApplicationInfo Application { get; }

        /// <summary>
        /// Gets the kind of the current process. It is <see cref="IApplicationInfo.ProcessKind"/> when the application
        /// declares one, and otherwise the kind that <see cref="ProcessKindDetector"/> finds. The value is computed once.
        /// </summary>
        public ProcessKind ProcessKind => this._processKind.Value;

        public ApplicationInfoProvider( IApplicationInfo initialApplication )
            : this( initialApplication, ProcessKindDetector.GetCurrentProcessKind ) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationInfoProvider"/> class with the function that detects
        /// the kind of the current process when the application does not declare one. A test passes a function that
        /// returns a fixed kind.
        /// </summary>
        internal ApplicationInfoProvider( IApplicationInfo initialApplication, Func<ProcessKind> detectProcessKind )
        {
            this.Application = initialApplication;
            this._processKind = new Lazy<ProcessKind>( () => initialApplication.ProcessKind ?? detectProcessKind() );
        }
    }
}
