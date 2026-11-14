# 오버레이 효과 설정

2026-10-23: 블러 강도와 표시·숨김 시간을 설정에서 각각 조절한다. Settings에 선택 필드 OverlayBlurRadius(기본 18), OverlayShowMilliseconds(280), OverlayHideMilliseconds(280)를 추가하며 schemaVersion 2와 기존 기록 호환성을 유지한다.

블러는 0–40, 시간은 각각 0–2000ms이며 0은 블러 끄기 또는 즉시 전환이다. Desktop 설정의 기존 저장·미저장 확인 흐름을 재사용한다. OverlayWindow는 현재 배경 진행률과 실제 blur radius에서 새 목표로 전환하므로 설정 변경 중에도 점프하지 않는다. 잘못된 외부 설정값은 렌더 경계에서 안전한 범위로 제한한다.

Core는 설정 값만 소유한다. WPF 입력, 애니메이션과 실시간 캡처 수명은 기존 Desktop 경계에 남는다. 구버전 기본값, 암호화 저장·재로드, 실제 설정 화면의 저장, 서로 다른 표시·숨김 시간, 즉시 전환과 블러 끄기를 검증한다.
