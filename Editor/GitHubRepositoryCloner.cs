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

namespace UnityEssentials
{
    /// <summary>
    /// Provides a Unity Editor window for cloning GitHub repositories into a Unity project.
    /// </summary>
    /// <remarks>This tool allows users to authenticate with a GitHub personal access token, fetch a list of
    /// repositories associated with the token, and select repositories to clone directly into the Unity project's
    /// Assets folder. Additional options include creating assembly definition files, package manifests, and copying
    /// template files for the cloned repositories.  To use this tool, navigate to the "Tools" menu in the Unity Editor
    /// and select "GitHub Repository Cloner".</remarks>
    public class GitHubRepositoryCloner : EditorWindow
    {
        private const string TemplateFolder = "Assets/Templates";
        private const string DefaultAuthorName = "Unity Essentials";
        private const string DefaultOrganizationName = "UnityEssentials";
        private const string DefaultUnityVersion = "2022.1";
        private const string DefaultDescription = "This is a part of the UnityEssentials Ecosystem";
        private const string DefaultDependency = "com.unityessentials.core";
        private const string DefaultDependencyVersion = "1.0.0";
        private const string ExcludeString = "Unity";
        private const string TokenKey = "GitToken";

        private static string s_token;
        private static List<string> s_repositoryNames = new();
        private static List<bool> s_repositorySelected = new();
        private bool _isFetching = false;

        private Vector2 _scrollPosition;

        private bool _createAssemblyDefinition = true;
        private bool _createPackageManifests = true;
        private bool _useTemplateFiles = true;

        private string _tokenPlaceholder = string.Empty;
        private string _repositoryNameFilter = string.Empty;

        [MenuItem("Tools/GitHub Repository Cloner")]
        public static void ShowWindow()
        {
            var window = GetWindow<GitHubRepositoryCloner>();
            window.minSize = new Vector2(400, 300);
        }

        public void OnGUI()
        {
            if (string.IsNullOrEmpty(s_token))
                s_token = EditorPrefs.GetString(TokenKey, "");

            GUILayout.Space(10);

            if (string.IsNullOrEmpty(s_token))
            {
                GUILayout.Label("Enter your GitHub token:");

                _tokenPlaceholder = EditorGUILayout.TextField(_tokenPlaceholder);

                if (GUILayout.Button("Save Token"))
                {
                    s_token = _tokenPlaceholder;
                    EditorPrefs.SetString(TokenKey, s_token);

                    FetchRepositories();
                }
                return; // Early return since no repositories to show yet
            }
            else
            {
                if (GUILayout.Button("Change Token"))
                {
                    s_token = "";
                    EditorPrefs.DeleteKey(TokenKey);
                    s_repositoryNames.Clear();
                    s_repositorySelected.Clear();
                    return;
                }

                if (s_repositoryNames.Count == 0 && !_isFetching)
                {
                    FetchRepositories();

                    EditorGUILayout.LabelField("Fetching...");

                    return; // Early return because no repositories to show yet
                }
                else if (s_repositoryNames.Count > 0)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Select repositories to clone:");
                    if (GUILayout.Button("Refresh", GUILayout.Width(100), GUILayout.Height(18)))
                    {
                        FetchRepositories();
                        GUI.FocusControl(null); // Remove focus from button to avoid accidental double-clicks
                    }
                    GUILayout.EndHorizontal();


                    GUILayout.Space(4);
                    GUILayout.Label("Filter repositories by name:");
                    string oldFilter = _repositoryNameFilter;
                    _repositoryNameFilter = EditorGUILayout.TextField(_repositoryNameFilter);
                    if (_repositoryNameFilter != oldFilter)
                    {
                        FetchRepositories();
                        return;
                    }

                    // Calculate space for the scroll view dynamically
                    float totalHeight = position.height;
                    float headerHeight = EditorGUIUtility.singleLineHeight * 3 + 30;
                    float toggleTemplateHeight = EditorGUIUtility.singleLineHeight + 6;
                    float buttonHeight = 30 + 20;
                    float padding = 20;
                    float scrollHeight = totalHeight - (headerHeight + toggleTemplateHeight + buttonHeight + padding);
                    scrollHeight = Mathf.Max(scrollHeight, 100);

                    using (new GUILayout.VerticalScope("box"))
                    {
                        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(scrollHeight));
                        for (int i = 0; i < s_repositoryNames.Count; i++)
                        {
                            if (s_repositorySelected.Count < s_repositoryNames.Count)
                                s_repositorySelected.Add(false);

                            s_repositorySelected[i] = EditorGUILayout.ToggleLeft(s_repositoryNames[i], s_repositorySelected[i]);
                        }
                        EditorGUILayout.EndScrollView();
                    }

                    GUILayout.Space(10);

                    _createAssemblyDefinition = EditorGUILayout.ToggleLeft("Create Assembly Definitions", _createAssemblyDefinition);
                    _createPackageManifests = EditorGUILayout.ToggleLeft("Create Package Manifests", _createPackageManifests);
                    _useTemplateFiles = EditorGUILayout.ToggleLeft("Copy Template Files", _useTemplateFiles);

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Clone Selected Repositories", GUILayout.Height(30)))
                    {
                        string targetPath = Application.dataPath;
                        CloneSelectedRepositories(targetPath);
                    }

                    GUILayout.Space(10);
                }
            }
        }

        /// <summary>
        /// Fetches the list of repositories for the authenticated GitHub user.
        /// </summary>
        /// <remarks>This method retrieves the repositories associated with the GitHub account linked to
        /// the provided token. If the token is invalid or empty, the method logs a warning or error, clears the token,
        /// and resets the repository lists. The method updates the repository names and selection states upon
        /// successful retrieval.</remarks>
        private async void FetchRepositories()
        {
            _isFetching = true;
            Repaint();

            if (string.IsNullOrEmpty(s_token))
            {
                Debug.LogWarning("[Git] Token is empty.");
                _isFetching = false;
                Repaint();
                return;
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UnityGitClient");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", s_token);

            var response = await client.GetAsync("https://api.github.com/user/repos?per_page=100");

            if (!response.IsSuccessStatusCode)
            {
                Debug.LogError("[Git] Invalid token or failed to fetch repositories. Clearing token.");
                EditorPrefs.DeleteKey(TokenKey);
                s_token = "";
                s_repositoryNames.Clear();
                s_repositorySelected.Clear();
                _isFetching = false;
                Repaint();
                return;
            }

            string json = await response.Content.ReadAsStringAsync();
            var allRepositories = ExtractRepositoryNames(json);

            // Filter out repositories that already exist anywhere in Assets
            var filteredByExistence = FilterExistingRepositories(allRepositories);

            // Further filter by the repository name filter
            s_repositoryNames = FilterByName(filteredByExistence, _repositoryNameFilter);

            // Reset selection list
            s_repositorySelected = new List<bool>(new bool[s_repositoryNames.Count]);

            _isFetching = false;
            Repaint();
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
        private List<string> FilterExistingRepositories(List<string> repoFullNames)
        {
            string assetsPath = Application.dataPath;
            var existingFolders = new HashSet<string>(
                Directory.GetDirectories(assetsPath, "*", SearchOption.AllDirectories)
                    .Select(path => Path.GetFileName(path))
            );

            var filtered = new List<string>();
            foreach (var fullName in repoFullNames)
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
        /// <param name="repoFullNames">The list of repository full names to filter.</param>
        /// <param name="filter">The filter string to apply. If empty or null, no filtering is applied.</param>
        /// <returns>A list of repository names that match the filter string.</returns>
        private List<string> FilterByName(List<string> repoFullNames, string filter)
        {
            if (string.IsNullOrEmpty(filter))
                return repoFullNames;

            return repoFullNames
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
        private async void CloneSelectedRepositories(string targetFolder)
        {
            List<string> selectedRepos = new();

            for (int i = 0; i < s_repositoryNames.Count; i++)
                if (s_repositorySelected[i])
                    selectedRepos.Add(s_repositoryNames[i]);

            if (selectedRepos.Count == 0)
            {
                EditorUtility.DisplayDialog("No selection", "Please select at least one repository to clone.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Confirm Clone", $"Clone {selectedRepos.Count} repositories into:\nAssets/ ?", "Yes", "Cancel"))
                return;

            foreach (var repositoryFullName in selectedRepos)
            {
                string cloneUrl = $"https://{s_token}@github.com/{repositoryFullName}.git"; // Include token in URL
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

                    if (_createAssemblyDefinition)
                        CreateAssemblyDefinition(localPath, packageName);

                    if (_createPackageManifests)
                        CreatePackageManifest(localPath, packageName);

                    if (_useTemplateFiles)
                        CopyTemplateFiles(TemplateFolder, localPath);
                }
                else
                {
                    Debug.LogError($"Failed to clone repository: {repositoryFullName}");
                }

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
                catch (System.Exception ex)
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
                catch (System.Exception ex)
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
                name = $"{DefaultOrganizationName}.{packageName}"
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
            var manifest = new PackageManifestEditor.PackageJson();
            manifest.name = $"com.{DefaultOrganizationName.ToLower()}.{packageName.ToLower()}";
            manifest.displayName = $"{DefaultOrganizationName} {packageName}";
            manifest.unity = DefaultUnityVersion;
            manifest.version = "1.0.0";
            manifest.description = DefaultDescription;
            manifest.author = new() { name = DefaultAuthorName };
            manifest.dependencies = new() { { DefaultDependency, DefaultDependencyVersion } };

            return manifest.ToJson();
        }

        [System.Serializable]
        public class AssemblyDefinitionData
        {
            public string name;
            public bool allowUnsafeCode = false;
            public bool noEngineReferences = false;
            public bool overrideReferences = false;
            public bool autoReferenced = true;
            public string rootNamespace = DefaultOrganizationName;
            public string[] references = new string[] { };
            public string[] precompiledReferences = new string[] { };
            public string[] includePlatforms = new string[] { };
            public string[] excludePlatforms = new string[] { };
            public string[] defineConstraints = new string[] { };
            public object[] versionDefines = new object[] { };
        }
    }
}
#endif