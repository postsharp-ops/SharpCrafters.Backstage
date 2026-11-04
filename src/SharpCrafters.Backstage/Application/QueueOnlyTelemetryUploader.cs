// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Telemetry;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Application;

/// <summary>
/// The telemetry uploader of an inner service provider of a composite product. It uploads nothing, so that a report of
/// a secondary product stays in the upload queue of that product, and the outer provider runs the only upload of the
/// process.
/// </summary>
internal sealed class QueueOnlyTelemetryUploader : ITelemetryUploader
{
    public static QueueOnlyTelemetryUploader Instance { get; } = new();

    private QueueOnlyTelemetryUploader() { }

    public bool StartUpload( bool force = false ) => false;

    public Task UploadAsync() => Task.CompletedTask;
}
