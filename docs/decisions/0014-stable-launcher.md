# 고정 실행기와 버전별 배포

2026-10-23: 작업 표시줄 아이콘을 한 번 고정한 뒤 같은 아이콘으로 최신 빌드를 실행한다.

`artifacts/Facet/Facet.Launcher.exe`는 고정 경로다. build.ps1은 매번 고유한 `releases/<version>-<build>/` 폴더에 publish하고 성공한 뒤에만 `latest.json`을 원자적으로 교체한다. 실행기는 폴더를 검색해 버전을 추측하지 않고 이 manifest를 검증한다. launcher 자체는 동일 경로에 배포하며 실행 시간이 짧으므로 앱 파일 잠금과 분리된다. 외부 업데이트 서버·설치 프로그램·새 패키지는 추가하지 않는다.

Launcher/LaunchProtocol.cs는 버전 없는 AppUserModelID, 데이터 경로별 인스턴스 식별과 IPC 이벤트 이름, 고정 실행 경로를 정의한다. Desktop이 ReleaseManifest 검증과 함께 source link로 재사용한다. 데이터 경로는 인스턴스를 구분하는 데만 사용하며 실행기는 사용자 기록을 열지 않는다.

기존 인스턴스가 최신 폴더에서 실행 중이면 활성화한다. 이전 폴더의 앱에는 update 이벤트로 정상 종료를 요청한다. 앱은 기존 저장 확인을 재사용하고 취소 이벤트로 전환을 중단한다. 종료를 확인한 후 최신 앱을 실행하며 강제 종료는 하지 않는다. 이 프로토콜 이전 버전이 실행 중이면 최초 한 번 직접 종료하도록 안내한다.

2026-10-23 시작 화면 변경: 일반 실행·기존 인스턴스의 open 요청·트레이 더블클릭은 관리 창 대신 오버레이를 표시한다. open 요청에서도 먼저 최신 배포를 확인하며 이미 열린 오버레이는 닫지 않는다. `--tray` 시작은 조용히 유지하되 사용자가 다시 열면서 새 버전으로 전환할 때는 이 인자를 전달하지 않는다. 관리 창은 명시적 트레이 메뉴와 관리 단축키로 연다.

후속 결정 [0022](0022-startup-options.md): 일반 시작/open 요청의 표시는 ShowOverlayOnLaunch를 따르고 로그인·백그라운드 요청은 기존 인스턴스를 활성화하거나 업데이트 확인 창을 열지 않는다. 명시적인 사용자 재열기는 --autostart와 --tray를 제거한다.

실행 중인 작업 표시줄 버튼은 새 프로세스를 시작하지 않고 기존 창을 활성화한다. 따라서 관리 창 Activated에서도 manifest를 검증해 새 경로가 있으면 실행기를 호출한다. 같은 배포본은 한 번만 자동 제안해 취소 후 반복 확인을 피하며 종료 확인 중에는 새 전환을 요청하지 않는다.

실행기와 관리 창은 LaunchProtocol.AppId의 기존 AppUserModelID를 공유한다. 관리 창은 창별 ID 및 RelaunchCommand/DisplayName/Icon을 고정 실행기로 지정한다. 이미 버전 경로를 가리키는 옛 아이콘은 한 번 제거하고 새 앱 창을 고정한다. 자동 시작을 새로 켤 때에도 고정 실행기를 사용하며 기존 사용자 등록은 임의로 변경하지 않는다.

검증은 합성 배포 폴더에서 실제 실행기의 인자 전달·최신 포인터 전환·기존 인스턴스 활성화·정상 종료 및 취소와 Windows 창 속성을 확인한다. 배포 실패는 기존 manifest를 보존한다.

Windows 창 속성의 조합·설정 시점은 [AppUserModelIDs](https://learn.microsoft.com/en-us/windows/win32/shell/appids)와 [RelaunchCommand](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-appusermodel-relaunchcommand)를 따른다. 고정할 때 생성되는 이름은 [RelaunchDisplayNameResource](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-appusermodel-relaunchdisplaynameresource)의 plain-text 형식으로 지정한다.
