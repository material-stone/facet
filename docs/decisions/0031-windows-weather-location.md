# Windows 위치와 수동 날씨 지역

2026-10-24: 사용자가 ipapi를 Windows 위치 서비스로 교체하고, 위치를 얻지 못하면 설정에서 지역을 직접 선택하도록 요청했다. 0018의 IP 조회 정책을 대체한다.

- Desktop/DashboardWeather만 Windows.Devices.Geolocation과 AppCapability를 호출한다. 현재 좌표는 날씨 조회에만 전달하고 Store·로그·AI에 저장하지 않는다. 자동 위치의 지명은 [0033](0033-offline-weather-region-names.md)에 따라 한국 17개 시·도 경계로 판별한 실제 한국어 명칭을 표시하고, 판별할 수 없는 좌표만 `현재 위치`를 유지한다.
- 시작·로그인·정기 갱신은 기존 권한으로만 읽는다. 최초 RequestAccessAsync는 설정의 `위치 접근 허용` 버튼에서 전경 UI 스레드로 실행한다. 권한 거절·서비스 비활성화·시간 초과는 지역 선택 안내로 처리하며 ipapi로 돌아가지 않는다. GPS 수신기가 없으면 Windows가 다른 위치 신호를 사용할 수 있다.
- Settings.WeatherRegionId는 선택 필드이며 누락·빈 값은 Windows 자동 위치다. 값이 있으면 선택한 한국 지역의 대표 좌표를 사용한다. 지역 선택은 기존 암호화 Store에 저장하고 저장 성공 후 진행 중 요청을 취소·무효화해 새 위치로 즉시 갱신한다.
- WeatherRegions가 같은 모듈의 GeoNames 한국 ADM1/ADM2 JSON을 로드·검증한다. 설정에서 검색 가능한 한국 시·도/시·군·구 목록을 제공하며 실제 좌표를 역지오코딩 서비스에 전송하지 않는다. 원자료·변환·CC BY 출처는 design/weather-regions.md에 기록한다.
- Desktop과 JournalChecks는 공식 WinRT 참조를 위해 net10.0-windows10.0.19041.0을 사용한다. NuGet source mapping에는 필요한 Microsoft.Windows.SDK.NET.Ref만 추가한다. Core·Cli·Launcher에는 Windows 위치 의존성을 추가하지 않는다.
- 기존 단일 요청, 10분/실패 5분 주기, 취소·늦은 응답 무시, 기준 시각 유지·레이아웃을 밀지 않는 진행 막대만 표시하는 정책을 보존한다. 일반 검사는 주입한 좌표·권한 실패와 합성 HTTP만 사용하며 실제 위치 권한 요청은 하지 않는다.

근거: [Windows 위치 API](https://learn.microsoft.com/en-us/windows/apps/develop/maps-and-location/get-location), [데스크톱 WinRT 참조](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps).
