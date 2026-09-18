namespace Nexora.Configuration;

using System.Text.RegularExpressions;

/// <summary>
/// Application identity, tool file names, and validation helpers.
/// Operational settings live in strongly-typed options classes:
/// <see cref="GameLoopOptions"/>, <see cref="EmulatorOptions"/>, <see cref="UpdateOptions"/>.
/// </summary>
public static class AppConstants
{
    public const string ApplicationName = "Nexora PUBG Mobile Tool";

    /// <summary>
    /// Current release tag matching the project version in Nexora.csproj.
    /// </summary>
    public const string CurrentVersion = "v1.1.0";

    public static class Tools
    {
        public const string TaskkillFileName = "taskkill.exe";
    }

    public static class Validation
    {
        /// <summary>
        /// Regular expression pattern for valid Android package names.
        /// </summary>
        public const string AndroidPackageNamePattern = @"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$";

        private static readonly Regex AndroidPackageNameRegex = new(AndroidPackageNamePattern, RegexOptions.Compiled);

        public static bool IsValidAndroidPackageName(string? value) =>
            !string.IsNullOrWhiteSpace(value) && AndroidPackageNameRegex.IsMatch(value);
    }
}
