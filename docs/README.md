# Project Documentation

- [Facet 실행과 사용](overlay/README.md)
- [앱 검증 결과](overlay/verification.md)

`docs/`는 **Facet**의 지속적인 project knowledge base입니다. 설명은 한국어로 간결하게 작성하고 file, symbol, command, state 이름은 English identifier를 사용합니다.

## Classification 규칙

- `architecture/`: boundary, dependency direction, interface, invariant, structural overview.
- `decisions/`: 중요한 대안·이유·결과를 기록하는 ADR.
- `workflows/`: 반복 가능한 개발·debugging·onboarding·release 절차.
- 관련 문서가 여러 개일 때만 concern-specific directory를 추가하고 그 안에 index를 둡니다.

실행 가능한 code와 test가 behavior의 source of truth입니다. architecture contract는 의도한 boundary를 선언하고 ADR은 이유를 설명하며 이 문서는 방법을 설명합니다. 상세 규칙을 여러 위치에 중복 작성하지 않습니다.

- [기록 디자인 시스템](design/README.md)
- [구조 결정 기록](decisions/README.md)
- [개발·검증 워크플로](workflows/README.md)
- [플러그인 작성](plugins/README.md)
