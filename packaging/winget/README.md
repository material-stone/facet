# winget manifest

`manifests/` 아래는 [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs)에 제출할 manifest입니다. 새 릴리스마다 버전 폴더를 복사하고 `PackageVersion`, `InstallerUrl`, `InstallerSha256`(릴리스의 `SHA256SUMS.txt`), `ReleaseDate`를 바꿉니다.

- 검증: `winget validate --manifest packaging/winget/manifests/m/MaterialStone/Facet/<버전>`
- 로컬 설치 시험: `winget install --manifest packaging/winget/manifests/m/MaterialStone/Facet/<버전>` (관리자 터미널에서 `winget settings --enable LocalManifestFiles` 필요)
- 제출: winget-pkgs를 fork해 같은 경로에 복사하고 PR을 엽니다. [wingetcreate](https://github.com/microsoft/winget-create)의 `wingetcreate submit <폴더>`로도 제출할 수 있습니다.