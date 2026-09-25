# The Windows image of the platform tests: Windows Server 2025 with the .NET 10 SDK, which includes PowerShell. The build
# agents run Windows Server 2025, so the image runs with process isolation.
#
# Nothing is copied in. DockerBuild.ps1 mounts the repository, and the tests build where they are, so the paths inside the
# container are the paths on the host.

FROM mcr.microsoft.com/dotnet/sdk:10.0-windowsservercore-ltsc2025
