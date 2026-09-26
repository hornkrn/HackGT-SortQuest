"""Explicit live HTTP/MongoDB test; inserts unique fixtures and deletes only those fixtures.
Run: python -m server.test_live --base-url http://COMPUTER_IP:8000
"""
import argparse
import uuid
import httpx
from server.main import GraspRecord
from server.test_api import record
import os
import certifi
from pymongo import MongoClient
from pymongo.errors import WriteError


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--base-url', default='http://127.0.0.1:8000')
    args = parser.parse_args()
    session_id = 'api-test-' + uuid.uuid4().hex
    fixtures = []
    for source, correct, dropped in [('human', True, False), ('augmented', True, False), ('robot', True, False), ('human', True, True)]:
        item = record()
        item.update(record_id=uuid.uuid4().hex, session_id=session_id, source=source)
        item['outcome'].update(correct=correct, dropped=dropped)
        item['image'] = dict(id='', cam_pos=[0, 0, 0], cam_rot=[0, 0, 0, 1], fov_y_deg=0, rgb_size=0, depth_size=0)
        fixtures.append(item)
    ids = [item['record_id'] for item in fixtures]
    options = {'serverSelectionTimeoutMS': 5000}
    if os.environ['MONGODB_URI'].startswith('mongodb+srv://'):
        options['tlsCAFile'] = certifi.where()
    with MongoClient(os.environ['MONGODB_URI'], **options) as mongo, httpx.Client(base_url=args.base_url, timeout=20) as client:
        collection = mongo[os.environ['MONGODB_DB']].grasps
        headers = {'X-API-Key': os.environ['SORTQUEST_API_KEY']}
        try:
            assert client.get('/health').json() == {'ok': True}
            for path in ['/grasps', '/stats']:
                assert client.get(path).status_code == 401
                assert client.get(path, headers={'X-API-Key': 'wrong'}).status_code == 401
            assert client.post('/grasps', json={'records': fixtures}).status_code == 401
            print('Health and authentication: PASS')
            result = client.post('/grasps', headers=headers, json={'records': fixtures})
            assert result.status_code == 200, result.status_code
            assert result.json() == {'inserted': 4, 'duplicates': 0}
            assert client.post('/grasps', headers=headers, json={'records': fixtures}).json() == {'inserted': 0, 'duplicates': 4}
            assert collection.count_documents({'_id': {'$in': ids}}) == 4
            for doc in collection.find({'_id': {'$in': ids}}):
                assert doc['_id'] == doc['record_id']
                assert doc['created_at'] and doc['received_at']
                assert doc['image']['id'] == ''
            print('HTTP writes, duplicate retries, MongoDB persistence and dates: PASS')
            results = client.get('/grasps?good=true&source=human,augmented&item_type=battery_aa', headers=headers).json()['records']
            found = {r['record_id'] for r in results} & set(ids)
            assert found == set(ids[:2]), found
            for result in results:
                GraspRecord.model_validate(result)
                assert '_id' not in result
            bad_results = client.get('/grasps?good=false&item_type=battery_aa', headers=headers).json()['records']
            assert ({r['record_id'] for r in bad_results} & set(ids)) == {ids[3]}
            assert len(client.get('/grasps?limit=1', headers=headers).json()['records']) == 1
            print('Read-back, good/source/item filters and result limits: PASS')
            for change in [{'source': 'invalid'}, {'record_id': 'invalid'}, {'timestamp': 'no date'}, {'grasp': {'pos_local': [0], 'rot_local': [0, 0, 0, 1], 'width_m': 1}}]:
                assert client.post('/grasps', headers=headers, json={'records': [dict(fixtures[0], **change)]}).status_code == 422
            assert client.post('/grasps', headers=headers, json={'records': fixtures * 26}).status_code == 422
            for query in ['limit=1001', 'limit=0', 'source=invalid', 'item_type=invalid']:
                assert client.get('/grasps?' + query, headers=headers).status_code == 422
            stats = client.get('/stats', headers=headers).json()
            assert stats['total'] >= 4
            assert any(row['source'] == 'robot' and row['good'] >= 1 for row in stats['counts'])
            assert all(0 <= row['success_rate'] <= 1 for row in stats['robot_daily'])
            print('Request validation, batch bounds and statistics: PASS')
            try:
                collection.update_one({'_id': ids[0]}, {'$set': {'source': 'invalid'}})
                raise AssertionError('Database validation did not reject invalid source')
            except WriteError as error:
                assert error.code == 121
            print('Database schema enforcement: PASS')
        finally:
            collection.delete_many({'_id': {'$in': ids}, 'session_id': session_id})
            assert collection.count_documents({'_id': {'$in': ids}}) == 0
            print('Test fixtures cleaned up.')


if __name__ == '__main__':
    main()
