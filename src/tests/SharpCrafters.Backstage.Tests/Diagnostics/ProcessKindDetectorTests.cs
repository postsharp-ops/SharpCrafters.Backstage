// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Infrastructure.ProcessClassification;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Diagnostics;

/// <summary>
/// The classification of a process is a pure function of its name and its command line, so every arm of the table
/// can be exercised without the corresponding process existing.
/// </summary>
/// <remarks>
/// What the kind decides is which section of <c>diagnostics.json</c> applies to the running process: whether it
/// logs, whether it launches a debugger, and whether it writes a crash dump. A host that is classified as
/// something else silently ignores the settings a user wrote for it, and a host that takes another product's kind
/// obeys settings that were not meant for it.
/// </remarks>
public sealed class ProcessKindDetectorTests
{
    [Theory]

    // The native hosts of the PostSharp compiler, one per processor architecture.
    [InlineData( "postsharp-x64", "", ProcessKind.PostSharpCompiler )]
    [InlineData( "postsharp-x86", "", ProcessKind.PostSharpCompiler )]
    [InlineData( "postsharp-arm64", "", ProcessKind.PostSharpCompiler )]

    // The pipe server. Its name begins with the name of the compiler that serves the same architecture, which is
    // why the suffix is tested before the prefix.
    [InlineData( "postsharp-x64-srv", "", ProcessKind.PostSharpPipeServer )]
    [InlineData( "postsharp-x86-srv", "", ProcessKind.PostSharpPipeServer )]
    [InlineData( "postsharp-arm64-srv", "", ProcessKind.PostSharpPipeServer )]

    // The hosts of earlier versions carry the framework in the name as well. A user who reports a problem may be
    // running one of them, and a report that says Other names nothing.
    [InlineData( "postsharp-net40-x64", "", ProcessKind.PostSharpCompiler )]
    [InlineData( "postsharp-net40-x64-srv", "", ProcessKind.PostSharpPipeServer )]

    // The PostSharp compiler of the .NET build has no native host: the MSBuild task starts it as an assembly, so
    // the command line is what identifies it.
    [InlineData(
        "dotnet",
        @"C:\dotnet\dotnet.exe C:\Users\x\.nuget\packages\postsharp\2027.0.0\tools\net10.0\PostSharp.Compiler.Hosting.CommandLine.dll /X:project.psproj",
        ProcessKind.PostSharpCompiler )]

    // The name is matched case insensitively, because the case a process name is reported in is the case of the
    // file on disk.
    [InlineData( "PostSharp-X64", "", ProcessKind.PostSharpCompiler )]
    public void PostSharpHostsAreRecognized( string processName, string commandLine, ProcessKind expected )
    {
        Assert.Equal( expected, ProcessKindDetector.GetProcessKind( processName, commandLine ) );
    }

    /// <summary>
    /// The Metalama compiler and the PostSharp compiler are separate kinds. A developer may have both products on
    /// one machine, and a setting written for one of them must not turn logging or a crash dump on for the other.
    /// </summary>
    [Theory]
    [InlineData( "csc", "" )]
    [InlineData( "vbcscompiler", "" )]
    [InlineData( "dotnet", @"C:\dotnet\dotnet.exe C:\sdk\Roslyn\bincore\csc.dll @rsp" )]
    public void TheMetalamaCompilerKeepsItsOwnKind( string processName, string commandLine )
    {
        Assert.Equal( ProcessKind.Compiler, ProcessKindDetector.GetProcessKind( processName, commandLine ) );
    }

    /// <summary>
    /// A name that merely begins with the product name is not one of our hosts. The prefix that the arm matches
    /// ends with the separator for this reason.
    /// </summary>
    [Theory]
    [InlineData( "postsharp", "" )]
    [InlineData( "postsharpservice", "" )]
    [InlineData( "mypostsharp-x64", "" )]
    public void OnlyTheHostNamesMatch( string processName, string commandLine )
    {
        Assert.Equal( ProcessKind.Other, ProcessKindDetector.GetProcessKind( processName, commandLine ) );
    }

    /// <summary>
    /// A sample of the arms that existed before the PostSharp ones were added, so that a change to the table is
    /// noticed here rather than in a support case about a log file that stopped appearing.
    /// </summary>
    [Theory]
    [InlineData( "devenv", "", ProcessKind.DevEnv )]
    [InlineData( "devhub", "", ProcessKind.RoslynCodeAnalysisService )]
    [InlineData( "msbuild", "", ProcessKind.MsBuild )]
    [InlineData( "testhost", "", ProcessKind.TestHost )]
    [InlineData( "linqpad8", "", ProcessKind.LinqPad )]
    [InlineData( "servicehub.host", "ServiceHub.Host.dll $codelensservice$", ProcessKind.CodeLensService )]
    [InlineData( "servicehub.host", "ServiceHub.Host.dll $othervalue$", ProcessKind.Other )]
    [InlineData( "notepad", "", ProcessKind.Other )]
    public void TheExistingArmsAreUnchanged( string processName, string commandLine, ProcessKind expected )
    {
        Assert.Equal( expected, ProcessKindDetector.GetProcessKind( processName, commandLine ) );
    }
}
