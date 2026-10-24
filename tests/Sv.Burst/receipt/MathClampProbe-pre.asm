
MathClampProbe-pre.bin:     file format binary


Disassembly of section .data:

0000000000000000 <.data>:
       0:	45 33 ed             	xor    %r13d,%r13d
       3:	e9 33 00 00 00       	jmp    0x3b
       8:	49 63 cd             	movslq %r13d,%rcx
       b:	48 c1 e1 02          	shl    $0x2,%rcx
       f:	49 8b c6             	mov    %r14,%rax
      12:	48 03 c1             	add    %rcx,%rax
      15:	48 89 44 24 20       	mov    %rax,0x20(%rsp)
      1a:	48 8d 7c 24 18       	lea    0x18(%rsp),%rdi
      1f:	e8 bc 73 ff ff       	call   0xffffffffffff73e0
      24:	48 8b c8             	mov    %rax,%rcx
      27:	48 8b 44 24 20       	mov    0x20(%rsp),%rax
      2c:	48 81 e1 ff ff ff 7f 	and    $0x7fffffff,%rcx
      33:	48 63 c9             	movslq %ecx,%rcx
      36:	89 08                	mov    %ecx,(%rax)
      38:	41 ff c5             	inc    %r13d
      3b:	45 3b ef             	cmp    %r15d,%r13d
      3e:	7c c8                	jl     0x8
      40:	4c 8b 2c 24          	mov    (%rsp),%r13
      44:	4c 8b 74 24 08       	mov    0x8(%rsp),%r14
      49:	4c 8b 7c 24 10       	mov    0x10(%rsp),%r15
      4e:	48 83 c4 28          	add    $0x28,%rsp
      52:	c3                   	ret
	...
      5f:	00 48 83             	add    %cl,-0x7d(%rax)
      62:	ec                   	in     (%dx),%al
      63:	18 48 89             	sbb    %cl,-0x77(%rax)
      66:	74 24                	je     0x8c
      68:	08 48 89             	or     %cl,-0x77(%rax)
      6b:	54                   	push   %rsp
      6c:	24 10                	and    $0x10,%al
      6e:	48 8b fe             	mov    %rsi,%rdi
      71:	48 63 74 24 10       	movslq 0x10(%rsp),%rsi
      76:	90                   	nop
      77:	e8 14 00 00 00       	call   0x90
      7c:	48 83 c4 18          	add    $0x18,%rsp
      80:	c3                   	ret
      81:	e8 6a c3 8e ff       	call   0xffffffffff8ec3f0
      86:	08 58 50             	or     %bl,0x50(%rax)
      89:	8b 1f                	mov    (%rdi),%ebx
      8b:	9f                   	lahf
      8c:	5e                   	pop    %rsi
      8d:	00 00                	add    %al,(%rax)
      8f:	00 48 83             	add    %cl,-0x7d(%rax)
      92:	ec                   	in     (%dx),%al
      93:	48                   	rex.W
      94:	48 89 1c 24          	mov    %rbx,(%rsp)
      98:	48 89 6c 24 08       	mov    %rbp,0x8(%rsp)
      9d:	4c 89 64 24 10       	mov    %r12,0x10(%rsp)
      a2:	4c 89 6c 24 18       	mov    %r13,0x18(%rsp)
      a7:	4c 89 74 24 20       	mov    %r14,0x20(%rsp)
      ac:	4c 89 7c 24 28       	mov    %r15,0x28(%rsp)
      b1:	4c 8b f7             	mov    %rdi,%r14
      b4:	48 89 74 24 38       	mov    %rsi,0x38(%rsp)
      b9:	45 33 ed             	xor    %r13d,%r13d
      bc:	e9 c2 00 00 00       	jmp    0x183
      c1:	48 8d ad 00 00 00 00 	lea    0x0(%rbp),%rbp
      c8:	49 63 c5             	movslq %r13d,%rax
      cb:	48 c1 e0 02          	shl    $0x2,%rax
      cf:	4d 8b e6             	mov    %r14,%r12
      d2:	4c 03 e0             	add    %rax,%r12
      d5:	49 63 cd             	movslq %r13d,%rcx
      d8:	48 c1 e1 02          	shl    $0x2,%rcx
      dc:	49 8b c6             	mov    %r14,%rax
      df:	48 03 c1             	add    %rcx,%rax
      e2:	48 63 18             	movslq (%rax),%rbx
      e5:	48 bd fb ff ff ff ff 	movabs $0xfffffffffffffffb,%rbp
      ec:	ff ff ff 
      ef:	41 bf 05 00 00 00    	mov    $0x5,%r15d
      f5:	83 fd 05             	cmp    $0x5,%ebp
      f8:	7e 0e                	jle    0x108
      fa:	48 8b fd             	mov    %rbp,%rdi
      fd:	49 8b f7             	mov    %r15,%rsi
     100:	66 66 90             	data16 xchg %ax,%ax
     103:	e8 a8 00 00 00       	call   0x1b0
     108:	3b dd                	cmp    %ebp,%ebx
     10a:	7d 06                	jge    0x112
     10c:	89 6c 24 30          	mov    %ebp,0x30(%rsp)
     110:	eb 0e                	jmp    0x120
     112:	41 3b df             	cmp    %r15d,%ebx
     115:	49 8b c7             	mov    %r15,%rax
     118:	48 0f 4e c3          	cmovle %rbx,%rax
     11c:	89 44 24 30          	mov    %eax,0x30(%rsp)
     120:	48 63 44 24 30       	movslq 0x30(%rsp),%rax
     125:	41 89 04 24          	mov    %eax,(%r12)
     129:	49 63 cd             	movslq %r13d,%rcx
     12c:	48 c1 e1 02          	shl    $0x2,%rcx
     130:	49 8b c6             	mov    %r14,%rax
     133:	48 03 c1             	add    %rcx,%rax
     136:	49 63 d5             	movslq %r13d,%rdx
     139:	48 c1 e2 02          	shl    $0x2,%rdx
     13d:	49 8b ce             	mov    %r14,%rcx
     140:	48 03 ca             	add    %rdx,%rcx
     143:	48 63 09             	movslq (%rcx),%rcx
     146:	ba 03 00 00 00       	mov    $0x3,%edx
     14b:	3b ca                	cmp    %edx,%ecx
     14d:	0f 4f ca             	cmovg  %edx,%ecx
     150:	89 08                	mov    %ecx,(%rax)
     152:	49 63 cd             	movslq %r13d,%rcx
     155:	48 c1 e1 02          	shl    $0x2,%rcx
     159:	49 8b c6             	mov    %r14,%rax
     15c:	48 03 c1             	add    %rcx,%rax
     15f:	49 63 d5             	movslq %r13d,%rdx
     162:	48 c1 e2 02          	shl    $0x2,%rdx
     166:	49 8b ce             	mov    %r14,%rcx
     169:	48 03 ca             	add    %rdx,%rcx
     16c:	48 63 09             	movslq (%rcx),%rcx
     16f:	48 ba fd ff ff ff ff 	movabs $0xfffffffffffffffd,%rdx
     176:	ff ff ff 
     179:	3b ca                	cmp    %edx,%ecx
     17b:	0f 4c ca             	cmovl  %edx,%ecx
     17e:	89 08                	mov    %ecx,(%rax)
     180:	41 ff c5             	inc    %r13d
     183:	44 3b 6c 24 38       	cmp    0x38(%rsp),%r13d
     188:	0f 8c 3a ff ff ff    	jl     0xc8
     18e:	48 8b 1c 24          	mov    (%rsp),%rbx
     192:	48 8b 6c 24 08       	mov    0x8(%rsp),%rbp
     197:	4c 8b 64 24 10       	mov    0x10(%rsp),%r12
     19c:	4c 8b 6c 24 18       	mov    0x18(%rsp),%r13
     1a1:	4c 8b 74 24 20       	mov    0x20(%rsp),%r14
     1a6:	4c 8b 7c 24 28       	mov    0x28(%rsp),%r15
     1ab:	48 83 c4 48          	add    $0x48,%rsp
     1af:	c3                   	ret
     1b0:	e8 3b c2 8e ff       	call   0xffffffffff8ec3f0
     1b5:	08 f0                	or     %dh,%al
     1b7:	20 bb 20 9f 5e 00    	and    %bh,0x5e9f20(%rbx)
     1bd:	00 00                	add    %al,(%rax)
     1bf:	00 48 83             	add    %cl,-0x7d(%rax)
     1c2:	ec                   	in     (%dx),%al
     1c3:	28 4c 89 2c          	sub    %cl,0x2c(%rcx,%rcx,4)
     1c7:	24 4c                	and    $0x4c,%al
     1c9:	8b ef                	mov    %edi,%ebp
     1cb:	48 89 74 24 08       	mov    %rsi,0x8(%rsp)
     1d0:	48 89 54 24 10       	mov    %rdx,0x10(%rsp)
     1d5:	4d 85 ed             	test   %r13,%r13
     1d8:	0f 84 39 00 00 00    	je     0x217
     1de:	49 8d 7d 10          	lea    0x10(%r13),%rdi
     1e2:	49 ba 78 cc bc 20 9f 	movabs $0x5e9f20bccc78,%r10
     1e9:	5e 00 00 
     1ec:	66 66 90             	data16 xchg %ax,%ax
     1ef:	e8 8c 95 ff ff       	call   0xffffffffffff9780
     1f4:	48 8b f8             	mov    %rax,%rdi
     1f7:	48 8b 74 24 08       	mov    0x8(%rsp),%rsi
     1fc:	48 63 54 24 10       	movslq 0x10(%rsp),%rdx
     201:	48 89 44 24 18       	mov    %rax,0x18(%rsp)
     206:	ff 50 18             	call   *0x18(%rax)
     209:	48 8b 44 24 18       	mov    0x18(%rsp),%rax
     20e:	4c 8b 2c 24          	mov    (%rsp),%r13
     212:	48 83 c4 28          	add    $0x28,%rsp
     216:	c3                   	ret
     217:	be 50 00 00 00       	mov    $0x50,%esi
     21c:	bf 4c 01 00 00       	mov    $0x14c,%edi
     221:	66 90                	xchg   %ax,%ax
     223:	e8 48 dd 8e ff       	call   0xffffffffff8edf70
     228:	e8 c3 c1 8e ff       	call   0xffffffffff8ec3f0
     22d:	08 b0 17 9c 1d 9f    	or     %dh,-0x60e263e9(%rax)
     233:	5e                   	pop    %rsi
	...
     240:	48 83 ec 48          	sub    $0x48,%rsp
     244:	48 89 64 24 38       	mov    %rsp,0x38(%rsp)
     249:	48 89 6c 24 30       	mov    %rbp,0x30(%rsp)
     24e:	48 89 1c 24          	mov    %rbx,(%rsp)
     252:	4c 89 64 24 08       	mov    %r12,0x8(%rsp)
     257:	4c 89 6c 24 10       	mov    %r13,0x10(%rsp)
     25c:	4c 89 74 24 18       	mov    %r14,0x18(%rsp)
     261:	4c 89 7c 24 20       	mov    %r15,0x20(%rsp)
     266:	4c 8b f7             	mov    %rdi,%r14
     269:	4c 8b fe             	mov    %rsi,%r15
     26c:	64 4c 8b 2c 25 78 5b 	mov    %fs:0xffffffffffff5b78,%r13
     273:	ff ff 
     275:	48 8d 44 24 28       	lea    0x28(%rsp),%rax
     27a:	49 8b 4d 00          	mov    0x0(%r13),%rcx
     27e:	48 89 4c 24 28       	mov    %rcx,0x28(%rsp)
     283:	49 89 45 00          	mov    %rax,0x0(%r13)
     287:	48 b8 80 80 6a 88 3f 	movabs $0x703f886a8080,%rax
     28e:	70 00 00 
     291:	49 8b fe             	mov    %r14,%rdi
     294:	49 8b f7             	mov    %r15,%rsi
     297:	48 89 64 24 38       	mov    %rsp,0x38(%rsp)
     29c:	ff d0                	call   *%rax
     29e:	48 b8 a8 b4 db de 36 	movabs $0x7036dedbb4a8,%rax
     2a5:	70 00 00 
     2a8:	8b 00                	mov    (%rax),%eax
     2aa:	85 c0                	test   %eax,%eax
     2ac:	0f 85 3e 00 00 00    	jne    0x2f0
     2b2:	eb 00                	jmp    0x2b4
     2b4:	48 8b 44 24 28       	mov    0x28(%rsp),%rax
     2b9:	49 89 45 00          	mov    %rax,0x0(%r13)
     2bd:	48 8b 1c 24          	mov    (%rsp),%rbx
     2c1:	4c 8b 64 24 08       	mov    0x8(%rsp),%r12
     2c6:	4c 8b 6c 24 10       	mov    0x10(%rsp),%r13
     2cb:	4c 8b 74 24 18       	mov    0x18(%rsp),%r14
     2d0:	4c 8b 7c 24 20       	mov    0x20(%rsp),%r15
     2d5:	48 83 c4 48          	add    $0x48,%rsp
     2d9:	c3                   	ret
     2da:	49 8d 87 88 00 00 00 	lea    0x88(%r15),%rax
     2e1:	c7 00 01 00 00 00    	movl   $0x1,(%rax)
     2e7:	49 8b ff             	mov    %r15,%rdi
     2ea:	90                   	nop
     2eb:	e8 80 d7 8e ff       	call   0xffffffffff8eda70
     2f0:	48 89 64 24 38       	mov    %rsp,0x38(%rsp)
     2f5:	66 90                	xchg   %ax,%ax
     2f7:	e8 b4 2d e2 00       	call   0xe230b0
     2fc:	4c 8b f8             	mov    %rax,%r15
     2ff:	48 85 c0             	test   %rax,%rax
     302:	75 d6                	jne    0x2da
     304:	eb ae                	jmp    0x2b4
     306:	e8 65 cd 8e ff       	call   0xffffffffff8ed070
     30b:	08 78 cf             	or     %bh,-0x31(%rax)
     30e:	bc 20 9f 5e 00       	mov    $0x5e9f20,%esp
	...
     31f:	00 48 83             	add    %cl,-0x7d(%rax)
     322:	ec                   	in     (%dx),%al
     323:	18 4c 89 34          	sbb    %cl,0x34(%rcx,%rcx,4)
     327:	24 4c                	and    $0x4c,%al
     329:	89 7c 24 08          	mov    %edi,0x8(%rsp)
     32d:	4c 8b ff             	mov    %rdi,%r15
     330:	48 c7 44 24 10 00 00 	movq   $0x0,0x10(%rsp)
     337:	00 00 
     339:	49 8b ff             	mov    %r15,%rdi
     33c:	66 66 90             	data16 xchg %ax,%ax
     33f:	e8 fc 68 24 00       	call   0x246c40
     344:	48 89 44 24 10       	mov    %rax,0x10(%rsp)
     349:	4c 8b fc             	mov    %rsp,%r15
     34c:	49 83 c7 10          	add    $0x10,%r15
     350:	48 8b 44 24 10       	mov    0x10(%rsp),%rax
     355:	48 85 c0             	test   %rax,%rax
     358:	40 0f 94 c0          	rex sete %al
     35c:	48 0f b6 c0          	movzbq %al,%rax
     360:	85 c0                	test   %eax,%eax
     362:	40 0f 94 c0          	rex sete %al
     366:	48 0f b6 c0          	movzbq %al,%rax
     36a:	85 c0                	test   %eax,%eax
     36c:	0f 84 4c 00 00 00    	je     0x3be
     372:	49 8b 07             	mov    (%r15),%rax
     375:	48 83 e0 01          	and    $0x1,%rax
     379:	48 85 c0             	test   %rax,%rax
     37c:	40 0f 94 c0          	rex sete %al
     380:	48 0f b6 c0          	movzbq %al,%rax
     384:	85 c0                	test   %eax,%eax
     386:	74 08                	je     0x390
     388:	49 8b 07             	mov    (%r15),%rax
     38b:	4c 8b 30             	mov    (%rax),%r14
     38e:	eb 0b                	jmp    0x39b
     390:	49 8b 3f             	mov    (%r15),%rdi
     393:	e8 ca 76 3e 00       	call   0x3e7a62
     398:	4c 8b f0             	mov    %rax,%r14
     39b:	49 ba e0 cf bc 20 9f 	movabs $0x5e9f20bccfe0,%r10
     3a2:	5e 00 00 
     3a5:	49 8b fe             	mov    %r14,%rdi
     3a8:	66 66 90             	data16 xchg %ax,%ax
     3ab:	e8 c0 15 0f 00       	call   0xf1970
     3b0:	4c 8b 34 24          	mov    (%rsp),%r14
     3b4:	4c 8b 7c 24 08       	mov    0x8(%rsp),%r15
     3b9:	48 83 c4 18          	add    $0x18,%rsp
     3bd:	c3                   	ret
     3be:	bf 3c a1 02 00       	mov    $0x2a13c,%edi
     3c3:	e8 2a 12 e2 00       	call   0xe215f2
     3c8:	48 8b f0             	mov    %rax,%rsi
     3cb:	bf 36 01 00 02       	mov    $0x2000136,%edi
     3d0:	48 8d 64 24 00       	lea    0x0(%rsp),%rsp
     3d5:	90                   	nop
     3d6:	49 bb f9 38 88 de 36 	movabs $0x7036de8838f9,%r11
     3dd:	70 00 00 
     3e0:	41 ff d3             	call   *%r11
     3e3:	48 8b f8             	mov    %rax,%rdi
     3e6:	90                   	nop
     3e7:	e8 84 d5 8e ff       	call   0xffffffffff8ed970
     3ec:	45 33 f6             	xor    %r14d,%r14d
     3ef:	eb 81                	jmp    0x372
     3f1:	e8 fa bf 8e ff       	call   0xffffffffff8ec3f0
     3f6:	08 40 11             	or     %al,0x11(%rax)
     3f9:	9c                   	pushf
     3fa:	1d 9f 5e 00 00       	sbb    $0x5e9f,%eax
     3ff:	00 48 83             	add    %cl,-0x7d(%rax)
     402:	ec                   	in     (%dx),%al
     403:	28 4c 89 24          	sub    %cl,0x24(%rcx,%rcx,4)
     407:	24 48                	and    $0x48,%al
     409:	89 7c 24 18          	mov    %edi,0x18(%rsp)
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
     463:	e8 28 fc ff ff       	call   0x90
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
     48f:	e8 1c 2c e2 00       	call   0xe230b0
     494:	4c 8b e0             	mov    %rax,%r12
     497:	48 85 c0             	test   %rax,%rax
     49a:	74 ba                	je     0x456
     49c:	49 8d 84 24 88 00 00 	lea    0x88(%r12),%rax
     4a3:	00 
     4a4:	c7 00 01 00 00 00    	movl   $0x1,(%rax)
     4aa:	49 8b fc             	mov    %r12,%rdi
     4ad:	66 90                	xchg   %ax,%ax
     4af:	e8 bc d5 8e ff       	call   0xffffffffff8eda70
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
     4fd:	48 63 00             	movslq (%rax),%rax
     500:	8b f0                	mov    %eax,%esi
     502:	49 8b fd             	mov    %r13,%rdi
     505:	66 90                	xchg   %ax,%ax
     507:	e8 e4 8c ff ff       	call   0xffffffffffff91f0
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
     53e:	00 00                	add    %al,(%rax)
     540:	48 81 ec e8 00 00 00 	sub    $0xe8,%rsp
     547:	4c 89 34 24          	mov    %r14,(%rsp)
     54b:	48 89 7c 24 68       	mov    %rdi,0x68(%rsp)
     550:	4c 8b f6             	mov    %rsi,%r14
     553:	48 89 54 24 70       	mov    %rdx,0x70(%rsp)
     558:	48 89 4c 24 78       	mov    %rcx,0x78(%rsp)
     55d:	4c 89 84 24 80 00 00 	mov    %r8,0x80(%rsp)
     564:	00 
     565:	48 bf 20 86 b5 21 9f 	movabs $0x5e9f21b58620,%rdi
     56c:	5e 00 00 
     56f:	be 0a 00 00 00       	mov    $0xa,%esi
     574:	66 66 90             	data16 xchg %ax,%ax
     577:	e8 a4 0f e2 00       	call   0xe21520
     57c:	48 8b f8             	mov    %rax,%rdi
     57f:	48 89 84 24 e0 00 00 	mov    %rax,0xe0(%rsp)
     586:	00 
     587:	33 f6                	xor    %esi,%esi
     589:	48 ba 20 01 bb dc 36 	movabs $0x7036dcbb0120,%rdx
     590:	70 00 00 
     593:	e8 e8 41 26 00       	call   0x264780
     598:	48 8b bc 24 e0 00 00 	mov    0xe0(%rsp),%rdi
     59f:	00 
     5a0:	48 8b c7             	mov    %rdi,%rax
     5a3:	48 89 84 24 d8 00 00 	mov    %rax,0xd8(%rsp)
     5aa:	00 
     5ab:	be 01 00 00 00       	mov    $0x1,%esi
     5b0:	49 8b d6             	mov    %r14,%rdx
     5b3:	e8 c8 41 26 00       	call   0x264780
     5b8:	48 8b bc 24 d8 00 00 	mov    0xd8(%rsp),%rdi
     5bf:	00 
     5c0:	48 8b c7             	mov    %rdi,%rax
     5c3:	48 89 84 24 d0 00 00 	mov    %rax,0xd0(%rsp)
     5ca:	00 
     5cb:	be 02 00 00 00       	mov    $0x2,%esi
     5d0:	48 ba c0 eb c2 dc 36 	movabs $0x7036dcc2ebc0,%rdx
     5d7:	70 00 00 
     5da:	90                   	nop
     5db:	e8 a0 41 26 00       	call   0x264780
     5e0:	48 8b bc 24 d0 00 00 	mov    0xd0(%rsp),%rdi
     5e7:	00 
     5e8:	48 8b c7             	mov    %rdi,%rax
     5eb:	48 89 84 24 c8 00 00 	mov    %rax,0xc8(%rsp)
     5f2:	00 
     5f3:	be 03 00 00 00       	mov    $0x3,%esi
     5f8:	48 8b 54 24 70       	mov    0x70(%rsp),%rdx
     5fd:	66 90                	xchg   %ax,%ax
     5ff:	e8 7c 41 26 00       	call   0x264780
     604:	48 8b bc 24 c8 00 00 	mov    0xc8(%rsp),%rdi
     60b:	00 
     60c:	48 8b c7             	mov    %rdi,%rax
     60f:	48 89 84 24 c0 00 00 	mov    %rax,0xc0(%rsp)
     616:	00 
     617:	be 04 00 00 00       	mov    $0x4,%esi
     61c:	48 ba f0 00 bb dc 36 	movabs $0x7036dcbb00f0,%rdx
     623:	70 00 00 
     626:	90                   	nop
     627:	e8 54 41 26 00       	call   0x264780
     62c:	48 8b 84 24 c0 00 00 	mov    0xc0(%rsp),%rax
     633:	00 
     634:	48 89 84 24 b8 00 00 	mov    %rax,0xb8(%rsp)
     63b:	00 
     63c:	48 89 84 24 b0 00 00 	mov    %rax,0xb0(%rsp)
     643:	00 
     644:	48 8d 7c 24 78       	lea    0x78(%rsp),%rdi
     649:	48 be 00 b8 c0 dc 36 	movabs $0x7036dcc0b800,%rsi
     650:	70 00 00 
     653:	e8 28 8d ff ff       	call   0xffffffffffff9380
     658:	48 8b d0             	mov    %rax,%rdx
     65b:	48 8b bc 24 b8 00 00 	mov    0xb8(%rsp),%rdi
     662:	00 
     663:	be 05 00 00 00       	mov    $0x5,%esi
     668:	66 66 90             	data16 xchg %ax,%ax
     66b:	e8 10 41 26 00       	call   0x264780
     670:	48 8b bc 24 b0 00 00 	mov    0xb0(%rsp),%rdi
     677:	00 
     678:	48 8b c7             	mov    %rdi,%rax
     67b:	48 89 84 24 a8 00 00 	mov    %rax,0xa8(%rsp)
     682:	00 
     683:	be 06 00 00 00       	mov    $0x6,%esi
     688:	48 ba 10 4b 1a 50 3f 	movabs $0x703f501a4b10,%rdx
     68f:	70 00 00 
     692:	90                   	nop
     693:	e8 e8 40 26 00       	call   0x264780
     698:	48 8b 84 24 a8 00 00 	mov    0xa8(%rsp),%rax
     69f:	00 
     6a0:	48 89 84 24 a0 00 00 	mov    %rax,0xa0(%rsp)
     6a7:	00 
     6a8:	48 89 84 24 98 00 00 	mov    %rax,0x98(%rsp)
     6af:	00 
     6b0:	48 8d bc 24 80 00 00 	lea    0x80(%rsp),%rdi
     6b7:	00 
     6b8:	48 be 00 b8 c0 dc 36 	movabs $0x7036dcc0b800,%rsi
     6bf:	70 00 00 
     6c2:	90                   	nop
     6c3:	e8 b8 8c ff ff       	call   0xffffffffffff9380
     6c8:	48 8b d0             	mov    %rax,%rdx
     6cb:	48 8b bc 24 a0 00 00 	mov    0xa0(%rsp),%rdi
     6d2:	00 
     6d3:	be 07 00 00 00       	mov    $0x7,%esi
     6d8:	66 66 90             	data16 xchg %ax,%ax
     6db:	e8 a0 40 26 00       	call   0x264780
     6e0:	48 8b bc 24 98 00 00 	mov    0x98(%rsp),%rdi
     6e7:	00 
     6e8:	48 8b c7             	mov    %rdi,%rax
     6eb:	48 89 84 24 90 00 00 	mov    %rax,0x90(%rsp)
     6f2:	00 
     6f3:	be 08 00 00 00       	mov    $0x8,%esi
     6f8:	48 ba a0 ca 75 50 3f 	movabs $0x703f5075caa0,%rdx
     6ff:	70 00 00 
     702:	90                   	nop
     703:	e8 78 40 26 00       	call   0x264780
     708:	48 8b 84 24 90 00 00 	mov    0x90(%rsp),%rax
     70f:	00 
     710:	48 8b c8             	mov    %rax,%rcx
     713:	48 8b 74 24 78       	mov    0x78(%rsp),%rsi
     718:	48 8b bc 24 80 00 00 	mov    0x80(%rsp),%rdi
     71f:	00 
     720:	48 89 8c 24 88 00 00 	mov    %rcx,0x88(%rsp)
     727:	00 
     728:	48 b9 60 00 bb dc 36 	movabs $0x7036dcbb0060,%rcx
     72f:	70 00 00 
     732:	48 ba 90 00 bb dc 36 	movabs $0x7036dcbb0090,%rdx
     739:	70 00 00 
     73c:	48 3b f7             	cmp    %rdi,%rsi
     73f:	48 0f 44 d1          	cmove  %rcx,%rdx
     743:	48 8b f8             	mov    %rax,%rdi
     746:	be 09 00 00 00       	mov    $0x9,%esi
     74b:	48 8b 00             	mov    (%rax),%rax
     74e:	ff 90 10 01 00 00    	call   *0x110(%rax)
     754:	48 8b bc 24 88 00 00 	mov    0x88(%rsp),%rdi
     75b:	00 
     75c:	66 66 90             	data16 xchg %ax,%ax
     75f:	e8 3c c9 36 00       	call   0x36d0a0
     764:	48 8b f8             	mov    %rax,%rdi
     767:	e8 d4 5e ff ff       	call   0xffffffffffff6640
     76c:	48 8b 44 24 78       	mov    0x78(%rsp),%rax
     771:	48 8b 8c 24 80 00 00 	mov    0x80(%rsp),%rcx
     778:	00 
     779:	48 3b c1             	cmp    %rcx,%rax
     77c:	0f 84 26 00 00 00    	je     0x7a8
     782:	48 bf 20 01 bb dc 36 	movabs $0x7036dcbb0120,%rdi
     789:	70 00 00 
     78c:	49 8b f6             	mov    %r14,%rsi
     78f:	48 ba 00 00 bb dc 36 	movabs $0x7036dcbb0000,%rdx
     796:	70 00 00 
     799:	66 90                	xchg   %ax,%ax
     79b:	e8 f4 2a 26 00       	call   0x263294
     7a0:	48 8b f8             	mov    %rax,%rdi
     7a3:	e8 b7 4a ff ff       	call   0xffffffffffff525f
     7a8:	48 bf a0 7e bd dc 36 	movabs $0x7036dcbd7ea0,%rdi
     7af:	70 00 00 
     7b2:	49 8b f6             	mov    %r14,%rsi
     7b5:	48 ba 70 7e bd dc 36 	movabs $0x7036dcbd7e70,%rdx
     7bc:	70 00 00 
     7bf:	e8 dc 50 2a 00       	call   0x2a58a0
     7c4:	48 8b f0             	mov    %rax,%rsi
     7c7:	48 8b 7c 24 68       	mov    0x68(%rsp),%rdi
     7cc:	66 66 90             	data16 xchg %ax,%ax
     7cf:	e8 ac db 26 00       	call   0x26e380
     7d4:	48 89 84 24 88 00 00 	mov    %rax,0x88(%rsp)
     7db:	00 
     7dc:	48 8d 7c 24 78       	lea    0x78(%rsp),%rdi
     7e1:	48 be 00 b8 c0 dc 36 	movabs $0x7036dcc0b800,%rsi
     7e8:	70 00 00 
     7eb:	e8 90 8b ff ff       	call   0xffffffffffff9380
     7f0:	48 89 84 24 90 00 00 	mov    %rax,0x90(%rsp)
     7f7:	00 
     7f8:	48 8d bc 24 80 00 00 	lea    0x80(%rsp),%rdi
     7ff:	00 
     800:	48 be 00 b8 c0 dc 36 	movabs $0x7036dcc0b800,%rsi
     807:	70 00 00 
     80a:	90                   	nop
     80b:	e8 70 8b ff ff       	call   0xffffffffffff9380
     810:	48 8b d0             	mov    %rax,%rdx
     813:	48 8b bc 24 90 00 00 	mov    0x90(%rsp),%rdi
     81a:	00 
     81b:	48 be a0 ca 75 50 3f 	movabs $0x703f5075caa0,%rsi
     822:	70 00 00 
     825:	66 90                	xchg   %ax,%ax
     827:	e8 74 50 2a 00       	call   0x2a58a0
     82c:	48 8b f0             	mov    %rax,%rsi
     82f:	48 8b bc 24 88 00 00 	mov    0x88(%rsp),%rdi
     836:	00 
     837:	e8 44 b7 ff ff       	call   0xffffffffffffbf80
     83c:	4c 8b 34 24          	mov    (%rsp),%r14
     840:	48 81 c4 e8 00 00 00 	add    $0xe8,%rsp
     847:	c3                   	ret
	...
