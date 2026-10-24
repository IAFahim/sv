
RunProgram.bin:     file format binary


Disassembly of section .data:

0000000000000000 <.data>:
   0:	55                   	push   %rbp
   1:	48 89 e5             	mov    %rsp,%rbp
   4:	e8 a7 06 00 00       	call   0x6b0
   9:	5d                   	pop    %rbp
   a:	c3                   	ret
   b:	0f 1f 44 00 00       	nopl   0x0(%rax,%rax,1)
  10:	55                   	push   %rbp
  11:	48 89 e5             	mov    %rsp,%rbp
  14:	5d                   	pop    %rbp
  15:	c3                   	ret
  16:	66 2e 0f 1f 84 00 00 	cs nopw 0x0(%rax,%rax,1)
  1d:	00 00 00 
  20:	55                   	push   %rbp
  21:	48 89 e5             	mov    %rsp,%rbp
  24:	5d                   	pop    %rbp
  25:	c3                   	ret
  26:	cc                   	int3
  27:	cc                   	int3
  28:	cc                   	int3
  29:	cc                   	int3
  2a:	cc                   	int3
  2b:	cc                   	int3
  2c:	cc                   	int3
  2d:	cc                   	int3
  2e:	cc                   	int3
  2f:	cc                   	int3
  30:	85 d2                	test   %edx,%edx
  32:	0f 8e d4 00 00 00    	jle    0x10c
  38:	55                   	push   %rbp
  39:	48 89 e5             	mov    %rsp,%rbp
  3c:	41 57                	push   %r15
  3e:	41 56                	push   %r14
  40:	53                   	push   %rbx
  41:	50                   	push   %rax
  42:	48 89 f3             	mov    %rsi,%rbx
  45:	49 89 fe             	mov    %rdi,%r14
  48:	41 89 d7             	mov    %edx,%r15d
  4b:	48 83 c3 58          	add    $0x58,%rbx
  4f:	eb 32                	jmp    0x83
  51:	66 2e 0f 1f 84 00 00 	cs nopw 0x0(%rax,%rax,1)
  58:	00 00 00 
  5b:	0f 1f 44 00 00       	nopl   0x0(%rax,%rax,1)
  60:	8b 53 e4             	mov    -0x1c(%rbx),%edx
  63:	48 8b 4b f0          	mov    -0x10(%rbx),%rcx
  67:	4c 8d 4b b0          	lea    -0x50(%rbx),%r9
  6b:	4c 89 f7             	mov    %r14,%rdi
  6e:	45 31 c0             	xor    %r8d,%r8d
  71:	e8 7a 06 00 00       	call   0x6f0
  76:	48 83 c3 60          	add    $0x60,%rbx
  7a:	49 ff cf             	dec    %r15
  7d:	0f 84 7f 00 00 00    	je     0x102
  83:	0f b6 43 a8          	movzbl -0x58(%rbx),%eax
  87:	85 c0                	test   %eax,%eax
  89:	8b 73 e0             	mov    -0x20(%rbx),%esi
  8c:	74 32                	je     0xc0
  8e:	83 f8 01             	cmp    $0x1,%eax
  91:	74 cd                	je     0x60
  93:	83 f8 02             	cmp    $0x2,%eax
  96:	75 48                	jne    0xe0
  98:	8b 13                	mov    (%rbx),%edx
  9a:	48 8b 4b f8          	mov    -0x8(%rbx),%rcx
  9e:	44 8b 4b e8          	mov    -0x18(%rbx),%r9d
  a2:	48 8d 43 b0          	lea    -0x50(%rbx),%rax
  a6:	48 89 04 24          	mov    %rax,(%rsp)
  aa:	4c 89 f7             	mov    %r14,%rdi
  ad:	45 31 c0             	xor    %r8d,%r8d
  b0:	e8 4b 06 00 00       	call   0x700
  b5:	eb bf                	jmp    0x76
  b7:	66 0f 1f 84 00 00 00 	nopw   0x0(%rax,%rax,1)
  be:	00 00 
  c0:	8b 53 e4             	mov    -0x1c(%rbx),%edx
  c3:	44 8b 43 e8          	mov    -0x18(%rbx),%r8d
  c7:	4c 8d 4b b0          	lea    -0x50(%rbx),%r9
  cb:	4c 89 f7             	mov    %r14,%rdi
  ce:	31 c9                	xor    %ecx,%ecx
  d0:	eb 9f                	jmp    0x71
  d2:	66 2e 0f 1f 84 00 00 	cs nopw 0x0(%rax,%rax,1)
  d9:	00 00 00 
  dc:	0f 1f 40 00          	nopl   0x0(%rax)
  e0:	8b 13                	mov    (%rbx),%edx
  e2:	4c 8b 43 f0          	mov    -0x10(%rbx),%r8
  e6:	48 8b 4b f8          	mov    -0x8(%rbx),%rcx
  ea:	48 8d 43 b0          	lea    -0x50(%rbx),%rax
  ee:	48 89 04 24          	mov    %rax,(%rsp)
  f2:	4c 89 f7             	mov    %r14,%rdi
  f5:	45 31 c9             	xor    %r9d,%r9d
  f8:	e8 03 06 00 00       	call   0x700
  fd:	e9 74 ff ff ff       	jmp    0x76
 102:	48 83 c4 08          	add    $0x8,%rsp
 106:	5b                   	pop    %rbx
 107:	41 5e                	pop    %r14
 109:	41 5f                	pop    %r15
 10b:	5d                   	pop    %rbp
 10c:	c3                   	ret
 10d:	cc                   	int3
 10e:	cc                   	int3
 10f:	cc                   	int3
 110:	85 d2                	test   %edx,%edx
 112:	0f 8e f1 01 00 00    	jle    0x309
 118:	55                   	push   %rbp
 119:	48 89 e5             	mov    %rsp,%rbp
 11c:	41 57                	push   %r15
 11e:	41 56                	push   %r14
 120:	41 54                	push   %r12
 122:	53                   	push   %rbx
 123:	89 d0                	mov    %edx,%eax
 125:	c5 fa 10 05 3b ed ff 	vmovss -0x12c5(%rip),%xmm0        # 0xffffffffffffee68
 12c:	ff 
 12d:	c5 fa 10 0d 37 ed ff 	vmovss -0x12c9(%rip),%xmm1        # 0xffffffffffffee6c
 134:	ff 
 135:	eb 14                	jmp    0x14b
 137:	66 0f 1f 84 00 00 00 	nopw   0x0(%rax,%rax,1)
 13e:	00 00 
 140:	ff c6                	inc    %esi
 142:	48 ff c8             	dec    %rax
 145:	0f 84 b6 01 00 00    	je     0x301
 14b:	48 85 c9             	test   %rcx,%rcx
 14e:	48 63 d6             	movslq %esi,%rdx
 151:	44 89 c3             	mov    %r8d,%ebx
 154:	74 03                	je     0x159
 156:	8b 1c 91             	mov    (%rcx,%rdx,4),%ebx
 159:	4c 8b 1f             	mov    (%rdi),%r11
 15c:	45 8b 14 93          	mov    (%r11,%rdx,4),%r10d
 160:	46 8d 34 13          	lea    (%rbx,%r10,1),%r14d
 164:	45 89 f7             	mov    %r14d,%r15d
 167:	45 31 d7             	xor    %r10d,%r15d
 16a:	45 89 f4             	mov    %r14d,%r12d
 16d:	41 31 dc             	xor    %ebx,%r12d
 170:	c1 eb 1f             	shr    $0x1f,%ebx
 173:	81 c3 ff ff ff 7f    	add    $0x7fffffff,%ebx
 179:	45 85 e7             	test   %r12d,%r15d
 17c:	41 0f 49 de          	cmovns %r14d,%ebx
 180:	45 0f b6 31          	movzbl (%r9),%r14d
 184:	41 83 fe 03          	cmp    $0x3,%r14d
 188:	74 66                	je     0x1f0
 18a:	41 83 fe 02          	cmp    $0x2,%r14d
 18e:	74 30                	je     0x1c0
 190:	41 83 fe 01          	cmp    $0x1,%r14d
 194:	75 1a                	jne    0x1b0
 196:	45 8b 71 04          	mov    0x4(%r9),%r14d
 19a:	44 39 f3             	cmp    %r14d,%ebx
 19d:	0f 8c 23 01 00 00    	jl     0x2c6
 1a3:	45 8b 71 08          	mov    0x8(%r9),%r14d
 1a7:	44 39 f3             	cmp    %r14d,%ebx
 1aa:	7c 04                	jl     0x1b0
 1ac:	44 89 f3             	mov    %r14d,%ebx
 1af:	90                   	nop
 1b0:	41 89 de             	mov    %ebx,%r14d
 1b3:	e9 0e 01 00 00       	jmp    0x2c6
 1b8:	0f 1f 84 00 00 00 00 	nopl   0x0(%rax,%rax,1)
 1bf:	00 
 1c0:	4d 8b 71 10          	mov    0x10(%r9),%r14
 1c4:	4d 85 f6             	test   %r14,%r14
 1c7:	0f 84 be 00 00 00    	je     0x28b
 1cd:	45 8b 34 96          	mov    (%r14,%rdx,4),%r14d
 1d1:	4d 8b 79 18          	mov    0x18(%r9),%r15
 1d5:	4d 85 ff             	test   %r15,%r15
 1d8:	0f 84 c0 00 00 00    	je     0x29e
 1de:	45 8b 3c 97          	mov    (%r15,%rdx,4),%r15d
 1e2:	e9 c9 00 00 00       	jmp    0x2b0
 1e7:	66 0f 1f 84 00 00 00 	nopw   0x0(%rax,%rax,1)
 1ee:	00 00 
 1f0:	4d 8b 79 20          	mov    0x20(%r9),%r15
 1f4:	41 be 00 00 00 80    	mov    $0x80000000,%r14d
 1fa:	4d 85 ff             	test   %r15,%r15
 1fd:	74 41                	je     0x240
 1ff:	c4 c1 79 6e 14 97    	vmovd  (%r15,%rdx,4),%xmm2
 205:	c4 c1 79 7e d7       	vmovd  %xmm2,%r15d
 20a:	41 81 e7 ff ff ff 7f 	and    $0x7fffffff,%r15d
 211:	45 31 f6             	xor    %r14d,%r14d
 214:	41 81 ff 00 00 80 7f 	cmp    $0x7f800000,%r15d
 21b:	77 23                	ja     0x240
 21d:	41 be ff ff ff 7f    	mov    $0x7fffffff,%r14d
 223:	c5 f8 2e d0          	vucomiss %xmm0,%xmm2
 227:	73 17                	jae    0x240
 229:	c5 f8 2e ca          	vucomiss %xmm2,%xmm1
 22d:	41 be 00 00 00 80    	mov    $0x80000000,%r14d
 233:	77 0b                	ja     0x240
 235:	c5 7a 2c f2          	vcvttss2si %xmm2,%r14d
 239:	0f 1f 80 00 00 00 00 	nopl   0x0(%rax)
 240:	4d 8b 61 28          	mov    0x28(%r9),%r12
 244:	41 bf ff ff ff 7f    	mov    $0x7fffffff,%r15d
 24a:	4d 85 e4             	test   %r12,%r12
 24d:	74 61                	je     0x2b0
 24f:	c4 c1 79 6e 14 94    	vmovd  (%r12,%rdx,4),%xmm2
 255:	c4 c1 79 7e d4       	vmovd  %xmm2,%r12d
 25a:	41 81 e4 ff ff ff 7f 	and    $0x7fffffff,%r12d
 261:	45 31 ff             	xor    %r15d,%r15d
 264:	41 81 fc 00 00 80 7f 	cmp    $0x7f800000,%r12d
 26b:	77 43                	ja     0x2b0
 26d:	c5 f8 2e d0          	vucomiss %xmm0,%xmm2
 271:	41 bf ff ff ff 7f    	mov    $0x7fffffff,%r15d
 277:	73 37                	jae    0x2b0
 279:	41 bf 00 00 00 80    	mov    $0x80000000,%r15d
 27f:	c5 f8 2e ca          	vucomiss %xmm2,%xmm1
 283:	77 2b                	ja     0x2b0
 285:	c5 7a 2c fa          	vcvttss2si %xmm2,%r15d
 289:	eb 25                	jmp    0x2b0
 28b:	41 be 00 00 00 80    	mov    $0x80000000,%r14d
 291:	4d 8b 79 18          	mov    0x18(%r9),%r15
 295:	4d 85 ff             	test   %r15,%r15
 298:	0f 85 40 ff ff ff    	jne    0x1de
 29e:	41 bf ff ff ff 7f    	mov    $0x7fffffff,%r15d
 2a4:	66 2e 0f 1f 84 00 00 	cs nopw 0x0(%rax,%rax,1)
 2ab:	00 00 00 
 2ae:	66 90                	xchg   %ax,%ax
 2b0:	45 39 fe             	cmp    %r15d,%r14d
 2b3:	45 0f 4d f7          	cmovge %r15d,%r14d
 2b7:	44 39 f3             	cmp    %r14d,%ebx
 2ba:	7c 0a                	jl     0x2c6
 2bc:	44 39 fb             	cmp    %r15d,%ebx
 2bf:	44 0f 4c fb          	cmovl  %ebx,%r15d
 2c3:	45 89 fe             	mov    %r15d,%r14d
 2c6:	45 89 34 93          	mov    %r14d,(%r11,%rdx,4)
 2ca:	4c 8b 5f 08          	mov    0x8(%rdi),%r11
 2ce:	4d 85 db             	test   %r11,%r11
 2d1:	0f 84 69 fe ff ff    	je     0x140
 2d7:	44 89 f3             	mov    %r14d,%ebx
 2da:	44 29 d3             	sub    %r10d,%ebx
 2dd:	45 31 f2             	xor    %r14d,%r10d
 2e0:	41 89 df             	mov    %ebx,%r15d
 2e3:	45 31 f7             	xor    %r14d,%r15d
 2e6:	41 c1 ee 1f          	shr    $0x1f,%r14d
 2ea:	41 81 c6 ff ff ff 7f 	add    $0x7fffffff,%r14d
 2f1:	45 85 d7             	test   %r10d,%r15d
 2f4:	44 0f 49 f3          	cmovns %ebx,%r14d
 2f8:	45 89 34 93          	mov    %r14d,(%r11,%rdx,4)
 2fc:	e9 3f fe ff ff       	jmp    0x140
 301:	5b                   	pop    %rbx
 302:	41 5c                	pop    %r12
 304:	41 5e                	pop    %r14
 306:	41 5f                	pop    %r15
 308:	5d                   	pop    %rbp
 309:	c3                   	ret
 30a:	cc                   	int3
 30b:	cc                   	int3
 30c:	cc                   	int3
 30d:	cc                   	int3
 30e:	cc                   	int3
 30f:	cc                   	int3
 310:	55                   	push   %rbp
 311:	48 89 e5             	mov    %rsp,%rbp
 314:	41 57                	push   %r15
 316:	41 56                	push   %r14
 318:	41 55                	push   %r13
 31a:	41 54                	push   %r12
 31c:	53                   	push   %rbx
 31d:	48 89 4d c0          	mov    %rcx,-0x40(%rbp)
 321:	89 75 d4             	mov    %esi,-0x2c(%rbp)
 324:	85 d2                	test   %edx,%edx
 326:	0f 8e 43 03 00 00    	jle    0x66f
 32c:	48 8b 45 10          	mov    0x10(%rbp),%rax
 330:	89 d1                	mov    %edx,%ecx
 332:	48 89 4d c8          	mov    %rcx,-0x38(%rbp)
 336:	31 d2                	xor    %edx,%edx
 338:	c5 fa 10 05 28 eb ff 	vmovss -0x14d8(%rip),%xmm0        # 0xffffffffffffee68
 33f:	ff 
 340:	c5 fa 10 0d 24 eb ff 	vmovss -0x14dc(%rip),%xmm1        # 0xffffffffffffee6c
 347:	ff 
 348:	eb 17                	jmp    0x361
 34a:	66 0f 1f 44 00 00    	nopw   0x0(%rax,%rax,1)
 350:	48 8b 55 b8          	mov    -0x48(%rbp),%rdx
 354:	48 ff c2             	inc    %rdx
 357:	48 3b 55 c8          	cmp    -0x38(%rbp),%rdx
 35b:	0f 84 0e 03 00 00    	je     0x66f
 361:	48 8b 4d c0          	mov    -0x40(%rbp),%rcx
 365:	4c 8b 1c d1          	mov    (%rcx,%rdx,8),%r11
 369:	48 89 55 b8          	mov    %rdx,-0x48(%rbp)
 36d:	89 d3                	mov    %edx,%ebx
 36f:	c1 e3 06             	shl    $0x6,%ebx
 372:	03 5d d4             	add    -0x2c(%rbp),%ebx
 375:	4d 85 db             	test   %r11,%r11
 378:	4d 89 de             	mov    %r11,%r14
 37b:	75 1e                	jne    0x39b
 37d:	48 83 7f 08 00       	cmpq   $0x0,0x8(%rdi)
 382:	0f 85 4d 02 00 00    	jne    0x5d5
 388:	eb c6                	jmp    0x350
 38a:	66 0f 1f 44 00 00    	nopw   0x0(%rax,%rax,1)
 390:	c4 c2 88 f3 ce       	blsr   %r14,%r14
 395:	0f 84 25 02 00 00    	je     0x5c0
 39b:	4c 89 f1             	mov    %r14,%rcx
 39e:	48 c1 e9 20          	shr    $0x20,%rcx
 3a2:	31 d2                	xor    %edx,%edx
 3a4:	45 85 f6             	test   %r14d,%r14d
 3a7:	0f 94 c2             	sete   %dl
 3aa:	49 0f 45 ce          	cmovne %r14,%rcx
 3ae:	c1 e2 05             	shl    $0x5,%edx
 3b1:	8d 72 01             	lea    0x1(%rdx),%esi
 3b4:	83 ca 11             	or     $0x11,%edx
 3b7:	49 89 ca             	mov    %rcx,%r10
 3ba:	49 c1 ea 10          	shr    $0x10,%r10
 3be:	66 85 c9             	test   %cx,%cx
 3c1:	0f 45 d6             	cmovne %esi,%edx
 3c4:	4c 0f 45 d1          	cmovne %rcx,%r10
 3c8:	8d 4a 08             	lea    0x8(%rdx),%ecx
 3cb:	4c 89 d6             	mov    %r10,%rsi
 3ce:	48 c1 ee 08          	shr    $0x8,%rsi
 3d2:	45 84 d2             	test   %r10b,%r10b
 3d5:	0f 45 ca             	cmovne %edx,%ecx
 3d8:	49 0f 45 f2          	cmovne %r10,%rsi
 3dc:	8d 51 04             	lea    0x4(%rcx),%edx
 3df:	49 89 f2             	mov    %rsi,%r10
 3e2:	49 c1 ea 04          	shr    $0x4,%r10
 3e6:	40 f6 c6 0f          	test   $0xf,%sil
 3ea:	0f 45 d1             	cmovne %ecx,%edx
 3ed:	4c 0f 45 d6          	cmovne %rsi,%r10
 3f1:	8d 4a 02             	lea    0x2(%rdx),%ecx
 3f4:	44 89 d6             	mov    %r10d,%esi
 3f7:	c1 ee 02             	shr    $0x2,%esi
 3fa:	41 f6 c2 03          	test   $0x3,%r10b
 3fe:	0f 45 ca             	cmovne %edx,%ecx
 401:	41 0f 45 f2          	cmovne %r10d,%esi
 405:	83 e6 01             	and    $0x1,%esi
 408:	01 d9                	add    %ebx,%ecx
 40a:	29 f1                	sub    %esi,%ecx
 40c:	4d 85 c0             	test   %r8,%r8
 40f:	4c 63 f9             	movslq %ecx,%r15
 412:	44 89 ca             	mov    %r9d,%edx
 415:	74 04                	je     0x41b
 417:	43 8b 14 b8          	mov    (%r8,%r15,4),%edx
 41b:	4c 8b 2f             	mov    (%rdi),%r13
 41e:	47 8b 64 bd 00       	mov    0x0(%r13,%r15,4),%r12d
 423:	42 8d 0c 22          	lea    (%rdx,%r12,1),%ecx
 427:	89 ce                	mov    %ecx,%esi
 429:	44 31 e6             	xor    %r12d,%esi
 42c:	41 89 ca             	mov    %ecx,%r10d
 42f:	41 31 d2             	xor    %edx,%r10d
 432:	c1 ea 1f             	shr    $0x1f,%edx
 435:	81 c2 ff ff ff 7f    	add    $0x7fffffff,%edx
 43b:	44 85 d6             	test   %r10d,%esi
 43e:	0f 49 d1             	cmovns %ecx,%edx
 441:	0f b6 08             	movzbl (%rax),%ecx
 444:	83 f9 03             	cmp    $0x3,%ecx
 447:	74 67                	je     0x4b0
 449:	83 f9 02             	cmp    $0x2,%ecx
 44c:	74 32                	je     0x480
 44e:	83 f9 01             	cmp    $0x1,%ecx
 451:	0f 85 f9 00 00 00    	jne    0x550
 457:	44 8b 50 04          	mov    0x4(%rax),%r10d
 45b:	44 39 d2             	cmp    %r10d,%edx
 45e:	0f 8c 20 01 00 00    	jl     0x584
 464:	44 8b 50 08          	mov    0x8(%rax),%r10d
 468:	44 39 d2             	cmp    %r10d,%edx
 46b:	44 0f 4c d2          	cmovl  %edx,%r10d
 46f:	e9 10 01 00 00       	jmp    0x584
 474:	66 2e 0f 1f 84 00 00 	cs nopw 0x0(%rax,%rax,1)
 47b:	00 00 00 
 47e:	66 90                	xchg   %ax,%ax
 480:	48 8b 48 10          	mov    0x10(%rax),%rcx
 484:	48 85 c9             	test   %rcx,%rcx
 487:	0f 84 c8 00 00 00    	je     0x555
 48d:	46 8b 14 b9          	mov    (%rcx,%r15,4),%r10d
 491:	48 8b 48 18          	mov    0x18(%rax),%rcx
 495:	48 85 c9             	test   %rcx,%rcx
 498:	0f 84 ca 00 00 00    	je     0x568
 49e:	42 8b 0c b9          	mov    (%rcx,%r15,4),%ecx
 4a2:	e9 c9 00 00 00       	jmp    0x570
 4a7:	66 0f 1f 84 00 00 00 	nopw   0x0(%rax,%rax,1)
 4ae:	00 00 
 4b0:	48 8b 48 20          	mov    0x20(%rax),%rcx
 4b4:	41 ba 00 00 00 80    	mov    $0x80000000,%r10d
 4ba:	48 85 c9             	test   %rcx,%rcx
 4bd:	74 41                	je     0x500
 4bf:	c4 a1 79 6e 14 b9    	vmovd  (%rcx,%r15,4),%xmm2
 4c5:	c5 f9 7e d1          	vmovd  %xmm2,%ecx
 4c9:	81 e1 ff ff ff 7f    	and    $0x7fffffff,%ecx
 4cf:	45 31 d2             	xor    %r10d,%r10d
 4d2:	81 f9 00 00 80 7f    	cmp    $0x7f800000,%ecx
 4d8:	77 26                	ja     0x500
 4da:	41 ba ff ff ff 7f    	mov    $0x7fffffff,%r10d
 4e0:	c5 f8 2e d0          	vucomiss %xmm0,%xmm2
 4e4:	73 1a                	jae    0x500
 4e6:	c5 f8 2e ca          	vucomiss %xmm2,%xmm1
 4ea:	41 ba 00 00 00 80    	mov    $0x80000000,%r10d
 4f0:	77 0e                	ja     0x500
 4f2:	c5 7a 2c d2          	vcvttss2si %xmm2,%r10d
 4f6:	66 2e 0f 1f 84 00 00 	cs nopw 0x0(%rax,%rax,1)
 4fd:	00 00 00 
 500:	48 8b 70 28          	mov    0x28(%rax),%rsi
 504:	b9 ff ff ff 7f       	mov    $0x7fffffff,%ecx
 509:	48 85 f6             	test   %rsi,%rsi
 50c:	74 62                	je     0x570
 50e:	c4 a1 79 6e 14 be    	vmovd  (%rsi,%r15,4),%xmm2
 514:	c5 f9 7e d6          	vmovd  %xmm2,%esi
 518:	81 e6 ff ff ff 7f    	and    $0x7fffffff,%esi
 51e:	31 c9                	xor    %ecx,%ecx
 520:	81 fe 00 00 80 7f    	cmp    $0x7f800000,%esi
 526:	77 48                	ja     0x570
 528:	c5 f8 2e d0          	vucomiss %xmm0,%xmm2
 52c:	b9 ff ff ff 7f       	mov    $0x7fffffff,%ecx
 531:	73 3d                	jae    0x570
 533:	b9 00 00 00 80       	mov    $0x80000000,%ecx
 538:	c5 f8 2e ca          	vucomiss %xmm2,%xmm1
 53c:	77 32                	ja     0x570
 53e:	c5 fa 2c ca          	vcvttss2si %xmm2,%ecx
 542:	eb 2c                	jmp    0x570
 544:	66 2e 0f 1f 84 00 00 	cs nopw 0x0(%rax,%rax,1)
 54b:	00 00 00 
 54e:	66 90                	xchg   %ax,%ax
 550:	41 89 d2             	mov    %edx,%r10d
 553:	eb 2f                	jmp    0x584
 555:	41 ba 00 00 00 80    	mov    $0x80000000,%r10d
 55b:	48 8b 48 18          	mov    0x18(%rax),%rcx
 55f:	48 85 c9             	test   %rcx,%rcx
 562:	0f 85 36 ff ff ff    	jne    0x49e
 568:	b9 ff ff ff 7f       	mov    $0x7fffffff,%ecx
 56d:	0f 1f 00             	nopl   (%rax)
 570:	41 39 ca             	cmp    %ecx,%r10d
 573:	44 0f 4d d1          	cmovge %ecx,%r10d
 577:	44 39 d2             	cmp    %r10d,%edx
 57a:	7c 08                	jl     0x584
 57c:	39 ca                	cmp    %ecx,%edx
 57e:	0f 4c ca             	cmovl  %edx,%ecx
 581:	41 89 ca             	mov    %ecx,%r10d
 584:	47 89 54 bd 00       	mov    %r10d,0x0(%r13,%r15,4)
 589:	48 8b 4f 08          	mov    0x8(%rdi),%rcx
 58d:	48 85 c9             	test   %rcx,%rcx
 590:	0f 84 fa fd ff ff    	je     0x390
 596:	44 89 d2             	mov    %r10d,%edx
 599:	44 29 e2             	sub    %r12d,%edx
 59c:	45 31 d4             	xor    %r10d,%r12d
 59f:	89 d6                	mov    %edx,%esi
 5a1:	44 31 d6             	xor    %r10d,%esi
 5a4:	41 c1 ea 1f          	shr    $0x1f,%r10d
 5a8:	41 81 c2 ff ff ff 7f 	add    $0x7fffffff,%r10d
 5af:	44 85 e6             	test   %r12d,%esi
 5b2:	44 0f 49 d2          	cmovns %edx,%r10d
 5b6:	46 89 14 b9          	mov    %r10d,(%rcx,%r15,4)
 5ba:	e9 d1 fd ff ff       	jmp    0x390
 5bf:	90                   	nop
 5c0:	48 83 7f 08 00       	cmpq   $0x0,0x8(%rdi)
 5c5:	0f 84 85 fd ff ff    	je     0x350
 5cb:	49 83 fb ff          	cmp    $0xffffffffffffffff,%r11
 5cf:	0f 84 7b fd ff ff    	je     0x350
 5d5:	49 f7 d3             	not    %r11
 5d8:	0f 1f 84 00 00 00 00 	nopl   0x0(%rax,%rax,1)
 5df:	00 
 5e0:	4c 89 d9             	mov    %r11,%rcx
 5e3:	48 c1 e9 20          	shr    $0x20,%rcx
 5e7:	31 d2                	xor    %edx,%edx
 5e9:	45 85 db             	test   %r11d,%r11d
 5ec:	0f 94 c2             	sete   %dl
 5ef:	49 0f 45 cb          	cmovne %r11,%rcx
 5f3:	c1 e2 05             	shl    $0x5,%edx
 5f6:	8d 72 01             	lea    0x1(%rdx),%esi
 5f9:	83 ca 11             	or     $0x11,%edx
 5fc:	49 89 ca             	mov    %rcx,%r10
 5ff:	49 c1 ea 10          	shr    $0x10,%r10
 603:	66 85 c9             	test   %cx,%cx
 606:	0f 45 d6             	cmovne %esi,%edx
 609:	4c 0f 45 d1          	cmovne %rcx,%r10
 60d:	8d 4a 08             	lea    0x8(%rdx),%ecx
 610:	4c 89 d6             	mov    %r10,%rsi
 613:	48 c1 ee 08          	shr    $0x8,%rsi
 617:	45 84 d2             	test   %r10b,%r10b
 61a:	0f 45 ca             	cmovne %edx,%ecx
 61d:	49 0f 45 f2          	cmovne %r10,%rsi
 621:	8d 51 04             	lea    0x4(%rcx),%edx
 624:	49 89 f2             	mov    %rsi,%r10
 627:	49 c1 ea 04          	shr    $0x4,%r10
 62b:	40 f6 c6 0f          	test   $0xf,%sil
 62f:	0f 45 d1             	cmovne %ecx,%edx
 632:	4c 0f 45 d6          	cmovne %rsi,%r10
 636:	8d 4a 02             	lea    0x2(%rdx),%ecx
 639:	44 89 d6             	mov    %r10d,%esi
 63c:	c1 ee 02             	shr    $0x2,%esi
 63f:	41 f6 c2 03          	test   $0x3,%r10b
 643:	0f 45 ca             	cmovne %edx,%ecx
 646:	41 0f 45 f2          	cmovne %r10d,%esi
 64a:	83 e6 01             	and    $0x1,%esi
 64d:	01 d9                	add    %ebx,%ecx
 64f:	29 f1                	sub    %esi,%ecx
 651:	c4 c2 a0 f3 cb       	blsr   %r11,%r11
 656:	48 8b 57 08          	mov    0x8(%rdi),%rdx
 65a:	48 63 c9             	movslq %ecx,%rcx
 65d:	c7 04 8a 00 00 00 00 	movl   $0x0,(%rdx,%rcx,4)
 664:	0f 85 76 ff ff ff    	jne    0x5e0
 66a:	e9 e1 fc ff ff       	jmp    0x350
 66f:	5b                   	pop    %rbx
 670:	41 5c                	pop    %r12
 672:	41 5d                	pop    %r13
 674:	41 5e                	pop    %r14
 676:	41 5f                	pop    %r15
 678:	5d                   	pop    %rbp
 679:	c3                   	ret
 67a:	cc                   	int3
 67b:	cc                   	int3
 67c:	cc                   	int3
 67d:	cc                   	int3
 67e:	cc                   	int3
 67f:	cc                   	int3
 680:	55                   	push   %rbp
 681:	48 89 e5             	mov    %rsp,%rbp
 684:	5d                   	pop    %rbp
 685:	e9 56 00 00 00       	jmp    0x6e0
 68a:	cc                   	int3
 68b:	cc                   	int3
 68c:	cc                   	int3
 68d:	cc                   	int3
 68e:	cc                   	int3
 68f:	cc                   	int3
 690:	50                   	push   %rax
 691:	e8 2a 00 00 00       	call   0x6c0
 696:	e8 35 00 00 00       	call   0x6d0
 69b:	58                   	pop    %rax
 69c:	c3                   	ret
 69d:	00 00                	add    %al,(%rax)
 69f:	00 ff                	add    %bh,%bh
 6a1:	35 32 21 00 00       	xor    $0x2132,%eax
 6a6:	ff 25 34 21 00 00    	jmp    *0x2134(%rip)        # 0x27e0
 6ac:	0f 1f 40 00          	nopl   0x0(%rax)
 6b0:	ff 25 32 21 00 00    	jmp    *0x2132(%rip)        # 0x27e8
 6b6:	68 00 00 00 00       	push   $0x0
 6bb:	e9 e0 ff ff ff       	jmp    0x6a0
 6c0:	ff 25 2a 21 00 00    	jmp    *0x212a(%rip)        # 0x27f0
 6c6:	68 01 00 00 00       	push   $0x1
 6cb:	e9 d0 ff ff ff       	jmp    0x6a0
 6d0:	ff 25 22 21 00 00    	jmp    *0x2122(%rip)        # 0x27f8
 6d6:	68 02 00 00 00       	push   $0x2
 6db:	e9 c0 ff ff ff       	jmp    0x6a0
 6e0:	ff 25 1a 21 00 00    	jmp    *0x211a(%rip)        # 0x2800
 6e6:	68 03 00 00 00       	push   $0x3
 6eb:	e9 b0 ff ff ff       	jmp    0x6a0
 6f0:	ff 25 12 21 00 00    	jmp    *0x2112(%rip)        # 0x2808
 6f6:	68 04 00 00 00       	push   $0x4
 6fb:	e9 a0 ff ff ff       	jmp    0x6a0
 700:	ff 25 0a 21 00 00    	jmp    *0x210a(%rip)        # 0x2810
 706:	68 05 00 00 00       	push   $0x5
 70b:	e9 90 ff ff ff       	jmp    0x6a0
 710:	0e                   	(bad)
 711:	00 00                	add    %al,(%rax)
 713:	00 00                	add    %al,(%rax)
 715:	00 00                	add    %al,(%rax)
 717:	00 bb 0a 00 00 00    	add    %bh,0xa(%rbx)
 71d:	00 00                	add    %al,(%rax)
 71f:	00 17                	add    %dl,(%rdi)
 721:	00 00                	add    %al,(%rax)
 723:	00 00                	add    %al,(%rax)
 725:	00 00                	add    %al,(%rax)
 727:	00 e8                	add    %ch,%al
 729:	0e                   	(bad)
 72a:	00 00                	add    %al,(%rax)
 72c:	00 00                	add    %al,(%rax)
 72e:	00 00                	add    %al,(%rax)
 730:	02 00                	add    (%rax),%al
 732:	00 00                	add    %al,(%rax)
 734:	00 00                	add    %al,(%rax)
 736:	00 00                	add    %al,(%rax)
 738:	90                   	nop
 739:	00 00                	add    %al,(%rax)
 73b:	00 00                	add    %al,(%rax)
 73d:	00 00                	add    %al,(%rax)
 73f:	00 03                	add    %al,(%rbx)
 741:	00 00                	add    %al,(%rax)
 743:	00 00                	add    %al,(%rax)
 745:	00 00                	add    %al,(%rax)
 747:	00 e0                	add    %ah,%al
 749:	48 00 00             	rex.W add %al,(%rax)
 74c:	00 00                	add    %al,(%rax)
 74e:	00 00                	add    %al,(%rax)
 750:	14 00                	adc    $0x0,%al
 752:	00 00                	add    %al,(%rax)
 754:	00 00                	add    %al,(%rax)
 756:	00 00                	add    %al,(%rax)
 758:	07                   	(bad)
 759:	00 00                	add    %al,(%rax)
 75b:	00 00                	add    %al,(%rax)
 75d:	00 00                	add    %al,(%rax)
 75f:	00 06                	add    %al,(%rsi)
 761:	00 00                	add    %al,(%rax)
 763:	00 00                	add    %al,(%rax)
 765:	00 00                	add    %al,(%rax)
 767:	00 98 02 00 00 00    	add    %bl,0x2(%rax)
 76d:	00 00                	add    %al,(%rax)
 76f:	00 0b                	add    %cl,(%rbx)
 771:	00 00                	add    %al,(%rax)
 773:	00 00                	add    %al,(%rax)
 775:	00 00                	add    %al,(%rax)
 777:	00 18                	add    %bl,(%rax)
 779:	00 00                	add    %al,(%rax)
 77b:	00 00                	add    %al,(%rax)
 77d:	00 00                	add    %al,(%rax)
 77f:	00 05 00 00 00 00    	add    %al,0x0(%rip)        # 0x785
 785:	00 00                	add    %al,(%rax)
 787:	00 08                	add    %cl,(%rax)
 789:	04 00                	add    $0x0,%al
 78b:	00 00                	add    %al,(%rax)
 78d:	00 00                	add    %al,(%rax)
 78f:	00 0a                	add    %cl,(%rdx)
 791:	00 00                	add    %al,(%rax)
 793:	00 00                	add    %al,(%rax)
 795:	00 00                	add    %al,(%rax)
 797:	00 df                	add    %bl,%bh
 799:	0a 00                	or     (%rax),%al
 79b:	00 00                	add    %al,(%rax)
 79d:	00 00                	add    %al,(%rax)
 79f:	00 f5                	add    %dh,%ch
 7a1:	fe                   	(bad)
 7a2:	ff 6f 00             	ljmp   *0x0(%rdi)
 7a5:	00 00                	add    %al,(%rax)
 7a7:	00 70 03             	add    %dh,0x3(%rax)
 7aa:	00 00                	add    %al,(%rax)
 7ac:	00 00                	add    %al,(%rax)
 7ae:	00 00                	add    %al,(%rax)
 7b0:	04 00                	add    $0x0,%al
 7b2:	00 00                	add    %al,(%rax)
 7b4:	00 00                	add    %al,(%rax)
 7b6:	00 00                	add    %al,(%rax)
 7b8:	b8 03 00 00 00       	mov    $0x3,%eax
	...
 7cd:	00 00                	add    %al,(%rax)
 7cf:	00 20                	add    %ah,(%rax)
 7d1:	38 00                	cmp    %al,(%rax)
	...
 7e7:	00 c6                	add    %al,%dh
 7e9:	27                   	(bad)
 7ea:	00 00                	add    %al,(%rax)
 7ec:	00 00                	add    %al,(%rax)
 7ee:	00 00                	add    %al,(%rax)
 7f0:	d6                   	udb
 7f1:	27                   	(bad)
 7f2:	00 00                	add    %al,(%rax)
 7f4:	00 00                	add    %al,(%rax)
 7f6:	00 00                	add    %al,(%rax)
 7f8:	e6 27                	out    %al,$0x27
 7fa:	00 00                	add    %al,(%rax)
 7fc:	00 00                	add    %al,(%rax)
 7fe:	00 00                	add    %al,(%rax)
 800:	f6 27                	mulb   (%rdi)
 802:	00 00                	add    %al,(%rax)
 804:	00 00                	add    %al,(%rax)
 806:	00 00                	add    %al,(%rax)
 808:	06                   	(bad)
 809:	28 00                	sub    %al,(%rax)
 80b:	00 00                	add    %al,(%rax)
 80d:	00 00                	add    %al,(%rax)
 80f:	00 16                	add    %dl,(%rsi)
 811:	28 00                	sub    %al,(%rax)
 813:	00 00                	add    %al,(%rax)
 815:	00 00                	add    %al,(%rax)
 817:	00 01                	add    %al,(%rcx)
 819:	11 01                	adc    %eax,(%rcx)
 81b:	25 0e 13 05 03       	and    $0x305130e,%eax
 820:	0e                   	(bad)
 821:	10 17                	adc    %dl,(%rdi)
 823:	1b 0e                	sbb    (%rsi),%ecx
 825:	11 01                	adc    %eax,(%rcx)
 827:	55                   	push   %rbp
 828:	17                   	(bad)
 829:	00 00                	add    %al,(%rax)
 82b:	02 2e                	add    (%rsi),%ch
 82d:	00 03                	add    %al,(%rbx)
 82f:	0e                   	(bad)
 830:	20 0b                	and    %cl,(%rbx)
 832:	00 00                	add    %al,(%rax)
 834:	03 2e                	add    (%rsi),%ebp
 836:	01 11                	add    %edx,(%rcx)
 838:	01 12                	add    %edx,(%rdx)
 83a:	06                   	(bad)
 83b:	03 0e                	add    (%rsi),%ecx
 83d:	00 00                	add    %al,(%rax)
 83f:	04 1d                	add    $0x1d,%al
 841:	00 31                	add    %dh,(%rcx)
 843:	13 11                	adc    (%rcx),%edx
 845:	01 12                	add    %edx,(%rdx)
 847:	06                   	(bad)
 848:	58                   	pop    %rax
 849:	0b 59 0b             	or     0xb(%rcx),%ebx
 84c:	00 00                	add    %al,(%rax)
 84e:	05 1d 00 31 13       	add    $0x1331001d,%eax
 853:	11 01                	adc    %eax,(%rcx)
 855:	12 06                	adc    (%rsi),%al
 857:	58                   	pop    %rax
 858:	0b 59 0b             	or     0xb(%rcx),%ebx
 85b:	57                   	push   %rdi
 85c:	0b 00                	or     (%rax),%eax
 85e:	00 06                	add    %al,(%rsi)
 860:	1d 01 31 13 11       	sbb    $0x11133101,%eax
 865:	01 12                	add    %edx,(%rdx)
 867:	06                   	(bad)
 868:	58                   	pop    %rax
 869:	0b 59 0b             	or     0xb(%rcx),%ebx
 86c:	57                   	push   %rdi
 86d:	0b 00                	or     (%rax),%eax
 86f:	00 00                	add    %al,(%rax)
 871:	e0 01                	loopne 0x874
 873:	00 00                	add    %al,(%rax)
 875:	04 00                	add    $0x0,%al
 877:	00 00                	add    %al,(%rax)
 879:	00 00                	add    %al,(%rax)
 87b:	08 01                	or     %al,(%rcx)
 87d:	81 00 00 00 02 00    	addl   $0x20000,(%rax)
 883:	41 00 00             	add    %al,(%r8)
 886:	00 00                	add    %al,(%rax)
 888:	00 00                	add    %al,(%rax)
 88a:	00 c9                	add    %cl,%cl
	...
 898:	00 00                	add    %al,(%rax)
 89a:	00 02                	add    %al,(%rdx)
 89c:	18 00                	sbb    %al,(%rax)
 89e:	00 00                	add    %al,(%rax)
 8a0:	01 03                	add    %eax,(%rbx)
 8a2:	40 21 00             	rex and %eax,(%rax)
 8a5:	00 00                	add    %al,(%rax)
 8a7:	00 00                	add    %al,(%rax)
 8a9:	00 dd                	add    %bl,%ch
 8ab:	00 00                	add    %al,(%rax)
 8ad:	00 6a 01             	add    %ch,0x1(%rdx)
 8b0:	00 00                	add    %al,(%rax)
 8b2:	04 2a                	add    $0x2a,%al
 8b4:	00 00                	add    %al,(%rax)
 8b6:	00 40 21             	add    %al,0x21(%rax)
 8b9:	00 00                	add    %al,(%rax)
 8bb:	00 00                	add    %al,(%rax)
 8bd:	00 00                	add    %al,(%rax)
 8bf:	d2 00                	rolb   %cl,(%rax)
 8c1:	00 00                	add    %al,(%rax)
 8c3:	02 00                	add    (%rax),%al
 8c5:	00 02                	add    %al,(%rdx)
 8c7:	27                   	(bad)
 8c8:	01 00                	add    %eax,(%rax)
 8ca:	00 01                	add    %al,(%rcx)
 8cc:	02 fd                	add    %ch,%bh
 8ce:	00 00                	add    %al,(%rax)
 8d0:	00 01                	add    %al,(%rcx)
 8d2:	02 49 00             	add    0x0(%rcx),%cl
 8d5:	00 00                	add    %al,(%rax)
 8d7:	01 02                	add    %eax,(%rdx)
 8d9:	55                   	push   %rbp
 8da:	00 00                	add    %al,(%rax)
 8dc:	00 01                	add    %al,(%rcx)
 8de:	02 46 01             	add    0x1(%rsi),%al
 8e1:	00 00                	add    %al,(%rax)
 8e3:	01 03                	add    %eax,(%rbx)
 8e5:	20 22                	and    %ah,(%rdx)
 8e7:	00 00                	add    %al,(%rax)
 8e9:	00 00                	add    %al,(%rax)
 8eb:	00 00                	add    %al,(%rax)
 8ed:	fa                   	cli
 8ee:	01 00                	add    %eax,(%rax)
 8f0:	00 6a 00             	add    %ch,0x0(%rdx)
 8f3:	00 00                	add    %al,(%rax)
 8f5:	05 55 00 00 00       	add    $0x55,%eax
 8fa:	70 22                	jo     0x91e
 8fc:	00 00                	add    %al,(%rax)
 8fe:	00 00                	add    %al,(%rax)
 900:	00 00                	add    %al,(%rax)
 902:	20 00                	and    %al,(%rax)
 904:	00 00                	add    %al,(%rax)
 906:	01 99 11 06 5b 00    	add    %ebx,0x5b0611(%rcx)
 90c:	00 00                	add    %al,(%rax)
 90e:	90                   	nop
 90f:	22 00                	and    (%rax),%al
 911:	00 00                	add    %al,(%rax)
 913:	00 00                	add    %al,(%rax)
 915:	00 46 01             	add    %al,0x1(%rsi)
 918:	00 00                	add    %al,(%rax)
 91a:	01 9a 11 06 67 00    	add    %ebx,0x670611(%rdx)
 920:	00 00                	add    %al,(%rax)
 922:	1a 23                	sbb    (%rbx),%ah
 924:	00 00                	add    %al,(%rax)
 926:	00 00                	add    %al,(%rax)
 928:	00 00                	add    %al,(%rax)
 92a:	2f                   	(bad)
 92b:	00 00                	add    %al,(%rax)
 92d:	00 01                	add    %al,(%rcx)
 92f:	86 11                	xchg   %dl,(%rcx)
 931:	05 61 00 00 00       	add    $0x61,%eax
 936:	1a 23                	sbb    (%rbx),%ah
 938:	00 00                	add    %al,(%rax)
 93a:	00 00                	add    %al,(%rax)
 93c:	00 00                	add    %al,(%rax)
 93e:	11 00                	adc    %eax,(%rax)
 940:	00 00                	add    %al,(%rax)
 942:	03 3d 0d 00 06 67    	add    0x6706000d(%rip),%edi        # 0x67060955
 948:	00 00                	add    %al,(%rax)
 94a:	00 6a 23             	add    %ch,0x23(%rdx)
 94d:	00 00                	add    %al,(%rax)
 94f:	00 00                	add    %al,(%rax)
 951:	00 00                	add    %al,(%rax)
 953:	31 00                	xor    %eax,(%rax)
 955:	00 00                	add    %al,(%rax)
 957:	01 87 11 05 61 00    	add    %eax,0x610511(%rdi)
 95d:	00 00                	add    %al,(%rax)
 95f:	6a 23                	push   $0x23
 961:	00 00                	add    %al,(%rax)
 963:	00 00                	add    %al,(%rax)
 965:	00 00                	add    %al,(%rax)
 967:	11 00                	adc    %eax,(%rax)
 969:	00 00                	add    %al,(%rax)
 96b:	03 3d 0d 00 00 05    	add    0x500000d(%rip),%edi        # 0x500097e
 971:	6d                   	insl   (%dx),(%rdi)
 972:	00 00                	add    %al,(%rax)
 974:	00 e7                	add    %ah,%bh
 976:	23 00                	and    (%rax),%eax
 978:	00 00                	add    %al,(%rax)
 97a:	00 00                	add    %al,(%rax)
 97c:	00 21                	add    %ah,(%rcx)
 97e:	00 00                	add    %al,(%rax)
 980:	00 01                	add    %al,(%rcx)
 982:	9e                   	sahf
 983:	15 00 02 14 01       	adc    $0x1140200,%eax
 988:	00 00                	add    %al,(%rax)
 98a:	01 03                	add    %eax,(%rbx)
 98c:	20 24 00             	and    %ah,(%rax,%rax,1)
 98f:	00 00                	add    %al,(%rax)
 991:	00 00                	add    %al,(%rax)
 993:	00 6a 03             	add    %ch,0x3(%rdx)
 996:	00 00                	add    %al,(%rax)
 998:	00 00                	add    %al,(%rax)
 99a:	00 00                	add    %al,(%rax)
 99c:	05 14 01 00 00       	add    $0x114,%eax
 9a1:	b2 24                	mov    $0x24,%dl
 9a3:	00 00                	add    %al,(%rax)
 9a5:	00 00                	add    %al,(%rax)
 9a7:	00 00                	add    %al,(%rax)
 9a9:	68 00 00 00 01       	push   $0x1000000
 9ae:	ac                   	lods   (%rsi),%al
 9af:	15 05 55 00 00       	adc    $0x5505,%eax
 9b4:	00 33                	add    %dh,(%rbx)
 9b6:	25 00 00 00 00       	and    $0x0,%eax
 9bb:	00 00                	add    %al,(%rax)
 9bd:	1e                   	(bad)
 9be:	00 00                	add    %al,(%rax)
 9c0:	00 01                	add    %al,(%rcx)
 9c2:	b0 15                	mov    $0x15,%al
 9c4:	06                   	(bad)
 9c5:	5b                   	pop    %rbx
 9c6:	00 00                	add    %al,(%rax)
 9c8:	00 51 25             	add    %dl,0x25(%rcx)
 9cb:	00 00                	add    %al,(%rax)
 9cd:	00 00                	add    %al,(%rax)
 9cf:	00 00                	add    %al,(%rax)
 9d1:	43 01 00             	rex.XB add %eax,(%r8)
 9d4:	00 01                	add    %al,(%rcx)
 9d6:	b1 15                	mov    $0x15,%cl
 9d8:	06                   	(bad)
 9d9:	67 00 00             	add    %al,(%eax)
 9dc:	00 d9                	add    %bl,%cl
 9de:	25 00 00 00 00       	and    $0x0,%eax
 9e3:	00 00                	add    %al,(%rax)
 9e5:	2d 00 00 00 01       	sub    $0x1000000,%eax
 9ea:	86 11                	xchg   %dl,(%rcx)
 9ec:	05 61 00 00 00       	add    $0x61,%eax
 9f1:	d9 25 00 00 00 00    	fldenv 0x0(%rip)        # 0x9f7
 9f7:	00 00                	add    %al,(%rax)
 9f9:	0f 00 00             	sldt   (%rax)
 9fc:	00 03                	add    %al,(%rbx)
 9fe:	3d 0d 00 06 67       	cmp    $0x6706000d,%eax
 a03:	00 00                	add    %al,(%rax)
 a05:	00 28                	add    %ch,(%rax)
 a07:	26 00 00             	es add %al,(%rax)
 a0a:	00 00                	add    %al,(%rax)
 a0c:	00 00                	add    %al,(%rax)
 a0e:	2c 00                	sub    $0x0,%al
 a10:	00 00                	add    %al,(%rax)
 a12:	01 87 11 05 61 00    	add    %eax,0x610511(%rdi)
 a18:	00 00                	add    %al,(%rax)
 a1a:	28 26                	sub    %ah,(%rsi)
 a1c:	00 00                	add    %al,(%rax)
 a1e:	00 00                	add    %al,(%rax)
 a20:	00 00                	add    %al,(%rax)
 a22:	0e                   	(bad)
 a23:	00 00                	add    %al,(%rax)
 a25:	00 03                	add    %al,(%rbx)
 a27:	3d 0d 00 00 05       	cmp    $0x500000d,%eax
 a2c:	6d                   	insl   (%dx),(%rdi)
 a2d:	00 00                	add    %al,(%rax)
 a2f:	00 a6 26 00 00 00    	add    %ah,0x26(%rsi)
 a35:	00 00                	add    %al,(%rax)
 a37:	00 20                	add    %ah,(%rax)
 a39:	00 00                	add    %al,(%rax)
 a3b:	00 01                	add    %al,(%rcx)
 a3d:	b5 19                	mov    $0x19,%ch
 a3f:	05 14 01 00 00       	add    $0x114,%eax
 a44:	f7 26                	mull   (%rsi)
 a46:	00 00                	add    %al,(%rax)
 a48:	00 00                	add    %al,(%rax)
 a4a:	00 00                	add    %al,(%rax)
 a4c:	68 00 00 00 01       	push   $0x1000000
 a51:	c1 15 00 00 40 21 00 	rcll   $0x0,0x21400000(%rip)        # 0x21400a58
 a58:	00 00                	add    %al,(%rax)
 a5a:	00 00                	add    %al,(%rax)
 a5c:	00 1d 22 00 00 00    	add    %bl,0x22(%rip)        # 0xa84
 a62:	00 00                	add    %al,(%rax)
 a64:	00 20                	add    %ah,(%rax)
 a66:	22 00                	and    (%rax),%al
 a68:	00 00                	add    %al,(%rax)
 a6a:	00 00                	add    %al,(%rax)
 a6c:	00 1a                	add    %bl,(%rdx)
 a6e:	24 00                	and    $0x0,%al
 a70:	00 00                	add    %al,(%rax)
 a72:	00 00                	add    %al,(%rax)
 a74:	00 20                	add    %ah,(%rax)
 a76:	24 00                	and    $0x0,%al
 a78:	00 00                	add    %al,(%rax)
 a7a:	00 00                	add    %al,(%rax)
 a7c:	00 8a 27 00 00 00    	add    %cl,0x27(%rdx)
 a82:	00 00                	add    %al,(%rax)
 a84:	00 90 27 00 00 00    	add    %dl,0x27(%rax)
 a8a:	00 00                	add    %al,(%rax)
 a8c:	00 9a 27 00 00 00    	add    %bl,0x27(%rdx)
	...
 aa2:	00 00                	add    %al,(%rax)
 aa4:	00 53 76             	add    %dl,0x76(%rbx)
 aa7:	2e 42 75 72          	rex.X jne,pn 0xb1d
 aab:	73 74                	jae    0xb21
 aad:	2e 4b                	cs rex.WXB
 aaf:	65 72 6e             	gs jb  0xb20
 ab2:	65 6c                	gs insb (%dx),(%rdi)
 ab4:	73 2e                	jae    0xae4
 ab6:	4d 61                	rex.WRB (bad)
 ab8:	73 6b                	jae    0xb25
 aba:	65 64 00 53 76       	gs add %dl,%fs:0x76(%rbx)
 abf:	2e 42 75 72          	rex.X jne,pn 0xb35
 ac3:	73 74                	jae    0xb39
 ac5:	2e 4b                	cs rex.WXB
 ac7:	65 72 6e             	gs jb  0xb38
 aca:	65 6c                	gs insb (%dx),(%rdi)
 acc:	73 2e                	jae    0xafc
 ace:	52                   	push   %rdx
 acf:	75 6e                	jne    0xb3f
 ad1:	50                   	push   %rax
 ad2:	72 6f                	jb     0xb43
 ad4:	67 72 61             	addr32 jb 0xb38
 ad7:	6d                   	insl   (%dx),(%rdi)
 ad8:	24 42                	and    $0x42,%al
 ada:	75 72                	jne    0xb4e
 adc:	73 74                	jae    0xb52
 ade:	4d 61                	rex.WRB (bad)
 ae0:	6e                   	outsb  (%rsi),(%dx)
 ae1:	61                   	(bad)
 ae2:	67 65 64 00 75 6e    	gs add %dh,%fs:0x6e(%ebp)
 ae8:	6b 6e 6f 77          	imul   $0x77,0x6f(%rsi),%ebp
 aec:	6e                   	outsb  (%rsi),(%dx)
 aed:	00 66 6c             	add    %ah,0x6c(%rsi)
 af0:	6f                   	outsl  (%rsi),(%dx)
 af1:	61                   	(bad)
 af2:	74 2e                	je     0xb22
 af4:	49 73 4e             	rex.WB jae 0xb45
 af7:	61                   	(bad)
 af8:	4e 00 53 76          	rex.WRX add %r10b,0x76(%rbx)
 afc:	2e 42 75 72          	rex.X jne,pn 0xb72
 b00:	73 74                	jae    0xb76
 b02:	2e 42 63 6c 2e 42    	cs movsxd 0x42(%rsi,%r13,1),%ebp
 b08:	6f                   	outsl  (%rsi),(%dx)
 b09:	75 6e                	jne    0xb79
 b0b:	64 4f                	fs rex.WRXB
 b0d:	66 00 53 76          	data16 add %dl,0x76(%rbx)
 b11:	2e 42 75 72          	rex.X jne,pn 0xb87
 b15:	73 74                	jae    0xb8b
 b17:	2e 4b                	cs rex.WXB
 b19:	65 72 6e             	gs jb  0xb8a
 b1c:	65 6c                	gs insb (%dx),(%rdi)
 b1e:	73 2e                	jae    0xb4e
 b20:	43 68 75 6e 6b 00    	rex.XB push $0x6b6e75
 b26:	42 75 72             	rex.X jne 0xb9b
 b29:	73 74                	jae    0xb9f
 b2b:	20 30                	and    %dh,(%rax)
 b2d:	2e 31 2e             	cs xor %ebp,(%rsi)
 b30:	30 2e                	xor    %ch,(%rsi)
 b32:	30 20                	xor    %ah,(%rax)
 b34:	28 46 72             	sub    %al,0x72(%rsi)
 b37:	6f                   	outsl  (%rsi),(%dx)
 b38:	6e                   	outsb  (%rsi),(%dx)
 b39:	74 65                	je     0xba0
 b3b:	6e                   	outsb  (%rsi),(%dx)
 b3c:	64 20 56 65          	and    %dl,%fs:0x65(%rsi)
 b40:	72 73                	jb     0xbb5
 b42:	69 6f 6e 20 3a 20 35 	imul   $0x35203a20,0x6e(%rdi),%ebp
 b49:	61                   	(bad)
 b4a:	33 34 34             	xor    (%rsp,%rsi,1),%esi
 b4d:	63 35 30 2d 34 62    	movsxd 0x62342d30(%rip),%esi        # 0x62343883
 b53:	32 33                	xor    (%rbx),%dh
 b55:	2d 34 34 39 36       	sub    $0x36393434,%eax
 b5a:	2d 61 65 33 35       	sub    $0x35336561,%eax
 b5f:	2d 61 66 36 63       	sub    $0x63366661,%eax
 b64:	33 30                	xor    (%rax),%esi
 b66:	63 33                	movsxd (%rbx),%esi
 b68:	32 65 66             	xor    0x66(%rbp),%ah
 b6b:	62 29 00 2f 68       	(bad)
 b70:	6f                   	outsl  (%rsi),(%dx)
 b71:	6d                   	insl   (%dx),(%rdi)
 b72:	65 2f                	gs (bad)
 b74:	69 2f 47 69 74 48    	imul   $0x48746947,(%rdi),%ebp
 b7a:	75 62                	jne    0xbde
 b7c:	2f                   	(bad)
 b7d:	73 76                	jae    0xbf5
 b7f:	2d 77 31 30 31       	sub    $0x31303177,%eax
 b84:	2f                   	(bad)
 b85:	74 65                	je     0xbec
 b87:	73 74                	jae    0xbfd
 b89:	73 2f                	jae    0xbba
 b8b:	53                   	push   %rbx
 b8c:	76 2e                	jbe    0xbbc
 b8e:	42 75 72             	rex.X jne 0xc03
 b91:	73 74                	jae    0xc07
 b93:	2f                   	(bad)
 b94:	75 6e                	jne    0xc04
 b96:	69 74 79 2f 75 6e 6b 	imul   $0x6e6b6e75,0x2f(%rcx,%rdi,2),%esi
 b9d:	6e 
 b9e:	6f                   	outsl  (%rsi),(%dx)
 b9f:	77 6e                	ja     0xc0f
 ba1:	00 53 76             	add    %dl,0x76(%rbx)
 ba4:	2e 42 75 72          	rex.X jne,pn 0xc1a
 ba8:	73 74                	jae    0xc1e
 baa:	2e 4b                	cs rex.WXB
 bac:	65 72 6e             	gs jb  0xc1d
 baf:	65 6c                	gs insb (%dx),(%rdi)
 bb1:	73 2e                	jae    0xbe1
 bb3:	41 70 70             	rex.B jo 0xc26
 bb6:	6c                   	insb   (%dx),(%rdi)
 bb7:	79 00                	jns    0xbb9
 bb9:	53                   	push   %rbx
 bba:	76 2e                	jbe    0xbea
 bbc:	42 75 72             	rex.X jne 0xc31
 bbf:	73 74                	jae    0xc35
 bc1:	2e 42 63 6c 2e 54    	cs movsxd 0x54(%rsi,%r13,1),%ebp
 bc7:	7a 63                	jp     0xc2c
 bc9:	6e                   	outsb  (%rsi),(%dx)
 bca:	74 00                	je     0xbcc
 bcc:	53                   	push   %rbx
 bcd:	76 2e                	jbe    0xbfd
 bcf:	42 75 72             	rex.X jne 0xc44
 bd2:	73 74                	jae    0xc48
 bd4:	2e 4b                	cs rex.WXB
 bd6:	65 72 6e             	gs jb  0xc47
 bd9:	65 6c                	gs insb (%dx),(%rdi)
 bdb:	73 2e                	jae    0xc0b
 bdd:	53                   	push   %rbx
 bde:	61                   	(bad)
 bdf:	74 75                	je     0xc56
 be1:	72 61                	jb     0xc44
 be3:	74 69                	je     0xc4e
 be5:	6e                   	outsb  (%rsi),(%dx)
 be6:	67 41                	addr32 rex.B
 be8:	64 64 00 53 76       	fs add %dl,%fs:0x76(%rbx)
 bed:	2e 42 75 72          	rex.X jne,pn 0xc63
 bf1:	73 74                	jae    0xc67
 bf3:	2e 4b                	cs rex.WXB
 bf5:	65 72 6e             	gs jb  0xc66
 bf8:	65 6c                	gs insb (%dx),(%rdi)
 bfa:	73 2e                	jae    0xc2a
 bfc:	53                   	push   %rbx
 bfd:	61                   	(bad)
 bfe:	74 75                	je     0xc75
 c00:	72 61                	jb     0xc63
 c02:	74 69                	je     0xc6d
 c04:	6e                   	outsb  (%rsi),(%dx)
 c05:	67 53                	addr32 push %rbx
 c07:	75 62                	jne    0xc6b
 c09:	74 72                	je     0xc7d
 c0b:	61                   	(bad)
 c0c:	63 74 00 53          	movsxd 0x53(%rax,%rax,1),%esi
 c10:	76 2e                	jbe    0xc40
 c12:	42 75 72             	rex.X jne 0xc87
 c15:	73 74                	jae    0xc8b
 c17:	2e 4b                	cs rex.WXB
 c19:	65 72 6e             	gs jb  0xc8a
 c1c:	65 6c                	gs insb (%dx),(%rdi)
 c1e:	73 2e                	jae    0xc4e
 c20:	52                   	push   %rdx
 c21:	75 6e                	jne    0xc91
 c23:	50                   	push   %rax
 c24:	72 6f                	jb     0xc95
 c26:	67 72 61             	addr32 jb 0xc8a
 c29:	6d                   	insl   (%dx),(%rdi)
 c2a:	5f                   	pop    %rdi
 c2b:	30 30                	xor    %dh,(%rax)
 c2d:	30 30                	xor    %dh,(%rax)
 c2f:	30 30                	xor    %dh,(%rax)
 c31:	30 35 24 42 75 72    	xor    %dh,0x72754224(%rip)        # 0x72754e5b
 c37:	73 74                	jae    0xcad
 c39:	44 69 72 65 63 74 43 	imul   $0x61437463,0x65(%rdx),%r14d
 c40:	61 
 c41:	6c                   	insb   (%dx),(%rdi)
 c42:	6c                   	insb   (%dx),(%rdi)
 c43:	2e 49 6e             	cs rex.WB outsb (%rsi),(%dx)
 c46:	76 6f                	jbe    0xcb7
 c48:	6b 65 00 4b          	imul   $0x4b,0x0(%rbp),%esp
 c4c:	03 00                	add    (%rax),%eax
 c4e:	00 04 00             	add    %al,(%rax,%rax,1)
 c51:	82                   	(bad)
 c52:	00 00                	add    %al,(%rax)
 c54:	00 01                	add    %al,(%rcx)
 c56:	01 01                	add    %eax,(%rcx)
 c58:	fb                   	sti
 c59:	0e                   	(bad)
 c5a:	0d 00 01 01 01       	or     $0x1010100,%eax
 c5f:	01 00                	add    %eax,(%rax)
 c61:	00 00                	add    %al,(%rax)
 c63:	01 00                	add    %eax,(%rax)
 c65:	00 01                	add    %al,(%rcx)
 c67:	2f                   	(bad)
 c68:	68 6f 6d 65 2f       	push   $0x2f656d6f
 c6d:	69 2f 47 69 74 48    	imul   $0x48746947,(%rdi),%ebp
 c73:	75 62                	jne    0xcd7
 c75:	2f                   	(bad)
 c76:	73 76                	jae    0xcee
 c78:	2d 77 31 30 31       	sub    $0x31303177,%eax
 c7d:	2f                   	(bad)
 c7e:	74 65                	je     0xce5
 c80:	73 74                	jae    0xcf6
 c82:	73 2f                	jae    0xcb3
 c84:	53                   	push   %rbx
 c85:	76 2e                	jbe    0xcb5
 c87:	42 75 72             	rex.X jne 0xcfc
 c8a:	73 74                	jae    0xd00
 c8c:	2f                   	(bad)
 c8d:	75 6e                	jne    0xcfd
 c8f:	69 74 79 2f 41 73 73 	imul   $0x65737341,0x2f(%rcx,%rdi,2),%esi
 c96:	65 
 c97:	74 73                	je     0xd0c
 c99:	2f                   	(bad)
 c9a:	53                   	push   %rbx
 c9b:	63 72 69             	movsxd 0x69(%rdx),%esi
 c9e:	70 74                	jo     0xd14
 ca0:	73 2f                	jae    0xcd1
 ca2:	53                   	push   %rbx
 ca3:	76 42                	jbe    0xce7
 ca5:	75 72                	jne    0xd19
 ca7:	73 74                	jae    0xd1d
 ca9:	00 75 6e             	add    %dh,0x6e(%rbp)
 cac:	6b 6e 6f 77          	imul   $0x77,0x6f(%rsi),%ebp
 cb0:	6e                   	outsb  (%rsi),(%dx)
 cb1:	00 00                	add    %al,(%rax)
 cb3:	4b                   	rex.WXB
 cb4:	65 72 6e             	gs jb  0xd25
 cb7:	65 6c                	gs insb (%dx),(%rdi)
 cb9:	73 2e                	jae    0xce9
 cbb:	63 73 00             	movsxd 0x0(%rbx),%esi
 cbe:	01 00                	add    %eax,(%rax)
 cc0:	00 75 6e             	add    %dh,0x6e(%rbp)
 cc3:	6b 6e 6f 77          	imul   $0x77,0x6f(%rsi),%ebp
 cc7:	6e                   	outsb  (%rsi),(%dx)
 cc8:	00 02                	add    %al,(%rdx)
 cca:	00 00                	add    %al,(%rax)
 ccc:	42 63 6c 2e 63       	movsxd 0x63(%rsi,%r13,1),%ebp
 cd1:	73 00                	jae    0xcd3
 cd3:	01 00                	add    %eax,(%rax)
 cd5:	00 00                	add    %al,(%rax)
 cd7:	05 1d 0a 00 09       	add    $0x9000a1d,%eax
 cdc:	02 40 21             	add    0x21(%rax),%al
 cdf:	00 00                	add    %al,(%rax)
 ce1:	00 00                	add    %al,(%rax)
 ce3:	00 00                	add    %al,(%rax)
 ce5:	03 ce                	add    %esi,%ecx
 ce7:	00 01                	add    %al,(%rcx)
 ce9:	06                   	(bad)
 cea:	03 b1 7f 82 03 cf    	add    -0x30fc7d81(%rcx),%esi
 cf0:	00 f2                	add    %dh,%dl
 cf2:	03 b1 7f 90 05 15    	add    0x1505907f(%rcx),%esi
 cf8:	06                   	(bad)
 cf9:	03 d8                	add    %eax,%ebx
 cfb:	00 e4                	add    %ah,%ah
 cfd:	05 00 06 03 a8       	add    $0xa8030600,%eax
 d02:	7f 08                	jg     0xd0c
 d04:	12 05 1d 06 03 cf    	adc    -0x30fcf9e3(%rip),%al        # 0xffffffffcf031327
 d0a:	00 58 05             	add    %bl,0x5(%rax)
 d0d:	11 cb                	adc    %ecx,%ebx
 d0f:	05 00 06 03 ae       	add    $0xae030600,%eax
 d14:	7f 66                	jg     0xd7c
 d16:	05 11 03 d2 00       	add    $0xd20311,%eax
 d1b:	58                   	pop    %rax
 d1c:	05 15 06 03 0a       	add    $0xa030615,%eax
 d21:	9e                   	sahf
 d22:	06                   	(bad)
 d23:	03 a4 7f 08 d6 06 03 	add    0x306d608(%rdi,%rdi,2),%esp
 d2a:	d4                   	(bad)
 d2b:	00 90 06 03 ac 7f    	add    %dl,0x7fac0306(%rax)
 d31:	08 20                	or     %ah,(%rax)
 d33:	06                   	(bad)
 d34:	03 e0                	add    %eax,%esp
 d36:	00 d6                	add    %dl,%dh
 d38:	08 b6 06 03 a4 7f    	or     %dh,0x7fa40306(%rsi)
 d3e:	58                   	pop    %rax
 d3f:	02 0b                	add    (%rbx),%cl
 d41:	00 01                	add    %al,(%rcx)
 d43:	01 05 1d 0a 00 09    	add    %eax,0x9000a1d(%rip)        # 0x9001766
 d49:	02 20                	add    (%rax),%ah
 d4b:	22 00                	and    (%rax),%al
 d4d:	00 00                	add    %al,(%rax)
 d4f:	00 00                	add    %al,(%rax)
 d51:	00 03                	add    %al,(%rbx)
 d53:	94                   	xchg   %eax,%esp
 d54:	01 01                	add    %eax,(%rcx)
 d56:	06                   	(bad)
 d57:	03 eb                	add    %ebx,%ebp
 d59:	7e 82                	jle    0xcdd
 d5b:	03 95 01 ac 03 eb    	add    -0x14fc53ff(%rbp),%edx
 d61:	7e 08                	jle    0xd6b
 d63:	3c 06                	cmp    $0x6,%al
 d65:	03 95 01 90 06 03    	add    0x3069001(%rbp),%edx
 d6b:	eb 7e                	jmp    0xdeb
 d6d:	ac                   	lods   (%rsi),%al
 d6e:	05 11 06 03 98       	add    $0x98030611,%eax
 d73:	01 3c 67             	add    %edi,(%rdi,%riz,2)
 d76:	05 00 06 03 e7       	add    $0xe7030600,%eax
 d7b:	7e 58                	jle    0xdd5
 d7d:	05 0d 06 03 e7       	add    $0xe703060d,%eax
 d82:	00 74 4b 03          	add    %dh,0x3(%rbx,%rcx,2)
 d86:	0b 08                	or     (%rax),%ecx
 d88:	ac                   	lods   (%rsi),%al
 d89:	05 11 08 5a 06       	add    $0x65a0811,%eax
 d8e:	03 8b 7f 08 58 05    	add    0x558087f(%rbx),%ecx
 d94:	0d 06 03 f3 00       	or     $0xf30306,%eax
 d99:	74 06                	je     0xda1
 d9b:	03 8d 7f 58 05 11    	add    0x1105587f(%rbp),%ecx
 da1:	06                   	(bad)
 da2:	03 fa                	add    %edx,%edi
 da4:	00 82 08 13 06 03    	add    %al,0x3061308(%rdx)
 daa:	85 7f 08             	test   %edi,0x8(%rdi)
 dad:	58                   	pop    %rax
 dae:	06                   	(bad)
 daf:	03 86 01 90 04 02    	add    0x2049001(%rsi),%eax
 db5:	05 00 06 03 fa       	add    $0xfa030600,%eax
 dba:	7e 08                	jle    0xdc4
 dbc:	90                   	nop
 dbd:	04 03                	add    $0x3,%al
 dbf:	05 0d 06 03 3d       	add    $0x3d03060d,%eax
 dc4:	08 12                	or     %dl,(%rdx)
 dc6:	06                   	(bad)
 dc7:	03 43 2e             	add    0x2e(%rbx),%eax
 dca:	06                   	(bad)
 dcb:	03 c2                	add    %edx,%eax
 dcd:	00 66 6b             	add    %ah,0x6b(%rsi)
 dd0:	bf 06 03 b4 7f       	mov    $0x7fb40306,%edi
 dd5:	4a 04 01             	rex.WX add $0x1,%al
 dd8:	05 11 06 03 87       	add    $0x87030611,%eax
 ddd:	01 74 04 02          	add    %esi,0x2(%rsp,%rax,1)
 de1:	05 00 06 03 f9       	add    $0xf9030600,%eax
 de6:	7e 08                	jle    0xdf0
 de8:	90                   	nop
 de9:	04 03                	add    $0x3,%al
 deb:	05 0d 06 03 3d       	add    $0x3d03060d,%eax
 df0:	08 12                	or     %dl,(%rdx)
 df2:	33 06                	xor    (%rsi),%eax
 df4:	03 be 7f ba 06 03    	add    0x306ba7f(%rsi),%edi
 dfa:	c7 00 66 6b 06 03    	movl   $0x3066b66,(%rax)
 e00:	b4 7f                	mov    $0x7f,%ah
 e02:	66 04 01             	data16 add $0x1,%al
 e05:	05 11 06 03 fb       	add    $0xfb030611,%eax
 e0a:	00 66 06             	add    %ah,0x6(%rsi)
 e0d:	03 85 7f c8 06 03    	add    0x306c87f(%rbp),%eax
 e13:	9b                   	fwait
 e14:	01 02                	add    %eax,(%rdx)
 e16:	28 01                	sub    %al,(%rcx)
 e18:	4b 05 0d 03 51 c8    	rex.WXB add $0xffffffffc851030d,%rax
 e1e:	67 05 15 03 30 08    	addr32 add $0x8300315,%eax
 e24:	9e                   	sahf
 e25:	06                   	(bad)
 e26:	03 e2                	add    %edx,%esp
 e28:	7e 90                	jle    0xdba
 e2a:	05 09 06 03 a1       	add    $0xa1030609,%eax
 e2f:	01 82 02 01 00 01    	add    %eax,0x1000102(%rdx)
 e35:	01 00                	add    %eax,(%rax)
 e37:	09 02                	or     %eax,(%rdx)
 e39:	20 24 00             	and    %ah,(%rax,%rax,1)
 e3c:	00 00                	add    %al,(%rax)
 e3e:	00 00                	add    %al,(%rax)
 e40:	00 03                	add    %al,(%rbx)
 e42:	a4                   	movsb  (%rsi),(%rdi)
 e43:	01 01                	add    %eax,(%rcx)
 e45:	05 1d 0a 08 3c       	add    $0x3c080a1d,%eax
 e4a:	06                   	(bad)
 e4b:	03 db                	add    %ebx,%ebx
 e4d:	7e 82                	jle    0xdd1
 e4f:	03 a5 01 4a 03 db    	add    -0x24fcb5ff(%rbp),%esp
 e55:	7e 08                	jle    0xe5f
 e57:	90                   	nop
 e58:	05 2c 06 03 a5       	add    $0xa503062c,%eax
 e5d:	01 9e 05 1d 06 3c    	add    %ebx,0x3c061d05(%rsi)
 e63:	03 db                	add    %ebx,%ebx
 e65:	7e 9e                	jle    0xe05
 e67:	05 11 06 03 a7       	add    $0xa7030611,%eax
 e6c:	01 4a 83             	add    %ecx,-0x7d(%rdx)
 e6f:	84 03                	test   %al,(%rbx)
 e71:	0f 82 06 03 c7 7e    	jb     0x7ec7117d
 e77:	c8 06 03 aa          	enter  $0x306,$0xaa
 e7b:	01 ac 06 03 d6 7e 66 	add    %ebp,0x667ed603(%rsi,%rax,1)
 e82:	04 03                	add    $0x3,%al
 e84:	05 0d 06 03 15       	add    $0x1503060d,%eax
 e89:	74 08                	je     0xe93
 e8b:	26 08 42 08          	es or  %al,0x8(%rdx)
 e8f:	42 08 50 f8          	rex.X or %dl,-0x8(%rax)
 e93:	04 01                	add    $0x1,%al
 e95:	05 15 03 fb 00       	add    $0xfb0315,%eax
 e9a:	90                   	nop
 e9b:	59                   	pop    %rcx
 e9c:	67 05 00 06 03 d0    	addr32 add $0xd0030600,%eax
 ea2:	7e 66                	jle    0xf0a
 ea4:	05 0d 06 03 e7       	add    $0xe703060d,%eax
 ea9:	00 82 4b 03 0b 08    	add    %al,0x80b034b(%rdx)
 eaf:	90                   	nop
 eb0:	05 11 08 5a 06       	add    $0x65a0811,%eax
 eb5:	03 8b 7f 08 ba 06    	add    0x6ba087f(%rbx),%ecx
 ebb:	03 fa                	add    %edx,%edi
 ebd:	00 ba 08 13 06 03    	add    %bh,0x3061308(%rdx)
 ec3:	85 7f 08             	test   %edi,0x8(%rdi)
 ec6:	58                   	pop    %rax
 ec7:	06                   	(bad)
 ec8:	03 86 01 90 04 02    	add    0x2049001(%rsi),%eax
 ece:	05 00 06 03 fa       	add    $0xfa030600,%eax
 ed3:	7e 08                	jle    0xedd
 ed5:	82                   	(bad)
 ed6:	04 03                	add    $0x3,%al
 ed8:	05 0d 06 03 3d       	add    $0x3d03060d,%eax
 edd:	e4 06                	in     $0x6,%al
 edf:	03 43 2e             	add    0x2e(%rbx),%eax
 ee2:	06                   	(bad)
 ee3:	03 c2                	add    %edx,%eax
 ee5:	00 66 6b             	add    %ah,0x6b(%rsi)
 ee8:	bf 06 03 b4 7f       	mov    $0x7fb40306,%edi
 eed:	4a 04 01             	rex.WX add $0x1,%al
