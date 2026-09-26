"""Offline API contract checks: python -m unittest discover -s server."""
import os
import unittest
from unittest.mock import MagicMock, patch
from fastapi.testclient import TestClient
from pymongo.errors import BulkWriteError, ServerSelectionTimeoutError
from server.main import app, require_api_key


def record():
    return dict(record_id='a' * 32, session_id='test', player='test', timestamp='2026-09-26T14:03:11Z', source='human', item_type='battery_aa', correct_bin='hazardous', hand='right', grasp=dict(pos_local=[0, 0, 0], rot_local=[0, 0, 0, 1], width_m=.016), item_pose_world=dict(pos=[0, 0, 0], rot=[0, 0, 0, 1]), outcome=dict(bin='hazardous', correct=True, dropped=False, hold_s=1.6))


class ApiTests(unittest.TestCase):
    def setUp(self):
        app.state.db = MagicMock()
        app.dependency_overrides[require_api_key] = lambda: None
        self.client = TestClient(app)  # No lifespan: no real database connection.

    def tearDown(self):
        self.client.close()
        app.dependency_overrides.clear()

    def test_upload_sets_server_fields(self):
        app.state.db.grasps.insert_many.return_value.inserted_ids = ['a' * 32]
        response = self.client.post('/grasps', json={'records': [record()]})
        self.assertEqual(response.json(), {'inserted': 1, 'duplicates': 0})
        document = app.state.db.grasps.insert_many.call_args.args[0][0]
        self.assertEqual(document['_id'], document['record_id'])
        self.assertIsNotNone(document['created_at'].tzinfo)
        self.assertIn('received_at', document)

    def test_duplicate_batch(self):
        app.state.db.grasps.insert_many.side_effect = BulkWriteError({'nInserted': 0, 'writeErrors': [{'code': 11000}], 'writeConcernErrors': []})
        self.assertEqual(self.client.post('/grasps', json={'records': [record()]}).json(), {'inserted': 0, 'duplicates': 1})

    def test_nonduplicate_failure_is_not_success(self):
        app.state.db.grasps.insert_many.side_effect = BulkWriteError({'nInserted': 0, 'writeErrors': [{'code': 121}], 'writeConcernErrors': []})
        self.assertEqual(self.client.post('/grasps', json={'records': [record()]}).status_code, 503)

    def test_bad_inputs_do_not_write(self):
        for change in ({'timestamp': '2026-09-26T14:03:11'}, {'source': 'bad'}, {'record_id': 'bad'}, {'grasp': {'pos_local': [0], 'rot_local': [0, 0, 0, 1], 'width_m': .1}}):
            self.assertEqual(self.client.post('/grasps', json={'records': [dict(record(), **change)]}).status_code, 422)
        self.assertEqual(self.client.post('/grasps', json={'records': [record()] * 101}).status_code, 422)
        app.state.db.grasps.insert_many.assert_not_called()

    def test_query_bounds(self):
        for query in ('limit=1001', 'source=unknown', 'item_type=unknown'):
            self.assertEqual(self.client.get('/grasps?' + query).status_code, 422)

    def test_authentication(self):
        app.dependency_overrides.clear()
        with patch.dict(os.environ, {'SORTQUEST_API_KEY': 'test-key'}):
            self.assertEqual(self.client.get('/stats').status_code, 401)
            self.assertEqual(self.client.get('/stats', headers={'X-API-Key': 'wrong'}).status_code, 401)
            self.assertEqual(self.client.get('/health').status_code, 200)

    def test_unconfigured_key_fails_closed(self):
        app.dependency_overrides.clear()
        with patch.dict(os.environ, {'SORTQUEST_API_KEY': ''}):
            self.assertEqual(self.client.get('/grasps').status_code, 503)

    def test_database_error_hides_credentials(self):
        app.state.db.command.side_effect = ServerSelectionTimeoutError('secret-connection-details')
        response = self.client.get('/health')
        self.assertEqual(response.status_code, 503)
        self.assertNotIn('secret-connection-details', response.text)

    def test_read_filter_and_projection(self):
        cursor = app.state.db.grasps.find.return_value.sort.return_value.limit.return_value.max_time_ms.return_value
        cursor.__iter__.return_value = iter([])
        self.assertEqual(self.client.get('/grasps?good=true&source=human,augmented&item_type=battery_aa').status_code, 200)
        filters, projection = app.state.db.grasps.find.call_args.args
        self.assertEqual(filters['source'], {'$in': ['human', 'augmented']})
        self.assertEqual(filters['outcome.dropped'], False)
        self.assertEqual(projection['_id'], 0)

    def test_write_concern_failure_is_retryable(self):
        app.state.db.grasps.insert_many.side_effect = BulkWriteError({'nInserted': 1, 'writeErrors': [], 'writeConcernErrors': [{'code': 64}]})
        self.assertEqual(self.client.post('/grasps', json={'records': [record()]}).status_code, 503)


if __name__ == '__main__':
    unittest.main()
