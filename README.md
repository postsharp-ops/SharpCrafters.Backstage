# SharpCrafters.Backstage

Infrastructure services shared by the Metalama and PostSharp products: configuration, diagnostics, telemetry,
licensing and user interface, with a customization package per product.

The product name inside the build system and on TeamCity is `Backstage`. The repository is named
`SharpCrafters.Backstage`, the neutral packages `SharpCrafters.Backstage*` and `SharpCrafters.Common`, and the product
customizations `Metalama.Backstage*` and, later, `PostSharp.Backstage*`.

## Packages

| Package | Contents |
|---|---|
| `SharpCrafters.Common.Abstractions` | The interfaces of the hooks that production code exposes for deterministic testing. It has no dependency |
| `SharpCrafters.Common` | The implementations of those hooks, which tests register. It depends on `SharpCrafters.Common.Abstractions` |
| `SharpCrafters.Backstage.Abstractions` | The contracts that the packages below share: the logger, the service marker interface and the environment variables. It has no dependency |
| `SharpCrafters.Backstage.Threading` | Named locks. It depends on the two abstractions packages |
| `SharpCrafters.Backstage.FileLocks` | The retry of file operations and the detection of the processes holding a file. It depends on `SharpCrafters.Backstage.Abstractions` |
| `SharpCrafters.Backstage.ProcessClassification` | The process kinds, the detection of a container and the launch of a debugger. It depends on `SharpCrafters.Backstage.Abstractions` |
| `SharpCrafters.Backstage` | The services: dependency injection, logging, configuration, the file system, the detection of an unattended process and of a continuous integration server, telemetry, licensing and user interface |
| `SharpCrafters.Backstage.Commands` | The command line over those services |
| `SharpCrafters.Backstage.Profiling` | The optional profiling feature, which needs `JetBrains.Profiler.SelfApi`. No product references it |
| `Metalama.Backstage`, `PostSharp.Backstage` | What each product family adds: its profile, its licence catalogue, its web links and its telemetry endpoints |

A type belongs in one of the packages that depend only on the abstractions when a component that starts no service
needs it, such as a build task loaded by MSBuild. Those packages must not gain a dependency on
`SharpCrafters.Backstage`. A type that needs the logging implementation, the configuration or the standard directories
belongs in `SharpCrafters.Backstage`.

## Documentation

| Document | Subject |
|---|---|
| [`docs/license-server.md`](docs/license-server.md) | The license server protocol, where a lease is stored, and when a seat is taken |
| [`docs/telemetry.md`](docs/telemetry.md) | The telemetry channels, consent, and the path of an exception report |
| [`docs/testing.md`](docs/testing.md) | The testing doctrine: what is tested where, how a test is documented, and where a fake lives |

## Building

The build entry point is `Build.ps1`, the front end of [PostSharp.Engineering](https://github.com/postsharp-ops/PostSharp.Engineering).

```powershell
.\Build.ps1 build
```

Run `.\Build.ps1 --help` for the full command set.

## Branches

| Branch | Purpose |
|---|---|
| `develop/2027.0` | Continuous development and integration |
| `release/2027.0` | Updated by deployment only |

Work happens on `topic/2027.0/XXXX-description` branches that are merged into `develop/2027.0`.

## History

The code of this repository was extracted from the `Metalama.Backstage` folder of the
[Metalama](https://github.com/metalama/Metalama) repository with its full history, using `git filter-repo`.
The branch `mirror/metalama-2026.1` is a read-only mirror of the same folder on the `develop/2026.1` branch of
Metalama, refreshed by running the same filter again. Changes made to Metalama 2026.1 reach `develop/2027.0` of this
repository by merging that mirror branch.
