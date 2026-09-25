# Platform tests

The platform tests run the platform-specific code of the Backstage packages on the platform itself: Linux and Windows in
a container, and macOS on the agent. The unit tests in `src/tests` replace the operating system with fakes and run on
Windows only, so they cannot tell whether `/proc`, `ps`, `lsof`, `ioreg`, a named mutex or a file mode behaves as the
code expects.

## What belongs here

A test belongs here only when all of the following conditions hold:

- The code under test has a branch per operating system, architecture or container, or it calls a facility of the
  operating system.
- The unit tests cannot reach that code, because they replace the facility with a fake.
- The test asserts what depends on the platform, and nothing else. It does not test the behaviour of .NET itself.

Everything else belongs to the unit tests: timeouts, retry policies, parsing, configuration and licensing rules. When a
platform test needs a parsing function, the function is extracted and tested by the unit tests as well, and the platform
test checks only that the real platform produces what the function parses. Windows code already runs in the unit tests
on a Windows host. The Windows container runs only the behaviour that differs inside a container.

Keeping this rule keeps the two test suites from testing the same thing twice.

## How the tests consume the product

The tests reference the packages of the build through `PackageReference`, as an application does. The version is
`BackstageVersion`, from `artifacts/publish/private/Backstage.version.props`. The directory blocks the
`Directory.Build.*`, `Directory.Packages.props` and `global.json` files of the repository.

## Layout

| Path | Content |
|---|---|
| `PlatformTests/` | The tests. One namespace per suite. |
| `PlatformTestHelper/` | The second process of the cross-process tests. |
| `PlatformTests/Conditions/` | `[PlatformFact]`, which selects the operating systems and the kinds of host of a test. |
| `Docker/<Suite>/` | One Docker test per suite: `test.psd1` gives its platforms, and `RunTest.ps1` starts its container. |
| `Images/<os>/Dockerfile` | The image of each operating system. |
| `RunSuite.ps1` | Builds and runs the tests of a suite where it runs, inside a container or on macOS. |
| `RunMacOSTests.ps1` | Runs every suite on a macOS agent. |

A test declares where it runs with `[PlatformFact( TestPlatforms.Unix )]` or
`[PlatformFact( TestPlatforms.All, TestHosts.Container )]`. The kind of host comes from the variable
`BACKSTAGE_PLATFORM_TEST_HOST`, which the launchers set, because detecting it is part of the code under test.

## Running the tests

Build the product first with `Build.ps1 build`, which produces the packages, `nuget.config` and `nuget.wsl.config`.

```powershell
# Every suite in a Linux container, through the engine inside WSL on a Windows machine.
pwsh ./eng/RunDockerTests.ps1 -Platform linux-x64

# One suite.
pwsh ./eng/RunDockerTests.ps1 -Platform linux-x64 -Test NamedLocks

# Every suite on macOS.
pwsh ./src/tests/Platform/RunMacOSTests.ps1
```

The process management suite ends every process that the product recognizes on the machine, including the compiler
server and MSBuild nodes. Run it in a container, or on an agent that runs one build at a time.
