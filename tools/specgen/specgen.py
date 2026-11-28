#!/usr/bin/env python3
"""Midnight Club: Los Santos - vehicle spec sheet generator.

Source: src/<class>.txt, one vehicle per line, '|' separated (see FORMAT below). Every value written there is a
published figure of the real counterpart ("pub"), or an estimate when the counterpart line starts with '~' ("est").
Everything else is derived here with the engineering rules below ("der"). Output: one JSON spec sheet per vehicle,
grouped per class in ../../res/specs/<class>.json, plus a counterpart list (counterparts.csv).

FORMAT
  id | counterpart | year | character/body | kg | front% | wheelbase | trackF/trackR | engine | hp@rpm | Nm@rpm | redline
     | gearbox | ratios (r=reverse) | final | drive | diffs | suspension | brakes | tyres | aero | extra k=v
  '-' = derive.  Variant:  id = base ; key=value ...   (keys: any column name below or an extra key)
  engine:   V6 3.8TT0.8 (layout+cylinders, litres, induction + peak boost bar, d = diesel, h = hybrid) or E (electric)
            induction: N natural, T single turbo, TT twin turbo, TW twin-scroll turbo, TX sequential twin turbo,
            TQ quad turbo, TE electrically assisted turbo, SR Roots / SW twin-screw / SC centrifugal supercharger,
            TC twincharged (turbo + supercharger)
  gearbox:  M6 manual, A8 torque-converter automatic, D7 dual clutch, S6 sequential, AM7 automated single clutch,
            C CVT, E1 single speed
  drive:    FR FF MR RR AWD-F:0.35 AWD-M:0.3 AWD-R:0.1 4WD:0.5 (front share)
  diffs:    R:lsd F:open C:visc   (open lsd tor visc ediff locked)
  susp:     MS/ML F+R+   front/rear type (MS MacPherson, DW double wishbone, ML multilink, PR pushrod,
            TB torsion/twist beam, LA live axle coils, LL leaf live axle, SA semi-trailing, TA trailing arm,
            SW swing axle, DD De Dion, TF/USD forks + SM swingarm for bikes), ARB fitted F/R
  brakes:   390V6/380V4  (disc mm, V vented / S solid / C carbon ceramic / D drum, pistons; xN = N discs)
  tyres:    255/40R20 285/35R20 uhp   (front [rear] compound)
  aero:     0.26x2.09 cl0.30 b0.45  (Cd x frontal area, lift area ClA, aero balance front) or CdA0.6
"""
import json, math, re, sys, os, csv, glob

COLS = ['id', 'cp', 'year', 'char', 'kg', 'fp', 'wb', 'track', 'engine', 'power', 'torque', 'redline', 'box', 'ratios', 'final',
        'drive', 'diffs', 'susp', 'brakes', 'tyres', 'aero', 'extra']

# ---------------------------------------------------------------- character: ride, roll, travel, damping
# ride Hz F/R, roll gradient deg/g, front share of roll stiffness, bump/droop travel m, damping scale
CHAR = {
    'lux':     (1.10, 1.20, 4.6, 0.56, 0.1, 0.10, 0.85),
    'comf':    (1.20, 1.30, 4.4, 0.57, 0.095, 0.10, 0.90),
    'fam':     (1.25, 1.35, 4.2, 0.58, 0.09, 0.10, 0.95),
    'hot':     (1.55, 1.70, 2.9, 0.52, 0.075, 0.085, 1.10),
    'sport':   (1.60, 1.75, 2.6, 0.55, 0.075, 0.08, 1.10),
    'gt':      (1.45, 1.60, 2.8, 0.55, 0.08, 0.085, 1.00),
    'super':   (1.90, 2.10, 1.8, 0.52, 0.065, 0.065, 1.20),
    'hyper':   (2.10, 2.30, 1.4, 0.50, 0.06, 0.060, 1.25),
    'race':    (2.80, 3.00, 0.8, 0.50, 0.045, 0.050, 1.40),
    'muscle':  (1.25, 1.35, 3.9, 0.62, 0.09, 0.10, 0.90),
    'mmuscle': (1.55, 1.70, 2.8, 0.58, 0.08, 0.085, 1.05),   # modern muscle
    'classic': (1.05, 1.15, 5.2, 0.60, 0.1, 0.11, 0.80),
    'csport':  (1.35, 1.45, 3.4, 0.57, 0.085, 0.09, 0.95),   # classic sports car
    'off':     (1.00, 1.10, 5.6, 0.55, 0.150, 0.20, 0.90),
    'truck':   (1.15, 1.40, 5.0, 0.55, 0.100, 0.13, 0.90),   # pickup / body-on-frame SUV
    'van':     (1.20, 1.45, 5.5, 0.55, 0.090, 0.11, 0.90),
    'hgv':     (1.40, 1.70, 4.0, 0.50, 0.090, 0.10, 0.90),   # lorries, buses
    'drift':   (1.90, 1.95, 1.8, 0.60, 0.06, 0.07, 1.20),
    'rally':   (1.40, 1.50, 3.0, 0.52, 0.13, 0.14, 1.10),
    'raid':    (1.10, 1.20, 4.0, 0.52, 0.250, 0.30, 1.10),   # desert racers / trophy trucks
    'arena':   (1.50, 1.60, 3.0, 0.55, 0.1, 0.11, 1.10),
    'drag':    (1.40, 1.30, 3.5, 0.60, 0.08, 0.12, 1.00),
    'buggy':   (1.10, 1.20, 4.0, 0.50, 0.150, 0.18, 1.00),
    'kart':    (0.00, 0.00, 0.3, 0.50, 0.020, 0.02, 1.00),
    'bike':    (0, 0, 0, 0, 0.12, 0.13, 1.0), 'sbike': (0, 0, 0, 0, 0.12, 0.13, 1.0), 'mx': (0, 0, 0, 0, 0.30, 0.31, 1.0),
    'cruiser': (0, 0, 0, 0, 0.10, 0.10, 1.0), 'scooter': (0, 0, 0, 0, 0.08, 0.09, 1.0),
}
BODY_CG = {'hatch': 0.55, 'sedan': 0.55, 'wagon': 0.57, 'coupe': 0.50, 'conv': 0.49, 'roadster': 0.46, 'sports': 0.47,
           'super': 0.42, 'hyper': 0.38, 'suv': 0.70, 'pickup': 0.72, 'van': 0.80, 'mpv': 0.65, 'race': 0.30, 'buggy': 0.55,
           'truck': 1.10, 'bus': 1.20, 'bike': 0.60, 'scooter': 0.55, 'kart': 0.25, 'limo': 0.58, 'muscle': 0.52}
RC = {'MS': 0.08, 'DW': 0.06, 'ML': 0.11, 'PR': 0.04, 'TB': 0.22, 'LA': 0.30, 'LL': 0.40, 'SA': 0.12, 'TA': 0.15, 'SW': 0.35, 'DD': 0.25, 'IB': 0.15}
ANTI = {'MS': 0.15, 'DW': 0.25, 'ML': 0.22, 'PR': 0.25, 'TB': 0.15, 'LA': 0.30, 'LL': 0.10, 'SA': 0.20, 'TA': 0.15, 'SW': 0.05, 'DD': 0.25, 'IB': 0.15}
MR = {'MS': 0.95, 'DW': 0.70, 'ML': 0.75, 'PR': 1.00, 'TB': 0.95, 'LA': 1.00, 'LL': 1.00, 'SA': 0.85, 'TA': 0.80, 'SW': 0.90, 'DD': 1.00, 'IB': 0.80}
SUSP_NAME = {'MS': 'MacPherson strut', 'DW': 'double wishbone', 'ML': 'multi-link', 'PR': 'pushrod double wishbone', 'TB': 'torsion beam',
             'LA': 'live axle, coil springs', 'LL': 'live axle, leaf springs', 'SA': 'semi-trailing arm', 'TA': 'trailing arm',
             'SW': 'swing axle', 'DD': 'De Dion axle', 'IB': 'independent beam', 'TF': 'telescopic fork', 'USD': 'upside-down fork', 'SM': 'swingarm, monoshock', 'SD': 'swingarm, twin shocks'}
# ---------------------------------------------------------------- tyre compounds
# mu lateral, base peak slip deg at CS 16/rad, MF C, cornering coeff offset, temperature window
COMP = {
    'bias':  dict(name='Bias-ply',            mu=0.82, peak=9.0, C=1.45, cs=-3.0, tOpt=55, tWin=45),
    'tour':  dict(name='Touring',             mu=0.92, peak=7.0, C=1.50, cs=0.0, tOpt=55, tWin=45),
    'at':    dict(name='All-terrain',         mu=0.86, peak=8.0, C=1.45, cs=-2.0, tOpt=50, tWin=50),
    'mt':    dict(name='Mud-terrain',         mu=0.80, peak=9.5, C=1.45, cs=-4.0, tOpt=45, tWin=50),
    'uhp':   dict(name='Ultra-high perf.',    mu=0.99, peak=6.0, C=1.60, cs=1.5, tOpt=65, tWin=42),
    'sport': dict(name='Sport',               mu=1.04, peak=5.5, C=1.65, cs=2.5, tOpt=70, tWin=40),
    'semi':  dict(name='Semi-slick',          mu=1.14, peak=5.0, C=1.70, cs=4.0, tOpt=80, tWin=38),
    'slick': dict(name='Race slick',          mu=1.40, peak=4.5, C=1.80, cs=7.0, tOpt=90, tWin=30),
    'drift': dict(name='Drift',               mu=0.88, peak=8.0, C=1.45, cs=0.5, tOpt=80, tWin=45),
    'drag':  dict(name='Drag radial',         mu=0.86, peak=8.0, C=1.60, cs=-3.0, tOpt=70, tWin=35),
    'hgv':   dict(name='Truck',               mu=0.80, peak=6.0, C=1.50, cs=-4.0, tOpt=50, tWin=50),
    'btour': dict(name='Touring (bike)',      mu=1.00, peak=7.0, C=1.55, cs=0.0, tOpt=60, tWin=45),
    'bsport':dict(name='Sport (bike)',        mu=1.08, peak=6.0, C=1.65, cs=2.0, tOpt=70, tWin=35),
    'btrack':dict(name='Track (bike)',        mu=1.22, peak=5.0, C=1.70, cs=4.0, tOpt=85, tWin=28),
    'knob':  dict(name='Off-road knobbly',    mu=0.80, peak=10.0, C=1.45, cs=-6.0, tOpt=45, tWin=50),
}
COMP_ID = {'bias': 0, 'tour': 1, 'uhp': 2, 'sport': 3, 'semi': 4, 'slick': 5, 'drift': 6, 'drag': 7, 'bsport': 8, 'btrack': 9, 'knob': 10,
           'at': 1, 'mt': 10, 'hgv': 1, 'btour': 8}

def mf(x, B, C, E): bx = B * x; return math.sin(C * math.atan(bx - E * (bx - math.atan(bx))))

def fit_E(B, C, peak_rad):
    best = None
    for i in range(-400, 99):
        E = i / 100.0
        # peak position of this E
        at, bv = 0, 0
        for j in range(1, 600):
            a = j * 0.001
            f = mf(a, B, C, E)
            if f > bv: bv, at = f, a
        d = abs(at - peak_rad)
        if best is None or d < best[0]: best = (d, E)
    return best[1]

E_CACHE = {}
WARN = []

def parse_tyre(s):
    s = s.strip().upper().replace('ZR', 'R').replace('LT', '')
    m = re.match(r'^(\d{2,3})/(\d{2,3})R(\d{2}(?:\.5)?)$', s)
    if m:
        w, a2, rim = int(m.group(1)) / 1000.0, int(m.group(2)), float(m.group(3))
        if a2 > 100: return w, (a2 / 2000.0 - rim * 0.0127) / w, rim     # race slick: width / overall diameter (mm)
        return w, a2 / 100.0, rim
    m = re.match(r'^([\d.]+)-(\d{2})$', s)                    # bias sizes: 6.70-15
    if m:
        wv = float(m.group(1)); return (wv / 1000.0 if wv > 30 else wv * 0.0254), (0.82 if wv > 30 else 0.92), float(m.group(2))
    m = re.match(r'^([A-N])(\d{2})-(\d{2})$', s)                       # 1970s US alpha-numeric: F70-14 (bias-belted)
    if m: return {'B': 165, 'C': 175, 'D': 185, 'E': 195, 'F': 205, 'G': 215, 'H': 225, 'J': 235, 'L': 245, 'N': 255}.get(m.group(1), 205) / 1000.0, int(m.group(2)) / 100.0, float(m.group(3))
    m = re.match(r'^(\d+)X([\d.]+)-(\d+)$', s)                         # kart: 10x4.50-5
    if m:
        D, W, rim = float(m.group(1)), float(m.group(2)), float(m.group(3)); return W * 0.0254, (D - rim) / 2 / W, rim
    m = re.match(r'^([\d.]+)R(\d{2}(?:\.5)?)$', s)                     # inch-width radial: 7.50R16
    if m: return float(m.group(1)) * 0.0254, 0.90, float(m.group(2))
    m = re.match(r'^([\d.]+)X([\d.]+)R?(\d{1,2}(?:\.5)?)$', s)            # 35x12.50R17 (flotation)
    if m:
        D, W, rim = float(m.group(1)), float(m.group(2)), float(m.group(3))
        return W * 0.0254, (D - rim) / 2 / W, rim
    raise ValueError('tyre size ' + s)

def tyre_sheet(size, comp, fz_nom, kind, press=None):
    w, ar, rim = parse_tyre(size)
    sw = w * ar; R = rim * 0.0254 / 2 + sw
    c = COMP[comp]
    if press is None:
        press = {'car': 2.4, 'truck': 8.0, 'bike': 2.5}.get(kind, 2.4)
        if comp == 'slick': press = 1.8
        if comp in ('at', 'mt', 'knob') and kind == 'car': press = 2.2
    # cornering stiffness per load: lower sidewalls are stiffer (published tyre data: 80-series ~11/rad, 40-series ~20/rad)
    csc = 11.0 + 22.0 * (0.80 - min(0.80, ar)) ** 0.85 + c['cs']
    csc *= (press / 2.4) ** 0.15 if kind != 'truck' else 1.0
    csc = max(6.0, min(32.0, csc))
    peak = c['peak'] * (16.0 / csc) ** 0.55
    B = csc / c['C']
    key = (round(B, 2), c['C'], round(peak, 2))
    if key not in E_CACHE: E_CACHE[key] = fit_E(B, c['C'], math.radians(peak))
    E = E_CACHE[key]
    # carcass: vertical and lateral stiffness from pressure, width and sidewall height
    kt = (60000 * press + 70000) * (w / 0.205) ** 0.5 * (0.113 / sw) ** 0.3
    if kind == 'truck': kt = (52000 * press + 120000) * (w / 0.295) ** 0.5 * (0.24 / sw) ** 0.3
    klat = 180000 * (w / 0.225) * (0.12 / sw) ** 0.8 * (press / 2.4) ** 0.5
    if kind == 'truck': klat *= 2.0
    relax = max(0.18, min(0.9, csc * fz_nom / klat))
    patch = fz_nom / (press * 1e5 * w * 0.85)
    return dict(size=size, compound=comp, compoundName=c['name'], widthM=round(w, 3), aspect=ar, rimIn=rim, sidewallM=round(sw, 3),
                radiusM=round(R, 3), pressureBar=round(press, 2), verticalNm=round(kt), lateralCarcassNm=round(klat),
                corneringPerLoad=round(csc, 2), peakSlipDeg=round(peak, 2), relaxM=round(relax, 3), contactLengthM=round(patch, 3),
                mf=dict(muY=c['mu'], muX=round(c['mu'] * 1.04, 3), By=round(B, 3), Cy=c['C'], Ey=round(E, 3)),
                tOpt=c['tOpt'], tWin=c['tWin'])

# ---------------------------------------------------------------- brakes
PISTON_AREA = {1: (2552, 1134), 2: (3181, 2036), 4: (2513, 1700), 6: (2765, 2000), 8: (3460, 2300), 10: (4000, 2600), 12: (4300, 2800)}

def parse_brake(s, front):
    s = s.strip().upper()
    n = 1
    m = re.match(r'^(.*)X(\d)$', s)
    if m: s, n = m.group(1), int(m.group(2))
    m = re.match(r'^D(\d{3})$', s)
    if m: return dict(type='drum', sizeMm=int(m.group(1)), pistons=0, count=n)
    m = re.match(r'^(\d{3})([VSC])(\d{0,2})$', s)
    if not m: raise ValueError('brake ' + s)
    d, t, p = int(m.group(1)), m.group(2), int(m.group(3) or (1 if not front else 1))
    return dict(type={'V': 'vented disc', 'S': 'solid disc', 'C': 'carbon-ceramic disc'}[t], sizeMm=d, pistons=p, count=n)

def brake_torque(b, front, pad, kind):
    pressure = 9e6 if kind != 'truck' else 8e5 * 10   # hydraulic line pressure at full braking (Pa); air brakes ~ 8 bar x chamber
    if b['type'] == 'drum':
        r = b['sizeMm'] / 2000 * 0.95
        return 2 * 0.35 * 1.6 * pressure * (1800e-6 if front else 1300e-6) * r * b['count'] * (6 if kind == 'truck' else 1)
    A = PISTON_AREA.get(b['pistons'], PISTON_AREA[4])[0 if front else 1] * 1e-6
    if kind == 'bike': A = (1600e-6 if b['pistons'] >= 4 else 1300e-6) if front else 700e-6
    mu = 0.45 if 'carbon' in b['type'] else pad
    r = b['sizeMm'] / 2000 - 0.028
    t = 2 * mu * pressure * A * r * b['count']
    if kind == 'truck': t *= 6
    return t

# ---------------------------------------------------------------- engine / gearbox
def parse_engine(s):
    s = s.strip()
    if s.upper().startswith('E'):
        return dict(layout='electric', cyl=0, litres=0, induction='electric', chargerType='none', chargerCount=0, boostBar=0, diesel=False, hybrid=False)
    m = re.match(r'^([IVFWRHB])(\d+)\s+([\d.]+)(TT|TW|TQ|TX|TE|TC|T|SR|SW|SC|S|N)([\d.]*)(d?)(h?)$', s)
    if not m: raise ValueError('engine ' + s)
    lay = {'I': 'inline', 'V': 'V', 'F': 'flat', 'W': 'W', 'R': 'rotary', 'H': 'flat', 'B': 'V'}[m.group(1)]
    ic = m.group(4)
    CH = {'N': ('na', 'none', 0), 'T': ('turbo', 'single turbo', 1), 'TT': ('turbo', 'twin turbo', 2), 'TW': ('turbo', 'twin-scroll turbo', 1),
          'TX': ('turbo', 'sequential twin turbo', 2), 'TQ': ('turbo', 'quad turbo', 4), 'TE': ('turbo', 'electric-assisted turbo', 1),
          'S': ('supercharged', 'Roots supercharger', 1), 'SR': ('supercharged', 'Roots supercharger', 1), 'SW': ('supercharged', 'twin-screw supercharger', 1),
          'SC': ('supercharged', 'centrifugal supercharger', 1), 'TC': ('turbo', 'turbo + supercharger', 2)}[ic]
    boost = float(m.group(5) or 0) if ic != 'N' else 0
    if ic != 'N' and boost <= 0: boost = 0.8 if CH[0] == 'supercharged' else 1.0
    return dict(layout=lay, code=m.group(1), cyl=int(m.group(2)), litres=float(m.group(3)), induction=CH[0],
                chargerType=CH[1], chargerCount=CH[2], boostBar=boost, diesel=bool(m.group(6)), hybrid=bool(m.group(7)))

BOX = {'M': ('manual', 350), 'A': ('automatic', 280), 'D': ('dual-clutch', 120), 'S': ('sequential', 70), 'AM': ('automated manual', 180), 'C': ('CVT', 0), 'E': ('single-speed', 0)}

def parse_box(s):
    s = s.strip().upper()
    m = re.match(r'^(AM|M|A|D|S|C|E)(\d*)$', s)
    if not m: raise ValueError('gearbox ' + s)
    t, ms = BOX[m.group(1)]
    g = int(m.group(2)) if m.group(2) else (1 if m.group(1) in ('C', 'E') else 5)
    return t, g, ms

def derive_ratios(gears, year, typ, char):
    if gears == 1: return [1.0]
    spread = {2: 1.8, 3: 2.4, 4: 3.0, 5: 3.9 if year >= 1995 else 3.6, 6: 4.8, 7: 5.6, 8: 6.6, 9: 7.8, 10: 8.5}.get(gears, 5.0)
    if char in ('race', 'drag'): spread *= 0.75
    top = {4: 1.0, 5: 0.82, 6: 0.75, 7: 0.72, 8: 0.67, 9: 0.62, 10: 0.64}.get(gears, 1.0)
    if char in ('sport', 'super', 'hyper', 'race') and gears <= 7: top = max(top, 0.8)
    return [round(top * spread * (1 / spread) ** ((i / (gears - 1)) ** 0.88), 3) for i in range(gears)]

DIFF_PARAMS = {  # preload Nm, lock power, lock coast
    'open': (15, 0.0, 0.0), 'lsd': (60, 0.40, 0.25), 'tor': (25, 0.55, 0.45), 'visc': (35, 0.25, 0.25),
    'ediff': (40, 0.60, 0.15), 'locked': (5000, 1.0, 1.0), 'spool': (5000, 1.0, 1.0)}

# ---------------------------------------------------------------- build one sheet
def num(x, d=None):
    x = (x or '').strip()
    if x in ('', '-'): return d
    return float(x)

def build(r, cls):
    prov = {}
    est = r['cp'].startswith('~')
    P = 'est' if est else 'pub'
    def mark(k, v, derived=False):
        prov[k] = 'der' if derived else P
        return v
    ex = dict(kv.split('=', 1) for kv in r.get('extra', '').split() if '=' in kv)
    year = int(num(r['year'], 2010))
    ch, _, body = r['char'].partition('/')
    body = body or {'super': 'super', 'hyper': 'hyper', 'race': 'race', 'muscle': 'muscle', 'mmuscle': 'muscle', 'off': 'suv', 'truck': 'pickup',
                    'van': 'van', 'hgv': 'truck', 'bike': 'bike', 'sbike': 'bike', 'mx': 'bike', 'cruiser': 'bike', 'scooter': 'scooter'}.get(ch, 'sedan')
    bike = ch in ('bike', 'sbike', 'mx', 'cruiser', 'scooter')
    kind = 'bike' if bike else 'truck' if ch == 'hgv' else 'car'
    kg = mark('chassis.massKg', num(r['kg'], 1500))
    fp = num(r['fp'])
    if fp is None:
        lay = (r['drive'] or 'FR').split(':')[0]
        fp = {'FF': 61, 'FR': 53, 'MR': 42, 'RR': 39, 'AWD-F': 57, 'AWD-M': 43, 'AWD-R': 39, '4WD': 55}.get(lay, 52)
        if bike: fp = 50
        mark('chassis.frontPct', fp, True)
    else: mark('chassis.frontPct', fp)
    wb = num(r['wb']); wbp = wb is not None
    if wb is None: wb = 2.65 if not bike else 1.45
    prov['chassis.wheelbaseM'] = P if wbp else 'der'
    tr = (r['track'] or '-').strip()
    if tr in ('', '-'): trF = trR = (1.58 if not bike else 0.0); prov['chassis.trackM'] = 'der'
    else:
        a = tr.split('/'); trF = float(a[0]); trR = float(a[1]) if len(a) > 1 else trF; prov['chassis.trackM'] = P
    cgh = float(ex['cgh']) if 'cgh' in ex else BODY_CG.get(body, 0.55)
    prov['chassis.cgHeightM'] = P if 'cgh' in ex else 'der'
    # engine
    eng = parse_engine(r['engine'] if r['engine'] not in ('', '-') else 'I4 2.0N')
    hp, hpr = (r['power'] or '150@6000').split('@'); nm, nmr = (r['torque'] or '200@4000').split('@')
    hp, hpr, nm, nmr = float(hp), float(hpr), float(nm), float(nmr)
    red = num(r['redline'], hpr * 1.08)
    # sanity: specific output must fit the displacement and induction (catches typos in the source tables)
    if eng['litres'] > 0 and not bike and not eng.get('hybrid') and ch not in ('rally', 'race', 'drag', 'arena', 'raid', 'drift', 'kart'):
        hpl, nml = hp / eng['litres'], nm / eng['litres']
        lim = {'na': (150, 135), 'turbo': (300, 260), 'supercharged': (230, 200)}[eng['induction']]
        if ch == 'race': lim = (lim[0] * 1.6, lim[1] * 1.3)
        if ch == 'hyper': lim = (lim[0] * 1.15, lim[1] * 1.15)
        if eng.get('layout') == 'rotary': lim = (lim[0] * 1.9, lim[1] * 1.6)
        if hpl > lim[0] or nml > lim[1] or hpl < 15: WARN.append('%s: %.0f hp/L, %.0f Nm/L for %s %.1f L' % (r['id'], hpl, nml, eng['induction'], eng['litres']))
    idle = float(ex.get('idle', 1000 if red > 9000 else 750 if eng.get('diesel') else 850))
    # gearbox
    btype, gears, shift = parse_box(r['box'] if r['box'] not in ('', '-') else ('M5' if year < 1995 else 'M6'))
    rat = (r['ratios'] or '-').strip(); rev = None
    if rat in ('', '-'):
        ratios = derive_ratios(gears, year, btype, ch); prov['drivetrain.ratios'] = 'der'
    else:
        parts = rat.split(); ratios = [float(x) for x in parts if not x.startswith('r')]
        rv = [x for x in parts if x.startswith('r')]; rev = float(rv[0][1:]) if rv else None
        prov['drivetrain.ratios'] = P
        gears = len(ratios)
    if rev is None: rev = round(max(ratios[0] * 1.05, 3.0) if gears > 1 else 1.0, 3)
    final = num(r['final'])
    prov['drivetrain.finalDrive'] = P if final is not None else 'der'
    dv = (r['drive'] or 'FR').strip(); lay, _, fs = dv.partition(':')
    fshare = float(fs) if fs else (1.0 if lay == 'FF' else 0.0)
    if bike: lay, fshare = 'chain', 0.0
    # diffs
    dd = {'R': 'open', 'F': 'open', 'C': 'none'}
    if lay in ('AWD-F', 'AWD-M', 'AWD-R'): dd['C'] = 'visc'
    if lay == '4WD': dd['C'] = 'locked'
    if r['diffs'] in ('', '-') or r['diffs'] is None:
        if ch in ('sport', 'super', 'hyper', 'race', 'drift', 'mmuscle', 'rally', 'hot') or (ch == 'muscle' and year >= 1965):
            dd['F' if lay == 'FF' else 'R'] = 'lsd'
        prov['drivetrain.diffs'] = 'der'
    else:
        for kv in r['diffs'].split(): k, v = kv.split(':'); dd[k] = v
        prov['drivetrain.diffs'] = P
    # tyres
    ty = (r['tyres'] or '').split()
    if not ty or ty[0] == '-':
        ty = {'super': ['245/35R20', '305/30R20', 'uhp'], 'hyper': ['265/35R20', '325/30R21', 'sport'], 'sport': ['235/40R18', '255/40R18', 'uhp'],
              'off': ['265/70R17', 'at'], 'truck': ['265/70R17', 'at'], 'van': ['225/75R16', 'tour'], 'hgv': ['315/80R22.5', 'hgv'],
              'classic': ['205/75R15', 'bias' if year < 1970 else 'tour'], 'muscle': ['225/70R15', 'bias' if year < 1970 else 'tour'],
              'bike': ['120/70R17', '180/55R17', 'bsport'], 'sbike': ['120/70R17', '190/55R17', 'bsport'], 'mx': ['80/100R21', '110/100R18', 'knob'],
              'cruiser': ['130/90R16', '150/80R16', 'btour'], 'scooter': ['110/70R12', '120/70R12', 'btour']}.get(ch, ['205/55R16', 'tour'])
        prov['tyres'] = 'der'
    else: prov['tyres'] = P
    comp = ty[-1] if ty[-1] in COMP else ('bsport' if bike else 'tour')
    sizes = [x for x in ty if x not in COMP]
    sF = sizes[0]; sR = sizes[1] if len(sizes) > 1 else sizes[0]
    fzF = kg * 9.81 * fp / 100 / (1 if bike else 2); fzR = kg * 9.81 * (1 - fp / 100) / (1 if bike else 2)
    if bike: fzF += 80 * 9.81 * 0.4; fzR += 80 * 9.81 * 0.6   # rider
    press = float(ex['psi']) / 14.5 if 'psi' in ex else None
    tF = tyre_sheet(sF, ex.get('compF', comp), fzF, kind, press); tR = tyre_sheet(sR, ex.get('compR', comp), fzR, kind, press)
    # suspension
    sp = (r['susp'] or '-').strip()
    if sp in ('', '-'):
        sF_t, sR_t = ('TF', 'SM') if bike else ('DW', 'LL') if ch in ('truck', 'hgv') else ('MS', 'TB' if lay == 'FF' else 'ML') if year >= 1995 else ('DW', 'LA') if lay != 'FF' else ('MS', 'TB')
        arbF, arbR = (not bike), (not bike and ch in ('sport', 'super', 'hyper', 'race', 'hot', 'mmuscle', 'drift'))
        prov['suspension.types'] = 'der'
    else:
        types, _, ab = sp.partition(' ')
        sF_t, sR_t = types.split('/')
        arbF = 'F+' in ab or (ab == '' and not bike); arbR = 'R+' in ab
        prov['suspension.types'] = P
    c = CHAR.get(ch, CHAR['fam'])
    if 'char' in ex: c = CHAR[ex['char']]
    unF = 12 + 1.1 * tF['rimIn'] + 25 * tF['widthM'] + (8 if not bike else 4)
    unR = 12 + 1.1 * tR['rimIn'] + 25 * tR['widthM'] + (7 if not bike else 6)
    if kind == 'truck': unF, unR = 180, 350
    if bike: unF, unR = 20, 28
    unF = min(unF, 0.08 * kg); unR = min(unR, 0.08 * kg)   # small vehicles (karts, ATVs, RC)
    susp = {}
    if not bike and ch != 'kart':
        ms = kg - 2 * unF - 2 * unR
        cmF = ms * fp / 100 / 2; cmR = ms * (1 - fp / 100) / 2
        fF = float(ex.get('rideF', c[0])); fR = float(ex.get('rideR', c[1]))
        kwF = cmF * (2 * math.pi * fF) ** 2; kwR = cmR * (2 * math.pi * fR) ** 2
        rcF = RC.get(sF_t, 0.08); rcR = RC.get(sR_t, 0.12)
        if ch in ('super', 'hyper', 'race'): rcF *= 0.6; rcR *= 0.7
        # roll stiffness for the character's roll gradient; the bars carry what the springs do not
        hrc = rcF * fp / 100 + rcR * (1 - fp / 100)
        Ktot = ms * 9.81 * max(0.05, cgh - hrc) / math.radians(c[2])
        KsF = kwF * trF ** 2 / 2; KsR = kwR * trR ** 2 / 2
        KF = Ktot * c[3]; KR = Ktot - KF
        arbKF = max(0.0, KF - KsF) if arbF else 0.0; arbKR = max(0.0, KR - KsR) if arbR else 0.0
        # springs alone far softer than any real car of this kind rolls (and no bar recorded): such cars carry a front
        # bar in reality (or stiffer leaf packs); fit the missing stiffness, front first (marked derived)
        Kmin = ms * 9.81 * max(0.05, cgh - hrc) / math.radians(c[2] * 1.4)
        if KsF + KsR + arbKF + arbKR < Kmin:
            need = Kmin - (KsF + KsR + arbKF + arbKR)
            arbKF += need * 0.7; arbKR += need * 0.3; prov['suspension.arb'] = 'der'
        # a bar is at most ~3x the axle's spring roll stiffness (heavier bars need matching dampers)
        arbKF = min(arbKF, 3.0 * KsF); arbKR = min(arbKR, 3.0 * KsR)
        if 'arbF' in ex: arbKF = float(ex['arbF']) * 1000 * trF ** 2
        if 'arbR' in ex: arbKR = float(ex['arbR']) * 1000 * trR ** 2
        zs = c[6] * min(1.35, (1.0 + (arbKF + arbKR) / max(1.0, KsF + KsR)) ** 0.25)
        def axle(t, kw, arbK, track, rc, front, bump, droop, fhz):
            return dict(type=SUSP_NAME.get(t, t), code=t, rideHz=round(fhz, 2), wheelRateNmm=round(kw / 1000, 1),
                        springRateNmm=round(kw / MR.get(t, 0.9) ** 2 / 1000, 1), motionRatio=MR.get(t, 0.9),
                        damping=dict(bumpLow=round(0.50 * zs, 3), bumpHigh=round(0.40 * zs, 3), reboundLow=round(0.80 * zs, 3), reboundHigh=round(0.60 * zs, 3), kneeMs=0.15),
                        arb=dict(fitted=arbK > 0, rollNmDeg=round(arbK * math.pi / 180), wheelRateNmm=round(arbK / track ** 2 / 1000, 1) if arbK > 0 else 0.0,
                                 ratioToSprings=round(arbK / (2 * kw * track ** 2 / 2), 3) if kw > 0 else 0.0),
                        rollCentreM=round(rc, 3), antiPct=round(ANTI.get(t, 0.15) * (0.3 if year < 1975 else 0.6 if year < 1990 else 1.0), 3),
                        travelBumpM=bump, travelDroopM=droop)
        susp['front'] = axle(sF_t, kwF, arbKF, trF, rcF, True, c[4], c[5], fF)
        susp['rear'] = axle(sR_t, kwR, arbKR, trR, rcR, False, c[4], c[5], fR)
        sporty = ch in ('sport', 'super', 'hyper', 'race', 'drift', 'hot')
        susp['front'].update(camberDeg=float(ex.get('camF', -1.0 if sporty else -0.5)), toeDeg=float(ex.get('toeF', 0.0)), casterDeg=float(ex.get('caster', 6.5 if sporty else 5.0)))
        susp['rear'].update(camberDeg=float(ex.get('camR', -1.5 if sporty else -1.0)), toeDeg=float(ex.get('toeR', 0.15)))
        rg = math.degrees(ms * 9.81 * max(0.05, cgh - hrc) / (KsF + KsR + arbKF + arbKR))
        susp['rollGradientDegG'] = round(rg, 2)
        prov['suspension.rates'] = 'der' if 'springF' not in ex else P
    else:
        susp['front'] = dict(type=SUSP_NAME.get(sF_t, sF_t), code=sF_t, travelBumpM=c[4], travelDroopM=c[5])
        susp['rear'] = dict(type=SUSP_NAME.get(sR_t, sR_t), code=sR_t, travelBumpM=c[4], travelDroopM=c[5])
    # brakes
    br = (r['brakes'] or '-').strip()
    pad = 0.45 if ch in ('race',) else 0.42 if ch in ('super', 'hyper', 'sport', 'drift') else 0.38
    if br in ('', '-'):
        if bike: bF, bR = parse_brake('310V4X2' if ch == 'sbike' else '300V2', True), parse_brake('240V1', False)
        elif kind == 'truck': bF, bR = parse_brake('D410', True), parse_brake('D410', False)
        elif year < 1965: bF, bR = parse_brake('D280', True), parse_brake('D254', False)
        elif year < 1985: bF, bR = parse_brake('280S2', True), parse_brake('D254', False)
        else:
            dF = int(min(400, max(256, 180 + kg * 0.09 + (40 if ch in ('sport', 'super', 'hyper') else 0))))
            bF = parse_brake('%dV%d' % (dF, 6 if ch in ('super', 'hyper') else 4 if ch == 'sport' else 1), True)
            bR = parse_brake('%d%s%d' % (int(dF * 0.92), 'V' if kg > 1300 or ch in ('sport', 'super') else 'S', 4 if ch in ('super', 'hyper') else 1), False)
        prov['brakes'] = 'der'
    else:
        a = br.split('/'); bF = parse_brake(a[0], True); bR = parse_brake(a[1] if len(a) > 1 else a[0], False); prov['brakes'] = P
    TF = brake_torque(bF, True, pad, kind); TR = brake_torque(bR, False, pad, kind)
    nF = 1 if bike else 2; nR = 1 if bike else 2
    totalF = TF * nF / tF['radiusM'] + TR * nR / tR['radiusM']
    brakes = dict(front=dict(bF, torqueNm=round(TF)), rear=dict(bR, torqueNm=round(TR)), padMu=pad,
                  biasFront=round(TF * nF / tF['radiusM'] / totalF, 3), maxForceN=round(totalF), maxDecelG=round(totalF / (kg * 9.81), 2),
                  abs=bool(int(ex.get('abs', 1 if year >= 1992 and kind != 'truck' else 0))))
    # final drive (if not published): power peak at the drag-limited top speed in top gear
    aer = (r['aero'] or '-').strip(); cda = cla = None; bal = 0.45 if lay not in ('MR', 'AWD-M') else 0.42
    if aer not in ('', '-'):
        for tok in aer.split():
            if 'x' in tok: cd, ar_ = tok.split('x'); cda = float(cd) * float(ar_)
            elif tok.startswith('CdA'): cda = float(tok[3:])
            elif tok.startswith('ClA'): cla = float(tok[3:])
            elif tok.startswith('cl'): cla = float(tok[2:])
            elif tok.startswith('b'): bal = float(tok[1:])
        prov['aero'] = P
    if cda is None:
        cda = {'super': 0.70, 'hyper': 0.75, 'sport': 0.66, 'race': 1.1, 'off': 1.05, 'truck': 1.05, 'van': 1.3, 'hgv': 5.5, 'muscle': 0.85, 'classic': 0.80}.get(ch, 0.70)
        if bike: cda = 0.45
        prov['aero'] = 'der'
    if cla is None: cla = {'super': 0.5, 'hyper': 1.5, 'race': 2.5, 'sport': 0.15}.get(ch, -0.05 if not bike else 0.0)
    if final is None:
        Pw = hp * 745.7 * 0.88
        v = (2 * Pw / (1.2 * cda)) ** (1 / 3.0)
        v = min(v, float(ex.get('vmax', 999)) / 3.6)
        final = round(red * 2 * math.pi / 60 * tR['radiusM'] / (v * red / hpr * ratios[-1]), 3)
        final = max(2.2 if not bike else 2.0, min(14.0 if bike else 7.5, final))
    eff = 0.92 if bike else 0.90 if lay == 'FF' else 0.83 if lay.startswith('AWD') or lay == '4WD' else 0.88
    sheet = dict(
        id=r['id'], kind=kind, gtaClass=cls,
        counterpart=dict(name=r['cp'].lstrip('~').strip(), year=year, estimated=est),
        character=ch, body=body,
        chassis=dict(massKg=kg, frontPct=fp, cgHeightM=cgh, wheelbaseM=wb, trackFrontM=trF, trackRearM=trR,
                     inertia=dict(yawDI=float(ex.get('yawDI', 0.9 if lay in ('MR', 'AWD-M') else 1.02 if lay in ('RR', 'AWD-R') else 1.12 if body in ('suv', 'pickup', 'van') else 1.25 if kind == 'truck' else 1.06 if ch in ('muscle', 'lux') else 1.0)),
                                  rollK=float(ex.get('rollK', 0.45 if kind == 'truck' else 0.42 if body in ('suv', 'pickup', 'van') else 0.34 if ch in ('super', 'hyper', 'race') else 0.36 if ch == 'sport' else 0.38)))),
        engine=dict(eng, powerHp=hp, powerRpm=hpr, torqueNm=nm, torqueRpm=nmr, redline=red, idle=idle),
        drivetrain=dict(layout=lay, frontShare=fshare, efficiency=eff,
                        gearbox=dict(type=btype, gears=gears, ratios=ratios, reverse=rev, finalDrive=final, shiftMs=int(ex.get('shift', shift))),
                        diffs={k: dict(type=v, preloadNm=DIFF_PARAMS[v][0], lockPower=DIFF_PARAMS[v][1], lockCoast=DIFF_PARAMS[v][2]) for k, v in dd.items() if v != 'none'}),
        suspension=susp, brakes=brakes,
        tyres=dict(front=tF, rear=tR, unsprungFrontKg=round(unF, 1), unsprungRearKg=round(unR, 1)),
        aero=dict(cdA=round(cda, 3), clA=round(cla, 3), balanceFront=bal),
        provenance=prov)
    return sheet

def read_src(path):
    rows, base = [], {}
    for ln in open(path, encoding='utf-8'):
        ln = ln.strip()
        if not ln or ln.startswith('#'): continue
        if re.match(r'^[\w]+\s*=\s*[\w]+\s*(;|$)', ln):
            head, _, ov = ln.partition(';')
            vid, bid = [x.strip() for x in head.split('=')]
            if bid not in base: raise ValueError('%s: base %s not defined above' % (vid, bid))
            r = dict(base[bid]); r['id'] = vid
            extra = r.get('extra', '')
            for kv in ov.split():
                k, v = kv.split('=', 1)
                v = v.replace('_', ' ')
                if k in COLS: r[k] = v
                else: extra += ' %s=%s' % (k, v)
            r['extra'] = extra
            if 'cp' not in ov: r['cp'] = r['cp']
            rows.append(r); base[vid] = r
            continue
        f = [x.strip() for x in ln.split('|')]
        f += [''] * (len(COLS) - len(f))
        r = dict(zip(COLS, f[:len(COLS)]))
        rows.append(r); base[r['id']] = r
    return rows

def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out = os.path.join(here, '..', '..', 'res', 'specs')
    os.makedirs(out, exist_ok=True)
    allrows = []; nerr = 0
    for p in sorted(glob.glob(os.path.join(here, 'src', '*.txt'))):
        cls = os.path.splitext(os.path.basename(p))[0]
        sheets = []
        for r in read_src(p):
            try: sheets.append(build(r, cls))
            except Exception as e: print('ERROR %s/%s: %s' % (cls, r.get('id'), e)); nerr += 1
        json.dump(dict(schema=1, gtaClass=cls, vehicles=sheets), open(os.path.join(out, cls + '.json'), 'w'), separators=(',', ':'))
        for s in sheets: allrows.append((cls, s))
        print('%-16s %3d sheets' % (cls, len(sheets)))
    with open(os.path.join(here, 'counterparts.csv'), 'w', newline='') as f:
        w = csv.writer(f)
        w.writerow(['class', 'id', 'counterpart', 'year', 'estimated', 'kg', 'front %', 'engine', 'hp', 'Nm', 'gearbox', 'gears', 'final', 'drive', 'susp F', 'susp R', 'tyres F', 'tyres R', 'brakes F', 'brakes R'])
        for cls, s in allrows:
            e, d, sp, t, b = s['engine'], s['drivetrain'], s['suspension'], s['tyres'], s['brakes']
            w.writerow([cls, s['id'], s['counterpart']['name'], s['counterpart']['year'], 'yes' if s['counterpart']['estimated'] else '', s['chassis']['massKg'], s['chassis']['frontPct'],
                        ('electric' if e['layout'] == 'electric' else '%s%d %.1f L %s' % (e.get('code', ''), e['cyl'], e['litres'], e['induction'])), e['powerHp'], e['torqueNm'],
                        d['gearbox']['type'], d['gearbox']['gears'], d['gearbox']['finalDrive'], d['layout'], sp['front']['type'], sp['rear']['type'],
                        t['front']['size'], t['rear']['size'], '%d %s' % (b['front']['sizeMm'], b['front']['type']), '%d %s' % (b['rear']['sizeMm'], b['rear']['type'])])
    for w in WARN: print('CHECK', w)
    print('total', len(allrows), 'errors', nerr, 'checks', len(WARN))
    return 1 if nerr else 0

if __name__ == '__main__': sys.exit(main())
