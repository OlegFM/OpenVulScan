# Compile Commands Format

When `MSBuildWorkspace` fails to load a project (e.g., missing SDK, malformed `.csproj`), the frontend can fall back to an `AdhocWorkspace` built from a `compile_commands.json` file.

## Location

The file must be named `compile_commands.json` and placed in the **same directory** as the `.csproj` file being loaded.

## Schema

```json
{
  "compilation": {
    "assemblyName": "MyAssembly",
    "language": "C#",
    "sources": ["Program.cs", "Utils.cs"],
    "references": ["System.Runtime.dll", "System.Console.dll"],
    "preprocessorSymbols": ["DEBUG", "TRACE"]
  }
}
```

### Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `compilation` | object | Yes | Root compilation descriptor. |
| `compilation.assemblyName` | string | No | Assembly name. Defaults to the project file name without extension. |
| `compilation.language` | string | No | Language identifier (currently ignored; always treated as C#). |
| `compilation.sources` | string[] | No | Source file paths, relative to the `compile_commands.json` directory. |
| `compilation.references` | string[] | No | Assembly reference paths. Relative paths are resolved against the project directory. |
| `compilation.preprocessorSymbols` | string[] | No | C# preprocessor symbols (e.g., `DEBUG`, `TRACE`). |

## Examples

### Minimal example

```json
{
  "compilation": {
    "sources": ["Program.cs"]
  }
}
```

### With references and symbols

```json
{
  "compilation": {
    "assemblyName": "MyApp",
    "sources": ["src/Program.cs", "src/Utils.cs"],
    "references": [
      "C:/Program Files/dotnet/shared/Microsoft.NETCore.App/10.0.0/System.Runtime.dll"
    ],
    "preprocessorSymbols": ["DEBUG"]
  }
}
```

## Error Handling

- If `compile_commands.json` is missing, the fallback is skipped and the original MSBuild error is reported.
- If the JSON is malformed, the fallback is skipped.
- Missing source files listed in `sources` are silently ignored.
- Missing reference files listed in `references` are silently ignored.

## Phase 1 Limitations

- Only C# is supported.
- No support for project-to-project references.
- No support for NuGet package resolution.
- Source generators and analyzers are not loaded.
