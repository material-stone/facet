# 0087 — 설치 없는 포터블 패키지

다른 PC에서 설치 프로그램과 .NET 설치 없이 실행하도록 `scripts/package.ps1`이 `artifacts/portable/Facet-<version>-win-x64.zip`을 만듭니다. 압축을 풀고 `Facet.Launcher.exe`를 실행합니다.

- 런처·앱·`Facet.Mcp`·`Facet.Cli`는 `runtime/`의 런타임 한 벌을 함께 씁니다(`AppHostDotNetSearch=AppRelative`). 실행 파일마다 self-contained로 묶으면 런타임이 중복되고 NuGet 허용 목록도 넓혀야 합니다. 플러그인도 개발 배포와 같은 framework-dependent 형태로 유지됩니다.
- 앱 호스트 설정이 일반 배포의 `obj`로 새지 않도록 임시 소스 복사본에서 publish합니다.
- 패키지의 `bundled-plugins`는 Desktop/PortablePlugins가 시작 시 `<data>/plugins`로 복사합니다. 패키지 ID별로 한 번만 설치하므로 사용자가 지운 플러그인은 새 패키지 전까지 돌아오지 않습니다. 패키지에 없는 플러그인은 건드리지 않고, `plugin-data`도 유지합니다.
- 플러그인을 자동으로 켜지 않습니다. 실행 승인은 ADR 0073대로 사용자에게 남깁니다.
- 음성 모델은 1GB 가까이 되므로 포함하지 않습니다. 코드 서명도 하지 않아 SmartScreen 경고가 뜰 수 있습니다.
- 저장 데이터는 DPAPI 사용자 범위로 암호화되어 패키지로 다른 PC에 옮길 수 없습니다.
