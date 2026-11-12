import struct,sys
a=open(sys.argv[1],'rb').read(); b=open(sys.argv[2],'rb').read()
n=len(a)//4
ua=struct.unpack('<%dI'%n,a); ub=struct.unpack('<%dI'%n,b)
d=[i for i in range(n) if ua[i]!=ub[i]]
isnan=lambda u:(u&0x7F800000)==0x7F800000 and (u&0x7FFFFF)
real=[i for i in d if not(isnan(ua[i]) and isnan(ub[i]))]
print('diff',len(d),'nonNaN-diff',len(real))
for i in real[:8]+d[:4]:
  fa=struct.unpack('<f',struct.pack('<I',ua[i]))[0]; fb=struct.unpack('<f',struct.pack('<I',ub[i]))[0]
  print(i,hex(ua[i]),hex(ub[i]),fa,fb)
