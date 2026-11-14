# 실시간 배경 블러

2026-10-24: 캡처 제외·실시간 갱신 정책은 [0025 — 오버레이 캡처 허용](0025-capturable-overlay.md)의 단일 프레임 정책으로 대체했습니다. 아래는 이전 결정 기록입니다.

2026-10-23: 배경 표시·숨김의 280ms 전환에 점진적 블러를 추가한다. WPF BlurEffect는 다른 HWND의 픽셀을 직접 흐리게 하지 못하고, DwmEnableBlurBehindWindow는 Windows 8 이후 블러 효과를 제공하지 않는다. 기존 오프라인 빌드와 Windows Desktop 프레임워크를 유지한다.

Desktop/BackdropCapture는 Windows 화면 캡처 제외 속성으로 자기 오버레이를 제외하고 모니터 한 개를 메모리로 읽는다. 긴 변은 최대 1280px, 갱신은 최대 20fps로 제한하고 GDI DIB와 WriteableBitmap을 재사용한다. 캡처 제외를 지원하지 않으면 오버레이를 열기 직전 한 프레임만 사용해 자기 화면이 반복 캡처되지 않게 한다. 화면을 파일에 저장하거나 네트워크·Agent 경계로 전달하지 않는다.

OverlayWindow는 같은 0–1 전환 값으로 배경 진하기, 배경 이미지 혼합, Gaussian blur 0–18 DIP를 조절한다. HUD는 없으며 완전히 투명한 시작·끝과 빠른 전환 반전을 유지한다. 숨김/종료 시 타이머와 캡처 버퍼를 해제한다. 화면을 읽지 못하면 기존 투명 배경 페이드만 유지한다.

검증은 모니터 전체를 합성 패턴 창으로 덮은 뒤 수행한다. 자기 캡처 제외, 뒤 화면의 색 변경 반영, 중간·최종·역전 blur radius와 리소스 해제를 검사하며 실제 사용자 화면을 QA 파일에 남기지 않는다.

참고: [DwmEnableBlurBehindWindow](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmenableblurbehindwindow), [SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity).

2026-10-24 성능 보완: 관리 캐러셀을 드래그하거나 스냅하는 동안 마지막 캡처 프레임과 블러를 유지하고 동기 GDI 재캡처를 건너뛴다. 이동 완료 후 기존 50ms 타이머에서 실시간 갱신을 재개한다. 3440×1440 → 1280×536 합성 배경 측정에서 기존 HALFTONE StretchBlt가 평균 약 47ms로 UI 스레드를 막았기 때문이다. COLORONCOLOR 실험도 약 29ms라 품질 변경은 채택하지 않는다. 캐러셀은 실제 이동 상태와 세대별 애니메이션 완료만 노출하며 OverlayWindow가 캡처 주기를 결정한다. 중단된 이전 스냅 완료가 새 드래그의 정지 상태를 덮지 못하게 한다. 흐림 강도·20fps 상한·1280px 상한·SRCCOPY·커서 처리와 숨김/종료 리소스 해제는 유지한다. 진단은 opt-in일 때 구간별 시간만 기록하며 화면 픽셀은 저장하거나 전달하지 않는다.

현재 비동기 구현은 [0021 — 비동기 배경 캡처](0021-async-backdrop-capture.md)가 이 문서의 동기 UI 캡처 경로를 대체한다. 드래그/스냅의 마지막 프레임 유지와 블러·HALFTONE·SRCCOPY 정책은 계속 유지한다.
