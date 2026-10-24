
MathClampProbe.bin:     file format binary


Disassembly of section .data:

0000000000000000 <.data>:
   0:	48 83 ec 28          	sub    $0x28,%rsp
   4:	4c 89 24 24          	mov    %r12,(%rsp)
   8:	48 89 7c 24 18       	mov    %rdi,0x18(%rsp)
   d:	48 89 74 24 20       	mov    %rsi,0x20(%rsp)
  12:	48 c7 44 24 08 00 00 	movq   $0x0,0x8(%rsp)
  19:	00 00 
  1b:	48 bf 40 bb 77 fc 3f 	movabs $0x703ffc77bb40,%rdi
  22:	70 00 00 
  25:	48 8d 74 24 08       	lea    0x8(%rsp),%rsi
  2a:	48 8d 6d 00          	lea    0x0(%rbp),%rbp
  2e:	49 bb 13 35 a0 de 36 	movabs $0x7036dea03513,%r11
  35:	70 00 00 
  38:	41 ff d3             	call   *%r11
  3b:	48 89 44 24 10       	mov    %rax,0x10(%rsp)
  40:	48 b8 a8 b4 db de 36 	movabs $0x7036dedbb4a8,%rax
  47:	70 00 00 
  4a:	8b 00                	mov    (%rax),%eax
  4c:	85 c0                	test   %eax,%eax
  4e:	0f 85 38 00 00 00    	jne    0x8c
  54:	eb 00                	jmp    0x56
  56:	48 8b 7c 24 18       	mov    0x18(%rsp),%rdi
  5b:	48 63 74 24 20       	movslq 0x20(%rsp),%rsi
  60:	66 66 90             	data16 xchg %ax,%ax
  63:	e8 28 fc ff ff       	call   0xfffffffffffffc90
  68:	48 8b 7c 24 10       	mov    0x10(%rsp),%rdi
  6d:	48 8d 74 24 08       	lea    0x8(%rsp),%rsi
  72:	48 8d 6d 00          	lea    0x0(%rbp),%rbp
  76:	49 bb c5 35 a0 de 36 	movabs $0x7036dea035c5,%r11
  7d:	70 00 00 
  80:	41 ff d3             	call   *%r11
  83:	4c 8b 24 24          	mov    (%rsp),%r12
  87:	48 83 c4 28          	add    $0x28,%rsp
  8b:	c3                   	ret
  8c:	66 66 90             	data16 xchg %ax,%ax
  8f:	e8 1c 2c e2 00       	call   0xe22cb0
  94:	4c 8b e0             	mov    %rax,%r12
  97:	48 85 c0             	test   %rax,%rax
  9a:	74 ba                	je     0x56
  9c:	49 8d 84 24 88 00 00 	lea    0x88(%r12),%rax
  a3:	00 
  a4:	c7 00 01 00 00 00    	movl   $0x1,(%rax)
  aa:	49 8b fc             	mov    %r12,%rdi
  ad:	66 90                	xchg   %ax,%ax
  af:	e8 bc d5 8e ff       	call   0xffffffffff8ed670
	...
  c0:	48 83 ec 28          	sub    $0x28,%rsp
  c4:	4c 89 24 24          	mov    %r12,(%rsp)
  c8:	4c 89 6c 24 08       	mov    %r13,0x8(%rsp)
  cd:	4c 89 74 24 10       	mov    %r14,0x10(%rsp)
  d2:	4c 89 7c 24 18       	mov    %r15,0x18(%rsp)
  d7:	4c 8b f7             	mov    %rdi,%r14
  da:	4c 8b fe             	mov    %rsi,%r15
  dd:	49 bd 25 23 22 84 e4 	movabs $0xcbf29ce484222325,%r13
  e4:	9c f2 cb 
  e7:	45 33 e4             	xor    %r12d,%r12d
  ea:	eb 26                	jmp    0x112
  ec:	48 8d 6d 00          	lea    0x0(%rbp),%rbp
  f0:	49 63 cc             	movslq %r12d,%rcx
  f3:	48 c1 e1 02          	shl    $0x2,%rcx
  f7:	49 8b c6             	mov    %r14,%rax
  fa:	48 03 c1             	add    %rcx,%rax
  fd:	48 63 00             	movslq (%rax),%rax
 100:	8b f0                	mov    %eax,%esi
 102:	49 8b fd             	mov    %r13,%rdi
 105:	66 90                	xchg   %ax,%ax
 107:	e8 e4 8c ff ff       	call   0xffffffffffff8df0
 10c:	4c 8b e8             	mov    %rax,%r13
 10f:	41 ff c4             	inc    %r12d
 112:	45 3b e7             	cmp    %r15d,%r12d
 115:	7c d9                	jl     0xf0
 117:	49 8b c5             	mov    %r13,%rax
 11a:	4c 8b 24 24          	mov    (%rsp),%r12
 11e:	4c 8b 6c 24 08       	mov    0x8(%rsp),%r13
 123:	4c 8b 74 24 10       	mov    0x10(%rsp),%r14
 128:	4c 8b 7c 24 18       	mov    0x18(%rsp),%r15
 12d:	48 83 c4 28          	add    $0x28,%rsp
 131:	c3                   	ret
	...
 13e:	00 00                	add    %al,(%rax)
 140:	48 81 ec e8 00 00 00 	sub    $0xe8,%rsp
 147:	4c 89 34 24          	mov    %r14,(%rsp)
 14b:	48 89 7c 24 68       	mov    %rdi,0x68(%rsp)
 150:	4c 8b f6             	mov    %rsi,%r14
 153:	48 89 54 24 70       	mov    %rdx,0x70(%rsp)
 158:	48 89 4c 24 78       	mov    %rcx,0x78(%rsp)
 15d:	4c 89 84 24 80 00 00 	mov    %r8,0x80(%rsp)
 164:	00 
 165:	48 bf 20 86 b5 21 9f 	movabs $0x5e9f21b58620,%rdi
 16c:	5e 00 00 
 16f:	be 0a 00 00 00       	mov    $0xa,%esi
 174:	66 66 90             	data16 xchg %ax,%ax
 177:	e8 a4 0f e2 00       	call   0xe21120
 17c:	48 8b f8             	mov    %rax,%rdi
 17f:	48 89 84 24 e0 00 00 	mov    %rax,0xe0(%rsp)
 186:	00 
 187:	33 f6                	xor    %esi,%esi
 189:	48 ba 20 01 bb dc 36 	movabs $0x7036dcbb0120,%rdx
 190:	70 00 00 
 193:	e8 e8 41 26 00       	call   0x264380
 198:	48 8b bc 24 e0 00 00 	mov    0xe0(%rsp),%rdi
 19f:	00 
 1a0:	48 8b c7             	mov    %rdi,%rax
 1a3:	48 89 84 24 d8 00 00 	mov    %rax,0xd8(%rsp)
 1aa:	00 
 1ab:	be 01 00 00 00       	mov    $0x1,%esi
 1b0:	49 8b d6             	mov    %r14,%rdx
 1b3:	e8 c8 41 26 00       	call   0x264380
 1b8:	48 8b bc 24 d8 00 00 	mov    0xd8(%rsp),%rdi
 1bf:	00 
 1c0:	48 8b c7             	mov    %rdi,%rax
 1c3:	48 89 84 24 d0 00 00 	mov    %rax,0xd0(%rsp)
 1ca:	00 
 1cb:	be 02 00 00 00       	mov    $0x2,%esi
 1d0:	48 ba c0 eb c2 dc 36 	movabs $0x7036dcc2ebc0,%rdx
 1d7:	70 00 00 
 1da:	90                   	nop
 1db:	e8 a0 41 26 00       	call   0x264380
 1e0:	48 8b bc 24 d0 00 00 	mov    0xd0(%rsp),%rdi
 1e7:	00 
 1e8:	48 8b c7             	mov    %rdi,%rax
 1eb:	48 89 84 24 c8 00 00 	mov    %rax,0xc8(%rsp)
 1f2:	00 
 1f3:	be 03 00 00 00       	mov    $0x3,%esi
 1f8:	48 8b 54 24 70       	mov    0x70(%rsp),%rdx
 1fd:	66 90                	xchg   %ax,%ax
 1ff:	e8 7c 41 26 00       	call   0x264380
 204:	48 8b bc 24 c8 00 00 	mov    0xc8(%rsp),%rdi
 20b:	00 
 20c:	48 8b c7             	mov    %rdi,%rax
 20f:	48 89 84 24 c0 00 00 	mov    %rax,0xc0(%rsp)
 216:	00 
 217:	be 04 00 00 00       	mov    $0x4,%esi
 21c:	48 ba f0 00 bb dc 36 	movabs $0x7036dcbb00f0,%rdx
 223:	70 00 00 
 226:	90                   	nop
 227:	e8 54 41 26 00       	call   0x264380
 22c:	48 8b 84 24 c0 00 00 	mov    0xc0(%rsp),%rax
 233:	00 
 234:	48 89 84 24 b8 00 00 	mov    %rax,0xb8(%rsp)
 23b:	00 
 23c:	48 89 84 24 b0 00 00 	mov    %rax,0xb0(%rsp)
 243:	00 
 244:	48 8d 7c 24 78       	lea    0x78(%rsp),%rdi
 249:	48 be 00 b8 c0 dc 36 	movabs $0x7036dcc0b800,%rsi
 250:	70 00 00 
 253:	e8 28 8d ff ff       	call   0xffffffffffff8f80
 258:	48 8b d0             	mov    %rax,%rdx
 25b:	48 8b bc 24 b8 00 00 	mov    0xb8(%rsp),%rdi
 262:	00 
 263:	be 05 00 00 00       	mov    $0x5,%esi
 268:	66 66 90             	data16 xchg %ax,%ax
 26b:	e8 10 41 26 00       	call   0x264380
 270:	48 8b bc 24 b0 00 00 	mov    0xb0(%rsp),%rdi
 277:	00 
 278:	48 8b c7             	mov    %rdi,%rax
 27b:	48 89 84 24 a8 00 00 	mov    %rax,0xa8(%rsp)
 282:	00 
 283:	be 06 00 00 00       	mov    $0x6,%esi
 288:	48 ba 10 4b 1a 50 3f 	movabs $0x703f501a4b10,%rdx
 28f:	70 00 00 
 292:	90                   	nop
 293:	e8 e8 40 26 00       	call   0x264380
 298:	48 8b 84 24 a8 00 00 	mov    0xa8(%rsp),%rax
 29f:	00 
 2a0:	48 89 84 24 a0 00 00 	mov    %rax,0xa0(%rsp)
 2a7:	00 
 2a8:	48 89 84 24 98 00 00 	mov    %rax,0x98(%rsp)
 2af:	00 
 2b0:	48 8d bc 24 80 00 00 	lea    0x80(%rsp),%rdi
 2b7:	00 
 2b8:	48 be 00 b8 c0 dc 36 	movabs $0x7036dcc0b800,%rsi
 2bf:	70 00 00 
 2c2:	90                   	nop
 2c3:	e8 b8 8c ff ff       	call   0xffffffffffff8f80
 2c8:	48 8b d0             	mov    %rax,%rdx
 2cb:	48 8b bc 24 a0 00 00 	mov    0xa0(%rsp),%rdi
 2d2:	00 
 2d3:	be 07 00 00 00       	mov    $0x7,%esi
 2d8:	66 66 90             	data16 xchg %ax,%ax
 2db:	e8 a0 40 26 00       	call   0x264380
 2e0:	48 8b bc 24 98 00 00 	mov    0x98(%rsp),%rdi
 2e7:	00 
 2e8:	48 8b c7             	mov    %rdi,%rax
 2eb:	48 89 84 24 90 00 00 	mov    %rax,0x90(%rsp)
 2f2:	00 
 2f3:	be 08 00 00 00       	mov    $0x8,%esi
 2f8:	48 ba a0 ca 75 50 3f 	movabs $0x703f5075caa0,%rdx
 2ff:	70 00 00 
 302:	90                   	nop
 303:	e8 78 40 26 00       	call   0x264380
 308:	48 8b 84 24 90 00 00 	mov    0x90(%rsp),%rax
 30f:	00 
 310:	48 8b c8             	mov    %rax,%rcx
 313:	48 8b 74 24 78       	mov    0x78(%rsp),%rsi
 318:	48 8b bc 24 80 00 00 	mov    0x80(%rsp),%rdi
 31f:	00 
 320:	48 89 8c 24 88 00 00 	mov    %rcx,0x88(%rsp)
 327:	00 
 328:	48 b9 60 00 bb dc 36 	movabs $0x7036dcbb0060,%rcx
 32f:	70 00 00 
 332:	48 ba 90 00 bb dc 36 	movabs $0x7036dcbb0090,%rdx
 339:	70 00 00 
 33c:	48 3b f7             	cmp    %rdi,%rsi
 33f:	48 0f 44 d1          	cmove  %rcx,%rdx
 343:	48 8b f8             	mov    %rax,%rdi
 346:	be 09 00 00 00       	mov    $0x9,%esi
 34b:	48 8b 00             	mov    (%rax),%rax
 34e:	ff 90 10 01 00 00    	call   *0x110(%rax)
 354:	48 8b bc 24 88 00 00 	mov    0x88(%rsp),%rdi
 35b:	00 
 35c:	66 66 90             	data16 xchg %ax,%ax
 35f:	e8 3c c9 36 00       	call   0x36cca0
 364:	48 8b f8             	mov    %rax,%rdi
 367:	e8 d4 5e ff ff       	call   0xffffffffffff6240
 36c:	48 8b 44 24 78       	mov    0x78(%rsp),%rax
 371:	48 8b 8c 24 80 00 00 	mov    0x80(%rsp),%rcx
 378:	00 
 379:	48 3b c1             	cmp    %rcx,%rax
 37c:	0f 84 26 00 00 00    	je     0x3a8
 382:	48 bf 20 01 bb dc 36 	movabs $0x7036dcbb0120,%rdi
 389:	70 00 00 
 38c:	49 8b f6             	mov    %r14,%rsi
 38f:	48 ba 00 00 bb dc 36 	movabs $0x7036dcbb0000,%rdx
 396:	70 00 00 
 399:	66 90                	xchg   %ax,%ax
 39b:	e8 f4 2a 26 00       	call   0x262e94
 3a0:	48 8b f8             	mov    %rax,%rdi
 3a3:	e8 b7 4a ff ff       	call   0xffffffffffff4e5f
 3a8:	48 bf a0 7e bd dc 36 	movabs $0x7036dcbd7ea0,%rdi
 3af:	70 00 00 
 3b2:	49 8b f6             	mov    %r14,%rsi
 3b5:	48 ba 70 7e bd dc 36 	movabs $0x7036dcbd7e70,%rdx
 3bc:	70 00 00 
 3bf:	e8 dc 50 2a 00       	call   0x2a54a0
 3c4:	48 8b f0             	mov    %rax,%rsi
 3c7:	48 8b 7c 24 68       	mov    0x68(%rsp),%rdi
 3cc:	66 66 90             	data16 xchg %ax,%ax
 3cf:	e8 ac db 26 00       	call   0x26df80
 3d4:	48 89 84 24 88 00 00 	mov    %rax,0x88(%rsp)
 3db:	00 
 3dc:	48 8d 7c 24 78       	lea    0x78(%rsp),%rdi
 3e1:	48 be 00 b8 c0 dc 36 	movabs $0x7036dcc0b800,%rsi
 3e8:	70 00 00 
 3eb:	e8 90 8b ff ff       	call   0xffffffffffff8f80
 3f0:	48 89 84 24 90 00 00 	mov    %rax,0x90(%rsp)
 3f7:	00 
 3f8:	48 8d bc 24 80 00 00 	lea    0x80(%rsp),%rdi
 3ff:	00 
 400:	48 be 00 b8 c0 dc 36 	movabs $0x7036dcc0b800,%rsi
 407:	70 00 00 
 40a:	90                   	nop
 40b:	e8 70 8b ff ff       	call   0xffffffffffff8f80
 410:	48 8b d0             	mov    %rax,%rdx
 413:	48 8b bc 24 90 00 00 	mov    0x90(%rsp),%rdi
 41a:	00 
 41b:	48 be a0 ca 75 50 3f 	movabs $0x703f5075caa0,%rsi
 422:	70 00 00 
 425:	66 90                	xchg   %ax,%ax
 427:	e8 74 50 2a 00       	call   0x2a54a0
 42c:	48 8b f0             	mov    %rax,%rsi
 42f:	48 8b bc 24 88 00 00 	mov    0x88(%rsp),%rdi
 436:	00 
 437:	e8 44 b7 ff ff       	call   0xffffffffffffbb80
 43c:	4c 8b 34 24          	mov    (%rsp),%r14
 440:	48 81 c4 e8 00 00 00 	add    $0xe8,%rsp
 447:	c3                   	ret
	...
