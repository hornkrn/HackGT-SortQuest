"""Offline API contract checks: python -m unittest discover -s server."""
import os
import unittest
import re
from pathlib import Path
from typing import get_args
from unittest.mock import MagicMock, patch
from fastapi.testclient import TestClient
from pymongo.errors import BulkWriteError, ServerSelectionTimeoutError
from server.main import app, require_api_key, ItemType


def record():
    return dict(record_id='a' * 32, session_id='test', player='test', timestamp='2026-09-26T14:03:11Z', source='human', item_type='battery_aa', correct_bin='hazardous', hand='right', grasp=dict(pos_local=[0, 0, 0], rot_local=[0, 0, 0, 1], width_m=.016), item_pose_world=dict(pos=[0, 0, 0], rot=[0, 0, 0, 1]), outcome=dict(bin='hazardous', correct=True, dropped=False, hold_s=1.6))


class ApiTests(unittest.TestCase):
    def test_catalog_matches_unity_and_all_types_upload(self):
        unity = (Path(__file__).parents[1] / 'Assets/Scripts/TrashTypes.cs').read_text()
        unity_ids = set(re.findall(r'case ItemType\.\w+: return "([a-z0-9_]+)";', unity))
        self.assertEqual(set(get_args(ItemType)), unity_ids)
        self.assertEqual(len(unity_ids), 24)
        for item_id in unity_ids:
            with self.subTest(item_id=item_id):
                response = self.client.post('/grasps', json={'records': [dict(record(), item_type=item_id)]})
                self.assertEqual(response.status_code, 200)
                self.assertEqual(self.client.get('/grasps', params={'item_type': item_id}).status_code, 200)

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

    def test_old_records_get_gripper_defaults(self):
        app.state.db.grasps.insert_many.return_value.inserted_ids = ['a' * 32]
        self.assertEqual(self.client.post('/grasps', json={'records': [record()]}).status_code, 200)
        document = app.state.db.grasps.insert_many.call_args.args[0][0]
        self.assertEqual((document['gripper'], document['input_device'], document['schema_version']),
                         ('parallel_100mm', 'unknown', 1))

    def test_new_gripper_fields_are_stored_and_validated(self):
        app.state.db.grasps.insert_many.return_value.inserted_ids = ['a' * 32]
        new = dict(record(), gripper='suction_40mm', input_device='controllers', schema_version=2)
        self.assertEqual(self.client.post('/grasps', json={'records': [new]}).status_code, 200)
        document = app.state.db.grasps.insert_many.call_args.args[0][0]
        self.assertEqual((document['gripper'], document['input_device'], document['schema_version']),
                         ('suction_40mm', 'controllers', 2))
        for change in ({'gripper': 'Bad Gripper!'}, {'input_device': 'joystick'}, {'schema_version': 0}):
            self.assertEqual(self.client.post('/grasps', json={'records': [dict(new, **change)]}).status_code, 422)

    def test_gripper_filter(self):
        cursor = app.state.db.grasps.find.return_value.sort.return_value.limit.return_value.max_time_ms.return_value
        cursor.__iter__.return_value = iter([])
        self.assertEqual(self.client.get('/grasps?gripper=suction_40mm').status_code, 200)
        self.assertEqual(app.state.db.grasps.find.call_args.args[0]['gripper'], 'suction_40mm')
        self.assertEqual(self.client.get('/grasps?gripper=parallel_100mm').status_code, 200)
        self.assertEqual(app.state.db.grasps.find.call_args.args[0]['gripper'], {'$in': ['parallel_100mm', None]})
        self.assertEqual(self.client.get('/grasps?gripper=Bad!').status_code, 422)

    def test_collision_fields_default_for_old_records(self):
        app.state.db.grasps.insert_many.return_value.inserted_ids = ['a' * 32]
        self.assertEqual(self.client.post('/grasps', json={'records': [record()]}).status_code, 200)
        document = app.state.db.grasps.insert_many.call_args.args[0][0]
        self.assertEqual((document['checker_version'], document['outcome']['failure_reason']), (1, 'none'))
        for field in ('feasible', 'parent_record_id', 'hand_penetration'):
            self.assertNotIn(field, document)
        for field in ('stage', 'grasp_success', 'motion_success'):
            self.assertNotIn(field, document['outcome'])

    def test_collision_fields_are_stored_and_validated(self):
        app.state.db.grasps.insert_many.return_value.inserted_ids = ['b' * 32]
        outcome = dict(bin='none', correct=False, dropped=False, hold_s=0, failure_reason='collision_on_approach',
                       stage='plan', grasp_success=False, motion_success=False)
        robot = dict(record(), record_id='b' * 32, source='robot', hand='gripper', schema_version=3, checker_version=2,
                     parent_record_id='a' * 32, hand_penetration=None, outcome=outcome)
        self.assertEqual(self.client.post('/grasps', json={'records': [robot]}).status_code, 200)
        document = app.state.db.grasps.insert_many.call_args.args[0][0]
        self.assertEqual((document['checker_version'], document['parent_record_id']), (2, 'a' * 32))
        self.assertEqual((document['outcome']['failure_reason'], document['outcome']['stage']),
                         ('collision_on_approach', 'plan'))
        self.assertNotIn('hand_penetration', document)  # Null means "not measured", so it is left out.
        for reason in ('unknown', 'arm_collision', 'aborted'):
            changed = dict(robot, outcome=dict(outcome, failure_reason=reason))
            self.assertEqual(self.client.post('/grasps', json={'records': [changed]}).status_code, 200)
        bad_changes = ({'outcome': dict(outcome, failure_reason='gremlins')}, {'outcome': dict(outcome, stage='lunch')},
                       {'checker_version': 0}, {'parent_record_id': 'not-an-id'}, {'feasible': 'maybe'})
        for change in bad_changes:
            self.assertEqual(self.client.post('/grasps', json={'records': [dict(robot, **change)]}).status_code, 422)

    def test_annotations_upload_is_idempotent_and_validated(self):
        annotation = dict(record_id='a' * 32, gripper='suction_40mm', checker_version=2, feasible=False)
        app.state.db.grasp_annotations.insert_many.return_value.inserted_ids = ['x']
        response = self.client.post('/grasp-annotations', json={'annotations': [annotation]})
        self.assertEqual(response.json(), {'inserted': 1, 'duplicates': 0})
        document = app.state.db.grasp_annotations.insert_many.call_args.args[0][0]
        self.assertEqual(document['_id'], 'a' * 32 + ':suction_40mm:2')
        self.assertEqual(document['context'], 'prefab_on_standin_belt')
        app.state.db.grasps.insert_many.assert_not_called()  # Human records are never rewritten.

        app.state.db.grasp_annotations.insert_many.side_effect = BulkWriteError(
            {'nInserted': 0, 'writeErrors': [{'code': 11000}], 'writeConcernErrors': []})
        response = self.client.post('/grasp-annotations', json={'annotations': [annotation]})
        self.assertEqual(response.json(), {'inserted': 0, 'duplicates': 1})

        app.state.db.grasp_annotations.insert_many.reset_mock()
        for change in ({'checker_version': 1}, {'record_id': 'bad'}, {'feasible': None}, {'context': 'real_world'},
                       {'gripper': 'Bad!'}, {'extra': 1}):
            body = {'annotations': [dict(annotation, **change)]}
            self.assertEqual(self.client.post('/grasp-annotations', json=body).status_code, 422)
        self.assertEqual(self.client.post('/grasp-annotations', json={'annotations': []}).status_code, 422)
        app.state.db.grasp_annotations.insert_many.assert_not_called()

    def test_annotations_read(self):
        cursor = app.state.db.grasp_annotations.find.return_value.limit.return_value.max_time_ms.return_value
        cursor.__iter__.return_value = iter([{'record_id': 'a' * 32, 'feasible': True}])
        response = self.client.get('/grasp-annotations?record_id=' + 'a' * 32)
        self.assertEqual(response.json(), {'annotations': [{'record_id': 'a' * 32, 'feasible': True}]})
        filters, projection = app.state.db.grasp_annotations.find.call_args.args
        self.assertEqual((filters, projection), ({'record_id': 'a' * 32}, {'_id': 0}))
        self.assertEqual(self.client.get('/grasp-annotations?record_id=bad').status_code, 422)
        self.assertEqual(self.client.get('/grasp-annotations').status_code, 422)

    def test_checker_and_feasible_filters(self):
        cursor = app.state.db.grasps.find.return_value.sort.return_value.limit.return_value.max_time_ms.return_value
        cursor.__iter__.return_value = iter([])
        self.assertEqual(self.client.get('/grasps?checker_version=2&feasible=true').status_code, 200)
        filters = app.state.db.grasps.find.call_args.args[0]
        self.assertEqual((filters['checker_version'], filters['feasible']), (2, True))
        self.assertEqual(self.client.get('/grasps?checker_version=1').status_code, 200)
        self.assertEqual(app.state.db.grasps.find.call_args.args[0]['checker_version'], {'$in': [1, None]})
        self.assertEqual(self.client.get('/grasps?checker_version=0').status_code, 422)

    def test_stats_failure_reasons(self):
        app.state.db.grasps.aggregate.return_value = iter([{
            'counts': [{'_id': {'item_type': 'battery_aa', 'source': 'robot', 'gripper': 'parallel_100mm'},
                        'total': 3, 'good': 1}],
            'robot_daily': [], 'robot_by_gripper': [],
            'failure_reasons': [{'_id': {'reason': 'unknown', 'stage': 'unknown', 'checker_version': 1,
                                         'gripper': 'parallel_100mm'}, 'total': 2}]}])
        body = self.client.get('/stats').json()
        self.assertEqual(body['failure_reasons'], [{'reason': 'unknown', 'stage': 'unknown', 'checker_version': 1,
                                                    'gripper': 'parallel_100mm', 'total': 2}])
        facet = app.state.db.grasps.aggregate.call_args.args[0][0]['$facet']
        reason = facet['failure_reasons'][1]['$group']['_id']['reason']
        self.assertEqual(reason['$ifNull'][1]['$cond'][1:], ['none', 'unknown'])

    def test_write_concern_failure_is_retryable(self):
        app.state.db.grasps.insert_many.side_effect = BulkWriteError({'nInserted': 1, 'writeErrors': [], 'writeConcernErrors': [{'code': 64}]})
        self.assertEqual(self.client.post('/grasps', json={'records': [record()]}).status_code, 503)


if __name__ == '__main__':
    unittest.main()
