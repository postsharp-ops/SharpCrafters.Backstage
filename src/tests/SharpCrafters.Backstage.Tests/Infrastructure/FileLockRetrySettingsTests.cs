// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Testing;
using System;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Infrastructure;

/// <summary>
/// The user sets how long a locked file is waited for, and when they are told about it, in the <c>FILE_LOCK_TIMEOUT</c>
/// and <c>FILE_LOCK_WARNING</c> environment variables of the product.
/// </summary>
public sealed class FileLockRetrySettingsTests
{
    [Theory]
    [InlineData( null, null )]
    [InlineData( "", null )]
    [InlineData( "  ", null )]
    [InlineData( "abc", null )]
    [InlineData( "0", null )]
    [InlineData( "-5", null )]
    [InlineData( "1.5", null )]
    [InlineData( "99999999999", null )]
    [InlineData( "1", 1 )]
    [InlineData( " 2000 ", 2000 )]
    public void TheTimeoutIsAtLeastOneMillisecond( string? value, int? expectedMilliseconds )
    {
        Assert.Equal( ToTimeSpan( expectedMilliseconds ), FileLockRetrySettings.ParseMilliseconds( value, 1 ) );
    }

    [Theory]
    [InlineData( null, null )]
    [InlineData( "-1", null )]
    [InlineData( "0", 0 )]
    [InlineData( "20000", 20000 )]
    public void TheWarningThresholdCanBeZero( string? value, int? expectedMilliseconds )
    {
        Assert.Equal( ToTimeSpan( expectedMilliseconds ), FileLockRetrySettings.ParseMilliseconds( value, 0 ) );
    }

    [Fact]
    public void TheVariablesHaveThePrefixOfTheProduct()
    {
        var environment = new TestEnvironmentVariableProvider();
        environment.Environment["TEST_FILE_LOCK_TIMEOUT"] = "30000";
        environment.Environment["TEST_FILE_LOCK_WARNING"] = "0";
        environment.Environment["FILE_LOCK_TIMEOUT"] = "1";

        var settings = new FileLockRetrySettings( new ServiceProvider( environment ) );

        Assert.Equal( TimeSpan.FromSeconds( 30 ), settings.Timeout );
        Assert.Equal( TimeSpan.Zero, settings.WarningThreshold );
    }

    [Fact]
    public void UnsetVariablesKeepTheValuesOfTheCaller()
    {
        var settings = new FileLockRetrySettings( new ServiceProvider( new TestEnvironmentVariableProvider() ) );

        Assert.Null( settings.Timeout );
        Assert.Null( settings.WarningThreshold );
    }

    private static TimeSpan? ToTimeSpan( int? milliseconds ) => milliseconds == null ? null : TimeSpan.FromMilliseconds( milliseconds.Value );

    private sealed class ServiceProvider : IServiceProvider
    {
        private static readonly ProductProfile _productProfile = new( "Test", "Test", "Test", "TEST_", @"Global\Test_", "TestLicense", "Test" );

        private readonly IEnvironmentVariableProvider _environment;

        public ServiceProvider( IEnvironmentVariableProvider environment )
        {
            this._environment = environment;
        }

        public object? GetService( Type serviceType )
            => serviceType == typeof(ProductProfile) ? _productProfile
                : serviceType == typeof(IEnvironmentVariableProvider) ? this._environment
                : null;
    }
}
