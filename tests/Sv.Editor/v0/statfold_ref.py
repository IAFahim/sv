import struct,sys
M=(1<<64)-1
st=[0x5EED5EED0115]
def nxt():
  st[0]=(st[0]+0x9E3779B97F4A7C15)&M; z=st[0]
  z=((z^(z>>30))*0xBF58476D1CE4E5B9)&M; z=((z^(z>>27))*0x94D049BB133111EB)&M
  return z^(z>>31)
def fb():
  r=nxt(); pick=r&63; sign=((r>>6)&1)<<31; man=(r>>8)&0x7FFFFF
  if pick==0: return 0x7FC00000
  if pick==1: return sign|0x7F800000
  if pick==2: return sign
  if pick==3: return sign|man
  if pick==4: return sign|((254-((r>>40)&3))<<23)|man
  e=127-24+((r>>32)%49); return sign|(e<<23)|man
f32=lambda x: struct.unpack('<f',struct.pack('<f',x))[0] if x==x and abs(x)<3.5e38 else (x if x!=x else (float('inf') if x>0 else float('-inf')))
def r32(x):
  try: return struct.unpack('<f',struct.pack('<f',x))[0]
  except OverflowError: return float('inf') if x>0 else float('-inf')
uf=lambda u: struct.unpack('<f',struct.pack('<I',u))[0]
bits=lambda f: struct.unpack('<I',struct.pack('<f',f))[0]
a=open(sys.argv[1],'rb').read(); b=open(sys.argv[2],'rb').read()
agree_a=agree_b=0;N=3000
for s in range(N):
  base=uf(fb()); c=nxt()%9; ents=[]
  for i in range(8):
    if i<c: k=nxt()&3; v=uf(fb()); ents.append((k,v))
  v=base; ad=1.0
  for k,x in ents:
    if k==0: v=r32(v+x)
  for k,x in ents:
    if k==1: ad=r32(ad+x)
  v=r32(v*ad)
  for k,x in ents:
    if k==2: v=r32(v*r32(1.0+x))
  for k,x in ents:
    if k==3: v= x if v>x else v
  rb=bits(v); ua=struct.unpack_from('<I',a,4*s)[0]; ub=struct.unpack_from('<I',b,4*s)[0]
  nan=lambda u:(u&0x7F800000)==0x7F800000 and (u&0x7FFFFF)
  if ua==rb or (nan(ua) and nan(rb)): agree_a+=1
  if ub==rb or (nan(ub) and nan(rb)): agree_b+=1
print('ref-strict agrees: A',agree_a,'/',N,' B',agree_b,'/',N)
