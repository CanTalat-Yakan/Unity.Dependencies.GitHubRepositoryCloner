#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityEssentials
{
    public partial class GitHubRepositoryCloner
    {
        public EditorWindowDrawer Window;
        public Action Repaint;
        public Action Close;

        [MenuItem("Assets/GitHub Repository Cloner", priority = -80)]
        public static void ShowWindow()
        {
            var editor = new GitHubRepositoryCloner();
            editor.Window = new EditorWindowDrawer("GitHub Repository Cloner", new(400, 500))
                .SetHeader(editor.Header, EditorWindowStyle.Toolbar)
                .SetBody(editor.Body, EditorWindowStyle.Margin)
                .SetFooter(editor.Footer)
                .GetRepaintEvent(out editor.Repaint)
                .GetCloseEvent(out editor.Close)
                .ShowUtility();
        }

        public void Header()
        {
            if (string.IsNullOrEmpty(_token))
            {
                GUILayout.Label("Enter your Git Token:");

                _tokenPlaceholder = EditorGUILayout.PasswordField(_tokenPlaceholder, EditorStyles.toolbarTextField);

                if (GUILayout.Button("Save Token", EditorStyles.toolbarButton))
                {
                    _token = _tokenPlaceholder;
                    EditorPrefs.SetString(TokenKey, _token);

                    FetchRepositories(Repaint);
                }
                return;
            }

            GUILayout.Space(4);

            string oldFilter = _repositoryNameFilter;
            _repositoryNameFilter = EditorGUILayout.TextField(_repositoryNameFilter, EditorStyles.toolbarSearchField);
            if (_repositoryNameFilter != oldFilter)
            {
                // Only filter locally, do not fetch
                _repositoryNames = FilterByName(_allRepositoryNames, _repositoryNameFilter);
                _repositorySelected = new(new bool[_repositoryNames.Count]);
            }

            GUILayout.Space(4);

            if (GUILayout.Button("Refresh Repositories", EditorStyles.toolbarButton))
            {
                FetchRepositories(Repaint);
                GUI.FocusControl(null);
            }

            if (GUILayout.Button("Change Token", EditorStyles.toolbarButton))
            {
                _token = "";
                EditorPrefs.DeleteKey(TokenKey);
                _repositoryNames.Clear();
                _allRepositoryNames.Clear();
                _repositorySelected.Clear();
                _tokenPlaceholder = string.Empty;
            }
        }

        public void Body()
        {
            if (string.IsNullOrEmpty(_token))
                return;

            if (_isFetching)
            {
                GUILayout.Label("Fetching...");
                return;
            }

            if (_repositoryNames.Count == 0)
            {
                GUILayout.Label("No repositories found matching the filter.");
                return;
            }

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("All", GUILayout.Width(30)))
                    for (int i = 0; i < _repositorySelected.Count; i++)
                        _repositorySelected[i] = true;

                if (GUILayout.Button("None", GUILayout.Width(40)))
                    for (int i = 0; i < _repositorySelected.Count; i++)
                        _repositorySelected[i] = false;
            }

            if (_repositoryNames.Count > 0)
                for (int i = 0; i < _repositoryNames.Count; i++)
                {
                    if (_repositorySelected.Count < _repositoryNames.Count)
                        _repositorySelected.Add(false);

                    _repositorySelected[i] = EditorGUILayout.ToggleLeft(_repositoryNames[i], _repositorySelected[i]);
                }
        }

        public void Footer()
        {
            if (string.IsNullOrEmpty(_token) || _isFetching)
                return;

            if (!_repositorySelected.Any(selected => selected))
                return;

            GUILayout.Space(-1);
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _shouldCreateAssemblyDefinition = EditorGUILayout.ToggleLeft("Create Assembly Definitions", _shouldCreateAssemblyDefinition);
                _shouldCreatePackageManifests = EditorGUILayout.ToggleLeft("Create Package Manifests", _shouldCreatePackageManifests);
                _shouldUseTemplateFiles = EditorGUILayout.ToggleLeft("Copy Template Files", _shouldUseTemplateFiles);

                if (GUILayout.Button("Clone Selected Repositories", GUILayout.Height(24)))
                {
                    string targetPath = Application.dataPath;
                    CloneSelectedRepositories(targetPath);

                    FetchRepositories(Repaint);
                    GUI.FocusControl(null);
                }
            }
        }
    }
}
#endif