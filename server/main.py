"""SortQuest database API. Run from the repository root with uvicorn server.main:app."""
import os
import secrets
from contextlib import asynccontextmanager
from datetime import datetime, timezone
from pathlib import Path
from typing import List, Literal, Optional

import certifi
from dotenv import load_dotenv
from fastapi import Depends, FastAPI, Header, HTTPException, Query
from pydantic import BaseModel, ConfigDict, Field, field_validator
from pymongo import MongoClient
from pymongo.errors import BulkWriteError, PyMongoError
from starlette.responses import JSONResponse

load_dotenv(Path(__file__).resolve().parents[1] / '.env')

ItemType = Literal[
    'aluminum_can', 'plastic_bottle', 'cardboard_box', 'crumpled_paper', 'battery_aa', 'power_bank',
    'food_tin', 'tuna_can', 'metal_lid', 'foil_tray', 'steel_bottle',
    'yogurt_cup', 'detergent_bottle', 'shampoo_bottle', 'plastic_tub', 'plastic_cap',
    'cereal_carton', 'egg_carton', 'paper_tube', 'folded_newspaper',
    'battery_9v', 'battery_coin', 'smartphone', 'circuit_board',
]
Source = Literal['human', 'augmented', 'robot']
Bin = Literal['metal', 'plastic', 'paper', 'hazardous']
InputDevice = Literal['hands', 'controllers', 'simulator', 'unknown', 'none']
GRIPPER_PATTERN = r'^[a-z0-9_]{1,40}$'
# Records sent before grippers existed were all for the standard two-finger gripper.
STANDARD_GRIPPER = 'parallel_100mm'
# 'unknown' marks robot failures saved before failure reasons existed.
FailureReason = Literal['none', 'collision_at_grasp', 'collision_on_approach', 'path_blocked',
                        'collision_during_motion', 'no_contact', 'no_seal', 'out_of_reach', 'arm_collision', 'aborted',
                        'unknown']


class Model(BaseModel):
    model_config = ConfigDict(extra='forbid', allow_inf_nan=False)


class Grasp(Model):
    pos_local: List[float] = Field(min_length=3, max_length=3)
    rot_local: List[float] = Field(min_length=4, max_length=4)
    width_m: float


class Pose(Model):
    pos: List[float] = Field(min_length=3, max_length=3)
    rot: List[float] = Field(min_length=4, max_length=4)


class Outcome(Model):
    bin: Literal['metal', 'plastic', 'paper', 'hazardous', 'none']
    correct: bool
    dropped: bool
    hold_s: float
    failure_reason: FailureReason = 'none'
    stage: Optional[Literal['plan', 'approach', 'grasp', 'carry', 'release']] = None
    grasp_success: Optional[bool] = None
    motion_success: Optional[bool] = None


class Image(Model):
    id: str
    cam_pos: List[float] = Field(min_length=3, max_length=3)
    cam_rot: List[float] = Field(min_length=4, max_length=4)
    fov_y_deg: float
    rgb_size: int
    depth_size: int


class GraspRecord(Model):
    record_id: str = Field(pattern=r'^[0-9a-f]{32}$')
    session_id: str
    player: str
    timestamp: str
    source: Source
    item_type: ItemType
    correct_bin: Bin
    hand: Literal['left', 'right', 'gripper']
    # Added in schema_version 2. Defaults keep older game builds working.
    gripper: str = Field(default=STANDARD_GRIPPER, pattern=GRIPPER_PATTERN)
    input_device: InputDevice = 'unknown'
    schema_version: int = Field(default=1, ge=1, le=100)
    grasp: Grasp
    item_pose_world: Pose
    outcome: Outcome
    image: Optional[Image] = None
    checker_version: int = Field(default=1, ge=1)
    feasible: Optional[bool] = None
    parent_record_id: Optional[str] = Field(default=None, pattern=r'^[0-9a-f]{32}$')
    hand_penetration: Optional[bool] = None

    @field_validator('timestamp')
    @classmethod
    def valid_timestamp(cls, value):
        parsed = datetime.fromisoformat(value.replace('Z', '+00:00'))
        if parsed.tzinfo is None:
            raise ValueError('timestamp must include a timezone')
        return value

    def document(self):
        return {
            **self.model_dump(exclude_none=True),
            '_id': self.record_id,
            'created_at': datetime.fromisoformat(self.timestamp.replace('Z', '+00:00')),
            'received_at': datetime.now(timezone.utc),
        }


class Batch(Model):
    records: List[GraspRecord] = Field(min_length=1, max_length=100)


class FeasibilityAnnotation(Model):
    record_id: str = Field(pattern=r'^[0-9a-f]{32}$')
    gripper: str = Field(pattern=GRIPPER_PATTERN)
    checker_version: int = Field(ge=2)
    feasible: bool
    context: Literal['prefab_on_standin_belt'] = 'prefab_on_standin_belt'

    def document(self):
        return {**self.model_dump(), '_id': f'{self.record_id}:{self.gripper}:{self.checker_version}'}


class AnnotationBatch(Model):
    annotations: List[FeasibilityAnnotation] = Field(min_length=1, max_length=100)


@asynccontextmanager
async def lifespan(app):
    uri = os.environ.get('MONGODB_URI')
    database = os.environ.get('MONGODB_DB')
    if not uri or not database:
        raise RuntimeError('Set MONGODB_URI and MONGODB_DB in .env')
    options = {'serverSelectionTimeoutMS': 5000, 'connectTimeoutMS': 5000, 'socketTimeoutMS': 10000}
    if uri.startswith('mongodb+srv://') or 'tls=true' in uri.lower():
        options['tlsCAFile'] = certifi.where()
    client = MongoClient(uri, **options)
    app.state.db = client[database]
    try:
        yield
    finally:
        client.close()


app = FastAPI(title='SortQuest API', lifespan=lifespan)


def require_api_key(x_api_key: Optional[str] = Header(default=None)):
    expected = os.environ.get('SORTQUEST_API_KEY', '')
    if not expected:
        raise HTTPException(503, 'Server API key is not configured')
    if not x_api_key or not secrets.compare_digest(x_api_key.encode(), expected.encode()):
        raise HTTPException(401, 'Invalid API key')


@app.exception_handler(PyMongoError)
async def database_error(request, exc):
    # Driver errors may contain server details; never expose them to clients.
    return JSONResponse(status_code=503, content={'detail': 'Database operation unavailable'})


@app.get('/health')
def health():
    app.state.db.command('ping')
    return {'ok': True}


@app.post('/grasps', dependencies=[Depends(require_api_key)])
def upload_grasps(batch: Batch):
    return insert_idempotent(app.state.db.grasps, [record.document() for record in batch.records])


@app.post('/grasp-annotations', dependencies=[Depends(require_api_key)])
def upload_annotations(batch: AnnotationBatch):
    # Separate collection: never rewrite human grasp coordinates, outcomes, or input provenance.
    return insert_idempotent(app.state.db.grasp_annotations, [row.document() for row in batch.annotations])


@app.get('/grasp-annotations', dependencies=[Depends(require_api_key)])
def get_annotations(record_id: str = Query(pattern=r'^[0-9a-f]{32}$')):
    return {'annotations': list(app.state.db.grasp_annotations.find(
        {'record_id': record_id}, {'_id': 0}).limit(100).max_time_ms(5000))}


def insert_idempotent(collection, documents):
    try:
        result = collection.insert_many(documents, ordered=False)
        return {'inserted': len(result.inserted_ids), 'duplicates': 0}
    except BulkWriteError as exc:
        details = exc.details
        errors = details.get('writeErrors', [])
        if details.get('writeConcernErrors') or any(error.get('code') != 11000 for error in errors):
            raise HTTPException(503, 'Batch could not be fully stored; retry with the same record IDs') from None
        return {'inserted': details.get('nInserted', 0), 'duplicates': len(errors)}


@app.get('/grasps', dependencies=[Depends(require_api_key)])
def get_grasps(
    item_type: Optional[ItemType] = None,
    good: Optional[bool] = None,
    source: Optional[str] = None,
    gripper: Optional[str] = Query(default=None, pattern=GRIPPER_PATTERN),
    checker_version: Optional[int] = Query(default=None, ge=1),
    feasible: Optional[bool] = None,
    limit: int = Query(default=500, ge=1, le=1000),
):
    filters = {}
    if checker_version is not None:
        filters['checker_version'] = {'$in': [1, None]} if checker_version == 1 else checker_version
    if feasible is not None:
        filters['feasible'] = feasible
    if item_type is not None:
        filters['item_type'] = item_type
    if gripper is not None:
        # Documents stored before the gripper field existed belong to the standard gripper.
        filters['gripper'] = {'$in': [gripper, None]} if gripper == STANDARD_GRIPPER else gripper
    if good is True:
        filters.update({'outcome.correct': True, 'outcome.dropped': False})
    elif good is False:
        filters['$or'] = [{'outcome.correct': False}, {'outcome.dropped': True}]
    if source is not None:
        sources = [value.strip() for value in source.split(',')]
        if any(value not in ('human', 'augmented', 'robot') for value in sources):
            raise HTTPException(422, 'source must contain human, augmented, or robot')
        filters['source'] = {'$in': sources}
    records = list(app.state.db.grasps.find(filters, {'_id': 0, 'created_at': 0, 'received_at': 0}).sort('created_at', -1).limit(limit).max_time_ms(5000))
    return {'records': records}


@app.get('/stats', dependencies=[Depends(require_api_key)])
def stats():
    good = {'$and': [{'$eq': ['$outcome.correct', True]}, {'$eq': ['$outcome.dropped', False]}]}
    gripper = {'$ifNull': ['$gripper', STANDARD_GRIPPER]}
    result = next(app.state.db.grasps.aggregate([{'$facet': {
        'counts': [{'$group': {'_id': {'item_type': '$item_type', 'source': '$source', 'gripper': gripper}, 'total': {'$sum': 1}, 'good': {'$sum': {'$cond': [good, 1, 0]}}}}, {'$sort': {'_id.item_type': 1, '_id.source': 1, '_id.gripper': 1}}],
        'robot_daily': [{'$match': {'source': 'robot'}}, {'$group': {'_id': {'$dateToString': {'format': '%Y-%m-%d', 'date': '$created_at', 'timezone': 'UTC'}}, 'attempts': {'$sum': 1}, 'successes': {'$sum': {'$cond': [good, 1, 0]}}}}, {'$sort': {'_id': 1}}],
        'robot_by_gripper': [{'$match': {'source': 'robot'}}, {'$group': {'_id': gripper, 'attempts': {'$sum': 1}, 'successes': {'$sum': {'$cond': [good, 1, 0]}}}}, {'$sort': {'_id': 1}}],
        'failure_reasons': [{'$match': {'source': 'robot'}}, {'$group': {'_id': {
            # Older documents have no reason: a success had none, a failure's reason was never saved.
            'reason': {'$ifNull': ['$outcome.failure_reason', {'$cond': [good, 'none', 'unknown']}]},
            'stage': {'$ifNull': ['$outcome.stage', 'unknown']},
            'checker_version': {'$ifNull': ['$checker_version', 1]}, 'gripper': gripper},
            'total': {'$sum': 1}}}, {'$sort': {'_id.reason': 1}}],
    }}], maxTimeMS=5000))
    counts = [{**row['_id'], 'total': row['total'], 'good': row['good']} for row in result['counts']]
    daily = [{'date': row['_id'], 'attempts': row['attempts'], 'successes': row['successes'], 'success_rate': row['successes'] / row['attempts']} for row in result['robot_daily']]
    by_gripper = [{'gripper': row['_id'], 'attempts': row['attempts'], 'successes': row['successes'], 'success_rate': row['successes'] / row['attempts']} for row in result['robot_by_gripper']]
    failures = [{**row['_id'], 'total': row['total']} for row in result['failure_reasons']]
    return {'total': sum(row['total'] for row in counts), 'counts': counts, 'robot_daily': daily,
            'robot_by_gripper': by_gripper, 'failure_reasons': failures}
