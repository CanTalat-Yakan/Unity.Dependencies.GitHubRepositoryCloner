# Unity Essentials

**Unity Essentials** is a lightweight, modular utility namespace designed to streamline development in Unity. 
It provides a collection of foundational tools, extensions, and helpers to enhance productivity and maintain clean code architecture.

## 📦 This Package

This package is part of the **Unity Essentials** ecosystem.  
It integrates seamlessly with other Unity Essentials modules and follows the same lightweight, dependency-free philosophy.

## 🌐 Namespace

All utilities are under the `UnityEssentials` namespace. This keeps your project clean, consistent, and conflict-free.

```csharp
using UnityEssentials;
```

# GitHub Repository Cloner for Unity

A Unity Editor tool to fetch and clone private or public GitHub repositories using a personal access token. Designed to accelerate multi-repo onboarding for modular Unity projects.


## Features

- Fetch repositories using a GitHub token
- Clone multiple repositories into the Unity `Assets/` folder
- Optional automation:
  - Generate `.asmdef` files
  - Create `package.json` manifests
  - Copy template files (e.g. `.gitignore`, `README.md`)
- Rename LICENSE to `LICENSE.md` to prevent Unity package interference
- Caches GitHub token in EditorPrefs
- Responsive EditorWindow with scrollable selection UI


## Usage

1. **Open the Tool**  
   `Tools > GitHub Repository Cloner`

2. **Enter GitHub Token**  
   Token must have `repo` scope access for private repos.

3. **Fetch Repositories**  
   Hit *Fetch Repositories* to retrieve a list.

4. **Select and Clone**  
   Choose repositories. Enable optional features:
   - **Create Assembly Definitions**
   - **Create Package Manifests**
   - **Copy Template Files**

5. **Cloning Target**  
   All repos are cloned into `Assets/`.


## Template Folder

Template files are recursively copied from:

`Assets/_Templates`

These will be duplicated into each cloned repository folder.


## Requirements

- Unity 2021.3+ recommended
- Git must be installed and in system path
- Internet access


## Token Security

Stored using `EditorPrefs`. Select *Change Token* to clear.

