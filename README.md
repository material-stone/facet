<div align="center">

# Facet

**단축키 하나로 여는 Windows 전체 화면 오버레이**<br>
필요한 위젯과 스킨만 골라 담아 나만의 화면을 만듭니다.

**한국어** · [English](README.en.md)

[![YouTube](https://img.shields.io/badge/YouTube-Material%20Stone-FF0000?logo=youtube&logoColor=white)](https://www.youtube.com/@MaterialStone)
[![Release](https://img.shields.io/github/v/release/material-stone/facet?label=%EB%8B%A4%EC%9A%B4%EB%A1%9C%EB%93%9C)](https://github.com/material-stone/facet/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

</div>

## Facet은 이런 앱이에요

- 단축키를 누르면 지금 하던 일 위로 오버레이가 펼쳐지고, 다시 누르면 사라집니다.
- 오버레이는 탭과 보드로 이루어져 있어요. 보드 위에 위젯을 놓고, 옮기고, 크기를 바꿉니다.
- 위젯은 **플러그인**으로 더합니다. 순정 Facet은 가볍게 비워 두고, 쓰고 싶은 것만 설치하세요.
- **스킨**으로 분위기를 바꿉니다. 다크와 라이트가 기본으로 들어 있고, 다른 스킨은 받아서 씁니다.
- **빠른 실행** 창에 프로그램 이름을 입력하면 바로 실행합니다.
- 기록은 내 PC에만 저장되고, 설정에서 언제든 백업하고 복원할 수 있어요.

> Facet은 "보석의 깎인 면"이라는 뜻이에요. 위젯과 스킨이 면처럼 모여 하나의 화면을 만듭니다.

## 설치

Windows 10(2004 이상) 또는 Windows 11이 설치된 64비트 PC가 필요합니다. .NET 같은 다른 프로그램은 따로 설치하지 않아도 돼요.

### 설치 프로그램 (추천)

1. [최신 릴리스](https://github.com/material-stone/facet/releases/latest)에서 `Facet-<버전>-setup-x64.exe`를 받습니다.
2. 받은 파일을 실행합니다. 관리자 권한은 필요 없어요.
3. 설치가 끝나면 시작 메뉴에서 **Facet**을 실행합니다.

> 아직 코드 서명이 없어서 "Windows의 PC 보호" 창이 뜰 수 있어요. **추가 정보 → 실행**을 누르면 됩니다.

### 포터블 (설치 없이)

[최신 릴리스](https://github.com/material-stone/facet/releases/latest)에서 `Facet-<버전>-win-x64.zip`을 받아 원하는 곳에 풀고 `Facet.Launcher.exe`를 실행합니다.

### 제거

**Windows 설정 → 앱 → 설치된 앱**에서 Facet을 제거합니다. 내 기록은 지워지지 않아서, 다시 설치하면 그대로 이어서 쓸 수 있어요.

## 사용법

| 기본 단축키 | 동작 |
| --- | --- |
| `Win` + `Alt` + `O` | 오버레이 열기/닫기 |
| `Win` + `Alt` + `Space` | 빠른 실행 |
| `Win` + `Alt` + `End` | Facet 완전히 종료 |
| `Esc` | 오버레이 숨기기 |

세 단축키는 모두 **설정 → 단축키**에서 바꿀 수 있어요. 작업 표시줄 오른쪽 트레이의 Facet 아이콘을 두 번 클릭해도 오버레이가 열리고, **설정 → 앱 시작**에서 Windows에 로그인할 때 자동으로 실행되게 할 수 있습니다.

### 빠른 실행

빠른 실행 창에 프로그램 이름을 입력하면 PATH에 등록된 프로그램 중에서 후보를 보여 줍니다. 위아래 화살표로 고르고 `Enter`를 누르면 실행돼요. 예: `notepad`, `calc`

## 플러그인

위젯은 플러그인으로 설치합니다. 👉 **[플러그인 목록 보기](registry/plugins/README.md)**

1. 오버레이에서 **설정** 탭을 엽니다.
2. **플러그인 → 플러그인 받기**에서 원하는 플러그인의 **설치**를 누릅니다. 설치하면 바로 켜져요.
3. 탭의 위젯 편집 버튼을 누르고 **위젯 추가**에서 플러그인 위젯을 배치합니다.
4. 지우려면 **플러그인 → 설치된 플러그인**에서 휴지통 버튼을 누릅니다.
   - **삭제**: 플러그인만 지우고 기록은 남깁니다. 다시 설치하면 이어서 써요.
   - **데이터까지 삭제**: 플러그인의 기록과 설정까지 모두 지웁니다.

> ⚠️ 플러그인은 Facet과 같은 권한으로 실행됩니다. 믿을 수 있는 플러그인만 설치하세요.

플러그인을 직접 만들고 싶다면 [플러그인 작성 안내](docs/plugins/README.md)와 [업로드 안내](registry/plugins/UPLOAD.md)를 참고하세요.

## 스킨

다크와 라이트는 기본으로 들어 있어요. 👉 **[스킨 목록 보기](registry/skins/README.md)**

1. **설정 → 스킨 → 스킨 받기**에서 원하는 스킨의 **설치**를 누르면 바로 적용됩니다.
2. 다른 스킨으로 바꾸려면 **설정 → 스킨 → 사용할 스킨**에서 고릅니다.

스킨을 직접 만들고 싶다면 [스킨 업로드 안내](registry/skins/UPLOAD.md)를 참고하세요.

## 내 기록과 백업

- 기록은 이 PC의 `%LOCALAPPDATA%\MaterialStone\Facet`에 저장되고 Windows 사용자 계정으로 암호화됩니다. 다른 사용자나 다른 PC에서는 읽을 수 없어요.
- **설정 → 데이터 → 백업**에서 **지금 백업하기**를 누르면 `문서\Facet 백업` 폴더에 zip 파일이 만들어집니다.
- **설정 → 데이터 → 복원**에서 백업 파일을 고르고 Facet을 다시 시작하면 그 시점으로 돌아갑니다. 복원 전 기록은 옆 폴더에 남겨 둬요. 백업은 같은 Windows 계정에서만 복원할 수 있습니다.
- Facet에는 계정이나 서버가 없고, 사용 기록을 밖으로 보내지 않습니다. 인터넷은 플러그인·스킨 목록을 열 때와, 플러그인이 필요할 때(예: 날씨)만 씁니다.

## 만든 사람

**Material Stone**이 만들고 있어요. 개발 과정과 소식은 유튜브 채널에서 볼 수 있습니다.

▶ **YouTube: [Material Stone](https://www.youtube.com/@MaterialStone)**

버그 제보나 제안은 [Issues](https://github.com/material-stone/facet/issues)에 남겨 주세요.

## 라이선스

Facet은 [MIT 라이선스](LICENSE)를 따릅니다. 앱에 포함된 [Pretendard](impls/Facet/Design/Fonts/Pretendard-LICENSE.txt) 글꼴은 SIL Open Font License를 따릅니다. 스킨에 포함된 글꼴은 각 스킨 폴더의 라이선스를 따릅니다.
