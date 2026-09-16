# SharpCrafters.Foundations

Foundation libraries shared by the Metalama and PostSharp products. The repository produces the
`SharpCrafters.Foundations` NuGet packages.

The product name inside the build system and on TeamCity is `Foundations`; the repository and the packages are named
`SharpCrafters.Foundations`.

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
