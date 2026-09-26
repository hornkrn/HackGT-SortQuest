# SortQuest LAN API for Quest

The data path is **Quest APK → FastAPI on your computer → MongoDB Atlas**.
Only the computer holds the MongoDB connection string. Unity uses `UnityWebRequest`
coroutines and the existing `GraspRecord` JSON fields.

## Run the computer server

### Quick setup on the other computer

After cloning/pulling the repository, install Python 3.9+ and run:

```powershell
# Windows (from the repository root)
py -3 server/host.py
```

```sh
# macOS/Linux (from the repository root)
python3 server/host.py
```

On first run, the launcher creates `.env` from `.env.example` and stops so you can
fill in the MongoDB URI and the name of the existing configured database. Run the
same command again: it creates `.venv`, installs dependencies, checks MongoDB
connectivity/read access and the collection validator, generates a local API key
if needed, and starts FastAPI on that computer's LAN address. It never overwrites
an existing `.env`. You can run the script by absolute path from any working folder.

Add the new computer's public IP to Atlas Network Access if needed, and allow
inbound TCP 8000 in its firewall. Keep the server window open. The previous
computer does not need to host the API anymore; both servers can also access the
same MongoDB database if desired. Run **SortQuest → Configure LAN API** in Unity
on the new API computer, save the scene, and rebuild the APK so it uses the new
address and key. If Unity runs elsewhere, enter those values manually instead.

For later runs, `server/host.py --skip-install` skips the dependency installation.
`server/host.py --check` checks database access without starting a listener.
Neither the MongoDB credentials nor the generated API key are pushed to Git.

### Manual setup

Use Python 3.9 or newer. From a fresh clone, copy `.env.example` to `.env`
and set the Atlas connection string and database name. Obtain credentials privately;
they are intentionally not in Git. For the existing configured database, reuse its
database name. If moving the API to another computer, allow that computer's public
IP in Atlas Network Access. A new database needs the root README's schema applied.

On macOS/Linux, run from the repository root:

```sh
python3 -m venv .venv
source .venv/bin/activate
pip install -r server/requirements.txt
python -m server.run
```

On Windows PowerShell (no activation-policy change needed):

```powershell
Copy-Item .env.example .env  # First setup only; then fill in the MongoDB values.
py -3 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r server/requirements.txt
.\.venv\Scripts\python.exe -m server.run
```

If the API stays on the original computer, the Unity-only computer needs no
Python installation or MongoDB credentials: configure its Unity components with
the API computer's LAN address and shared API key instead.

The launcher binds to `0.0.0.0:8000`, prints the computer's LAN URL, and creates a
random `SORTQUEST_API_KEY` in `.env` if one is missing. The existing
`MONGODB_URI` and `MONGODB_DB` values are loaded from that file. Environment
variables take precedence. The MongoDB validator and indexes must be applied first.

Keep the process running while playing. Allow inbound TCP port 8000 in the computer's
firewall. Put the headset and computer on the same network. Guest/campus networks
may isolate clients even when both use the same Wi-Fi; use a private router or
hotspot if the headset cannot open `/health`.

## Configure Unity and build the APK

1. Open the game scene containing `GraspDataset`.
2. On the API computer, choose **SortQuest → Configure LAN API**. This adds
   `SortQuestApi` and `DataUploader` to that dataset's GameObject and writes the LAN
   address and API key to `Assets/Resources/SortQuestApiSettings.json`. That file is
   gitignored but packed into builds, so the key is never stored in the scene and the
   scene can be committed safely. Save the scene.
3. If building on another computer, create that file yourself (it is not in Git):
   `{"baseUrl": "http://10.90.56.133:8000", "apiKey": "<SORTQUEST_API_KEY from the API computer's .env>"}`.
   In Android Player Settings, set **Allow downloads over HTTP** to **Always
   allowed** (the setup menu does this automatically).
4. Build/install the Android APK normally. The Android manifest now requests
   INTERNET access and allows HTTP for this LAN setup. No MongoDB credential
   belongs in Assets or an Inspector.
5. In the headset browser open `http://COMPUTER_IP:8000/health`. Expect
   `{"ok":true}`. Then play, record a grasp, and check `GET /stats` with the key.

`127.0.0.1` on the headset means the headset itself. Always use the computer's LAN
IP, and update the Inspector/rebuild if DHCP changes it. Reserve the computer's
address in your router for a stable setup. The menu detects the address of the
computer running the Unity editor, so double-check it when the API is elsewhere.

HTTP and its shared key are for a trusted local network; traffic is unencrypted.
Use HTTPS before exposing this API over the internet. The APK's API key is a
shared access gate, not a protected user credential. It lives only in the gitignored
settings file, so never commit `Assets/Resources/SortQuestApiSettings.json`.

## Use from other Unity scripts

```csharp
// Coroutines: run from a MonoBehaviour with a reference to SortQuestApi.
StartCoroutine(api.Health(result => Debug.Log(result.ok), Debug.LogWarning));
StartCoroutine(api.ReadGoodGrasps("battery_aa",
    result => dataset.MergeRemote(result.records), Debug.LogWarning));
StartCoroutine(api.ReadStats(result => Debug.Log(result.total), Debug.LogWarning));
// Upload manually if not using DataUploader:
StartCoroutine(api.Upload(new[] { record },
    result => Debug.Log(result.inserted), Debug.LogWarning));
```

`DataUploader` automatically subscribes to new local records, persists a pending
queue, batches at most 100 records, and retries failed/ambiguous requests with
backoff up to 60 seconds. It removes records only after the server acknowledges
all records in the batch. `PendingCount` and `LastError` expose status. Local
history is replayed at startup to recover interrupted writes; MongoDB's record
IDs make this idempotent. Invalid batches remain queued for diagnosis rather
than silently losing data. Downloads are explicit via `DownloadGoodGrasps` or
`ReadGoodGrasps`, capped at 1,000 records per call; this is not full database sync.
Imported grasps are kept in memory without invoking local upload/augmentation
events. Queue file I/O is synchronous; network operations yield to gameplay.
Images are represented as metadata only; image-byte upload is not implemented.

## API

Interactive docs: `http://COMPUTER_IP:8000/docs`. Protected requests require
`X-API-Key: <SORTQUEST_API_KEY>`. Health is unauthenticated.

- `GET /health`: MongoDB connectivity (503 when unavailable).
- `POST /grasps`: `{"records":[...]}`, 1–100 records. Adds `_id`, `created_at`,
  `received_at`. Returns inserted and duplicate counts. Retrying IDs is safe.
- `GET /grasps?item_type=battery_aa&good=true&source=human,augmented&limit=500`:
  newest matching game records, max 1,000. `good=false` means incorrect or dropped.
- `GET /stats`: totals by item/source, good counts, daily UTC robot success rates.

Invalid requests return 422; missing/wrong keys return 401; an unconfigured server
key or database failure returns 503. Driver error details are not exposed.

## Tests

```sh
pip install -r server/requirements-dev.txt
python -m unittest discover -s server
# Start server first. Live test inserts unique fixtures and cleans them up:
python -m server.test_live --base-url http://COMPUTER_IP:8000
```

The live test checks authentication, real HTTP writes, duplicate retries, MongoDB
persistence/dates, read-back and filtering, request bounds, stats, and database
validation. It deletes only records with the generated test IDs and session ID.
`Assets/Editor/SortQuestApiSmokeTest.cs` also exercises the actual Unity HTTP client
and JSON serializer in batch mode; it requires `SORTQUEST_TEST_RECORD_ID` and a
caller that removes that one test record after exit. `SORTQUEST_TEST_URL` chooses
the API address. It uses the API key from `.env`, without logging it.

Unity networking reference:
https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Networking.UnityWebRequest.html
