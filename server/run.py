"""Start the API on this computer's LAN: python -m server.run."""
import argparse
import os
import secrets
import socket
from pathlib import Path

import certifi
from dotenv import load_dotenv, set_key
from pymongo import MongoClient


def validate_config(config):
    uri = config.get('MONGODB_URI', '')
    database = config.get('MONGODB_DB', '')
    if not uri.startswith(('mongodb://', 'mongodb+srv://')) or 'YOUR_' in uri or not database:
        raise ValueError('Set real MONGODB_URI and MONGODB_DB values in .env before hosting.')
    try:
        port = int(config.get('PORT', '8000'))
    except ValueError:
        raise ValueError('PORT must be an integer from 1 to 65535.') from None
    if not 1 <= port <= 65535:
        raise ValueError('PORT must be an integer from 1 to 65535.')
    return uri, database, port


def check_database(uri, database):
    options = {'serverSelectionTimeoutMS': 7000, 'connectTimeoutMS': 7000, 'socketTimeoutMS': 7000}
    if uri.startswith('mongodb+srv://') or 'tls=true' in uri.lower():
        options['tlsCAFile'] = certifi.where()
    try:
        with MongoClient(uri, **options) as client:
            db = client[database]
            db.command('ping')
            db.grasps.find_one({}, {'_id': 1}, max_time_ms=5000)
            # Schema setup is a separate, deliberate operation; don't create an unvalidated collection.
            if 'grasps' not in db.list_collection_names() or not db.grasps.options().get('validator'):
                raise ValueError('The selected database has no validated grasps collection. Use the already configured database or apply the README schema first.')
    except ValueError:
        raise
    except Exception:
        raise ValueError('MongoDB access check failed. Check credentials, network access, and this computer\'s public IP in Atlas Network Access. Connection details were hidden.') from None


def lan_address():
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
            probe.connect(('192.0.2.1', 80))
            return probe.getsockname()[0]
    except OSError:
        return 'YOUR_COMPUTER_LAN_IP'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    env = Path(__file__).resolve().parents[1] / '.env'
    load_dotenv(env)
    try:
        uri, database, port = validate_config(os.environ)
        check_database(uri, database)
    except ValueError as error:
        print(str(error), flush=True)
        return 1
    print('MongoDB connection, read access, and collection validator: PASS', flush=True)
    if args.check:
        return 0
    if not os.environ.get('SORTQUEST_API_KEY'):
        key = secrets.token_urlsafe(32)
        set_key(str(env), 'SORTQUEST_API_KEY', key)
        os.environ['SORTQUEST_API_KEY'] = key
        print('Created SORTQUEST_API_KEY in .env. The Unity setup menu imports this key.', flush=True)
    print(f'Quest API URL: http://{lan_address()}:{port}', flush=True)
    print('Keep this window open. Run SortQuest > Configure LAN API in Unity on this computer.', flush=True)
    import uvicorn
    uvicorn.run('server.main:app', host='0.0.0.0', port=port)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
