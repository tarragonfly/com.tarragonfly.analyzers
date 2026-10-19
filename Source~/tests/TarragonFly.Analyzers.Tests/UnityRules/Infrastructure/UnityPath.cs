#nullable enable

using System;
using System.IO;
using System.Linq;

namespace TarragonFly.Analyzers.Tests.UnityRules;

// Finds an editor's Data folder for the Unity rule tests: UNITY_EDITOR_PATH (Unity.exe or its
// Editor folder) first, then Unity Hub's install folders, newest final release first. Windows only for now,
// revisit if the tests need to run on macOS or Linux.
internal static class UnityPath
{
	private static readonly Lazy<string?> Installation = new(Find);

	public static string? FirstInstallation()
	{
		return Installation.Value;
	}

	private static string? Find()
	{
		var fromEnvironment = Environment.GetEnvironmentVariable("UNITY_EDITOR_PATH");
		if (!string.IsNullOrEmpty(fromEnvironment))
		{
			var editor = File.Exists(fromEnvironment) ? Path.GetDirectoryName(fromEnvironment)! : fromEnvironment;
			var data = Path.Combine(editor, "Data");
			if (Directory.Exists(Path.Combine(data, "Managed")))
				return data;
		}

		var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		var hubFolders = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor") }
			.Concat(ReadHubSecondaryFolder(Path.Combine(appData, "UnityHub", "secondaryInstallPath.json")));

		return hubFolders
			.Where(Directory.Exists)
			.SelectMany(Directory.EnumerateDirectories)
			// Final releases ("6000.6.6f1") before alphas and betas, then newest first.
			.OrderByDescending(path => Path.GetFileName(path).Contains('f'))
			.ThenByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
			.Select(path => Path.Combine(path, "Editor", "Data"))
			.FirstOrDefault(data => Directory.Exists(Path.Combine(data, "Managed")));
	}

	// The Hub stores its custom install folder as a JSON string, such as "C:\\GameDev".
	private static string[] ReadHubSecondaryFolder(string file)
	{
		if (!File.Exists(file))
			return [];
		var folder = File.ReadAllText(file).Trim().Trim('"').Replace("\\\\", "\\");
		return folder.Length == 0 ? [] : [folder];
	}
}