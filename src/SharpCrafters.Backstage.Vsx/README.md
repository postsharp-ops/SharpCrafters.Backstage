# SharpCrafters.Backstage.Vsx

The `SharpCrafters.Backstage.Vsx` package is not meant to be directly referenced in user projects.

It is the Backstage product of the PostSharp and Metalama Visual Studio extension, which hosts the Metalama and PostSharp services of [SharpCrafters.Backstage](https://www.nuget.org/packages/SharpCrafters.Backstage) in one process. The extension passes `VsxProduct.Instance` to `BackstageInitializationOptions`.

The extension registers its tools with `SharpCrafters.Backstage.Vsx.Tools`: a worker of its own, `SharpCrafters.Backstage.Vsx.Worker`, and a desktop notifier of its own, `SharpCrafters.Backstage.Vsx.Windows`, which displays the toast notifications of the extension under its name and with the PostSharp logo.
