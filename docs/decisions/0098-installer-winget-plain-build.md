# 0098 — 설치 프로그램, winget, 플러그인 없는 공개 빌드

2026-11-14: Windows부터 공개 배포한다. 순정 앱은 플러그인 없이 가볍게 배포하고, 기능은 레지스트리([0097](0097-plugin-uninstall-and-in-repo-registry.md))에서 플러그인으로 받는다.

- **설치 프로그램:** `installer/Facet.iss`(Inno Setup)가 포터블 패키지([0087](0087-portable-package.md)) 폴더를 그대로 `%LOCALAPPDATA%\Programs\Facet`에 설치한다. 사용자 범위라 관리자 권한이 없어도 된다. 한국어·영어, 시작 메뉴와 선택형 바탕 화면 바로 가기, 제거 프로그램을 만든다. 제거하면 프로그램 폴더와 앱의 자동 실행 값을 지우고 기록(`%LOCALAPPDATA%\MaterialStone\DailyDock`)은 남긴다.
- **릴리스:** `.github/workflows/release.yml`이 `v<버전>` 태그에서 Core 검사, 포터블 zip, 설치 프로그램을 만들고, 깨끗한 러너에서 조용히 설치·제거해 본 뒤 GitHub 릴리스에 올린다. 태그와 `Facet.csproj`의 버전이 다르면 멈춘다.
- **winget:** 릴리스의 설치 프로그램 URL과 SHA-256으로 `packaging/winget/`의 manifest를 만들고 `microsoft/winget-pkgs`에 제출한다. 사용자 범위 Inno 설치 프로그램이므로 `InstallerType: inno`, `Scope: user`다.
- **플러그인 선택:** `scripts/package.ps1`과 `scripts/setup-tools.ps1`은 플러그인 프로젝트가 없는 체크아웃에서도 동작한다. 공개 저장소는 앱·Core·플러그인 계약·Launcher·CLI·MCP·검사만 담는다.

## 대가

- 코드 서명이 없어 SmartScreen 경고가 뜬다. 서명 인증서는 나중에 정한다.
- 자동 업데이트는 없다. 새 버전은 설치 프로그램이나 `winget upgrade`로 받는다.
- Windows 전용이다(WPF). 다른 OS는 별도 포팅 결정이 필요하다.