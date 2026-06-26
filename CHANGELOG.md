# Changelog

## v1.0.0 - 2026-06-26

Initial GitHub-ready release.

### Added

- Infinity / 無下限
- Blue / 蒼
- Red / 赫
- Purple / 茈
- Domain Expansion / 領域展開
- Right grip teleport / 瞬間移動
- VR wheel menu for ability selection
- GitHub-ready README, CHANGELOG, LICENSE, `.gitignore`, `.editorconfig`
- `Directory.Build.props.example` for local game path setup

### Changed

- Removed local hard-coded Steam paths from `GojoMOD.csproj`
- Changed project references to use `$(GameDir)` / `I_AM_CAT_GAME_DIR`
- Set `UnityEngine.XRModule` and other references to `Private=false` so generated Unity/game DLLs are not copied into the repo output intentionally
- Updated MelonInfo version to `1.0.0`

### Removed from repository package

- `bin/` build outputs
- `obj/` intermediate files
- copied `UnityEngine.*.dll`
- `.pdb`, `.deps.json`, and other generated files
- empty placeholder source files

## Notes

This is an unofficial fan-made mod. Do not commit game files, MelonLoader generated files, Unity DLLs, paid assets, or copyrighted media assets to the source repository.
