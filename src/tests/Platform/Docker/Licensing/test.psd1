@{
    # Windows runs this code in the unit tests on a Windows host, and it does not behave differently in a container.
    Platforms = @( 'linux-x64', 'linux-arm64' )

    TimeoutSeconds = 1800
}
