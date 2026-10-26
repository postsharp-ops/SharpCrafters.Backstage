// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.IO;

namespace SharpCrafters.Aspects.Introspection.Query.Tests.Commands;

/// <summary>
/// A project in a directory of its own, with the file of its last build.
/// </summary>
internal sealed class TestProject : IDisposable
{
    public const string SymbolsNamespace = "http://schemas.postsharp.org/2.0/symbols";

    /// <summary>
    /// A file like the one PostSharp writes for a project that uses NotifyPropertyChanged, a logging aspect multicast from the
    /// assembly, and a caching aspect that the licence does not cover.
    /// </summary>
    public const string DefaultSymbols = $"""
                                          <Symbols xmlns="{SymbolsNamespace}" PostSharpVersion="2027.0.6" LinesOfCode="120">
                                            <Evolution Source="F:[App]App.Customer::&lt;FirstName&gt;k__BackingField" Target="F:[App]App.Customer::&lt;FirstName&gt;k__OriginalField" />
                                            <Class Class="T:[Lib]Lib.NotifyPropertyChangedAttribute">
                                              <Instance Declaration="T:[App]App.Customer" Id="1">
                                                <Target>
                                                  <JoinPoint Advised="M:[App]App.Customer::get_FullName()" Description="Activates notifications for the property" Semantic="Getter" Ordinal="3" />
                                                  <JoinPoint Advised="M:[App]App.Customer::set_FirstName(System.String)" Description="Enqueues change notifications" Semantic="Setter" Ordinal="4" />
                                                </Target>
                                              </Instance>
                                            </Class>
                                            <Class Class="T:[App]App.LogAttribute">
                                              <Instance Declaration="A:App" Id="2">
                                                <Target Target="M:[App]App.Repository::Get(System.Int32)" LinesAvoided="2"><JoinPoint Description="Wrapped by OnEntry" Ordinal="5" /></Target>
                                                <Target Target="M:[App]App.Repository::Save([App]App.Customer)"><JoinPoint Ordinal="6" /></Target>
                                              </Instance>
                                              <Instance Declaration="M:[App]App.Customer::get_FullName()"><Target><JoinPoint Ordinal="2" /></Target></Instance>
                                            </Class>
                                            <Class Class="T:[App]App.CacheAttribute">
                                              <Instance Declaration="M:[App]App.Repository::Get(System.Int32)"><Target Tags="premium"><JoinPoint Ordinal="1" /></Target></Instance>
                                            </Class>
                                            <Annotation Target="P:[App]App.Customer::FullName()" Description="The FullName property depends on FirstName." />
                                          </Symbols>
                                          """;

    public TestProject(
        string? symbols = DefaultSymbols,
        string projectName = "App",
        string targetFrameworks = "<TargetFramework>net10.0</TargetFramework>",
        bool setProperty = true,
        string? directory = null,
        string assemblyName = "App" )
    {
        this.AssemblyName = assemblyName;
        this.Directory = directory ?? Path.Combine( Path.GetTempPath(), "introspection-tests", Guid.NewGuid().ToString( "N" ) );
        System.IO.Directory.CreateDirectory( this.Directory );

        var property = setProperty ? "<TestSymbolFile>bin/$(Configuration)/$(TargetFramework)/$(AssemblyName).pssym</TestSymbolFile>" : "";

        // An SDK-style project, which is evaluated and never built or restored.
        File.WriteAllText(
            Path.Combine( this.Directory, projectName + ".csproj" ),
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 {targetFrameworks}
                 <AssemblyName>{assemblyName}</AssemblyName>
                 {property}
               </PropertyGroup>
             </Project>
             """ );

        this.SourceFile = Path.Combine( this.Directory, "Customer.cs" );
        File.WriteAllText( this.SourceFile, "namespace App { public class Customer { } }" );

        // The source is older than the file, which a build writes after it.
        File.SetLastWriteTimeUtc( this.SourceFile, DateTime.UtcNow.AddMinutes( -10 ) );
        File.SetLastWriteTimeUtc( Path.Combine( this.Directory, projectName + ".csproj" ), DateTime.UtcNow.AddMinutes( -10 ) );

        if ( symbols != null )
        {
            this.WriteSymbols( "net10.0", symbols );
        }
    }

    public string Directory { get; }

    public string AssemblyName { get; }

    public string SourceFile { get; }

    public string WriteSymbols( string targetFramework, string symbols )
    {
        var path = Path.Combine( this.Directory, "bin", "Debug", targetFramework, this.AssemblyName + ".pssym" );
        System.IO.Directory.CreateDirectory( Path.GetDirectoryName( path )! );
        File.WriteAllText( path, symbols );

        return path;
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete( this.Directory, true );
        }
        catch ( IOException )
        {
            // A file locked by an evaluation that has not released it yet; the temporary directory is cleaned up later.
        }
    }
}
