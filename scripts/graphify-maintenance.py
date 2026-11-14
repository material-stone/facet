"""Offline, evidence-preserving Graphify rebuild and integrity verification.

Use the interpreter recorded in graphify-out/.graphify_python. Semantic extraction
is supplied by the host agent with `import-semantic`; this script never calls an API.
The canonical graph is a MultiDiGraph. Clustering uses a disposable simple projection.
"""
from __future__ import annotations

import argparse
import ast
import copy
import hashlib
import importlib.util
import json
import re
import sys
import tempfile
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
import xml.etree.ElementTree as ET

import networkx as nx


ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'graphify-out'
SNAPSHOT = OUT / 'semantic-extractions.json'
SPEC = Path.home() / '.codex/skills/graphify/references/extraction-spec.md'


def read_json(path, default=None):
    return json.loads(Path(path).read_text(encoding='utf-8')) if Path(path).exists() else default


def write_json(path, value):
    from graphify.paths import write_json_atomic
    write_json_atomic(str(path), value, indent=2)


def content_hash(path):
    return hashlib.md5(Path(path).read_bytes()).hexdigest()


def relative_source(value, root=ROOT):
    if not value:
        return ''
    path = Path(value)
    path = (path if path.is_absolute() else root/path).resolve()
    return path.relative_to(root.resolve()).as_posix()


def source_line(item, root):
    source = item.get('source_file')
    match = re.fullmatch(r'L(\d+)(?:[-:]L?\d+)?', str(item.get('source_location') or ''))
    if not source or not match:
        raise ValueError(f'Cannot verify source location: {item}')
    path = (root/source).resolve()
    path.relative_to(root.resolve())
    lines = path.read_text(encoding='utf-8-sig').splitlines()
    index = int(match[1])-1
    if index < 0 or index >= len(lines):
        raise ValueError(f'Stale source location: {source}:{item["source_location"]}')
    return lines[index]


def prompt_hash():
    return hashlib.sha256(SPEC.read_bytes()).hexdigest()


def import_semantic(fragment_path, hashes_path):
    """Cache only nonempty, current source extractions; never trust an empty hit."""
    from graphify.cache import save_semantic_cache
    fragment = read_json(fragment_path)
    expected = {relative_source(p): h for p,h in read_json(hashes_path).items()}
    grouped = defaultdict(lambda: {'nodes': [], 'edges': [], 'hyperedges': []})
    for kind in ('nodes', 'edges', 'hyperedges'):
        for original in fragment.get(kind, []):
            item = copy.deepcopy(original)
            source = relative_source(item.get('source_file'))
            if source not in expected:
                raise ValueError(f'Unrequested semantic source: {source}')
            item['source_file'] = source
            if kind == 'nodes':
                item['_origin'] = 'semantic'
            grouped[source][kind].append(item)
    snapshot = read_json(SNAPSHOT, {'version': 1, 'files': {}})
    imported, stale, omitted = [], [], []
    for source, expected_hash in expected.items():
        if source not in grouped or not grouped[source]['nodes']:
            omitted.append(source)
            continue
        if not (ROOT/source).is_file() or content_hash(ROOT/source) != expected_hash:
            stale.append(source)
            continue
        snapshot['files'][source] = {'content_md5': expected_hash,
            'prompt_sha256': prompt_hash(), **grouped[source]}
        imported.append(source)
    write_json(SNAPSHOT, snapshot)
    # Copies prevent builder source normalization from emptying subsequent caches.
    items = {k: [item for source in imported for item in grouped[source][k]]
             for k in ('nodes', 'edges', 'hyperedges')}
    if imported:
        save_semantic_cache(**items, root=ROOT, allowed_source_files=imported,
                            prompt_file=SPEC)
    print(json.dumps({'imported': len(imported), 'stale': stale, 'omitted': omitted}, ensure_ascii=False))


def available_semantic(detection):
    snapshot = read_json(SNAPSHOT, {'files': {}})
    result = {'nodes': [], 'edges': [], 'hyperedges': []}
    complete, pending = [], []
    for kind in ('document', 'paper', 'image'):
        for absolute in detection['files'].get(kind, []):
            source = relative_source(absolute)
            entry = snapshot['files'].get(source, {})
            if (entry.get('nodes') and entry.get('content_md5') == content_hash(absolute)
                    and entry.get('prompt_sha256') == prompt_hash()):
                complete.append(source)
                for key in result:
                    result[key].extend(copy.deepcopy(entry.get(key, [])))
            else:
                pending.append(source)
    return result, complete, pending


def cold_extract(detection, root=ROOT, out=OUT):
    """XAML parses its code-behind too: a per-run cache prevents stale sidecars."""
    from graphify.extract import extract
    with tempfile.TemporaryDirectory(prefix='.cold-extract-', dir=out) as directory:
        cache = Path(directory).resolve()
        cache.relative_to(out.resolve())  # prove cleanup remains inside output
        all_files = [Path(f) for t in ('code', 'document') for f in detection['files'].get(t, [])]
        configs = [p for p in all_files if p.suffix.lower() in ('.json', '.config')]
        result = extract([p for p in all_files if p not in configs], root=root, cache_root=cache, parallel=False)
        for path in configs:
            source = relative_source(path, root)
            identifier = re.sub(r'[^a-z0-9]+', '_', str(Path(source).with_suffix('')).lower()).strip('_')
            if path.suffix.lower() == '.json':
                value = json.loads(path.read_text(encoding='utf-8-sig'))
                keys = sorted(value) if isinstance(value, dict) else []
                format_name = 'json'
            else:
                tree = ET.parse(path).getroot()
                keys = sorted({child.tag.rsplit('}', 1)[-1] for child in tree})
                format_name = 'xml'
            result['nodes'].append({'id': identifier, 'label': path.name,
                'file_type': 'code', 'source_file': source, 'source_location': 'L1',
                '_origin': 'ast', 'config_format': format_name, 'configuration_keys': keys})
        return result


def is_source_project(project):
    path = Path(project)
    return (path.suffix.lower() == '.csproj' and
            not path.name.lower().endswith('_wpftmp.csproj') and
            not any(part.lower() in ('obj', 'bin') for part in path.parts))


def owning_projects(source, root):
    root = root.resolve()
    for directory in source.resolve().parents:
        if not directory.is_relative_to(root):
            break
        projects = sorted(p for p in directory.glob('*.csproj') if is_source_project(p))
        if projects:
            return projects
    return []


def detect_corpus():
    from graphify.detect import detect
    detection = detect(ROOT)
    # WPF creates project copies beside the real project during compilation.
    # They can vanish at any time and are neither source nor dependency evidence.
    generated = {p for files in detection['files'].values() for p in files
                 if Path(p).suffix.lower() == '.csproj' and not is_source_project(p)}
    for kind, files in detection['files'].items():
        detection['files'][kind] = [p for p in files if p not in generated]
    detection['total_files'] -= len(generated)
    detection['skipped_generated_projects'] = sorted(relative_source(p, ROOT) for p in generated)
    # Graphify's extension detector otherwise leaves this build-critical XML file
    # unclassified. Classify it explicitly and parse it, rather than claim coverage.
    configuration = ROOT/'NuGet.Config'
    if configuration.is_file() and str(configuration) not in detection['files']['code']:
        detection['files']['code'].append(str(configuration))
        detection['files']['code'].sort()
        detection['total_files'] += 1
        detection['classification_overrides'] = ['NuGet.Config: parsed XML configuration']
        detection['unclassified'] = [p for p in detection.get('unclassified', []) if Path(p) != configuration]
    return detection


def verify_csharp_import(edge, root):
    target = edge.get('metadata', {}).get('target_fqn')
    if not target:
        raise ValueError(f'Import lacks qualified source evidence: {edge}')
    line = source_line(edge, root)
    compact = re.sub(r'\s+', '', line).split('//', 1)[0]
    # Exact referenced name and a using statement are required, including aliases.
    if not re.search(r'(?:global)?using(?:static)?', compact) or target+';' not in compact:
        raise ValueError(f'Import does not match source: {edge}')
    return target


def package_evidence(root):
    packages = []
    for project in sorted((root/'impls').rglob('*.csproj')):
        if not is_source_project(project):
            continue
        for element in ET.parse(project).getroot().iter():
            if element.tag.rsplit('}', 1)[-1] == 'PackageReference':
                name = element.attrib.get('Include') or element.attrib.get('Update')
                if name:
                    packages.append({'name': name, 'version': element.attrib.get('Version'),
                                     'source_file': relative_source(project, root)})
    return packages


def alias_target(edge, target, nodes, root):
    if '::' not in target:
        return None
    alias, qualified = target.split('::', 1)
    importer = root/edge['source_file']
    if not re.search(r'\bextern\s+alias\s+'+re.escape(alias)+r'\s*;',
                     importer.read_text(encoding='utf-8-sig')):
        raise ValueError(f'Unverified extern alias: {target}')
    permitted_projects = []
    for project in owning_projects(importer, root):
        for element in ET.parse(project).getroot().iter():
            if element.tag.rsplit('}', 1)[-1] != 'ProjectReference':
                continue
            aliases = element.attrib.get('Aliases', '')
            aliases += ','+','.join(child.text or '' for child in element
                                    if child.tag.rsplit('}', 1)[-1] == 'Aliases')
            if alias in re.split(r'[,;\s]+', aliases):
                permitted_projects.append((project.parent/element.attrib['Include']).resolve().parent)
    candidates = []
    for node in nodes.values():
        namespace = node.get('metadata', {}).get('namespace', '')
        fqn = '.'.join(x for x in (namespace, node.get('label', '')) if x)
        if fqn != qualified or not node.get('source_file'):
            continue
        definition = (root/node['source_file']).resolve()
        if any(definition.is_relative_to(project) for project in permitted_projects):
            candidates.append(node['id'])
    if len(set(candidates)) != 1:
        raise ValueError(f'Extern alias is not uniquely resolved: {target}: {candidates}')
    return candidates[0]


def external_import_name(edge, root, packages):
    suffix = Path(edge['source_file']).suffix.lower()
    if suffix == '.cs':
        name = verify_csharp_import(edge, root)
        if name.startswith(('System.', 'Microsoft.Win32')) or name == 'System':
            return name, 'framework_namespace', None
        # These SDK namespaces are referenced by the compiled Windows location
        # adapter. Require a versioned Windows target in its own nearest project.
        if name in ('Windows.Devices.Geolocation', 'Windows.Security.Authorization.AppCapabilityAccess'):
            importer = (root/edge['source_file']).resolve()
            matches = []
            for project in owning_projects(importer, root):
                for element in ET.parse(project).getroot().iter():
                    if element.tag.rsplit('}', 1)[-1] in ('TargetFramework', 'TargetFrameworks'):
                        for framework in (element.text or '').split(';'):
                            if re.fullmatch(r'net\d+\.\d+-windows\d+\.\d+(?:\.\d+){1,2}', framework.strip()):
                                matches.append({'name': 'Windows SDK', 'target_framework': framework.strip(),
                                                'source_file': relative_source(project, root)})
            if len(matches) == 1:
                return name, 'framework_namespace', matches[0]
        owners = {relative_source(p, root) for p in owning_projects(root/edge['source_file'], root)}
        declared = [p for p in packages if p['source_file'] in owners and
                    (name == p['name'] or name.startswith(p['name']+'.'))]
        if len(declared) == 1:
            return name, 'dependency_reference', declared[0]
        raise ValueError(f'Cannot prove a missing C# import is external: {edge}')
    if suffix == '.py':
        line = source_line(edge, root)
        statements = ast.parse(line.lstrip()).body
        candidates = []
        for statement in statements:
            if isinstance(statement, ast.Import):
                candidates += [alias.name for alias in statement.names]
            elif isinstance(statement, ast.ImportFrom):
                module = statement.module or ''
                candidates += [module]+[module+'.'+alias.name for alias in statement.names]
        target = str(edge['target'])
        matching = [name for name in candidates if re.sub(r'[^a-z0-9]+', '_', name.lower()).strip('_') == target]
        if len(matching) != 1:
            raise ValueError(f'Cannot identify missing Python import from source: {edge}: {candidates}')
        name = matching[0]
        module = name.split('.', 1)[0]
        spec = importlib.util.find_spec(module)
        if module in sys.stdlib_module_names or (spec and spec.origin and
                not Path(spec.origin).is_relative_to(root)):
            return name, 'external_python_reference', None
    raise ValueError(f'Unsupported missing external endpoint: {edge}')


def repair_extraction(raw, root=ROOT):
    """Preserve every distinct evidence row and fail on unproved missing targets."""
    nodes, declarations = {}, defaultdict(list)
    for original in raw['nodes']:
        node = copy.deepcopy(original)
        node['source_file'] = relative_source(node.get('source_file'), root)
        nid = node['id']
        declaration = {k: node.get(k) for k in ('source_file', 'source_location', 'label', '_origin')}
        if declaration not in declarations[nid]:
            declarations[nid].append(declaration)
        if nid in nodes and nodes[nid].get('label') != node.get('label'):
            if nodes[nid].get('source_file') != node.get('source_file'):
                raise ValueError(f'Conflicting source entities share node ID: {nid}')
            aliases = nodes[nid].setdefault('label_aliases', [])
            if node.get('label') not in aliases:
                aliases.append(node.get('label'))
        if nid not in nodes:
            nodes[nid] = node
        else:
            # Same-ID partial declarations keep provenance instead of last-writer wins.
            for key, value in node.items():
                if key not in nodes[nid] or nodes[nid][key] in (None, '', []):
                    nodes[nid][key] = value
        if node.get('_origin') == 'semantic':
            nodes[nid]['semantic_coverage'] = True
    for nid, evidence in declarations.items():
        if len(evidence) > 1:
            nodes[nid]['declaration_evidence'] = evidence
        if not nodes[nid].get('source_file') and nodes[nid].get('_origin') == 'ast':
            nodes[nid]['definition_available'] = False
            nodes[nid]['symbol_kind'] = 'unresolved_type_reference'
    packages = package_evidence(root)
    repaired_edges, external, aliases, call_fixes = [], {}, [], []
    original_nodes = set(nodes)
    dangling_before = sum(e.get('source') not in original_nodes or
                          e.get('target') not in original_nodes for e in raw['edges'])
    for original in raw['edges']:
        edge = copy.deepcopy(original)
        edge['source_file'] = relative_source(edge.get('source_file'), root)
        if edge.get('source') not in nodes:
            raise ValueError(f'Missing source endpoint: {edge}')
        target = nodes.get(edge.get('target'))
        # The extractor binds Brush()/Action() calls to homonymous type stubs.
        if edge.get('relation') == 'calls' and target and not target.get('_callable'):
            name = target.get('label', '')
            candidates = [n for n in nodes.values() if n.get('_callable') and
                not n.get('_callable_class') and n.get('label') == f'.{name}()' and
                n.get('source_file') == edge['source_file']]
            if len(candidates) == 1 and re.search(r'\b'+re.escape(name)+r'\s*\(', source_line(edge, root)):
                correction = {'source_file': edge['source_file'], 'source_location': edge.get('source_location'),
                              'old_target': edge['target'], 'target': candidates[0]['id']}
                edge['target'] = candidates[0]['id']
                edge['repair'] = 'source_verified_local_callable'
                call_fixes.append(correction)
        if original.get('target') not in original_nodes:
            if edge.get('relation') not in ('imports', 'imports_from'):
                raise ValueError(f'Missing non-import target requires source review: {edge}')
            qualified = verify_csharp_import(edge, root) if Path(edge['source_file']).suffix == '.cs' else None
            resolved = alias_target(edge, qualified, nodes, root) if qualified and '::' in qualified else None
            if resolved:
                aliases.append({'old_target': edge['target'], 'target': resolved,
                                'source_file': edge['source_file'], 'source_location': edge.get('source_location')})
                edge['target'] = resolved
                edge['repair'] = 'source_and_project_verified_extern_alias'
            else:
                name, kind, package = external_import_name(edge, root, packages)
                nid = edge['target']
                evidence = {'source_file': edge['source_file'], 'source_location': edge.get('source_location')}
                if nid not in nodes:
                    nodes[nid] = {'id': nid, 'label': name, 'file_type': 'concept',
                        'source_file': '', 'source_location': None, '_origin': 'source_verified_external',
                        'external': True, 'definition_available': False, 'qualified_name': name,
                        'symbol_kind': kind, 'referenced_from': [], 'package_evidence': package}
                    external[nid] = name
                elif nodes[nid].get('qualified_name') != name:
                    raise ValueError(f'Conflicting qualified names share external ID: {nid}')
                if evidence not in nodes[nid]['referenced_from']:
                    nodes[nid]['referenced_from'].append(evidence)
        edge.setdefault('confidence', 'EXTRACTED')
        if edge['confidence'] == 'EXTRACTED':
            edge['confidence_score'] = 1.0
        if edge.get('confidence_score') is None:
            raise ValueError(f'Unscored non-extracted edge: {edge}')
        repaired_edges.append(edge)
    # Identity includes direction, relation, source line, context and metadata.
    # Only duplicate production of the *same* evidence can share a key.
    evidence_rows = {}
    for edge in repaired_edges:
        identity = {k:v for k,v in edge.items() if k not in ('key', '_src', '_tgt', 'weight', 'extraction_occurrences')}
        key = hashlib.sha256(json.dumps(identity, sort_keys=True, ensure_ascii=False).encode()).hexdigest()
        if key not in evidence_rows:
            evidence_rows[key] = {**edge, 'key': key, 'extraction_occurrences': 0}
        evidence_rows[key]['extraction_occurrences'] += 1
    repaired = {'nodes': list(nodes.values()), 'edges': list(evidence_rows.values()),
                'hyperedges': copy.deepcopy(raw.get('hyperedges', []))}
    audit = {'raw_nodes': len(raw['nodes']), 'raw_edges': len(raw['edges']),
        'dangling_before': dangling_before, 'external_nodes_added': len(external),
        'external_symbols': external, 'extern_alias_repairs': aliases, 'call_target_repairs': call_fixes,
        'distinct_evidence_edges': len(evidence_rows),
        'identical_evidence_duplicates': len(repaired_edges)-len(evidence_rows),
        'evidence_occurrences_preserved': sum(e['extraction_occurrences'] for e in evidence_rows.values()),
        'lost_evidence_occurrences': len(raw['edges'])-sum(e['extraction_occurrences'] for e in evidence_rows.values())}
    return repaired, audit


def build_multigraph(extraction):
    from graphify.multigraph_compat import require_multigraph_capabilities
    require_multigraph_capabilities()
    graph = nx.MultiDiGraph()
    for node in extraction['nodes']:
        graph.add_node(node['id'], **{k:v for k,v in node.items() if k != 'id'})
    for edge in extraction['edges']:
        graph.add_edge(edge['source'], edge['target'], key=edge['key'],
                       **{k:v for k,v in edge.items() if k not in ('source', 'target', 'key')})
    graph.graph['hyperedges'] = extraction.get('hyperedges', [])
    graph.graph['integrity_format'] = 'source-verified-directed-evidence-v1'
    return graph


def verify_graph(data):
    if not data.get('directed') or not data.get('multigraph'):
        raise ValueError('Canonical graph must be directed and preserve parallel evidence')
    nodes = {n['id'] for n in data['nodes']}
    keys = set()
    for edge in data['links']:
        if edge['source'] not in nodes or edge['target'] not in nodes:
            raise ValueError(f'Dangling serialized edge: {edge}')
        identity = (edge['source'], edge['target'], edge['key'])
        if identity in keys:
            raise ValueError(f'Overwritten multigraph key: {identity}')
        keys.add(identity)
    for hyperedge in data.get('hyperedges', []):
        if not set(hyperedge.get('nodes', [])) <= nodes:
            raise ValueError(f'Dangling hyperedge member: {hyperedge}')
    if data.get('graph', {}).get('integrity_format') == 'source-verified-directed-evidence-v1':
        for edge in data['links']:
            identity = {k:v for k,v in edge.items() if k not in
                ('key', '_src', '_tgt', 'weight', 'extraction_occurrences')}
            expected_key = hashlib.sha256(json.dumps(identity, sort_keys=True, ensure_ascii=False).encode()).hexdigest()
            if edge['key'] != expected_key:
                raise ValueError(f'Changed evidence no longer matches stable key: {edge["key"]}')
            if not isinstance(edge.get('extraction_occurrences'), int) or edge['extraction_occurrences'] < 1:
                raise ValueError('Missing evidence occurrence accounting')
    return {'nodes': len(nodes), 'edges': len(keys), 'dangling_after': 0,
            'overwritten_evidence_keys': 0, 'missing_hyperedge_members': 0}


def refresh(allow_partial=False, diagnose_only=False):
    from graphify.detect import detect, save_manifest
    from graphify.diagnostics import diagnose_extraction
    from graphify.cluster import cluster, score_all
    from graphify.analyze import god_nodes, surprising_connections, suggest_questions
    from graphify.export import to_json
    from graphify.exporters.html import to_html
    from graphify.report import generate
    detection = detect_corpus()
    semantic, complete, pending = available_semantic(detection)
    if pending and not allow_partial and not diagnose_only:
        raise ValueError('Semantic extraction required before full refresh: '+', '.join(pending))
    files = [p for fl in detection['files'].values() for p in fl]
    snapshot_hashes = {p: content_hash(p) for p in files}
    raw = cold_extract(detection)
    for key in ('nodes', 'edges', 'hyperedges'):
        raw.setdefault(key, []).extend(semantic[key])
    write_json(OUT/'raw-extraction.json', raw)
    before = diagnose_extraction(copy.deepcopy(raw), directed=False, root=ROOT)
    repaired, audit = repair_extraction(raw)
    write_json(OUT/'repaired-extraction.json', repaired)
    graph = build_multigraph(repaired)
    # Simple projection is only for clustering/report statistics, never persisted
    # as the canonical graph and never used to count retained evidence.
    projection = nx.Graph()
    projection.add_nodes_from(graph.nodes(data=True))
    for source, target in graph.edges():
        if projection.has_edge(source, target):
            projection[source][target]['weight'] += 1
        else:
            projection.add_edge(source, target, weight=1)
    communities = cluster(projection)
    old = read_json(OUT/'graph.json', {'nodes': []})
    old_names = {n['id']:n.get('community_name', '') for n in old['nodes']}
    labels = {}
    for cid, members in communities.items():
        votes = Counter(old_names[n] for n in members if old_names.get(n))
        label = votes.most_common(1)[0][0] if votes else ''
        if not label:
            top = max(members, key=lambda n: projection.degree(n))
            label = graph.nodes[top].get('label', top)+' Concepts'
        labels[cid] = label
    audit['before_simple_graph_diagnostic'] = before
    audit.update({'semantic_complete': complete, 'semantic_pending': pending,
                  'source_count': len(files), 'graph_type': 'MultiDiGraph',
                  'source_hashes': {relative_source(p):h for p,h in snapshot_hashes.items()}})
    candidate = nx.node_link_data(graph, edges='links')
    audit.update(verify_graph(candidate))
    if audit['lost_evidence_occurrences']:
        raise ValueError('Repair lost source evidence')
    write_json(OUT/'integrity-report.json', audit)
    if diagnose_only:
        print(json.dumps({k:v for k,v in audit.items() if k not in
            ('before_simple_graph_diagnostic','external_symbols','semantic_complete','semantic_pending')}, ensure_ascii=False))
        return
    modified = [p for p,h in snapshot_hashes.items() if not Path(p).exists() or content_hash(p) != h]
    if modified:
        raise ValueError('Source changed during refresh; no final export: '+', '.join(modified))
    if not to_json(graph, communities, str(OUT/'graph.json'), community_labels=labels):
        raise ValueError('Shrink guard preserved existing graph; inspect source delta before replacement')
    verify_graph(read_json(OUT/'graph.json'))
    to_html(graph, communities, str(OUT/'graph.html'), community_labels=labels)
    cohesion = score_all(projection, communities)
    gods = god_nodes(projection)
    surprises = surprising_connections(projection, communities)
    questions = suggest_questions(projection, communities, labels)
    report = generate(projection, communities, cohesion, labels, gods, surprises,
                      detection, {'input': 0, 'output': 0}, str(ROOT), suggested_questions=questions)
    report += '\n## Evidence integrity\n\nCanonical graph.json is a directed multigraph. Its edge count is '+str(graph.number_of_edges())+' distinct evidence rows; the report/community statistics above use a disposable simple projection.\n\n'
    report += f'Raw dangling edges: {audit["dangling_before"]}; added source-verified external nodes: {audit["external_nodes_added"]}; local extern-alias fixes: {len(audit["extern_alias_repairs"])}; same-name call fixes: {len(audit["call_target_repairs"])}.\n\n'
    report += f'Original simple-graph collapse candidates: {before.get("undirected_same_endpoint_collapsed_edges",0)}. Actual lost evidence: {audit["lost_evidence_occurrences"]}; repaired dangling endpoints: 0. Exact repeated production is retained as extraction_occurrences, while distinct lines/relations and reverse directions keep separate edge keys.\n\n'
    report += f'Semantic coverage: {len(complete)} complete, {len(pending)} pending supported non-code files. Cold per-run extraction prevents XAML caches from replaying obsolete code-behind locations.\n\n'
    report += 'No external API calls were made. API token counters are zero; host-agent token usage is unavailable. Unsupported or unresolved imports fail the rebuild instead of fabricating a target.\n'
    (OUT/'GRAPH_REPORT.md').write_text(report, encoding='utf-8')
    write_json(OUT/'.graphify_labels.json', labels)
    write_json(OUT/'.graphify_analysis.json', {'communities': communities, 'cohesion': cohesion,
        'gods': gods, 'surprises': surprises, 'questions': questions})
    write_json(OUT/'.graphify_health.json', {k:audit[k] for k in
        ('dangling_before','dangling_after','lost_evidence_occurrences','overwritten_evidence_keys','missing_hyperedge_members')})
    represented = {n.get('source_file') for n in repaired['nodes'] if n.get('_origin') == 'ast'}
    code = [p for p in detection['files']['code'] if relative_source(p) in represented]
    zero = [p for p in detection['files']['code'] if p not in code]
    save_manifest({'code': code}, root=ROOT, kind='ast', scan_corpus=set(files), clear_ast=set(zero))
    save_manifest({'document': [str(ROOT/p) for p in complete]}, root=ROOT, kind='semantic',
                  clear_semantic={str(ROOT/p) for p in pending})
    (OUT/'.graphify_root').write_text(str(ROOT), encoding='utf-8')
    if pending:
        write_json(OUT/'.needs_update', {'semantic_retry': pending, 'zero_ast_files': zero})
    else:
        (OUT/'.needs_update').unlink(missing_ok=True)
    cost = read_json(OUT/'cost.json', {'runs': [], 'total_input_tokens': 0, 'total_output_tokens': 0})
    cost['runs'].append({'date': datetime.now(timezone.utc).isoformat(), 'files': len(files),
        'input_tokens': 0, 'output_tokens': 0, 'api_calls': 0, 'host_agent_token_usage': 'unavailable'})
    write_json(OUT/'cost.json', cost)
    print(json.dumps({k:audit[k] for k in ('source_count','nodes','edges','dangling_before','dangling_after',
        'external_nodes_added','lost_evidence_occurrences')}, ensure_ascii=False))
    print(f'Semantic coverage: {len(complete)} complete, {len(pending)} pending.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    command = sub.add_parser('import-semantic')
    command.add_argument('--fragment', type=Path, required=True)
    command.add_argument('--hashes', type=Path, required=True)
    command = sub.add_parser('refresh')
    command.add_argument('--allow-partial', action='store_true')
    sub.add_parser('diagnose')
    sub.add_parser('verify')
    args = parser.parse_args()
    if args.command == 'import-semantic':
        import_semantic(args.fragment, args.hashes)
    elif args.command == 'verify':
        data = read_json(OUT/'graph.json')
        result = verify_graph(data)
        audit = read_json(OUT/'integrity-report.json', {})
        stale = [p for p,h in audit.get('source_hashes', {}).items()
                 if not (ROOT/p).is_file() or content_hash(ROOT/p) != h]
        if stale:
            raise ValueError('Source changed after graph build: '+', '.join(stale))
        occurrences = sum(e.get('extraction_occurrences', 1) for e in data['links'])
        if audit.get('evidence_occurrences_preserved') != occurrences:
            raise ValueError('Serialized graph does not preserve audited evidence occurrence count')
        result.update({'stale_sources': 0, 'lost_evidence_occurrences': audit.get('lost_evidence_occurrences')})
        print(json.dumps(result))
    else:
        refresh(getattr(args, 'allow_partial', True), args.command == 'diagnose')


if __name__ == '__main__':
    main()
