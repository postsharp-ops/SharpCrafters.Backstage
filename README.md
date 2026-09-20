# SharpCrafters.Backstage

Infrastructure services shared by the Metalama and PostSharp products: configuration, diagnostics, telemetry,
licensing and user interface, with a customization package per product.

The product name inside the build system and on TeamCity is `Backstage`. The repository is named
`SharpCrafters.Backstage`, the neutral packages `SharpCrafters.Backstage*` and `SharpCrafters.Common`, and the product
customizations `Metalama.Backstage*` and, later, `PostSharp.Backstage*`.

## Packages

| Package | Contents |
|---|---|
| `SharpCrafters.Common` | The hooks that production code exposes for deterministic testing. It has no dependency |
| `SharpCrafters.Backstage.Infrastructure` | Named locks, the detection of the processes holding a file, the runtime information, the detection of an unattended process and of a continuous integration server, the process kinds, and the contracts of the services those rest on. Its only dependency is `SharpCrafters.Common` |
| `SharpCrafters.Backstage` | The services: dependency injection, logging, configuration, the file system, telemetry, licensing and user interface |
| `SharpCrafters.Backstage.Commands` | The command line over those services |
| `Metalama.Backstage`, `PostSharp.Backstage` | What each product family adds: its profile, its licence catalogue, its web links and its telemetry endpoints |

A type belongs in `SharpCrafters.Backstage.Infrastructure` when a build tool loaded by MSBuild needs it. Such a tool
is merged into a single assembly and can take no dependency, so that package must keep having none beyond
`SharpCrafters.Common`. A type that needs the logging implementation, the configuration or the standard directories
belongs in `SharpCrafters.Backstage`.

A few of those source files are also compiled by projects that can reference nothing at all, not even a package. They
travel in the `shared\` folder of the infrastructure package, and `SharedSourcesProbe` compiles them the way such a
project does, so that the form they take outside this repository is checked by the build.

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
