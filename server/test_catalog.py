"""Catalog compatibility checks with only the Python standard library."""
import ast
from pathlib import Path
import re
import unittest


class CatalogTests(unittest.TestCase):
    def test_server_and_unity_ids_match(self):
        root = Path(__file__).parents[1]
        server = ast.parse((root / 'server/main.py').read_text())
        item_type = next(node.value for node in server.body if isinstance(node, ast.Assign)
                         and any(isinstance(t, ast.Name) and t.id == 'ItemType' for t in node.targets))
        ids = [ast.literal_eval(node) for node in item_type.slice.elts]
        unity = (root / 'Assets/Scripts/TrashTypes.cs').read_text()
        unity_ids = re.findall(r'case ItemType\.\w+: return "([a-z0-9_]+)";', unity)
        self.assertEqual(set(ids), set(unity_ids))
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(len(ids), 24)

    def test_original_serialized_enum_values_are_preserved(self):
        source = (Path(__file__).parents[1] / 'Assets/Scripts/TrashTypes.cs').read_text()
        body = re.search(r'public enum ItemType\s*\{(.*?)\}', source, re.S).group(1)
        body = re.sub(r'//[^\n]*', '', body)
        names = [name.strip() for name in body.split(',') if name.strip()]
        self.assertEqual(names[:6], ['AluminumCan', 'PlasticBottle', 'CardboardBox',
                                   'CrumpledPaper', 'BatteryAA', 'PowerBank'])
        self.assertEqual(len(names), 24)


if __name__ == '__main__':
    unittest.main()
