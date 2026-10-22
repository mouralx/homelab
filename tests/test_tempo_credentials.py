import importlib.util
from pathlib import Path
import stat
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('credentials', Path(__file__).resolve().parents[1] / 'scripts/prepare-tempo-env.py')
credentials = importlib.util.module_from_spec(spec)
spec.loader.exec_module(credentials)

class CredentialTests(unittest.TestCase):
    def test_credentials_persist_and_are_private(self):
        with tempfile.TemporaryDirectory() as root:
            directory = Path(root)/'tempo'
            first = credentials.prepare(directory,{})
            self.assertEqual(first, credentials.prepare(directory,{}))
            self.assertEqual(4,len(set(first.values())))
            self.assertEqual(0o600,stat.S_IMODE((directory/'credentials.json').stat().st_mode))
            self.assertEqual(0o700,stat.S_IMODE(directory.stat().st_mode))

    def test_overrides_are_preserved_and_changes_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            override = {credentials.KEYS[0]:'A_secure_example_password_123'}
            first = credentials.prepare(Path(root),override)
            self.assertEqual(override[credentials.KEYS[0]],first[credentials.KEYS[0]])
            self.assertEqual(first,credentials.prepare(Path(root),override))
            with self.assertRaises(ValueError):
                credentials.prepare(Path(root),{credentials.KEYS[0]:'A_different_password_123'})

    def test_unsafe_connection_string_values_are_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            with self.assertRaises(ValueError):
                credentials.prepare(Path(root),{credentials.KEYS[0]:'unsafe;Password=other'})

if __name__ == '__main__':
    unittest.main()
