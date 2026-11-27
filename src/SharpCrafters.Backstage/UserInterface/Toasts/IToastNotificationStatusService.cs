// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using System;

namespace SharpCrafters.Backstage.UserInterface.Toasts;

public interface IToastNotificationStatusService : IBackstageService
{
    /// <summary>
    /// Tries to acquire the right to display a notification, and updates the snooze period.
    /// </summary>
    bool TryAcquire( ToastNotificationKind kind );

    void Snooze( ToastNotificationKind kind );

    /// <summary>
    /// Mutes a kind of notification for good. A kind that cannot be muted is not affected.
    /// </summary>
    /// <remarks>
    /// Muting <see cref="ToastNotificationKinds.RequiresLicense"/> is also how the user tells that the license key is in
    /// the source code, for instance in an MSBuild property, so that no license is registered on the machine.
    /// </remarks>
    void Mute( ToastNotificationKind kind );

    /// <summary>
    /// Gets a value indicating whether a kind of notification has been muted by <see cref="Mute"/>. A kind that cannot be
    /// muted is never muted, whatever the configuration stores.
    /// </summary>
    bool IsMuted( ToastNotificationKind kind );

    /// <summary>
    /// Pause all notifications. This is used when the VSX setup wizard is scheduled or open, and
    /// should take care of activation. This method returns a cookie that must be disposed to resume operations.
    /// </summary>
    /// <param name="timeSpan">The <see cref="TimeSpan"/> during which notifications should be paused.</param>
    IDisposable PauseAll( TimeSpan timeSpan );

    DateTime? LastNotificationTime { get; }

    bool CanDisplayLowPriorityNotifications { get; }
}