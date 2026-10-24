
FloatFoldProbe-pre.bin:     file format binary


Disassembly of section .data:

0000000000000000 <.data>:
   0:	48 8b 75 d0          	mov    -0x30(%rbp),%rsi
   4:	49 8b fd             	mov    %r13,%rdi
   7:	ba 00 10 00 00       	mov    $0x1000,%edx
   c:	41 ff 55 18          	call   *0x18(%r13)
  10:	48 8b 75 c8          	mov    -0x38(%rbp),%rsi
  14:	49 8b fe             	mov    %r14,%rdi
  17:	ba 00 10 00 00       	mov    $0x1000,%edx
  1c:	41 ff 56 18          	call   *0x18(%r14)
  20:	48 8b 75 d0          	mov    -0x30(%rbp),%rsi
  24:	49 8b ff             	mov    %r15,%rdi
  27:	ba 00 10 00 00       	mov    $0x1000,%edx
  2c:	41 ff 57 18          	call   *0x18(%r15)
  30:	48 89 85 70 ff ff ff 	mov    %rax,-0x90(%rbp)
  37:	48 8b 75 c8          	mov    -0x38(%rbp),%rsi
  3b:	49 8b ff             	mov    %r15,%rdi
  3e:	ba 00 10 00 00       	mov    $0x1000,%edx
  43:	41 ff 57 18          	call   *0x18(%r15)
  47:	4c 8b c0             	mov    %rax,%r8
  4a:	48 8b 8d 70 ff ff ff 	mov    -0x90(%rbp),%rcx
  51:	48 8b 7d 88          	mov    -0x78(%rbp),%rdi
  55:	48 8b 75 80          	mov    -0x80(%rbp),%rsi
  59:	48 8b 95 78 ff ff ff 	mov    -0x88(%rbp),%rdx
  60:	66 66 90             	data16 xchg %ax,%ax
  63:	e8 08 fc ff ff       	call   0xfffffffffffffc70
  68:	48 c7 45 c0 00 00 00 	movq   $0x0,-0x40(%rbp)
  6f:	00 
  70:	48 83 ec 08          	sub    $0x8,%rsp
  74:	e8 1c 00 00 00       	call   0x95
  79:	48 83 c4 08          	add    $0x8,%rsp
  7d:	48 8b 45 c0          	mov    -0x40(%rbp),%rax
  81:	48 85 c0             	test   %rax,%rax
  84:	0f 84 06 00 00 00    	je     0x90
  8a:	90                   	nop
  8b:	e8 bc 0c e2 00       	call   0xe20d4c
  90:	e9 68 00 00 00       	jmp    0xfd
  95:	48 89 65 d8          	mov    %rsp,-0x28(%rbp)
  99:	48 8b 45 d0          	mov    -0x30(%rbp),%rax
  9d:	48 c7 45 b8 00 00 00 	movq   $0x0,-0x48(%rbp)
  a4:	00 
  a5:	48 8b cd             	mov    %rbp,%rcx
  a8:	48 83 c1 b8          	add    $0xffffffffffffffb8,%rcx
  ac:	48 89 4d b0          	mov    %rcx,-0x50(%rbp)
  b0:	48 89 45 a8          	mov    %rax,-0x58(%rbp)
  b4:	48 8b 45 b0          	mov    -0x50(%rbp),%rax
  b8:	48 8b 4d a8          	mov    -0x58(%rbp),%rcx
  bc:	48 89 08             	mov    %rcx,(%rax)
  bf:	48 8b 7d b8          	mov    -0x48(%rbp),%rdi
  c3:	e8 28 89 ff ff       	call   0xffffffffffff89f0
  c8:	48 8b 45 c8          	mov    -0x38(%rbp),%rax
  cc:	48 c7 45 a0 00 00 00 	movq   $0x0,-0x60(%rbp)
  d3:	00 
  d4:	48 8b cd             	mov    %rbp,%rcx
  d7:	48 83 c1 a0          	add    $0xffffffffffffffa0,%rcx
  db:	48 89 4d 98          	mov    %rcx,-0x68(%rbp)
  df:	48 89 45 90          	mov    %rax,-0x70(%rbp)
  e3:	48 8b 45 98          	mov    -0x68(%rbp),%rax
  e7:	48 8b 4d 90          	mov    -0x70(%rbp),%rcx
  eb:	48 89 08             	mov    %rcx,(%rax)
  ee:	48 8b 7d a0          	mov    -0x60(%rbp),%rdi
  f2:	90                   	nop
  f3:	e8 f8 88 ff ff       	call   0xffffffffffff89f0
  f8:	48 8b 65 d8          	mov    -0x28(%rbp),%rsp
  fc:	c3                   	ret
  fd:	4c 8b 6d e8          	mov    -0x18(%rbp),%r13
 101:	4c 8b 75 f0          	mov    -0x10(%rbp),%r14
 105:	4c 8b 7d f8          	mov    -0x8(%rbp),%r15
 109:	c9                   	leave
 10a:	c3                   	ret
 10b:	e8 10 ba 8e ff       	call   0xffffffffff8ebb20
 110:	08 88 06 dc 22 9f    	or     %cl,-0x60dd23fa(%rax)
 116:	5e                   	pop    %rsi
	...
 11f:	00 48 83             	add    %cl,-0x7d(%rax)
 122:	ec                   	in     (%dx),%al
 123:	28 4c 89 2c          	sub    %cl,0x2c(%rcx,%rcx,4)
 127:	24 4c                	and    $0x4c,%al
 129:	89 74 24 08          	mov    %esi,0x8(%rsp)
 12d:	4c 89 7c 24 10       	mov    %r15,0x10(%rsp)
 132:	4c 8b f7             	mov    %rdi,%r14
 135:	4c 8b fe             	mov    %rsi,%r15
 138:	48 c7 44 24 18 00 00 	movq   $0x0,0x18(%rsp)
 13f:	00 00 
 141:	c7 44 24 20 00 00 00 	movl   $0x0,0x20(%rsp)
 148:	00 
 149:	48 b8 f0 de bc 9a 78 	movabs $0x123456789abcdef0,%rax
 150:	56 34 12 
 153:	48 89 44 24 18       	mov    %rax,0x18(%rsp)
 158:	45 33 ed             	xor    %r13d,%r13d
 15b:	e9 3f 00 00 00       	jmp    0x19f
 160:	48 8d 7c 24 18       	lea    0x18(%rsp),%rdi
 165:	66 90                	xchg   %ax,%ax
 167:	e8 a4 69 ff ff       	call   0xffffffffffff6b10
 16c:	b9 ff ff ff ff       	mov    $0xffffffff,%ecx
 171:	48 23 c1             	and    %rcx,%rax
 174:	c1 e8 00             	shr    $0x0,%eax
 177:	89 44 24 20          	mov    %eax,0x20(%rsp)
 17b:	49 63 cd             	movslq %r13d,%rcx
 17e:	48 c1 e1 02          	shl    $0x2,%rcx
 182:	49 8b c6             	mov    %r14,%rax
 185:	48 03 c1             	add    %rcx,%rax
 188:	f3 0f 10 44 24 20    	movss  0x20(%rsp),%xmm0
 18e:	f3 0f 5a c0          	cvtss2sd %xmm0,%xmm0
 192:	f2 44 0f 5a f8       	cvtsd2ss %xmm0,%xmm15
 197:	f3 44 0f 11 38       	movss  %xmm15,(%rax)
 19c:	41 ff c5             	inc    %r13d
 19f:	45 3b ef             	cmp    %r15d,%r13d
 1a2:	7c bc                	jl     0x160
 1a4:	4c 8b 2c 24          	mov    (%rsp),%r13
 1a8:	4c 8b 74 24 08       	mov    0x8(%rsp),%r14
 1ad:	4c 8b 7c 24 10       	mov    0x10(%rsp),%r15
 1b2:	48 83 c4 28          	add    $0x28,%rsp
 1b6:	c3                   	ret
	...
 1bf:	00 48 83             	add    %cl,-0x7d(%rax)
 1c2:	ec                   	in     (%dx),%al
 1c3:	18 48 89             	sbb    %cl,-0x77(%rax)
 1c6:	74 24                	je     0x1ec
 1c8:	08 48 89             	or     %cl,-0x77(%rax)
 1cb:	54                   	push   %rsp
 1cc:	24 10                	and    $0x10,%al
 1ce:	48 8b fe             	mov    %rsi,%rdi
 1d1:	48 63 74 24 10       	movslq 0x10(%rsp),%rsi
 1d6:	90                   	nop
 1d7:	e8 14 00 00 00       	call   0x1f0
 1dc:	48 83 c4 18          	add    $0x18,%rsp
 1e0:	c3                   	ret
 1e1:	e8 3a b9 8e ff       	call   0xffffffffff8ebb20
 1e6:	08 38                	or     %bh,(%rax)
 1e8:	52                   	push   %rdx
 1e9:	8b 1f                	mov    (%rdi),%ebx
 1eb:	9f                   	lahf
 1ec:	5e                   	pop    %rsi
 1ed:	00 00                	add    %al,(%rax)
 1ef:	00 48 83             	add    %cl,-0x7d(%rax)
 1f2:	ec                   	in     (%dx),%al
 1f3:	18 4c 89 2c          	sbb    %cl,0x2c(%rcx,%rcx,4)
 1f7:	24 4c                	and    $0x4c,%al
 1f9:	89 74 24 08          	mov    %esi,0x8(%rsp)
 1fd:	4c 89 7c 24 10       	mov    %r15,0x10(%rsp)
 202:	4c 8b f7             	mov    %rdi,%r14
 205:	4c 8b fe             	mov    %rsi,%r15
 208:	45 33 ed             	xor    %r13d,%r13d
 20b:	e9 4f 00 00 00       	jmp    0x25f
 210:	49 63 cd             	movslq %r13d,%rcx
 213:	48 c1 e1 02          	shl    $0x2,%rcx
 217:	49 8b c6             	mov    %r14,%rax
 21a:	48 03 c1             	add    %rcx,%rax
 21d:	49 63 d5             	movslq %r13d,%rdx
 220:	48 c1 e2 02          	shl    $0x2,%rdx
 224:	49 8b ce             	mov    %r14,%rcx
 227:	48 03 ca             	add    %rdx,%rcx
 22a:	f3 0f 10 01          	movss  (%rcx),%xmm0
 22e:	f3 0f 5a c0          	cvtss2sd %xmm0,%xmm0
 232:	f3 0f 10 0d 56 00 00 	movss  0x56(%rip),%xmm1        # 0x290
 239:	00 
 23a:	f3 0f 5a c9          	cvtss2sd %xmm1,%xmm1
 23e:	f2 0f 59 c1          	mulsd  %xmm1,%xmm0
 242:	f3 0f 10 0d 36 00 00 	movss  0x36(%rip),%xmm1        # 0x280
 249:	00 
 24a:	f3 0f 5a c9          	cvtss2sd %xmm1,%xmm1
 24e:	f2 0f 58 c1          	addsd  %xmm1,%xmm0
 252:	f2 44 0f 5a f8       	cvtsd2ss %xmm0,%xmm15
 257:	f3 44 0f 11 38       	movss  %xmm15,(%rax)
 25c:	41 ff c5             	inc    %r13d
 25f:	45 3b ef             	cmp    %r15d,%r13d
 262:	7c ac                	jl     0x210
 264:	4c 8b 2c 24          	mov    (%rsp),%r13
 268:	4c 8b 74 24 08       	mov    0x8(%rsp),%r14
 26d:	4c 8b 7c 24 10       	mov    0x10(%rsp),%r15
 272:	48 83 c4 18          	add    $0x18,%rsp
 276:	c3                   	ret
 277:	24 4c                	and    $0x4c,%al
 279:	8b 74 24 08          	mov    0x8(%rsp),%esi
 27d:	4c 8b 7c 95 bf       	mov    -0x41(%rbp,%rdx,4),%r15
 282:	d6                   	udb
 283:	33 c4                	xor    %esp,%eax
 285:	28 c3                	sub    %al,%bl
 287:	ba 00 10 00 00       	mov    $0x1000,%edx
 28c:	41 ff 56 18          	call   *0x18(%r14)
 290:	01 00                	add    %eax,(%rax)
 292:	80 3f 00             	cmpb   $0x0,(%rdi)
	...
 29d:	00 00                	add    %al,(%rax)
 29f:	00 48 83             	add    %cl,-0x7d(%rax)
 2a2:	ec                   	in     (%dx),%al
 2a3:	28 4c 89 2c          	sub    %cl,0x2c(%rcx,%rcx,4)
 2a7:	24 4c                	and    $0x4c,%al
 2a9:	8b ef                	mov    %edi,%ebp
 2ab:	48 89 74 24 08       	mov    %rsi,0x8(%rsp)
 2b0:	48 89 54 24 10       	mov    %rdx,0x10(%rsp)
 2b5:	4d 85 ed             	test   %r13,%r13
 2b8:	0f 84 39 00 00 00    	je     0x2f7
 2be:	49 8d 7d 18          	lea    0x18(%r13),%rdi
 2c2:	49 ba 08 d2 bc 20 9f 	movabs $0x5e9f20bcd208,%r10
 2c9:	5e 00 00 
 2cc:	66 66 90             	data16 xchg %ax,%ax
 2cf:	e8 dc 8b ff ff       	call   0xffffffffffff8eb0
 2d4:	48 8b f8             	mov    %rax,%rdi
 2d7:	48 8b 74 24 08       	mov    0x8(%rsp),%rsi
 2dc:	48 63 54 24 10       	movslq 0x10(%rsp),%rdx
 2e1:	48 89 44 24 18       	mov    %rax,0x18(%rsp)
 2e6:	ff 50 18             	call   *0x18(%rax)
 2e9:	48 8b 44 24 18       	mov    0x18(%rsp),%rax
 2ee:	4c 8b 2c 24          	mov    (%rsp),%r13
 2f2:	48 83 c4 28          	add    $0x28,%rsp
 2f6:	c3                   	ret
 2f7:	be 50 00 00 00       	mov    $0x50,%esi
 2fc:	bf 4c 01 00 00       	mov    $0x14c,%edi
 301:	66 90                	xchg   %ax,%ax
 303:	e8 98 d3 8e ff       	call   0xffffffffff8ed6a0
 308:	e8 13 b8 8e ff       	call   0xffffffffff8ebb20
 30d:	08 a0 71 bc 20 9f    	or     %ah,-0x60df438f(%rax)
 313:	5e                   	pop    %rsi
	...
 320:	48 83 ec 48          	sub    $0x48,%rsp
 324:	48 89 64 24 38       	mov    %rsp,0x38(%rsp)
 329:	48 89 6c 24 30       	mov    %rbp,0x30(%rsp)
 32e:	48 89 1c 24          	mov    %rbx,(%rsp)
 332:	4c 89 64 24 08       	mov    %r12,0x8(%rsp)
 337:	4c 89 6c 24 10       	mov    %r13,0x10(%rsp)
 33c:	4c 89 74 24 18       	mov    %r14,0x18(%rsp)
 341:	4c 89 7c 24 20       	mov    %r15,0x20(%rsp)
 346:	4c 8b f7             	mov    %rdi,%r14
 349:	4c 8b fe             	mov    %rsi,%r15
 34c:	64 4c 8b 2c 25 78 5b 	mov    %fs:0xffffffffffff5b78,%r13
 353:	ff ff 
 355:	48 8d 44 24 28       	lea    0x28(%rsp),%rax
 35a:	49 8b 4d 00          	mov    0x0(%r13),%rcx
 35e:	48 89 4c 24 28       	mov    %rcx,0x28(%rsp)
 363:	49 89 45 00          	mov    %rax,0x0(%r13)
 367:	48 b8 c0 80 6a 88 3f 	movabs $0x703f886a80c0,%rax
 36e:	70 00 00 
 371:	49 8b fe             	mov    %r14,%rdi
 374:	49 8b f7             	mov    %r15,%rsi
 377:	48 89 64 24 38       	mov    %rsp,0x38(%rsp)
 37c:	ff d0                	call   *%rax
 37e:	48 b8 a8 b4 db de 36 	movabs $0x7036dedbb4a8,%rax
 385:	70 00 00 
 388:	8b 00                	mov    (%rax),%eax
 38a:	85 c0                	test   %eax,%eax
 38c:	0f 85 3e 00 00 00    	jne    0x3d0
 392:	eb 00                	jmp    0x394
 394:	48 8b 44 24 28       	mov    0x28(%rsp),%rax
 399:	49 89 45 00          	mov    %rax,0x0(%r13)
 39d:	48 8b 1c 24          	mov    (%rsp),%rbx
 3a1:	4c 8b 64 24 08       	mov    0x8(%rsp),%r12
 3a6:	4c 8b 6c 24 10       	mov    0x10(%rsp),%r13
 3ab:	4c 8b 74 24 18       	mov    0x18(%rsp),%r14
 3b0:	4c 8b 7c 24 20       	mov    0x20(%rsp),%r15
 3b5:	48 83 c4 48          	add    $0x48,%rsp
 3b9:	c3                   	ret
 3ba:	49 8d 87 88 00 00 00 	lea    0x88(%r15),%rax
 3c1:	c7 00 01 00 00 00    	movl   $0x1,(%rax)
 3c7:	49 8b ff             	mov    %r15,%rdi
 3ca:	90                   	nop
 3cb:	e8 d0 cd 8e ff       	call   0xffffffffff8ed1a0
 3d0:	48 89 64 24 38       	mov    %rsp,0x38(%rsp)
 3d5:	66 90                	xchg   %ax,%ax
 3d7:	e8 04 24 e2 00       	call   0xe227e0
 3dc:	4c 8b f8             	mov    %rax,%r15
 3df:	48 85 c0             	test   %rax,%rax
 3e2:	75 d6                	jne    0x3ba
 3e4:	eb ae                	jmp    0x394
 3e6:	e8 b5 c3 8e ff       	call   0xffffffffff8ec7a0
 3eb:	08 b0 d4 bc 20 9f    	or     %dh,-0x60df432c(%rax)
 3f1:	5e                   	pop    %rsi
	...
 3fe:	00 00                	add    %al,(%rax)
 400:	48 83 ec 28          	sub    $0x28,%rsp
 404:	4c 89 24 24          	mov    %r12,(%rsp)
 408:	48 89 7c 24 18       	mov    %rdi,0x18(%rsp)
 40d:	48 89 74 24 20       	mov    %rsi,0x20(%rsp)
 412:	48 c7 44 24 08 00 00 	movq   $0x0,0x8(%rsp)
 419:	00 00 
 41b:	48 bf 40 bb 77 fc 3f 	movabs $0x703ffc77bb40,%rdi
 422:	70 00 00 
 425:	48 8d 74 24 08       	lea    0x8(%rsp),%rsi
 42a:	48 8d 6d 00          	lea    0x0(%rbp),%rbp
 42e:	49 bb 13 35 a0 de 36 	movabs $0x7036dea03513,%r11
 435:	70 00 00 
 438:	41 ff d3             	call   *%r11
 43b:	48 89 44 24 10       	mov    %rax,0x10(%rsp)
 440:	48 b8 a8 b4 db de 36 	movabs $0x7036dedbb4a8,%rax
 447:	70 00 00 
 44a:	8b 00                	mov    (%rax),%eax
 44c:	85 c0                	test   %eax,%eax
 44e:	0f 85 38 00 00 00    	jne    0x48c
 454:	eb 00                	jmp    0x456
 456:	48 8b 7c 24 18       	mov    0x18(%rsp),%rdi
 45b:	48 63 74 24 20       	movslq 0x20(%rsp),%rsi
 460:	66 66 90             	data16 xchg %ax,%ax
 463:	e8 88 fd ff ff       	call   0x1f0
 468:	48 8b 7c 24 10       	mov    0x10(%rsp),%rdi
 46d:	48 8d 74 24 08       	lea    0x8(%rsp),%rsi
 472:	48 8d 6d 00          	lea    0x0(%rbp),%rbp
 476:	49 bb c5 35 a0 de 36 	movabs $0x7036dea035c5,%r11
 47d:	70 00 00 
 480:	41 ff d3             	call   *%r11
 483:	4c 8b 24 24          	mov    (%rsp),%r12
 487:	48 83 c4 28          	add    $0x28,%rsp
 48b:	c3                   	ret
 48c:	66 66 90             	data16 xchg %ax,%ax
 48f:	e8 4c 23 e2 00       	call   0xe227e0
 494:	4c 8b e0             	mov    %rax,%r12
 497:	48 85 c0             	test   %rax,%rax
 49a:	74 ba                	je     0x456
 49c:	49 8d 84 24 88 00 00 	lea    0x88(%r12),%rax
 4a3:	00 
 4a4:	c7 00 01 00 00 00    	movl   $0x1,(%rax)
 4aa:	49 8b fc             	mov    %r12,%rdi
 4ad:	66 90                	xchg   %ax,%ax
 4af:	e8 ec cc 8e ff       	call   0xffffffffff8ed1a0
	...
 4c0:	48 83 ec 28          	sub    $0x28,%rsp
 4c4:	4c 89 24 24          	mov    %r12,(%rsp)
 4c8:	4c 89 6c 24 08       	mov    %r13,0x8(%rsp)
 4cd:	4c 89 74 24 10       	mov    %r14,0x10(%rsp)
 4d2:	4c 89 7c 24 18       	mov    %r15,0x18(%rsp)
 4d7:	4c 8b f7             	mov    %rdi,%r14
 4da:	4c 8b fe             	mov    %rsi,%r15
 4dd:	49 bd 25 23 22 84 e4 	movabs $0xcbf29ce484222325,%r13
 4e4:	9c f2 cb 
 4e7:	45 33 e4             	xor    %r12d,%r12d
 4ea:	eb 26                	jmp    0x512
 4ec:	48 8d 6d 00          	lea    0x0(%rbp),%rbp
 4f0:	49 63 cc             	movslq %r12d,%rcx
 4f3:	48 c1 e1 02          	shl    $0x2,%rcx
 4f7:	49 8b c6             	mov    %r14,%rax
 4fa:	48 03 c1             	add    %rcx,%rax
 4fd:	8b 00                	mov    (%rax),%eax
 4ff:	8b f0                	mov    %eax,%esi
 501:	49 8b fd             	mov    %r13,%rdi
 504:	66 66 90             	data16 xchg %ax,%ax
 507:	e8 14 84 ff ff       	call   0xffffffffffff8920
 50c:	4c 8b e8             	mov    %rax,%r13
 50f:	41 ff c4             	inc    %r12d
 512:	45 3b e7             	cmp    %r15d,%r12d
 515:	7c d9                	jl     0x4f0
 517:	49 8b c5             	mov    %r13,%rax
 51a:	4c 8b 24 24          	mov    (%rsp),%r12
 51e:	4c 8b 6c 24 08       	mov    0x8(%rsp),%r13
 523:	4c 8b 74 24 10       	mov    0x10(%rsp),%r14
 528:	4c 8b 7c 24 18       	mov    0x18(%rsp),%r15
 52d:	48 83 c4 28          	add    $0x28,%rsp
 531:	c3                   	ret
	...
