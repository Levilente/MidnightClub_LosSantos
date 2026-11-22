from astar import *
import random, json
random.seed(11)
deg=[len(a) for a in adj]
def heading(a,b): return math.atan2(nodes[b][1]-nodes[a][1],nodes[b][0]-nodes[a][0])
def angdiff(x,y): d=(x-y+math.pi)%(2*math.pi)-math.pi; return d
def walk(start,prev,maxlen,want):
    path=[start] if prev is None else [prev,start]; L=0; tot=0; absturn=0
    cur,pv=start,prev
    while L<maxlen:
        best=None;bs=1e9
        for nb in adj[cur]:
            if nb in path or flags[nb]&8: continue
            if not flags[nb]&1: continue
            if pv is None: s=0
            else:
                t=abs(angdiff(heading(cur,nb),heading(pv,cur)))
                s= t if want=='straight' else (abs(t-0.25) if t<1.2 else 9)
            if s<bs: bs,best=s,nb
        if best is None: return None
        if pv is not None: 
            t=angdiff(heading(cur,best),heading(pv,cur)); tot+=t; absturn+=abs(t)
        L+=math.dist(nodes[cur],nodes[best]); path.append(best); pv,cur=cur,best
    return path,L,abs(tot),absturn
def far(p,chosen,mind):
    return all(math.dist(nodes[p][:2],nodes[c][:2])>mind for c in chosen)
speed=[];sc=[]
cands=[i for i in range(N) if flags[i]&1 and not flags[i]&8 and deg[i]==2]
random.shuffle(cands)
for i in cands:
    if len(speed)>=12: break
    nb=list(adj[i])
    r=walk(i,nb[0],900,'straight')
    if not r: continue
    path,L,tot,absturn=r
    if tot<0.25 and absturn<1.2 and far(i,sc,1500):
        speed.append(path); sc.append(i)
drift=[];dc=[]
for i in cands:
    if len(drift)>=10: break
    if flags[i]&2: continue
    x,y,z=nodes[i]
    nb=list(adj[i])
    r=walk(i,nb[0],650,'curvy')
    if not r: continue
    path,L,tot,absturn=r
    if absturn>3.5 and far(i,dc,1500) and all(not flags[p]&2 for p in path):
        drift.append(path); dc.append(i)
# barn finds: dead ends, countryside, gravel/backroad
barn=[];bc=[]
dead=[i for i in range(N) if deg[i]==1 and not flags[i]&8 and nodes[i][1]>900 and (flags[i]&4 or flags[i]&16)]
random.shuffle(dead)
for i in dead:
    if len(barn)>=8: break
    if far(i,bc,2500): barn.append(i); bc.append(i)
def pts(path): 
    out=[path[0]]; acc=0
    for a,b in zip(path,path[1:]):
        acc+=math.dist(nodes[a],nodes[b])
        if acc>=25: out.append(b); acc=0
    if out[-1]!=path[-1]: out.append(path[-1])
    return [[round(c,2) for c in nodes[k]] for k in out]
data={"speed":[pts(p) for p in speed],"drift":[pts(p) for p in drift],"barn":[[round(c,2) for c in nodes[i]] for i in barn]}
print(len(speed),len(drift),len(barn))
for p in speed: print('S',[round(v) for v in nodes[p[0]]], round(sum(math.dist(nodes[a],nodes[b]) for a,b in zip(p,p[1:]))))
for p in drift: print('D',[round(v) for v in nodes[p[0]]])
for b in barn: print('B',[round(v) for v in nodes[b]])
json.dump(data,open('skills.json','w'))
