import pickle, struct, gzip
nodes,flags,edges=pickle.load(open('graph.pkl','rb'))
# drop water-only nodes from adjacency
out=bytearray()
N=len(nodes)
out+=struct.pack('<4sii',b'MCRG',1,N)
for (x,y,z),f in zip(nodes,flags): out+=struct.pack('<fffB',x,y,z,f)
offs=[];tg=[];ln=[]
for i in range(N):
    offs.append(len(tg))
    for b,(lf,lb) in edges.get(i,{}).items():
        tg.append(b); ln.append(min(15,lf)|(min(15,lb)<<4))
offs.append(len(tg))
out+=struct.pack('<i',len(tg))
out+=struct.pack('<%di'%(N+1),*offs)
out+=struct.pack('<%di'%len(tg),*tg)
out+=bytes(ln)
gz=gzip.compress(bytes(out),9)
open('roads.bin.gz','wb').write(gz)
print(len(out),len(gz))
