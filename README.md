<div align="center">

# Facet

**단축키 하나로 여는 Windows 전체 화면 오버레이**<br>
시계·날씨·일정·할 일·알림을 한 화면에 두고, 필요한 기능은 플러그인으로 더합니다.

**한국어** · [English](README.en.md)

[![YouTube](https://img.shields.io/badge/YouTube-Material%20Stone-FF0000?logo=youtube&logoColor=white)](https://www.youtube.com/@MaterialStone)
[![Release](https://img.shields.io/github/v/release/material-stone/facet?label=%EB%8B%A4%EC%9A%B4%EB%A1%9C%EB%93%9C)](https://github.com/material-stone/facet/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

</div>

## Facet은 이런 앱이에요

- **`Win` + `Alt` + `O`** 를 누르면 지금 하던 일 위로 오버레이가 펼쳐집니다. 다시 누르거나 `Esc`를 누르면 사라져요.
- 대시보드에서 **시계, 날씨, 오늘 일정**을 한눈에 봅니다. 위젯은 격자 위에서 옮기고 크기를 바꿀 수 있어요.
- **달력**에서 날짜별 일정·할 일·노트(Markdown)를 관리하고, 정해 둔 시각에 **알림**을 받습니다.
- **`Win` + `Alt` + `Space`** 로 앱·폴더를 바로 열거나 웹을 검색하는 **빠른 실행** 창을 엽니다.
- **스킨**으로 분위기를 바꿉니다. 우주(Space), 복셀(Voxel), 미드나이트(Midnight)가 들어 있어요.
- 순정 Facet은 가볍게 유지하고, 다른 기능은 **플러그인**으로 골라서 설치합니다.

> Facet은 "보석의 깎인 면"이라는 뜻이에요. 위젯과 플러그인이 면처럼 모여 나만의 화면을 만듭니다.

## 설치

Windows 10(2004 이상) 또는 Windows 11이 설치된 64비트 PC가 필요합니다. .NET 같은 다른 프로그램은 따로 설치하지 않아도 돼요.

### 방법 1. 설치 프로그램 (추천)

1. [최신 릴리스](https://github.com/material-stone/facet/releases/latest)에서 `Facet-<버전>-setup-x64.exe`를 받습니다.
2. 받은 파일을 실행합니다. 관리자 권한은 필요 없어요.
3. 설치가 끝나면 시작 메뉴에서 **Facet**을 실행합니다.

> 아직 코드 서명이 없어서 "Windows의 PC 보호" 창이 뜰 수 있어요. **추가 정보 → 실행**을 누르면 됩니다.

### 방법 2. 명령어 한 줄 (winget)

```powershell
winget install MaterialStone.Facet
```

winget 등록은 준비 중이에요. 등록되기 전에는 방법 1을 사용하세요.

### 방법 3. 포터블 (설치 없이)

[최신 릴리스](https://github.com/material-stone/facet/releases/latest)에서 `Facet-<버전>-win-x64.zip`을 받아 원하는 곳에 풀고 `Facet.Launcher.exe`를 실행합니다.

### 제거

**설정 → 앱 → 설치된 앱**에서 Facet을 제거합니다. 내 기록은 지워지지 않아서, 다시 설치하면 그대로 이어서 쓸 수 있어요.

## 사용법

| 단축키 | 동작 |
| --- | --- |
| `Win` + `Alt` + `O` | 오버레이 열기/닫기 (설정에서 바꿀 수 있어요) |
| `Win` + `Alt` + `Space` | 빠른 실행 |
| `Esc` | 오버레이 숨기기 |
| `Win` + `Alt` + `End` | Facet 완전히 종료 |

작업 표시줄 오른쪽 트레이의 Facet 아이콘을 두 번 클릭해도 오버레이가 열립니다. **설정 → 앱 시작**에서 Windows에 로그인할 때 자동으로 실행되게 할 수 있어요.

## 플러그인

### 설치와 삭제

1. 오버레이에서 **설정** 탭을 엽니다.
2. **플러그인 → 스킨·플러그인 받기**에서 원하는 플러그인의 **적용**을 누르면 내려받아 바로 켜집니다. zip 파일로도 설치할 수 있어요.
3. 대시보드 편집 모드의 **위젯 추가**에서 플러그인 위젯을 배치합니다.
4. 지우려면 **플러그인 → 플러그인 구성**에서 플러그인 옆 휴지통 버튼을 누릅니다.
   - **삭제**: 플러그인만 지우고 기록은 남깁니다. 다시 설치하면 이어서 써요.
   - **데이터까지 삭제**: 플러그인의 기록과 설정까지 모두 지웁니다.

목록은 이 저장소의 [`registry/`](registry/README.md) 폴더에 있고, 파일은 이 저장소의 릴리스로 배포됩니다. Facet은 목록에 적힌 SHA-256과 내려받은 파일이 일치할 때만 설치합니다.

> ⚠️ 플러그인은 Facet과 같은 권한으로 실행됩니다. 믿을 수 있는 플러그인만 켜 주세요.

### 직접 만들기

플러그인은 `Facet.Plugins`만 참조하는 .NET 10 WPF 클래스 라이브러리입니다. [플러그인 작성 안내](docs/plugins/README.md)와 예제 [`impls/Facet.SamplePlugin`](impls/Facet.SamplePlugin)을 참고하세요. 다 만들었다면 [플러그인 업로드 양식](registry/docs/plugin.md)으로 레지스트리에 올릴 수 있어요.

## AI 에이전트와 함께 쓰기 (선택)

Facet에는 MCP 서버(`Facet.Mcp.exe`)가 들어 있어요. Codex나 Claude Code 같은 에이전트에 등록하면 "내일 오후 3시에 회의 일정 넣어 줘"처럼 말로 일정·알림·날씨를 다룰 수 있습니다. 기본 도구는 `facet_today`, `facet_list_schedules`, `facet_add_schedule`, `facet_delete_schedule`, `facet_add_reminder`, `facet_weather`이고, 플러그인을 켜면 플러그인 도구가 더해집니다.

## 내 기록과 개인정보

- 기록은 이 PC의 `%LOCALAPPDATA%\MaterialStone\DailyDock`에 저장되고 Windows 사용자 계정으로 암호화됩니다. 다른 사용자나 다른 PC에서는 읽을 수 없어요.
- Facet에는 계정이나 서버가 없고, 사용 기록을 밖으로 보내지 않습니다.
- 인터넷은 날씨를 가져올 때와 플러그인 목록을 열 때만 씁니다.

## 소스에서 빌드하기

필요한 것: Windows 10 이상, [.NET SDK 10.0.302 이상](https://dotnet.microsoft.com/download/dotnet/10.0), Node.js(검사용)

```powershell
git clone https://github.com/material-stone/facet.git
cd facet
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check.ps1     # 검사
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1     # 내 PC용 빌드 → artifacts/Facet
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1   # 포터블 zip → artifacts/portable
```

`Start-Facet.cmd`를 실행하면 빌드한 Facet이 열립니다. 구조와 결정 기록은 [프로젝트 문서](docs/README.md)에 있어요.

| 폴더 | 내용 |
| --- | --- |
| `impls/Facet` | WPF 앱 (오버레이, 대시보드, 달력, 설정, 플러그인 호스트) |
| `impls/Facet.Core` | 일정·알림·저장 데이터 |
| `impls/Facet.Plugins` | 플러그인 계약 |
| `impls/Facet.Launcher` | 최신 버전을 여는 고정 실행기 |
| `impls/Facet.Cli`, `impls/Facet.Mcp` | 알림 CLI, 에이전트용 MCP 서버 |
| `installer/` | 설치 프로그램 (Inno Setup) |
| `registry/` | 플러그인·스킨 레지스트리 |

새 버전을 낼 때는 `impls/Facet/Facet.csproj`의 `<Version>`을 올리고 `v<버전>` 태그를 푸시합니다. GitHub Actions가 검사, 패키지, 설치 테스트를 거쳐 릴리스를 만들어요.

## 만든 사람

**Material Stone**이 만들고 있어요. 개발 과정과 소식은 유튜브 채널에서 볼 수 있습니다.

▶ **YouTube: [Material Stone](https://www.youtube.com/@MaterialStone)**

버그 제보나 제안은 [Issues](https://github.com/material-stone/facet/issues)에 남겨 주세요.

## 라이선스

Facet 코드는 [MIT 라이선스](LICENSE)를 따릅니다. 포함된 글꼴은 각자의 라이선스(SIL Open Font License)를 따릅니다.

- [Pretendard](impls/Facet/Design/Fonts/Pretendard-LICENSE.txt)
- [Gowun Dodum](impls/Facet/Design/Fonts/GowunDodum-LICENSE.txt)
- [Jua](impls/Facet/Design/Fonts/Jua-LICENSE.txt)
