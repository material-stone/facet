# ADR-0001: Architecture-first development environment

- Status: accepted
- Scope: MaterialStone

## Decision

stack-neutral architecture contract, compact `AGENTS.md` onboarding entry point, classified `docs/` content, Graphify rebuildable project map을 사용합니다. project-native tooling을 authoritative하게 유지하고 선택한 stack에 필요할 때만 stronger boundary check를 추가합니다.

## Consequences

- new session은 전체 repository를 읽지 않고 작은 context ladder에서 시작할 수 있습니다.
- feature 추가 전에 architecture intent가 명시됩니다.
- generated Graphify data는 versioning하지 않고 contract와 ADR을 versioning합니다.
- product와 stack이 정해질 때까지 초기 project에 `TBD` decision이 남을 수 있습니다.

## Revisit when

runtime을 선택하거나 mandatory boundary framework를 도입하거나 public interface를 변경하는 migration이 필요할 때 재검토합니다.
