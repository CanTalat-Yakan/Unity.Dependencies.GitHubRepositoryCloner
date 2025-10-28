#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Net.Http;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Debug = UnityEngine.Debug;
using System;

namespace UnityEssentials
{
    [Serializable]
    public class AssemblyDefinitionData
    {
        public string name;
        public bool allowUnsafeCode = false;
        public bool noEngineReferences = false;
        public bool overrideReferences = false;
        public bool autoReferenced = true;
        public string rootNamespace = string.Empty;
        public string[] references = new string[] { };
        public string[] precompiledReferences = new string[] { };
        public string[] includePlatforms = new string[] { };
        public string[] excludePlatforms = new string[] { };
        public string[] defineConstraints = new string[] { };
        public object[] versionDefines = new object[] { };
    }

    /// <summary>
    /// Provides a Unity Editor window for cloning GitHub repositories into a Unity project.
    /// </summary>
    /// <remarks>This tool allows users to authenticate with a GitHub personal access token, fetch a list of
    /// repositories associated with the token, and select repositories to clone directly into the Unity project's
    /// Assets folder. Additional options include creating assembly definition files, package manifests, and copying
    /// template files for the cloned repositories.  To use this tool, navigate to the "Tools" menu in the Unity Editor
    /// and select "GitHub Repository Cloner".</remarks>
    public partial class GitHubRepositoryCloner
    {
        public string _token;
        public List<string> _repositoryNames = new();
        public List<string> _allRepositoryNames = new();
        public List<bool> _repositorySelected = new();
        public bool _isFetching = false;

        public bool _shouldCreateAssemblyDefinition = true;
        public bool _shouldCreatePackageManifests = true;
        public bool _shouldUseTemplateFiles = true;

        private string _tokenPlaceholder = string.Empty;
        private string _repositoryNameFilter = string.Empty;

        private const string TokenKey = "GitToken";
        private const string TemplateFolder = "Assets/Templates";
        private const string DefaultAuthorName = "Unity Essentials";
        private const string DefaultOrganizationName = "UnityEssentials";
        private const string DefaultUnityVersion = "2022.1";
        private const string DefaultDescription = "This is a part of the UnityEssentials Ecosystem";
        private const string DefaultDependency = "com.unityessentials.core";
        private const string DefaultDependencyVersion = "1.0.0";
        private const string ExcludeString = "Unity";

        public GitHubRepositoryCloner()
        {
            // Restore token from EditorPrefs if needed
            if (string.IsNullOrEmpty(_token))
                _token = EditorPrefs.GetString(TokenKey, "");

            // If we have a token, fetch repositories automatically
            if (!string.IsNullOrEmpty(_token) && _repositoryNames.Count == 0 && !_isFetching)
                FetchRepositories();
        }

        /// <summary>
        /// Fetches the list of repositories for the authenticated GitHub user.
        /// </summary>
        /// <remarks>This method retrieves the repositories associated with the GitHub account linked to
        /// the provided token. If the token is invalid or empty, the method logs a warning or error, clears the token,
        /// and resets the repository lists. The method updates the repository names and selection states upon
        /// successful retrieval.</remarks>
        public async void FetchRepositories(Action repaint = null)
        {
            _isFetching = true;
            repaint?.Invoke();

            if (string.IsNullOrEmpty(_token))
            {
                Debug.LogWarning("[Git] Token is empty.");
                _isFetching = false;
                repaint?.Invoke();
                return;
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UnityGitClient");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", _token);

            var response = await client.GetAsync("https://api.github.com/user/repos?per_page=100");
            if (!response.IsSuccessStatusCode)
            {
                Debug.LogError("[Git] Invalid token or failed to fetch repositories. Clearing token.");
                EditorPrefs.DeleteKey(TokenKey);
                _token = "";
                _repositoryNames.Clear();
                _allRepositoryNames.Clear();
                _repositorySelected.Clear();
                _isFetching = false;
                repaint?.Invoke();
                return;
            }

            string json = await response.Content.ReadAsStringAsync();
            var allRepositories = ExtractRepositoryNames(json);

            // Filter out repositories that already exist anywhere in Assets
            var filteredByExistence = FilterExistingRepositories(allRepositories);

            // Store all fetched repositories
            _allRepositoryNames = filteredByExistence;

            // Apply current filter
            _repositoryNames = FilterByName(_allRepositoryNames, _repositoryNameFilter);

            // Reset selection list
            _repositorySelected = new List<bool>(new bool[_repositoryNames.Count]);

            _isFetching = false;
            repaint?.Invoke();
        }

        /// <summary>
        /// Extracts a list of repository names from a JSON string.
        /// </summary>
        /// <remarks>This method searches for occurrences of the "full_name" field in the provided JSON
        /// string and extracts their values. The method does not validate the overall structure of the JSON string and
        /// assumes that "full_name" fields are properly formatted.</remarks>
        /// <param name="json">A JSON-formatted string containing repository data. The string must include "full_name" fields for
        /// repositories.</param>
        /// <returns>A list of repository names extracted from the JSON string. If no repository names are found, returns an
        /// empty list.</returns>
        private List<string> ExtractRepositoryNames(string json)
        {
            List<string> names = new();

            int index = 0;
            while ((index = json.IndexOf("\"full_name\":\"", index)) != -1)
            {
                index += "\"full_name\":\"".Length;
                int end = json.IndexOf("\"", index);
                string name = json.Substring(index, end - index);
                names.Add(name);
                index = end;
            }

            return names;
        }

        /// <summary>
        /// Filters out repositories that already exist anywhere in the Assets folder.
        /// </summary>
        private List<string> FilterExistingRepositories(List<string> repositoryFullNames)
        {
            string assetsPath = Application.dataPath;
            var existingFolders = new HashSet<string>(
                Directory.GetDirectories(assetsPath, "*", SearchOption.AllDirectories)
                    .Select(path => Path.GetFileName(path))
            );

            var filtered = new List<string>();
            foreach (var fullName in repositoryFullNames)
            {
                string repoFolderName = fullName.Split('/')[1];
                if (!existingFolders.Contains(repoFolderName))
                    filtered.Add(fullName);
            }
            return filtered;
        }

        /// <summary>
        /// Filters the list of repository names based on the provided filter string.
        /// </summary>
        /// <param name="repositoryFullNames">The list of repository full names to filter.</param>
        /// <param name="filter">The filter string to apply. If empty or null, no filtering is applied.</param>
        /// <returns>A list of repository names that match the filter string.</returns>
        public List<string> FilterByName(List<string> repositoryFullNames, string filter)
        {
            if (string.IsNullOrEmpty(filter))
                return repositoryFullNames;

            return repositoryFullNames
                .Where(name => name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        /// <summary>
        /// Clones the selected repositories into the specified target folder.
        /// </summary>
        /// <remarks>This method clones all repositories that are marked as selected. If no repositories
        /// are selected,  the operation is canceled, and a dialog is displayed to inform the user. Before cloning, the
        /// user  is prompted to confirm the operation. If a repository folder already exists in the target location, 
        /// it is skipped, and a warning is logged.  After cloning, additional operations such as renaming license
        /// files, creating assembly definitions,  generating package manifests, and copying template files may be
        /// performed based on the current settings.  The method ensures that all file operations and UI updates are
        /// performed on the main thread.  A progress bar is displayed during the cloning process, and the Unity asset
        /// database is refreshed  upon completion.</remarks>
        /// <param name="targetFolder">The path to the target folder where the repositories will be cloned. This must be a valid directory path.</param>
        public async void CloneSelectedRepositories()
        {
            var targetFolder = GetSelectedPath();

            var selectedRepositories = new List<string>();

            for (int i = 0; i < _repositoryNames.Count; i++)
                if (_repositorySelected[i])
                    selectedRepositories.Add(_repositoryNames[i]);

            if (selectedRepositories.Count == 0)
            {
                EditorUtility.DisplayDialog("No selection", "Please select at least one repository to clone.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Confirm Clone", $"Clone {selectedRepositories.Count} repositories into:\n{GetSelectedPath()} ?", "Yes", "Cancel"))
                return;

            foreach (var repositoryFullName in selectedRepositories)
            {
                string cloneUrl = $"https://{_token}@github.com/{repositoryFullName}.git"; // Include token in URL
                string repositoryFolderName = repositoryFullName.Split('/')[1];
                string packageName = repositoryFolderName.Replace(ExcludeString, "");
                string localPath = Path.Combine(targetFolder, repositoryFolderName);

                if (Directory.Exists(localPath))
                {
                    Debug.LogWarning($"Repository folder already exists, skipping: {localPath}");
                    continue;
                }

                EditorUtility.DisplayProgressBar("Cloning Repositories", $"Cloning {repositoryFullName}...", 0);

                bool success = await CloneGitRepository(cloneUrl, localPath).ConfigureAwait(true); // Ensure main thread continuation

                if (success)
                {
                    Debug.Log($"Cloned {repositoryFullName} into {localPath}");

                    // Ensure all file operations are on the main thread
                    await Task.Run(() => { }).ConfigureAwait(true); // Explicitly switch context if needed

                    RenameLicenseFile(localPath, repositoryFullName);

                    if (_shouldCreateAssemblyDefinition)
                        CreateAssemblyDefinition(localPath, packageName);

                    if (_shouldCreatePackageManifests)
                        CreatePackageManifest(localPath, packageName);

                    if (_shouldUseTemplateFiles)
                        CopyTemplateFiles(TemplateFolder, localPath);
                }
                else Debug.LogError($"Failed to clone repository: {repositoryFullName}");

                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Clone Complete", "Selected repositories cloned successfully.", "OK");
        }

        /// <summary>
        /// Clones a Git repository from the specified URL to the specified local path.
        /// </summary>
        /// <remarks>This method uses the Git command-line tool to perform the clone operation. Ensure
        /// that Git is installed and available in the system's PATH environment variable. If the operation fails, error
        /// details will be logged.</remarks>
        /// <param name="cloneUrl">The URL of the Git repository to clone. This must be a valid Git repository URL.</param>
        /// <param name="localPath">The local file system path where the repository will be cloned. This path must be writable.</param>
        /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the
        /// repository was successfully cloned; otherwise, <see langword="false"/>.</returns>
        private Task<bool> CloneGitRepository(string cloneUrl, string localPath) =>
            Task.Run(() =>
            {
                try
                {
                    // Step 1: Clone the repository
                    ProcessStartInfo psi = new()
                    {
                        FileName = "git",
                        Arguments = $"clone {cloneUrl} \"{localPath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };

                    using (Process process = Process.Start(psi))
                    {
                        process.WaitForExit();

                        if (process.ExitCode != 0)
                        {
                            string error = process.StandardError.ReadToEnd();
                            Debug.LogError($"Git clone error: {error}");
                            return false;
                        }
                    }

                    // Step 2: Check for LFS usage
                    string gitattributesPath = Path.Combine(localPath, ".gitattributes");
                    if (File.Exists(gitattributesPath))
                    {
                        string[] lines = File.ReadAllLines(gitattributesPath);
                        bool usesLfs = false;
                        foreach (var line in lines)
                            if (line.Contains("filter=lfs"))
                            {
                                usesLfs = true;
                                break;
                            }

                        // Step 3: If LFS is used, run 'git lfs pull'
                        if (usesLfs)
                        {
                            ProcessStartInfo lfsPsi = new()
                            {
                                FileName = "git",
                                Arguments = "lfs pull",
                                WorkingDirectory = localPath,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                            };

                            using (Process lfsProcess = Process.Start(lfsPsi))
                            {
                                lfsProcess.WaitForExit();

                                if (lfsProcess.ExitCode != 0)
                                {
                                    string lfsError = lfsProcess.StandardError.ReadToEnd();
                                    if (lfsError.Contains("git-lfs: command not found") || lfsError.Contains("'lfs' is not a git command"))
                                        Debug.LogWarning("Git LFS is required but not installed. LFS files were not pulled.");
                                    else
                                    {
                                        Debug.LogError($"Git LFS pull error: {lfsError}");
                                        return false;
                                    }
                                }
                            }
                        }
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception cloning repository: {ex.Message}");
                    return false;
                }
            });

        private void RenameLicenseFile(string localPath, string repositoryFullName)
        {
            string licensePath = Path.Combine(localPath, "LICENSE");
            if (File.Exists(licensePath) && string.IsNullOrEmpty(Path.GetExtension(licensePath)))
            {
                string newLicensePath = licensePath + ".md";
                try
                {
                    File.Move(licensePath, newLicensePath);
                    Debug.Log($"Renamed LICENSE to LICENSE.md in {repositoryFullName}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to rename LICENSE file: {ex.Message}");
                }
            }
        }

        private void CreateAssemblyDefinition(string localPath, string packageName)
        {
            // Delete existing .asmdef files to avoid duplicates
            string[] existingAsmdefs = Directory.GetFiles(localPath, "*.asmdef");
            foreach (string asmdef in existingAsmdefs)
            {
                File.Delete(asmdef);
                Debug.Log($"Deleted existing assembly definition: {asmdef}");
            }

            // Create new .asmdef with the correct name
            var asmdefData = new AssemblyDefinitionData
            {
                name = $"{DefaultOrganizationName}.{packageName}",
                rootNamespace = DefaultOrganizationName
            };

            string asmdefPath = Path.Combine(localPath, $"{DefaultOrganizationName}.{packageName}.asmdef");
            string json = JsonUtility.ToJson(asmdefData, true);
            File.WriteAllText(asmdefPath, json);
        }

        private void CreatePackageManifest(string localPath, string packageName)
        {
            var json = DefaultPackageManifestToJson(packageName);

            // Write to package.json file
            string packageJsonPath = Path.Combine(localPath, "package.json");
            File.WriteAllText(packageJsonPath, json);
        }

        private void CopyTemplateFiles(string sourceFolder, string destinationFolder)
        {
            if (!Directory.Exists(sourceFolder))
            {
                Debug.LogWarning($"Template folder not found: {sourceFolder}");
                return;
            }

            foreach (string dirPath in Directory.GetDirectories(sourceFolder, "*", SearchOption.AllDirectories))
            {
                string newDirPath = dirPath.Replace(sourceFolder, destinationFolder);
                if (!Directory.Exists(newDirPath))
                    Directory.CreateDirectory(newDirPath);
            }

            foreach (string filePath in Directory.GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories))
            {
                // Ignore .meta files
                if (filePath.EndsWith(".meta"))
                    continue;

                string newFilePath = filePath.Replace(sourceFolder, destinationFolder);
                File.Copy(filePath, newFilePath, overwrite: true);
            }
        }

        private string DefaultPackageManifestToJson(string packageName)
        {
            var manifest = new PackageManifestData();
            manifest.name = $"com.{DefaultOrganizationName.ToLower()}.{packageName.ToLower()}";
            manifest.displayName = $"{DefaultOrganizationName} {packageName}";
            manifest.unity = DefaultUnityVersion;
            manifest.version = "1.0.0";
            manifest.description = DefaultDescription;
            manifest.author = new() { name = DefaultAuthorName };
            manifest.dependencies = new() { { DefaultDependency, DefaultDependencyVersion } };

            return manifest.ToJson();
        }

        private static string GetSelectedPath()
        {
            string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(assetPath)) return null;
            string fullPath = Path.GetFullPath(assetPath);
            return Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        }
    }
}
#endif