The `SharpCrafters.Backstage.Vsx.Tools` package is not meant to be referenced in user projects.

It carries the Windows notifier of the Visual Studio Tools for PostSharp and Metalama, `SharpCrafters.Backstage.Vsx.Windows`, as a zipped executable. The notifier shows the toast notifications of the extension under the name and the logo of the extension. The extension extracts it on the machine of the user when it is needed.

The extension keeps the worker of Metalama, which `Metalama.Backstage.Tools` carries. `BackstageToolsExtensions.AddTools` registers an extractor that takes each tool from the right package.
