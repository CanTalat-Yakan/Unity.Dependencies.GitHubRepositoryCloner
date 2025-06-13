using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityEssentials
{
    public partial class GitHubRepositoryCloner
    {
        public EditorWindowDrawer window;
        public Action Repaint;
        public Action Close;

        [MenuItem("Assets/GitHub Repository Cloner", priority = -80)]
        public static void ShowWindow()
        {
            var editor = new GitHubRepositoryCloner();
            editor.window = new EditorWindowDrawer("GitHub Repository Cloner", new(700, 800), new(400, 500))
                .SetHeader(editor.Header)
                .SetBody(editor.Body)
                .SetFooter(editor.Footer)
                .GetRepaintEvent(out editor.Repaint)
                .GetCloseEvent(out editor.Close)
                .ShowUtility();
        }

        public void Header()
        {
            if (string.IsNullOrEmpty(_token))
            {
                GUILayout.Label("Enter your GitHub token:");

                _tokenPlaceholder = EditorGUILayout.TextField(_tokenPlaceholder);

                if (GUILayout.Button("Save Token"))
                {
                    _token = _tokenPlaceholder;
                    EditorPrefs.SetString(TokenKey, _token);

                    FetchRepositories(Repaint);
                }
                return; // Early return since no repositories to show yet
            }

            if (GUILayout.Button("Change Token"))
            {
                _token = "";
                EditorPrefs.DeleteKey(TokenKey);
                _repositoryNames.Clear();
                _allRepositoryNames.Clear();
                _repositorySelected.Clear();
                return;
            }

            if (_isFetching)
            {
                GUILayout.Label("Fetching...");
                return;
            }

            GUILayout.BeginVertical();
            {
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Label("Filter repositories by name:");
                    if (GUILayout.Button("Refresh", GUILayout.Width(100), GUILayout.Height(18)))
                    {
                        FetchRepositories(Repaint);
                        GUI.FocusControl(null);
                    }
                }
                GUILayout.EndHorizontal();

                string oldFilter = _repositoryNameFilter;
                _repositoryNameFilter = EditorGUILayout.TextField(_repositoryNameFilter);
                if (_repositoryNameFilter != oldFilter)
                {
                    // Only filter locally, do not fetch
                    _repositoryNames = FilterByName(_allRepositoryNames, _repositoryNameFilter);
                    _repositorySelected = new List<bool>(new bool[_repositoryNames.Count]);
                }
            }
            GUILayout.EndVertical();
        }

        public void Body()
        {
            if (string.IsNullOrEmpty(_token))
                return; // Early return since no repositories to show yet

            if (_isFetching)
                return;

            if (_repositoryNames.Count == 0)
            {
                GUILayout.Label("No repositories found matching the filter.");
                return;
            }
            else if (_repositoryNames.Count > 0)
            {
                // Calculate space for the scroll view dynamically
                float totalHeight = window.position.height;
                float headerHeight = EditorGUIUtility.singleLineHeight * 3 + 30;
                float toggleTemplateHeight = EditorGUIUtility.singleLineHeight + 6;
                float buttonHeight = 90;
                float scrollHeight = totalHeight - (headerHeight + toggleTemplateHeight + buttonHeight);
                scrollHeight = Mathf.Max(scrollHeight, 100);

                for (int i = 0; i < _repositoryNames.Count; i++)
                {
                    if (_repositorySelected.Count < _repositoryNames.Count)
                        _repositorySelected.Add(false);

                    _repositorySelected[i] = EditorGUILayout.ToggleLeft(_repositoryNames[i], _repositorySelected[i]);
                }
            }
        }

        public void Footer()
        {
            if (_isFetching)
                return;

            if (string.IsNullOrEmpty(_token))
                return; // Early return since no repositories to show yet

            _shouldCreateAssemblyDefinition = EditorGUILayout.ToggleLeft("Create Assembly Definitions", _shouldCreateAssemblyDefinition);
            _shouldCreatePackageManifests = EditorGUILayout.ToggleLeft("Create Package Manifests", _shouldCreatePackageManifests);
            _shouldUseTemplateFiles = EditorGUILayout.ToggleLeft("Copy Template Files", _shouldUseTemplateFiles);

            GUI.enabled = _repositorySelected.Any(selected => selected);
            if (GUILayout.Button("Clone Selected Repositories", GUILayout.Height(24)))
            {
                string targetPath = Application.dataPath;
                CloneSelectedRepositories(targetPath);

                FetchRepositories(Repaint);
                GUI.FocusControl(null);
            }
            GUI.enabled = true;
        }
    }
}
