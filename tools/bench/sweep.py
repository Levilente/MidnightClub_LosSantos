#!/usr/bin/env python3
"""Runs every spec sheet through the full-vehicle bench and flags the ones outside real-car ranges."""
import json, glob, os, re, subprocess, sys
here = os.path.dirname(os.path.abspath(__file__))
lines, meta = [], {}
for f in sorted(glob.glob(os.path.join(here, '..', '..', 'res', 'specs', '*.json'))):
    for v in json.load(open(f))['vehicles']:
        if v['kind'] != 'car' or 'rideHz' not in v['suspension'].get('front', {}): continue
        ch, s, t = v['chassis'], v['suspension'], v['tyres']
        F, R = s['front'], s['rear']
        ls = {'bias': -0.25, 'tour': -0.22, 'at': -0.22, 'mt': -0.22, 'uhp': -0.18, 'sport': -0.17, 'semi': -0.15, 'slick': -0.12, 'drift': -0.2, 'drag': -0.15, 'knob': -0.22}.get(t['front']['compound'], -0.2)
        vals = [v['id'], ch['massKg'], ch['wheelbaseM'], (ch['trackFrontM'] + ch['trackRearM']) / 2, ch['cgHeightM'], ch['frontPct'] / 100, F['rideHz'], R['rideHz'],
                F['damping']['bumpLow'], F['damping']['bumpHigh'], F['damping']['reboundLow'], F['damping']['reboundHigh'], F['arb']['ratioToSprings'], R['arb']['ratioToSprings'],
                F['rollCentreM'], R['rollCentreM'], ch['inertia']['yawDI'], ch['inertia']['rollK'], t['rear']['radiusM'], t['unsprungFrontKg'], t['front']['verticalNm'],
                t['front']['mf']['By'], t['front']['mf']['Cy'], t['front']['mf']['Ey'], t['front']['mf']['muY'], ls, F['antiPct'], R['antiPct'], F['travelBumpM'], 0.6]
        lines.append(' '.join(str(x) for x in vals)); meta[v['id']] = v
out = subprocess.run([sys.argv[1] if len(sys.argv) > 1 else '/tmp/ball'], input='\n'.join(lines) + '\n', capture_output=True, text=True).stdout
res, cur = {}, None
for ln in out.split('\n'):
    m = re.match(r'=== (\S+)', ln)
    if m: cur = res.setdefault(m.group(1), {}); continue
    if cur is None: continue
    for key, rx in [('roll', r'roll gradient ([\d.]+)'), ('latg', r'max lateral ([\d.]+)'), ('pitch', r'pitch gradient (-?[\d.]+)'), ('rigid', r'rigid contacts (\d+)'),
                    ('settle', r'settled \(<0.15 deg\) after (-?[\d.]+)'), ('ovs', r'roll [\d.]+ deg, overshoot (-?\d+)%'), ('caught', r'flick \+ catch: .*\((caught|spun)\)')]:
        mm = re.search(rx, ln)
        if mm:
            if key == 'rigid': cur['rigid'] = cur.get('rigid', 0) + int(mm.group(1))
            else: cur[key] = mm.group(1)
bad = []
for vid, r in res.items():
    ch = meta[vid]['character']
    lim = {'race': (0.3, 1.5), 'super': (1.0, 2.6), 'hyper': (0.8, 2.2), 'sport': (1.6, 3.6), 'drift': (1.0, 2.8), 'hot': (2.0, 3.8), 'off': (2.5, 9.0), 'truck': (2.5, 9.0), 'classic': (2.5, 8.5), 'buggy': (1.0, 9.0), 'raid': (2.5, 8.0), 'muscle': (1.2, 6.5)}.get(ch, (2.0, 6.5))
    why = []
    if 'roll' in r and not lim[0] <= float(r['roll']) <= lim[1]: why.append('roll %s deg/g' % r['roll'])
    if r.get('rigid', 0) > 0: why.append('%d rigid contacts' % r['rigid'])
    if 'settle' in r and float(r['settle']) > 0.8: why.append('settles in %s s' % r['settle'])
    if 'ovs' in r and int(r['ovs']) > 25: why.append('roll overshoot %s%%' % r['ovs'])
    if 'pitch' in r and not (0.1 if ch == 'race' else 0.3) <= float(r['pitch']) <= (6.0 if ch in ('off', 'buggy', 'classic') else 4.5): why.append('pitch %s deg/g' % r['pitch'])
    if r.get('caught') == 'spun' and ch not in ('drift', 'race', 'off', 'truck', 'buggy', 'raid', 'arena'): why.append('flick not catchable')
    if why: bad.append((vid, ch, why))
print('%d vehicles on the bench, %d outside real-car ranges' % (len(res), len(bad)))
for b in bad: print('  %-14s %-8s %s' % (b[0], b[1], '; '.join(b[2])))
