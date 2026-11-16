# 로컬 확인과 배포·릴리스

변경을 커밋 전에 실제 앱에서 보고, 푸시·릴리스 뒤에는 앱의 업데이트 버튼으로 바로 받을 수 있게 하는 절차입니다.

## 앱이 읽는 위치

- 앱 본체: 실행한 checkout의 `artifacts/Facet` (`Facet.Launcher.exe` → `latest.json`의 release).
- 플러그인: `%LOCALAPPDATA%\MaterialStone\Facet\plugins\<name>`. `artifacts/Facet/plugins`가 아닙니다.
- 플러그인·스킨 목록: `main`의 `registry/index.json`. 앱은 GitHub API로 먼저 읽어 머지 즉시 반영되고, API가 막히면(시간당 60회 제한) 몇 분 캐시되는 raw 주소로 대신 읽습니다.

## 커밋 없이 로컬에서 확인

1. 작업한 checkout(worktree 포함)에서 `scripts/build.ps1`을 실행합니다.
2. build는 이미 설치된 first-party 플러그인 폴더에 새 DLL을 덮어씁니다. `plugin.json`은 그대로 두어 설치 버전이 registry 버전과 비교되게 합니다.
3. Facet을 완전히 종료하고 다시 켭니다. worktree에서 빌드했다면 그 worktree의 `artifacts\Facet\Facet.Launcher.exe`로 켜야 앱 본체 변경도 보입니다.
4. 원래 상태로 되돌리려면 플러그인 화면에서 해당 플러그인을 다시 설치하거나 업데이트합니다.

## 푸시·릴리스 후 업데이트 버튼으로 확인

- **앱 본체**: 앱 안 업데이트 기능은 없습니다. PR을 `main`에 머지한 뒤 메인 checkout을 fast-forward하고 그곳에서 `build.ps1`을 실행합니다.
- **플러그인·스킨**: 코드가 바뀌면 항상 버전을 올려 릴리스합니다. 같은 버전을 다시 올리면 업데이트 버튼이 뜨지 않습니다.
  1. `plugin.json`(또는 스킨 manifest) 버전을 올립니다.
  2. 이전 release zip과 같은 구성으로 zip을 만들고 `plugin-<id>-<version>` 태그로 GitHub release에 올립니다.
  3. 올라간 zip을 다시 받아 SHA-256을 확인하고 `registry/index.json`의 `version`, `url`, `sha256`을 바꿔 `main`에 머지합니다.
  4. 앱의 **플러그인 받기**(또는 스킨) 화면을 다시 열면 `업데이트` 버튼이 뜹니다.
