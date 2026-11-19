"""Opt-in smoke test: one persisted plan and at most one image request. Never retries."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import time
import urllib.error
import urllib.request
import uuid

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--allow-paid', action='store_true', help='Explicitly authorize provider charges for one plan and one image')
args = parser.parse_args()
if not args.allow_paid:
    parser.error('--allow-paid is required; default automated tests never call a paid provider')
root = Path(__file__).resolve().parents[1]
api = root / 'src/backend/ImageFlow.Api'
if not (api / 'appsettings.Local.json').exists():
    raise SystemExit('Configure ignored appsettings.Local.json first. See README.')
run_dir = root / '.cache/live-provider' / str(uuid.uuid4())
run_dir.mkdir(parents=True)
env = os.environ.copy()
env.update(Engine__DataDirectory=str(run_dir), Engine__MaxConcurrency='1', Engine__MaxAttempts='1', Engine__WorkerEnabled='true')
base = 'http://localhost:5084'
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

def request(path, body=None):
    payload = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(base + path, data=payload, headers={'Content-Type': 'application/json'})
    try:
        with opener.open(req, timeout=180) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        # Do not print provider response bodies or configuration.
        raise RuntimeError(f'Application HTTP {error.code}; inspect local attempt history. No automatic retry.') from None

report = {'status': 'starting', 'dataDirectory': str(run_dir)}
with (run_dir / 'api.log').open('w', encoding='utf-8') as log:
    process = subprocess.Popen(['dotnet', 'bin/Debug/net10.0/ImageFlow.Api.dll', '--urls', base], cwd=api, env=env, stdout=log, stderr=log)
    try:
        for _ in range(100):
            try:
                request('/health')
                break
            except Exception:
                if process.poll() is not None:
                    raise RuntimeError('API did not start; see local api.log')
                time.sleep(.1)
        settings = request('/api/v1/settings')
        if settings['provider'] == 'Mock':
            raise RuntimeError('Local provider is Mock; opt-in test requires a configured real provider')
        report['provider'] = settings['provider']
        plan = request('/api/v1/plans', {'theme': 'A single small blue ceramic cup on a white background, minimal studio product photograph, no text.', 'count': 1})
        report['planId'] = plan['id']
        job = request('/api/v1/jobs', {'planId': plan['id']})
        report['jobId'] = job['id']
        for _ in range(360):
            job = request('/api/v1/jobs/' + job['id'])
            if job['status'] in ('Completed', 'NeedsAttention', 'Cancelled'):
                break
            time.sleep(.5)
        task = job['tasks'][0]
        report.update(status=job['status'], taskStatus=task['status'], imagePath=task['imagePath'], attempts=len(task['attempts']))
        if task['attempts']:
            attempt = task['attempts'][0]
            report.update(requestId=attempt['requestId'], measuredUsageJson=attempt['usageJson'])
        if job['status'] != 'Completed':
            report['note'] = 'No resend. Inspect provider records before replacement work.'
    except Exception as error:
        report.update(status='failed', error=str(error))
    finally:
        process.terminate()
        process.wait(timeout=15)
        (run_dir / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(json.dumps(report, ensure_ascii=True))
raise SystemExit(0 if report['status'] == 'Completed' else 1)
