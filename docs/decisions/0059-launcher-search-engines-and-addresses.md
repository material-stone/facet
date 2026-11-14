# 런처 검색 엔진과 웹 주소

2026-10-25: [0058](0058-launcher-web-search.md)의 Google 고정 검색을 설정 선택으로 확장한다. Core의 선택 필드 `Settings.LauncherSearchEngine`은 `WebSearchEngine.Google/Naver/Bing`이며 구형 저장에서 누락되면 Google이다. Core는 선택 값만 보관하고 URL·프로세스·브라우저 API를 참조하지 않는다. schemaVersion 2와 기존 암호화 Store를 유지한다.

오버레이 설정 → 단축키·빠른 실행 → 검색 엔진은 Google·네이버·Bing 선택 목록이다. 기존 SettingsEditing 검증과 변경 필드 자동 저장·실패 초안·재시도 경계를 재사용한다. QuickLaunchView는 Enter 실행 시 최신 Controller.Data.Settings를 읽으므로 런처나 앱 재열기 없이 선택이 적용된다. 검색 URL은 QuickLaunchWebSearch가 각 서비스의 고정 HTTPS endpoint와 query 인코딩으로 만든다. 기본 브라우저와 그 아이콘은 엔진과 독립적으로 유지한다.

`? ` 뒤의 HTTP/HTTPS URL은 검색하지 않고 그대로 연다. scheme 없는 도메인·localhost·IPv4/IPv6 주소는 HTTPS를 보충하며 경로·포트·query·fragment를 유지한다. 공백이 있는 일반 검색어, 이메일, 파일 경로와 다른 scheme는 검색어 데이터로 인코딩한다. 완전한 HTTP/HTTPS URL은 `?` 없이 입력해도 브라우저 모드로 열 수 있다. 일반 PATH 앱 이름·alias·예약 명령어·채팅 모드 판별은 보존한다. 입력 중 DNS·네트워크 조회나 검색어 저장은 하지 않는다.

도움말은 특정 엔진 이름 대신 검색/주소 열기와 설정 위치를 안내한다. `? `의 아이콘 비동기 조회·재사용·숨김 정책과 명시적인 Enter 실행 경계는 유지한다.
