// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Diagnostics;
using System.Text;

namespace SharpCrafters.Backstage.PlatformTests;

/// <summary>
/// A running instance of the helper process. A test synchronizes with it through the lines of its standard input and
/// output, never through a fixed delay.
/// </summary>
internal sealed class HelperProcess : IDisposable
{
    private const string _assemblyName = "SharpCrafters.Backstage.PlatformTestHelper";

    /// <summary>
    /// The longest time a test waits for the helper. It bounds a test that would otherwise hang; a passing test does not
    /// wait for it.
    /// </summary>
    public static TimeSpan Timeout { get; } = TimeSpan.FromMinutes( 2 );

    private readonly StringBuilder _standardError = new();

    private HelperProcess( ProcessStartInfo startInfo )
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        this.Process = Process.Start( startInfo ) ?? throw new InvalidOperationException( $"Cannot start '{startInfo.FileName}'." );

        this.Process.ErrorDataReceived += ( _, e ) =>
        {
            if ( e.Data != null )
            {
                lock ( this._standardError )
                {
                    this._standardError.AppendLine( e.Data );
                }
            }
        };

        this.Process.BeginErrorReadLine();
    }

    /// <summary>
    /// Gets the path of the helper assembly, which the output directory of the tests holds.
    /// </summary>
    public static string AssemblyPath => Path.Combine( AppContext.BaseDirectory, _assemblyName + ".dll" );

    /// <summary>
    /// Gets the path of the apphost of the helper, which runs the helper under its own name instead of <c>dotnet</c>.
    /// </summary>
    public static string AppHostPath => Path.Combine( AppContext.BaseDirectory, _assemblyName + (OperatingSystem.IsWindows() ? ".exe" : "") );

    public Process Process { get; }

    /// <summary>
    /// Starts <c>dotnet &lt;assembly&gt; &lt;arguments&gt;</c>. The assembly is the helper unless <paramref name="assemblyPath"/>
    /// names a copy of it.
    /// </summary>
    public static HelperProcess StartAssembly( string? assemblyPath, params string[] arguments )
    {
        var startInfo = new ProcessStartInfo( DotNetHost.ExecutablePath );
        startInfo.ArgumentList.Add( assemblyPath ?? AssemblyPath );

        foreach ( var argument in arguments )
        {
            startInfo.ArgumentList.Add( argument );
        }

        return new HelperProcess( startInfo );
    }

    public static HelperProcess Start( params string[] arguments ) => StartAssembly( null, arguments );

    /// <summary>
    /// Starts an apphost of the helper, which is <see cref="AppHostPath"/> or a copy of it under another name.
    /// </summary>
    public static HelperProcess StartAppHost( string appHostPath, params string[] arguments )
    {
        var startInfo = new ProcessStartInfo( appHostPath );

        foreach ( var argument in arguments )
        {
            startInfo.ArgumentList.Add( argument );
        }

        return new HelperProcess( startInfo );
    }

    /// <summary>
    /// Reads the standard output of the helper until a line equal to <paramref name="expectedLine"/>, and returns the lines
    /// before it.
    /// </summary>
    public async Task<IReadOnlyList<string>> ReadUntilAsync( string expectedLine )
    {
        using var cancellation = new CancellationTokenSource( Timeout );
        var lines = new List<string>();

        while ( true )
        {
            string? line;

            try
            {
                line = await this.Process.StandardOutput.ReadLineAsync( cancellation.Token );
            }
            catch ( OperationCanceledException )
            {
                throw new TimeoutException( this.Describe( $"The helper did not write '{expectedLine}' within {Timeout}." ) );
            }

            if ( line == null )
            {
                await this.Process.WaitForExitAsync( cancellation.Token );

                throw new InvalidOperationException(
                    this.Describe( $"The helper exited with code {this.Process.ExitCode} before writing '{expectedLine}'." ) );
            }

            if ( line == expectedLine )
            {
                return lines;
            }

            lines.Add( line );
        }
    }

    /// <summary>
    /// Writes a line to the standard input of the helper, which is the signal that a waiting command waits for.
    /// </summary>
    public void Signal()
    {
        this.Process.StandardInput.WriteLine();
        this.Process.StandardInput.Flush();
    }

    public void Kill()
    {
        this.Process.Kill( entireProcessTree: true );
        this.Process.WaitForExit();
    }

    public async Task WaitForExitAsync()
    {
        using var cancellation = new CancellationTokenSource( Timeout );

        try
        {
            await this.Process.WaitForExitAsync( cancellation.Token );
        }
        catch ( OperationCanceledException )
        {
            throw new TimeoutException( this.Describe( $"The helper did not exit within {Timeout}." ) );
        }
    }

    private string Describe( string message )
    {
        lock ( this._standardError )
        {
            return this._standardError.Length == 0 ? message : $"{message} Its standard error was:{Environment.NewLine}{this._standardError}";
        }
    }

    public void Dispose()
    {
        if ( !this.Process.HasExited )
        {
            this.Kill();
        }

        this.Process.Dispose();
    }
}
