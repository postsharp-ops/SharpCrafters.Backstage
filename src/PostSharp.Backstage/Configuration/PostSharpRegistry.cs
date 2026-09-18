// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;

namespace PostSharp.Backstage.Configuration;

/// <summary>
/// The keys and the value names under which PostSharp keeps its settings in the Windows registry.
/// </summary>
/// <remarks>
/// <para>
/// None of these may change. PostSharp 2026.0 reads and writes the same names beside this version, and a name that
/// drifts is not a defect this version notices: it is a setting that the other version silently stops seeing.
/// </para>
/// <para>
/// The product version in the key is the literal <c>3</c>. It has been frozen since PostSharp 3.x and is not the
/// release version of anything, so it must not be derived from one.
/// </para>
/// </remarks>
[PublicAPI]
public static class PostSharpRegistry
{
    /// <summary>
    /// The key that holds the settings of the user, and, under <c>HKEY_LOCAL_MACHINE</c>, those an administrator
    /// sets for every user of the machine.
    /// </summary>
    public const string RootKeyPath = @"Software\SharpCrafters\PostSharp 3";

    /// <summary>
    /// The key that holds the telemetry settings.
    /// </summary>
    public const string FeedbackKeyPath = RootKeyPath + @"\Feedback";

    /// <summary>
    /// The value that holds the one license key that PostSharp 3.0 could read. PostSharp 2026.0 still reads it and
    /// only ever deletes it.
    /// </summary>
    public const string LegacyLicenseValueName = "LicenseKey";

    /// <summary>
    /// The sub-key whose values are the registered license keys. Their names are the decimal indices <c>0</c>,
    /// <c>1</c> and so on, and a sub-key of it named after a version holds the keys that require that version.
    /// </summary>
    public const string LicenseKeysKeyName = "LicenseKeys";

    /// <summary>
    /// The value that holds the date on which the trial started.
    /// </summary>
    public const string EvaluationValueName = "Evaluation";

    /// <summary>
    /// The value that PostSharp 2026.0 watches to learn that the registered licenses have changed. Writing it is
    /// what makes a license registered by this version visible to a running instance of that one.
    /// </summary>
    public const string LicenseTimestampValueName = "LicenseTimestamp";

    /// <summary>
    /// The value that silences the warning about a license server reached over an insecure address. PostSharp
    /// 2026.0 has no such value and reads an MSBuild property instead, so it ignores this one.
    /// </summary>
    public const string AllowInsecureLicenseServerValueName = "AllowInsecureLicenseServer";

    /// <summary>
    /// The value that records why a community license was registered. PostSharp 2026.0 has no such value.
    /// </summary>
    public const string CommunityLicenseReasonValueName = "CommunityLicenseReason";

    /// <summary>
    /// The value that counts the writes made to a configuration object. PostSharp 2026.0 has no such value and
    /// ignores it.
    /// </summary>
    public const string ConfigurationVersionValueName = "ConfigurationVersion";
}
