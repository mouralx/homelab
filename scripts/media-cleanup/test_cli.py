import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import controller as c


class CLITests(unittest.TestCase):
    def test_secret_env_file_and_xml(self):
        with tempfile.TemporaryDirectory() as tmp, patch.dict(os.environ, {}, clear=True):
            file = Path(tmp) / 'key'
            file.write_text('fixture-key\n')
            os.environ['RADARR_API_KEY_FILE'] = str(file)
            self.assertEqual(c.secret('RADARR_API_KEY'), 'fixture-key')
            os.environ['RADARR_API_KEY'] = 'env-fixture'
            self.assertEqual(c.secret('RADARR_API_KEY'), 'env-fixture')
            del os.environ['RADARR_API_KEY']
            del os.environ['RADARR_API_KEY_FILE']
            file.write_text('<Config><ApiKey>xml-fixture</ApiKey></Config>')
            os.environ['RADARR_CONFIG_XML'] = str(file)
            self.assertEqual(c.radarr_key(), 'xml-fixture')

    def test_cli_default_dry_run_and_no_mount_fails(self):
        self.assertFalse(c.parser().parse_args([]).apply)
        self.assertTrue(c.parser().parse_args(['--apply']).apply)
        with tempfile.TemporaryDirectory() as tmp:
            env = {'PATH': os.environ.get('PATH', ''), 'RADARR_API_KEY': 'test-only-key',
                   'MEDIA_VOLUME_ID': 'test-volume'}
            run = subprocess.run([sys.executable, str(Path(__file__).with_name('controller.py')), '--state-dir', tmp],
                                 env=env, text=True, capture_output=True)
            self.assertEqual(run.returncode, 2)
            self.assertIn('stopped', run.stderr)
            self.assertNotIn('test-only-key', run.stderr)


if __name__ == '__main__':
    unittest.main()
