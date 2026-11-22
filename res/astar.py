import pickle, math, heapq
nodes,flags,edges=pickle.load(open('graph.pkl','rb'))
N=len(nodes)
adj=[set() for _ in range(N)]
for a,m in edges.items():
    for b in m: adj[a].add(b); adj[b].add(a)
import collections
grid=collections.defaultdict(list)
for i,(x,y,z) in enumerate(nodes):
    if flags[i]&8: continue
    grid[(math.floor(x/100),math.floor(y/100))].append(i)
def nearest(p,maxd=300):
    cx,cy=math.floor(p[0]/100),math.floor(p[1]/100); best=-1;bd=1e18
    for r in range(4):
        for dx in range(-r,r+1):
            for dy in range(-r,r+1):
                if max(abs(dx),abs(dy))!=r: continue
                for i in grid.get((cx+dx,cy+dy),[]):
                    x,y,z=nodes[i]; d=(x-p[0])**2+(y-p[1])**2+((z-p[2])*3)**2
                    if not flags[i]&1: d*=1.6
                    if d<bd: bd,best=d,i
        if best>=0 and math.sqrt(bd)<(r-0.5)*100: break
    return best
def cost(a,b):
    d=math.dist(nodes[a],nodes[b]); f=flags[b]; m=1
    if f&8: return d*50
    if not f&1: m*=1.35
    if f&4: m*=1.8
    if f&2: m*=0.92
    return d*m
def route(p,q):
    s,g=nearest(p),nearest(q)
    if s<0 or g<0: return None
    gs={s:0};came={};op=[(0,s)];closed=set()
    H=lambda a: math.dist(nodes[a][:2],nodes[g][:2])*0.9
    while op:
        _,c=heapq.heappop(op)
        if c==g: break
        if c in closed: continue
        closed.add(c)
        for nb in adj[c]:
            t=gs[c]+cost(c,nb)
            if t<gs.get(nb,1e18): gs[nb]=t; came[nb]=c; heapq.heappush(op,(t+H(nb),nb))
    if g not in came: return None
    path=[g]
    while path[-1]!=s: path.append(came[path[-1]])
    return [nodes[i] for i in reversed(path)]
