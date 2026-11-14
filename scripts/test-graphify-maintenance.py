"""Offline regression checks for source verification and lossless graph repair."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('graphify_maintenance', Path(__file__).with_name('graphify-maintenance.py'))
maintenance = importlib.util.module_from_spec(spec)
spec.loader.exec_module(maintenance)


def node(identifier, label=None, source='', **attributes):
    return {'id': identifier, 'label': label or identifier, 'source_file': source,
            'file_type': 'code', '_origin': 'ast', **attributes}


def edge(source, target, line=1, relation='references', file='impls/App/View.cs', **attributes):
    return {'source': source, 'target': target, 'relation': relation,
            'source_file': file, 'source_location': f'L{line}',
            'confidence': 'EXTRACTED', **attributes}


class GraphRepairTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='.repair-test-', dir=maintenance.OUT)
        self.root = Path(self.temporary.name).resolve()
        self.root.relative_to(maintenance.OUT.resolve())
        (self.root/'impls/App').mkdir(parents=True)

    def tearDown(self):
        self.temporary.cleanup()

    def write(self, name, text):
        path = self.root/name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding='utf-8')
        return path

    def test_external_imports_require_and_keep_source_evidence(self):
        self.write('impls/App/View.cs', 'using System.Text.Json;\nusing System.Text.Json;\n')
        imports = [edge('file', 'system_text_json', line, 'imports',
                        metadata={'target_fqn': 'System.Text.Json', 'using_kind': 'namespace'})
                   for line in (1, 2)]
        repaired, audit = maintenance.repair_extraction({'nodes': [node('file')], 'edges': imports}, self.root)
        external = next(n for n in repaired['nodes'] if n['id'] == 'system_text_json')
        self.assertTrue(external['external'])
        self.assertFalse(external['definition_available'])
        self.assertEqual(2, len(external['referenced_from']))
        self.assertEqual(2, audit['dangling_before'])
        self.assertEqual(1, audit['external_nodes_added'])
        self.assertEqual(2, len(repaired['edges']))

    def test_stale_or_fabricated_import_is_rejected(self):
        self.write('impls/App/View.cs', 'using System.IO;\n')
        bad = edge('file', 'system_text_json', relation='imports',
                   metadata={'target_fqn': 'System.Text.Json', 'using_kind': 'namespace'})
        with self.assertRaisesRegex(ValueError, 'does not match source'):
            maintenance.repair_extraction({'nodes': [node('file')], 'edges': [bad]}, self.root)

    def test_unknown_external_namespace_is_rejected(self):
        self.write('impls/App/View.cs', 'using Invented.Package;\n')
        bad = edge('file', 'invented_package', relation='imports',
                   metadata={'target_fqn': 'Invented.Package', 'using_kind': 'namespace'})
        with self.assertRaisesRegex(ValueError, 'Cannot prove'):
            maintenance.repair_extraction({'nodes': [node('file')], 'edges': [bad]}, self.root)

    def test_package_import_uses_its_own_project_and_ignores_wpf_build_copies(self):
        self.write('impls/App/Nested/View.cs', 'using Markdig;\nusing Markdig.Syntax;\n')
        project = '<Project><ItemGroup><PackageReference Include="Markdig" Version="1.4.2"/></ItemGroup></Project>'
        self.write('impls/App/App.csproj', project)
        self.write('impls/App/App_abcd_wpftmp.csproj', project)
        self.write('impls/App/obj/App.csproj', project)
        self.write('impls/Other/Other.csproj', project.replace('1.4.2', '0.37.0'))
        imports = [edge('file', name.lower().replace('.', '_'), line, 'imports', file='impls/App/Nested/View.cs',
                        metadata={'target_fqn': name, 'using_kind': 'namespace'})
                   for line, name in enumerate(('Markdig', 'Markdig.Syntax'), 1)]
        repaired, _ = maintenance.repair_extraction({'nodes': [node('file')], 'edges': imports}, self.root)
        dependencies = [n for n in repaired['nodes'] if n.get('external')]
        self.assertEqual(2, len(dependencies))
        self.assertTrue(all(n['package_evidence'] == {'name': 'Markdig', 'version': '1.4.2',
            'source_file': 'impls/App/App.csproj'} for n in dependencies))

    def test_sibling_or_generated_project_cannot_prove_package_import(self):
        self.write('impls/App/View.cs', 'using Markdig;\n')
        self.write('impls/App/App.csproj', '<Project/>')
        project = '<Project><ItemGroup><PackageReference Include="Markdig" Version="1.4.2"/></ItemGroup></Project>'
        self.write('impls/App/App_abcd_wpftmp.csproj', project)
        self.write('impls/Other/Other.csproj', project)
        imported = edge('file', 'markdig', relation='imports', metadata={'target_fqn': 'Markdig'})
        with self.assertRaisesRegex(ValueError, 'Cannot prove'):
            maintenance.repair_extraction({'nodes': [node('file')], 'edges': [imported]}, self.root)

    def test_source_detection_excludes_transient_wpf_projects(self):
        real = self.write('impls/App/App.csproj', '<Project/>')
        transient = self.write('impls/App/App_abcd_wpftmp.csproj', '<Project/>')
        detection = {'files': {'code': [str(real), str(transient)], 'document': []}, 'total_files': 2}
        with patch.object(maintenance, 'ROOT', self.root), patch('graphify.detect.detect', return_value=detection):
            filtered = maintenance.detect_corpus()
        self.assertEqual([str(real)], filtered['files']['code'])
        self.assertEqual(1, filtered['total_files'])
        self.assertEqual(['impls/App/App_abcd_wpftmp.csproj'], filtered['skipped_generated_projects'])

    def test_windows_location_import_requires_own_versioned_sdk_target(self):
        self.write('impls/App/View.cs', 'using Windows.Devices.Geolocation;\n')
        imported = edge('file', 'windows_devices_geolocation', relation='imports',
                        metadata={'target_fqn': 'Windows.Devices.Geolocation', 'using_kind': 'namespace'})
        self.write('impls/Other/Other.csproj', '<Project><PropertyGroup><TargetFramework>net10.0-windows10.0.19041.0</TargetFramework></PropertyGroup></Project>')
        self.write('impls/App/App.csproj', '<Project><PropertyGroup><TargetFramework>net10.0-windows</TargetFramework></PropertyGroup></Project>')
        with self.assertRaisesRegex(ValueError, 'Cannot prove'):
            maintenance.repair_extraction({'nodes': [node('file')], 'edges': [imported]}, self.root)
        self.write('impls/App/App.csproj', '<Project><PropertyGroup><TargetFramework>net10.0-windows10.0.19041.0</TargetFramework></PropertyGroup></Project>')
        repaired, _ = maintenance.repair_extraction({'nodes': [node('file')], 'edges': [imported]}, self.root)
        external = next(n for n in repaired['nodes'] if n['id'] == 'windows_devices_geolocation')
        self.assertTrue(external['external'])
        self.assertEqual('net10.0-windows10.0.19041.0', external['package_evidence']['target_framework'])
        self.assertEqual('impls/App/App.csproj', external['package_evidence']['source_file'])
        self.write('impls/App/App_abcd_wpftmp.csproj', '<Project><PropertyGroup><TargetFramework>net10.0-windows10.0.19041.0</TargetFramework></PropertyGroup></Project>')
        repaired, _ = maintenance.repair_extraction({'nodes': [node('file')], 'edges': [imported]}, self.root)
        self.assertEqual('impls/App/App.csproj', repaired['nodes'][-1]['package_evidence']['source_file'])

    def test_extern_alias_binds_to_verified_project_definition(self):
        self.write('impls/App/View.cs', 'extern alias Launcher;\nusing ReleaseManifest = Launcher::Demo.ReleaseManifest;\n')
        self.write('impls/App/App.csproj', '<Project><ItemGroup><ProjectReference Include="../Launcher/Launcher.csproj" Aliases="Launcher" /></ItemGroup></Project>')
        self.write('impls/Launcher/Launcher.csproj', '<Project/>')
        self.write('impls/Launcher/ReleaseManifest.cs', 'namespace Demo; class ReleaseManifest {}')
        target = node('manifest', 'ReleaseManifest', 'impls/Launcher/ReleaseManifest.cs', metadata={'namespace': 'Demo'})
        imported = edge('file', 'launcher_demo_releasemanifest', 2, 'imports',
                        metadata={'target_fqn': 'Launcher::Demo.ReleaseManifest', 'using_kind': 'alias'})
        repaired, audit = maintenance.repair_extraction({'nodes': [node('file'), target], 'edges': [imported]}, self.root)
        self.assertEqual('manifest', repaired['edges'][0]['target'])
        self.assertEqual(1, len(audit['extern_alias_repairs']))
        self.assertEqual(0, audit['external_nodes_added'])

    def test_homonymous_call_uses_local_callable(self):
        self.write('impls/App/View.cs', 'class View {\n Brush Brush(string key) => null;\n void Render() { Brush("Ink"); }\n}')
        nodes = [node('render', '.Render()', 'impls/App/View.cs', _callable=True),
                 node('brush_type', 'Brush'),
                 node('brush_method', '.Brush()', 'impls/App/View.cs', _callable=True)]
        repaired, audit = maintenance.repair_extraction({'nodes': nodes,
            'edges': [edge('render', 'brush_type', 3, 'calls')]}, self.root)
        self.assertEqual('brush_method', repaired['edges'][0]['target'])
        self.assertEqual(1, len(audit['call_target_repairs']))

    def test_parallel_reverse_and_distinct_relation_evidence_roundtrips(self):
        first = edge('a', 'b', 1, 'calls')
        raw = {'nodes': [node('a'), node('b')], 'edges': [first, copy.deepcopy(first),
            edge('a', 'b', 2, 'calls'), edge('b', 'a', 3, 'references')], 'hyperedges': []}
        repaired, audit = maintenance.repair_extraction(raw, self.root)
        graph = maintenance.build_multigraph(repaired)
        self.assertEqual(3, graph.number_of_edges())
        self.assertEqual(4, sum(e['extraction_occurrences'] for e in repaired['edges']))
        self.assertEqual(0, audit['lost_evidence_occurrences'])
        data = maintenance.nx.node_link_data(graph, edges='links')
        loaded = maintenance.nx.node_link_graph(json.loads(json.dumps(data)), edges='links')
        self.assertTrue(loaded.has_edge('b', 'a'))
        self.assertEqual(2, loaded.number_of_edges('a', 'b'))
        self.assertEqual(0, maintenance.verify_graph(data)['dangling_after'])

    def test_duplicate_multigraph_keys_are_rejected(self):
        data = {'directed': True, 'multigraph': True, 'nodes': [{'id': 'a'}, {'id': 'b'}],
                'links': [{'source': 'a', 'target': 'b', 'key': 'same'},
                          {'source': 'a', 'target': 'b', 'key': 'same'}]}
        with self.assertRaisesRegex(ValueError, 'Overwritten multigraph key'):
            maintenance.verify_graph(data)

    def test_cold_xaml_extraction_cannot_replay_old_codebehind_lines(self):
        xaml = self.write('impls/App/App.xaml', '<Application x:Class="Demo.App" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"/>')
        code = self.write('impls/App/App.xaml.cs', 'namespace Demo;\npublic partial class App : Application {\n private void Original() {}\n}')
        output = self.root/'output'; output.mkdir()
        detection = {'files': {'code': [str(xaml), str(code)], 'document': []}}
        maintenance.cold_extract(detection, self.root, output)
        code.write_text('\n'*5+code.read_text(encoding='utf-8'), encoding='utf-8')
        fresh = maintenance.cold_extract(detection, self.root, output)
        locations = {n.get('source_location') for n in fresh['nodes'] if n.get('label') == '.Original()'}
        self.assertEqual({'L8'}, locations)

    def test_json_configuration_is_parsed_and_has_a_file_anchor(self):
        config = self.write('global.json', '{"sdk":{"version":"10.0.100"}}')
        output = self.root/'output'; output.mkdir()
        result = maintenance.cold_extract({'files': {'code': [str(config)], 'document': []}}, self.root, output)
        anchor = next(n for n in result['nodes'] if n['label'] == 'global.json')
        self.assertEqual(['sdk'], anchor['configuration_keys'])
        self.assertEqual('json', anchor['config_format'])

    def test_xml_configuration_is_parsed_and_has_a_file_anchor(self):
        config = self.write('NuGet.Config', '<configuration><packageSources/><packageSourceMapping/></configuration>')
        output = self.root/'output'; output.mkdir()
        result = maintenance.cold_extract({'files': {'code': [str(config)], 'document': []}}, self.root, output)
        anchor = next(n for n in result['nodes'] if n['label'] == 'NuGet.Config')
        self.assertEqual(['packageSourceMapping', 'packageSources'], anchor['configuration_keys'])
        self.assertEqual('xml', anchor['config_format'])


if __name__ == '__main__':
    unittest.main()
