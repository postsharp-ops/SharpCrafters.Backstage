The `SharpCrafters.Backstage.Profiling` package is not meant to be referenced in user projects.

It contains the optional profiling feature of Backstage, which drives the JetBrains dotTrace and dotMemory command
line tools from inside a product process, under the control of the `profiling` section of `diagnostics.json`.

It is separate from [SharpCrafters.Backstage](https://www.nuget.org/packages/SharpCrafters.Backstage) because it is
the only part of Backstage that needs `JetBrains.Profiler.SelfApi`, and that package brings four assemblies that
every consumer would otherwise carry and publish. No product references this package today.
