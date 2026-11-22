import json, struct, math, pickle
d=json.load(open('/tmp/nodes/nodes.json'))
key=lambda p:(round(p['X']*4),round(p['Y']*4),round(p['Z']*4))
idx={}; nodes=[]; flags=[]
def nid(n):
    k=key(n['Position'])
    if k not in idx:
        idx[k]=len(nodes); p=n['Position']; nodes.append((p['X'],p['Y'],p['Z']))
        f=(1 if n['IsValidForGps'] else 0)|(2 if n['IsFreeway'] else 0)|(4 if n['IsGravelRoad'] else 0)|(8 if n['IsOnWater'] else 0)|(16 if n['IsBackroad'] else 0)|(32 if n['IsJunction'] else 0)|(64 if n['TrafficlightExists'] else 0)
        flags.append(f)
    return idx[k]
edges={}
for area in d:
    for n in area['Nodes'] or []:
        a=nid(n)
        for c in n['ConnectedNodes'] or []:
            b=nid(c['Node'])
            if a==b: continue
            lf,lb=c['LaneCountForward'],c['LaneCountBackward']
            edges.setdefault(a,{})[b]=(lf,lb)
print(len(nodes), sum(len(v) for v in edges.values()))
pickle.dump((nodes,flags,edges),open('graph.pkl','wb'))
