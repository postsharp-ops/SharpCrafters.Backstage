// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.UserInterface.Rss;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Commands.Tests.Commands.Configuration
{
    /// <summary>
    /// Tests the command that writes every configuration as one JSON document.
    /// </summary>
    /// <remarks>
    /// What the command is for is comparing what the product reads with a store read by other means, so the document
    /// has to be machine-readable and has to carry the values themselves. These tests therefore parse the output
    /// rather than look for words in it: output that is not valid JSON would pass a substring check and be useless.
    /// </remarks>
    public sealed class DumpConfigurationCommandTests : CommandsTestsBase
    {
        public DumpConfigurationCommandTests( ITestOutputHelper logger ) : base( logger ) { }

        /// <summary>
        /// Runs the command and parses what it wrote.
        /// </summary>
        private async Task<JsonElement> DumpAsync( string commandLine )
        {
            var output = await this.GetCommandOutputAsync( commandLine );

            try
            {
                // The console may wrap or decorate a table; this output is a document, so it must survive being parsed.
                using var document = JsonDocument.Parse( output );

                return document.RootElement.Clone();
            }
            catch ( JsonException e )
            {
                // What the command wrote is the evidence, so it goes in the message rather than being left to be
                // guessed at from a position in a stream that nobody can see.
                throw new InvalidOperationException( $"'{commandLine}' did not write JSON ({e.Message}). It wrote:{Environment.NewLine}{output}", e );
            }
        }

        private static JsonElement Configurations( JsonElement dump ) => dump.GetProperty( "configurations" );

        [Fact]
        public async Task TheDumpIsValidJson()
        {
            var dump = await this.DumpAsync( "config dump" );

            Assert.Equal( JsonValueKind.Object, dump.ValueKind );
            Assert.Equal( JsonValueKind.Object, Configurations( dump ).ValueKind );
        }

        /// <summary>
        /// The configurations are keyed by alias, so a reader asks for the one it wants by name instead of searching
        /// a list for it.
        /// </summary>
        [Fact]
        public async Task TheConfigurationsAreKeyedByAlias()
        {
            var configurations = Configurations( await this.DumpAsync( "config dump" ) );

            var aliases = configurations.EnumerateObject().Select( p => p.Name ).ToArray();

            Assert.Contains( "telemetry", aliases );
            Assert.Contains( "diagnostics", aliases );
            Assert.Contains( "licensing", aliases );

            // Each one is the configuration itself, and not an entry describing it.
            Assert.Equal( JsonValueKind.Object, configurations.GetProperty( "rss" ).ValueKind );
            Assert.False( configurations.GetProperty( "rss" ).TryGetProperty( "alias", out _ ) );
            Assert.False( configurations.GetProperty( "rss" ).TryGetProperty( "store", out _ ) );
            Assert.False( configurations.GetProperty( "rss" ).TryGetProperty( "value", out _ ) );
        }

        /// <summary>
        /// Every line of a configuration is indented under its key, rather than starting at the margin.
        /// </summary>
        /// <remarks>
        /// A configuration is serialized on its own, with its own indentation, and copying that in verbatim produced
        /// a document that parsed but could not be read: each value opened at its key and then fell back to column
        /// zero. The values themselves are unchanged, so this is about nothing but whitespace — which is the reason
        /// it is worth a test, because nothing else would fail if it came back.
        /// </remarks>
        [Fact]
        public async Task TheConfigurationsAreIndentedUnderTheirKey()
        {
            var output = await this.GetCommandOutputAsync( "config dump telemetry" );

            var lines = output.Split( '\n' )
                .Select( line => line.TrimEnd( '\r' ) )
                .Where( line => line.Length > 0 )
                .ToArray();

            // "configurations" sits at one level, its one key at two, and the members of that configuration at three.
            var configurationsLine = Assert.Single( lines, l => l.Contains( "\"configurations\"", StringComparison.Ordinal ) );
            var aliasLine = Assert.Single( lines, l => l.Contains( "\"telemetry\"", StringComparison.Ordinal ) );

            var indent = new Func<string, int>( line => line.Length - line.TrimStart( ' ' ).Length );

            Assert.Equal( 2, indent( configurationsLine ) );
            Assert.Equal( 4, indent( aliasLine ) );

            // Nothing inside a configuration is at the margin or shallower than its key.
            foreach ( var line in lines )
            {
                if ( line is "{" or "}" )
                {
                    continue;
                }

                Assert.True( indent( line ) >= 2, $"This line is not indented: '{line}'." );
            }
        }

        [Fact]
        public async Task TheConfigurationsAreSortedByAlias()
        {
            var aliases = Configurations( await this.DumpAsync( "config dump" ) ).EnumerateObject().Select( p => p.Name ).ToArray();

            Assert.Equal( aliases.OrderBy( a => a, StringComparer.Ordinal ).ToArray(), aliases );
        }

        /// <summary>
        /// The values are what the product reads, so a change made through the configuration manager shows in the
        /// dump. A dump that reported defaults would compare equal to a store it had never read.
        /// </summary>
        [Fact]
        public async Task TheValuesAreTheOnesTheProductReads()
        {
            this.ConfigurationManager!.Update<RssClientConfiguration>( c => c with { PreferredFeed = RssFeed.Posts } );

            var rss = Configurations( await this.DumpAsync( "config dump" ) ).GetProperty( "rss" );

            // The value is whatever the configuration serializes to, which for this enumeration is its number. The
            // dump reproduces what 'config print' writes rather than reformatting it.
            Assert.Equal( (int) RssFeed.Posts, rss.GetProperty( "PreferredFeed" ).GetInt32() );
        }

        /// <summary>
        /// The licensing configurations are dumped although the other <c>config</c> commands do not offer them: they
        /// are the ones that share a store with an earlier version, so they are the ones worth comparing.
        /// </summary>
        [Fact]
        public async Task TheLicensingConfigurationsAreDumped()
        {
            this.ConfigurationManager!.Update<LicensingConfiguration>(
                c => c with { Licenses = ImmutableArray.Create<string?>( "a-license-key" ) } );

            var configurations = Configurations( await this.DumpAsync( "config dump" ) );

            Assert.Equal(
                "a-license-key",
                configurations.GetProperty( "licensing" ).GetProperty( "licenses" )[0].GetString() );

            Assert.True( configurations.TryGetProperty( "audit", out _ ) );
            Assert.True( configurations.TryGetProperty( "licenseServer", out _ ) );
        }

        /// <summary>
        /// Naming a configuration dumps that one alone, which is what makes the output worth redirecting when only
        /// one store is in question.
        /// </summary>
        [Fact]
        public async Task AnAliasDumpsThatConfigurationAlone()
        {
            var configurations = Configurations( await this.DumpAsync( "config dump telemetry" ) );

            Assert.Equal( "telemetry", Assert.Single( configurations.EnumerateObject() ).Name );
        }

        /// <summary>
        /// Asking for everything is the same as asking for nothing in particular.
        /// </summary>
        [Fact]
        public async Task AllIsTheSameAsNothing()
        {
            var all = Configurations( await this.DumpAsync( "config dump --all" ) ).EnumerateObject().Select( p => p.Name );
            var nothing = Configurations( await this.DumpAsync( "config dump" ) ).EnumerateObject().Select( p => p.Name );

            Assert.Equal( nothing, all );
        }

        [Fact]
        public async Task AnAliasThatDoesNotExistIsRefusedWithTheOnesThatDo()
            => await this.TestCommandAsync( "config dump nonsense", "Invalid configuration alias", expectedExitCode: 1 );

        [Fact]
        public async Task AnAliasAndAllTogetherAreRefused()
            => await this.TestCommandAsync( "config dump telemetry --all", "not both", expectedExitCode: 1 );
    }
}
