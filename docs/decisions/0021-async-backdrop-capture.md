# 비동기 배경 캡처의 스레드·버퍼 소유권

2026-10-24: 오버레이의 캡처 제외·연속 갱신은 [0025 — 오버레이 캡처 허용](0025-capturable-overlay.md)으로 대체했습니다. 스레드·버퍼 소유권과 취소·재열기 barrier는 유지하며 단일 프레임 경로를 기본으로 사용합니다.

2026-10-24: 동기 HALFTONE StretchBlt가 3440×1440 → 1280×536에서 평균 약 47ms 동안 UI를 막는 실측에 따라 GDI 읽기를 Desktop의 전용 background thread로 분리한다. 블러·그림자·색·전환과 HALFTONE/SRCCOPY 품질은 유지한다.

Desktop/BackdropCapture는 GDI DC/DIB 생성·캡처·복사·해제를 제공하는 worker-owned source다. Desktop/BackdropCaptureSession은 전용 스레드, 요청 병합, 50ms 최소 시작 간격, 단일 재사용 managed pixel buffer와 Dispatcher 전달을 소유한다. UI는 요청만 제출하고 기다리지 않는다. worker는 UI 반영 완료 또는 취소를 확인한 뒤에만 버퍼를 재사용한다. 대기 프레임과 다음 요청은 각각 최대 하나이며 좌표가 바뀌면 오래된 프레임을 버린다. WPF WriteableBitmap은 OverlayWindow의 UI 스레드에서만 생성·갱신한다. Core와 저장 스키마는 변경하지 않는다.

숨김·블러 해제·종료 시 UI가 세션을 즉시 분리하고 pending Dispatcher 전달을 취소한다. UI는 worker Join/Wait를 하지 않는다. 이미 진행 중인 취소 불가능한 GDI 호출이 반환하면 worker의 finally가 같은 스레드에서 DC/DIB를 해제한다. 이전 세션·다른 모니터의 완료는 UI에 반영하지 않는다. 크기 변경은 worker의 DIB/managed buffer와 UI bitmap을 각각 소유 스레드에서 교체한다. 최대 긴 변 1280px, 최대 시작 빈도 20회/초, 메모리 전용·픽셀 외부 전달 금지와 드래그/스냅 중 마지막 블러 프레임 유지 정책을 보존한다.

캡처 제외가 지원되면 창을 즉시 표시하고 첫 비동기 프레임부터 배경을 반영한다. 지원되지 않으면 숨긴 상태에서 한 프레임을 얻은 뒤 현재 요청의 표시 전환을 시작한다. 대기 중 숨김 요청은 표시를 취소한다. 캡처 실패 시 배경 색조만 표시하며 창 자체를 재캡처하지 않는다.

검증은 느린/실패/모니터 변경 source의 결정적 테스트로 UI 비차단·버퍼 수명·요청 병합·중단·오래된 결과 차단을 확인하고, 실제 합성 패턴 위에서 블러·그림자·드래그·숨김/재열기·자원 해제와 worker/UI 구간 시간을 측정한다. 픽셀은 합성 테스트 외 저장하지 않으며 성능 기록에는 시간만 남긴다.
재열기는 이전 세션의 Stopped 완료 뒤 새 worker를 시작하는 비동기 barrier를 사용한다. UI는 기다리지 않으며, 빠른 반복 토글에서도 실제 GDI 소유 스레드가 겹치지 않는다.
