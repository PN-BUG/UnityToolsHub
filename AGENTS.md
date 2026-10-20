# UnityToolsHub AI tool reuse

- Before generating a utility for an editor task, read `Editor/Tools/AI/README.md` and search existing tools in `Editor/Tools` for equivalent behavior.
- Reuse an existing public method or extend the nearest suitable tool before creating another implementation.
- Save new reusable AI-authored editor tools under `Editor/Tools/AI`, with `[ToolInfo("Tool name", "AI工具", ...)]` and descriptive search tags.
- Expose core logic through explicit, parameterized public methods so subsequent AI tasks can invoke it without rebuilding a script or depending on window state.
- Update the AI directory README index with purpose, source, callable entry point, input/output, side effects, and validation limits.
- Follow parent project instructions for compilation, Play Mode, tests, and builds. A reusable entry point does not grant permission to run it.
