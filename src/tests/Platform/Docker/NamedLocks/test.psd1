@{
    Platforms = @( 'linux-x64', 'linux-arm64', 'win-x64' )

    # An hour rather than half of one, because the timeout includes acquiring the image, and the first pull of the Windows
    # SDK image on an agent is several gigabytes.
    TimeoutSeconds = 3600
}
