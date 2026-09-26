"""Portable first-run launcher: py server/host.py (Windows), python3 server/host.py (Mac/Linux)."""
import argparse
import shutil
import subprocess
import sys
import venv
from pathlib import Path


def environment_python(root):
    return root / '.venv' / ('Scripts/python.exe' if sys.platform == 'win32' else 'bin/python')


def prepare_environment_file(root):
    """Never overwrite an existing credential file."""
    env = root / '.env'
    if env.exists():
        return False
    shutil.copyfile(root / '.env.example', env)
    if sys.platform != 'win32':
        env.chmod(0o600)
    return True


def main():
    parser = argparse.ArgumentParser(description='Host the SortQuest API on this computer.')
    parser.add_argument('--check', action='store_true', help='Check config and MongoDB access, then exit without starting a listener.')
    parser.add_argument('--skip-install', action='store_true', help='Reuse dependencies already installed in .venv.')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    if sys.version_info < (3, 9):
        print('Python 3.9 or newer is required.', file=sys.stderr)
        return 1
    if prepare_environment_file(root):
        print(f'Created {root / ".env"}. Set MONGODB_URI and MONGODB_DB, then run this command again.')
        print('Use the existing Atlas database to share the same data and schema. Obtain credentials privately.')
        return 1
    python = environment_python(root)
    if not python.exists():
        print('Creating local Python environment...', flush=True)
        venv.EnvBuilder(with_pip=True).create(root / '.venv')
    if not args.skip_install:
        result = subprocess.run([str(python), '-m', 'pip', 'install', '-r', str(root / 'server/requirements.txt')], cwd=root)
        if result.returncode:
            return result.returncode
    command = [str(python), '-m', 'server.run']
    if args.check:
        command.append('--check')
    return subprocess.call(command, cwd=root)


if __name__ == '__main__':
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        sys.exit(0)
