import tempfile
import unittest
from pathlib import Path
from server.host import prepare_environment_file
from server.run import validate_config


class HostTests(unittest.TestCase):
    def test_new_env_and_existing_secrets_preserved(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / '.env.example').write_text('example')
            self.assertTrue(prepare_environment_file(root))
            (root / '.env').write_text('private existing config')
            self.assertFalse(prepare_environment_file(root))
            self.assertEqual((root / '.env').read_text(), 'private existing config')

    def test_placeholder_and_missing_database_rejected(self):
        for config in ({}, {'MONGODB_URI': 'mongodb+srv://YOUR_USERNAME@example', 'MONGODB_DB': 'db'}, {'MONGODB_URI': 'mongodb://localhost'}):
            with self.assertRaises(ValueError):
                validate_config(config)

    def test_port_bounds(self):
        for port in ('0', '65536', 'abc'):
            with self.assertRaises(ValueError):
                validate_config({'MONGODB_URI': 'mongodb://localhost', 'MONGODB_DB': 'db', 'PORT': port})
        self.assertEqual(validate_config({'MONGODB_URI': 'mongodb://localhost', 'MONGODB_DB': 'db'})[2], 8000)
