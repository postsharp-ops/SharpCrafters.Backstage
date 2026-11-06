// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.UserInterface.Toasts;
using SharpCrafters.Backstage.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCrafters.Backstage.Application.Composite;

/// <summary>
/// The <see cref="IToastNotificationStatusService"/> of a composite product.
/// </summary>
/// <remarks>
/// The composite product shows no notification of its own detection, because the builds of each product show theirs.
/// It pauses them, though, while the setup wizard of the extension is open, so <see cref="PauseAll"/> pauses the
/// notifications of every product. The other members answer for the notifications that the extension shows, which
/// are those of the primary member.
/// </remarks>
internal sealed class CompositeToastNotificationStatusService : IToastNotificationStatusService
{
    private readonly IToastNotificationStatusService _primary;
    private readonly CompositeProductMembers _members;

    public CompositeToastNotificationStatusService( IToastNotificationStatusService primary, CompositeProductMembers members )
    {
        this._primary = primary;
        this._members = members;
    }

    public bool TryAcquire( ToastNotificationKind kind ) => this._primary.TryAcquire( kind );

    public void Snooze( ToastNotificationKind kind ) => this._primary.Snooze( kind );

    public void Mute( ToastNotificationKind kind ) => this._primary.Mute( kind );

    public IDisposable PauseAll( TimeSpan timeSpan )
    {
        var cookies = new List<IDisposable> { this._primary.PauseAll( timeSpan ) };

        // An inner provider has no user interface, so the status service of another member is created over its store.
        this._members.Members.Skip( 1 )
            .ForEachContinueOnException( m => cookies.Add( new ToastNotificationStatusService( m.ServiceProvider ).PauseAll( timeSpan ) ) );

        return new DisposableAction( () => cookies.ForEachContinueOnException( c => c.Dispose() ) );
    }

    public DateTime? LastNotificationTime => this._primary.LastNotificationTime;

    public bool CanDisplayLowPriorityNotifications => this._primary.CanDisplayLowPriorityNotifications;
}
