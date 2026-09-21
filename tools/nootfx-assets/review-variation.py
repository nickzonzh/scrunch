"""Review native captures, retaining timestamps and raw evidence.

python tools/nootfx-assets/review-variation.py <capture-directory> [--ffmpeg path]
No renderer output is synthesized. Crops assume the documented 1600x880 lab
capture on this development desktop; fail on other layouts rather than miscrop.
"""
import argparse
import json
import math
import subprocess
from pathlib import Path
from PIL import Image, ImageDraw, ImageChops

parser = argparse.ArgumentParser()
parser.add_argument('directory', type=Path)
parser.add_argument('--ffmpeg')
parser.add_argument('--slow-directory', type=Path, help='Use separately retained pre-fix slow captures of unchanged deformation')
args = parser.parse_args()
root = args.directory.resolve()
report_name = 'variation-ui-verification.json' if (root/'variation-ui-verification.json').exists() else 'moderate-ui-verification.json'
report = json.loads((root / report_name).read_text(encoding='utf-8-sig'))
rows = report['rows']
families = ['Corner', 'Side', 'Centre']
orientations = ['None', 'X', 'Y', 'XY']
crop = (390, 65, 845, 530)

def thumb(path, size=210, held=False):
    im = Image.open(path).convert('RGB')
    if max(b-a for a,b in im.getextrema()) < 12:
        raise ValueError(f'Blank capture: {path}')
    if held:
        w,h = im.size
        radius = max(w,h) / 2.3 * .65
        im = im.crop((w/2-radius,h/2-radius,w/2+radius,h/2+radius))
    else:
        if im.size != (1600,880):
            raise ValueError(f'Adjust crop for capture layout {im.size}: {path}')
        im = im.crop(crop)
    im.thumbnail((size,size))
    return im

def sheet(cells, name, columns=6, size=210):
    canvas = Image.new('RGB',(columns*size,math.ceil(len(cells)/columns)*(size+35)),'#eeeeee')
    draw = ImageDraw.Draw(canvas)
    for i,(im,label) in enumerate(cells):
        x=(i%columns)*size; y=(i//columns)*(size+35)
        draw.text((x+4,y+3),label,fill='#111111')
        canvas.paste(im,(x,y+35))
    canvas.save(root/name)

def paper_box(im, background=None):
    r,g,b=im.split()
    high=ImageChops.lighter(ImageChops.lighter(r,g),b)
    low=ImageChops.darker(ImageChops.darker(r,g),b)
    mask=ImageChops.subtract(high,low).point(lambda p:255 if p>16 else 0)
    if background is not None:
        r,g,b=ImageChops.difference(im,background).split()
        changed=ImageChops.lighter(ImageChops.lighter(r,g),b).point(lambda p:255 if p>12 else 0)
        mask=ImageChops.multiply(mask,changed)
    return mask.getbbox()

def detail(path, background):
    im=Image.open(path).convert('RGB').crop(crop)
    box=paper_box(im,background)
    if not box:
        raise ValueError(f'No coloured wad in selected production outcome: {path}')
    x=(box[0]+box[2])/2; y=(box[1]+box[3])/2
    radius=max(55,(box[2]-box[0])*0.85,(box[3]-box[1])*0.85)
    return im.crop((x-radius,y-radius,x+radius,y+radius)).resize((250,250),Image.Resampling.LANCZOS)

outcomes=[]; details=[]; intermediate=[]; manifests=[]; flashes=[]
for row in rows:
    seed=row['seed']; label=f"{seed} {families[seed%3]} / {orientations[(seed//3)%4]}"
    key=row.get('prefix',f'seed-{seed}'); review_id=row.get('prefix',str(seed))
    for pose in ['0.4','0.7','1']:
        intermediate.append((thumb(root/f'{key}-pose-{pose}.png',held=True),f'{label} / {pose}\n{row["width"]}x{row["height"]}'))
    for mode in ['production','slow']:
        source=args.slow_directory.resolve() if mode=='slow' and args.slow_directory else root
        folder=source/f'{key}-{mode}.frames'
        manifest=json.loads((folder/'manifest.json').read_text())
        frames=[json.loads(line) for line in (folder/'frames.ndjson').read_text().splitlines()]
        changes=[f for f in frames if f['changed']]
        if manifest['status']!='complete' or manifest['frames']['truncated']:
            raise ValueError(f'Incomplete recording: {folder}')
        manifests.append(dict(seed=seed,mode=mode,source=str(folder),**manifest['timing']))
        # All observed production image changes; 36 timestamp-spaced slow
        # observations over the changing interval, not arbitrary video indices.
        selected=changes
        if mode=='slow':
            start=changes[1]['elapsedMs']; end=changes[-1]['elapsedMs']
            selected=[changes[0]]+[min(changes,key=lambda f:abs(f['elapsedMs']-(start+(end-start)*i/35))) for i in range(36)]
        sheet([(thumb(folder/f['file']),f'{label} {mode}\n{f["elapsedMs"]} ms') for f in selected],f'review-{review_id}-{mode}.png')
        if mode=='production':
            # The controlled pastel-paper fixture contains no white rectangle.
            # Detect the observed terminal HWND flash in every production image,
            # including frames that a sparse contact sheet could otherwise miss.
            coloured=[]
            background=Image.open(folder/frames[-1]['file']).convert('RGB').crop(crop)
            for f in changes:
                cropped=Image.open(folder/f['file']).convert('RGB').crop(crop)
                coloured.append((f,paper_box(cropped,background) is not None))
                channels=cropped.split()
                minimum=ImageChops.darker(ImageChops.darker(channels[0],channels[1]),channels[2])
                white=sum(minimum.histogram()[251:])
                if white>1000:
                    flashes.append(dict(seed=seed,elapsedMs=f['elapsedMs'],whitePixels=white,file=str(folder/f['file'])))
            # Last full compact interval, before release/fade. Keep the actual
            # timestamp visible; this is an observed frame, not a held render.
            # Ignore later cursor/control changes in the full-window recording.
            # Find disappearance within the paper crop, not the last changed JPEG.
            end=next(f['elapsedMs'] for f,visible in coloured if not visible)
            chosen=min(changes,key=lambda f:abs(f['elapsedMs']-(end-260)))
            outcomes.append((thumb(folder/chosen['file'],size=250),f'{label} / {row["width"]}x{row["height"]}\nNative frame {chosen["elapsedMs"]} ms'))
            details.append((detail(folder/chosen['file'],background),f'{label} / {row["width"]}x{row["height"]}\nEnlarged native detail / {chosen["elapsedMs"]} ms'))
        if args.ffmpeg:
            # WinApp emits CFR mediaTimeMs even if screen capture undershoots
            # its requested fps. Rebuild from elapsedMs so playback speed is
            # faithful to the native effect. Never interpolate new geometry.
            concat=root/f'{key}-{mode}-timestamps.ffconcat'
            lines=['ffconcat version 1.0']
            for i,f in enumerate(changes):
                end=changes[i+1]['elapsedMs'] if i+1<len(changes) else manifest['timing']['elapsedMs']
                file=(folder/f['file']).as_posix().replace("'", "'\\''")
                lines.extend([f"file '{file}'",'option framerate 1000',f'duration {max(1,end-f["elapsedMs"])/1000:.6f}'])
            lines.extend([f"file '{file}'",'option framerate 1000'])
            concat.write_text('\n'.join(lines)+'\n')
            output=root/f'{key}-{mode}-walltime.mp4'
            subprocess.run([args.ffmpeg,'-hide_banner','-loglevel','error','-y','-f','concat','-safe','0','-i',str(concat),
                '-vf','crop=455:465:390:65,pad=ceil(iw/2)*2:ceil(ih/2)*2','-fps_mode','vfr',
                '-c:v','libx264','-bf','0','-enc_time_base','1:1000','-video_track_timescale','1000',
                '-preset','fast','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(output)],check=True)
    print(f'Built review sheets for seed {seed}',flush=True)
sheet(outcomes,'production-outcomes.png',size=250)
sheet(details,'production-details.png',size=250)
sheet(intermediate,'held-intermediates.png')
(root/'capture-timing.json').write_text(json.dumps(manifests,indent=2))
(root/'white-flash-findings.json').write_text(json.dumps(dict(productionRecordings=len(rows),candidates=flashes),indent=2))
cards=[]
for row in rows:
    seed=row['seed']; label=f"Seed {seed} · {families[seed%3]} · {orientations[(seed//3)%4]} · {row['width']} × {row['height']}"
    key=row.get('prefix',f'seed-{seed}'); review_id=row.get('prefix',str(seed))
    cards.append(f'<article><h2>{label}</h2><p>{row["outcome"]}</p><div class="pair">'+''.join(
        f'<div><p>{mode.title()} — native elapsed time</p><video controls preload="metadata" src="{key}-{mode}-walltime.mp4"></video></div>' for mode in ['production','slow'])+
        f'</div><p><a href="review-{review_id}-production.png">Every production image change</a> · <a href="review-{review_id}-slow.png">Slow temporal samples</a></p></article>')
(root/'index.html').write_text('''<!doctype html><meta charset="utf-8"><title>NootFX variation QA</title>
<style>body{font:16px system-ui;max-width:1100px;margin:40px auto;padding:0 24px;background:#f3f3f3;color:#222}h1{font-size:32px}.pair{display:grid;grid-template-columns:1fr 1fr;gap:24px}video{width:100%}article{border-top:1px solid #ccc;padding:20px 0}a{color:#245d82}img{width:100%}</style>
<h1>NootFX deterministic variation QA</h1><p>Native D3D captures. Videos rebuilt from observed elapsed timestamps; no interpolated frames. Capture cadence is not GPU frame rate.</p>
<p><a href="production-outcomes.png">Production outcomes</a> · <a href="production-details.png">Enlarged silhouettes</a> · <a href="held-intermediates.png">Held intermediate matrix</a></p>
<img src="production-outcomes.png" alt="Representative late crumple outcomes">'''+
    ('<p>Slow captures precede the terminal window-hide correction. Deformation, material and timing are byte-identical; production captures verify the final close path.</p>' if args.slow_directory else '')+
    ''.join(cards),encoding='utf-8')
