The `SharpCrafters.Backstage.Vsx.Tools` package is not meant to be referenced in user projects.

It carries the tools of the Visual Studio Tools for PostSharp and Metalama as zipped executables, which the extension extracts on the machine of the user when they are needed:

- `SharpCrafters.Backstage.Vsx.Worker`, which uploads the telemetry of the extension and serves the web pages that its toast notifications open;
- `SharpCrafters.Backstage.Vsx.Windows`, the Windows notifier, which shows the toast notifications of the extension under the name and the logo of the extension.

`BackstageToolsExtensions.AddTools` registers the extractor of these tools. The extension does not need `Metalama.Backstage.Tools` or `PostSharp.Backstage.Tools`.
