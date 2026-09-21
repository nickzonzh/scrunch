"""Make labelled review sheets from WinApp's actual captures, never render proxies.
Usage: python tools/nootfx-assets/review-captures.py <capture-directory>
Requires Pillow in the development environment. Raw images/videos stay intact.
"""
import json
import sys
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(sys.argv[1])
seeds = json.loads((Path(__file__).resolve().parents[2] / 'proto/fx-golden-seeds.json').read_text())['seeds']
poses = ['0', '0.2', '0.4', '0.6', '0.8', '1']

def held(file, size=250):
    im = Image.open(file).convert('RGB')
    w, h = im.size
    # Overlay padding is 65% of max paper dimension on each side. Include the
    # entire sheet plus modest surrounding space at a consistent relative scale.
    radius = max(w, h) / 2.3 * .67
    im = im.crop((w/2-radius,h/2-radius,w/2+radius,h/2+radius))
    im.thumbnail((size,size))
    return im

def sheet(rows, columns, name, cell=250):
    canvas = Image.new('RGB',(columns*cell,len(rows)*(cell+24)),'#eeeeee')
    draw=ImageDraw.Draw(canvas)
    for r, cells in enumerate(rows):
        for c,(im,label) in enumerate(cells):
            canvas.paste(im,(c*cell,r*(cell+24)+24))
            draw.text((c*cell+5,r*(cell+24)+5),label,fill='black')
    canvas.save(root/name)

for page in range(0,len(seeds),3):
    sheet([[(held(root/f'seed-{s}-pose-{p}.png'),f'Seed {s} / {p}') for p in poses]
           for s in seeds[page:page+3]],6,f'golden-{page//3+1}.png')

for sample in range(6):
    sheet([[(held(root/f'aspect-{sample}-seed-{s}-pose-{p}.png'),f'Sample {sample}, seed {s} / {p}')
            for p in ['0.4','0.7','1']] for s in [0,1,2]],3,f'aspect-review-{sample}.png')

for seed in seeds:
    folder=root/f'seed-{seed}-production.frames'
    samples=[json.loads(s) for s in (folder/'frames.ndjson').read_text().splitlines()]
    # Include all distinct images, including source, every observed motion state,
    # fade and empty desktop. Timestamps retain real elapsed capture time.
    changes=[f for f in samples if f['changed']]
    cells=[]
    for f in changes:
        im=Image.open(folder/f['file']).convert('RGB')
        # Current native lab fixture: maximized 2560px desktop, captured at
        # 1600px; note centred near (900,400). This is an evidence crop only.
        im=im.crop((410,90,850,530));im.thumbnail((250,250))
        cells.append((im,f"Seed {seed} / {f['elapsedMs']}ms"))
    sheet([cells[i:i+6] for i in range(0,len(cells),6)],6,f'motion-review-{seed}.png')
