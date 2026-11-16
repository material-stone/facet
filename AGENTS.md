<!-- bootstrap-development-environment:start -->
## Development Environment

이 프로젝트는 architecture-first, stack-neutral 개발 제어면을 사용합니다.

### Session entry

1. `docs/README.md`와 `docs/architecture/README.md`를 읽습니다.
2. `docs/architecture/architecture-contract.yaml`과 작업 관련 ADR만 읽습니다.
3. 전체 검색 전에 제한된 Graphify query를 실행합니다:
   `graphify query "<question>" --budget 1200`.
4. 영향받는 경계와 경로만 확인한 뒤 수정합니다.
5. 인계 전에 native test와 선언된 architecture check를 실행합니다.
6. 앱 기능을 추가·수정했다면 contract의 `build`(`scripts/build.ps1`)로 배포까지 마쳐 고정된 런처가 새 release를 실행하게 합니다. `dotnet build`만으로는 런처가 갱신되지 않습니다. `build.ps1`은 실행한 checkout의 `artifacts/Facet`에 배포하므로, git worktree에서 작업했다면 worktree가 아니라 메인 checkout(고정된 런처가 있는 곳)을 최신 `main`으로 fast-forward한 뒤 그곳에서 `build`를 실행합니다.
7. 사용자가 커밋 전에 볼 수 있게 작업 checkout에서도 `build`를 실행합니다(설치된 first-party 플러그인을 덮어씀). 플러그인·스킨 코드를 바꿔 푸시했다면 버전을 올려 release와 `registry/index.json`까지 갱신해 앱의 `업데이트` 버튼으로 받을 수 있게 합니다. 절차: `docs/workflows/local-test-and-release.md`.

### Architecture guardrails

- `architecture-contract.yaml`의 dependency direction, public interfaces, invariants를 보존합니다.
- 의도적인 구조 변경 전 contract를 갱신하고 ADR을 추가합니다.
- 기존 abstraction을 재사용하고 speculative layer나 dependency를 추가하지 않습니다.
- feature-owned asset은 로드·검증하는 module 뒤에 둡니다.
- 프로젝트 문서는 concern별로 `docs/` 아래에 분류하고 각 category에 index를 둡니다.
- setup, test, lint, build, architecture 명령은 contract에 선언된 값을 사용하며 `null`은 미결정 상태로 취급합니다.

### Graphify

- `graphify-out/`은 source of truth가 아닌 재생성 가능한 local index입니다.
- `EXTRACTED` edge를 우선하고 `INFERRED`, `AMBIGUOUS` edge는 source에서 검증합니다.
- code-only 변경 후에는 incremental update를, docs·configuration·asset 변경 후에는 full update를 실행합니다.
- graph가 없거나 stale이면 architecture 영향 질문에 답하기 전에 재생성합니다.

`$bootstrap-development-environment`가 관리하는 영역입니다. 이 block은 작게 유지하고 상세 지침은 `docs/`에 작성합니다.
<!-- bootstrap-development-environment:end -->
