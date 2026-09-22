"""Verify evidence videos against every native image-change timestamp."""
import argparse
import json
import subprocess
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('directory', type=Path)
parser.add_argument('--ffprobe', required=True)
args = parser.parse_args()
root = args.directory.resolve()
results = []
for capture in json.loads((root / 'capture-timing.json').read_text()):
    source = Path(capture['source'])
    frames = [json.loads(line) for line in (source / 'frames.ndjson').read_text().splitlines()]
    changes = [frame for frame in frames if frame['changed']]
    first = changes[0]['elapsedMs']
    expected = [(frame['elapsedMs'] - first) / 1000 for frame in changes]
    expected.append((capture['elapsedMs'] - first) / 1000)
    video = root / (source.name.removesuffix('.frames') + '-walltime.mp4')
    data = json.loads(subprocess.check_output([
        args.ffprobe, '-v', 'error', '-select_streams', 'v:0',
        '-show_entries', 'frame=best_effort_timestamp_time:format=duration',
        '-of', 'json', str(video)], text=True))
    observed = [float(frame['best_effort_timestamp_time']) for frame in data['frames']]
    assert len(observed) == len(expected), f'Frame count differs: {video}'
    error = max(abs(a-b) for a, b in zip(observed, expected))
    duration_error = abs(float(data['format']['duration']) - (expected[-1] + .001))
    assert error < .002 and duration_error < .005, f'Timing differs: {video}'
    results.append(dict(video=video.name, frames=len(observed), maxTimestampErrorMs=error*1000,
                        durationErrorMs=duration_error*1000))
(root / 'video-timing-verification.json').write_text(json.dumps(dict(passed=True, videos=results), indent=2))
print(f'PASS: {len(results)} videos; every native image-change timestamp and full duration verified')
