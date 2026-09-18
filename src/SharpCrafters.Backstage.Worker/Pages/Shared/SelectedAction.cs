// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;

namespace SharpCrafters.Backstage.Worker.Pages.Shared;

[PublicAPI]
public enum SelectedAction
{
    None,
    OpenSource,

    /// <summary>
    /// Register one of the editions that the product family offers, named by
    /// <see cref="GlobalState.SelfRegisteredEditionAlias"/>.
    /// </summary>
    SelfRegisteredEdition,
    Trial,
    Register,
    Skip
}