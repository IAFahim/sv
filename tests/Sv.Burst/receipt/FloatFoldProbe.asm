
FloatFoldProbe.bin:     file format binary


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
  63:	e8 88 fd ff ff       	call   0xfffffffffffffdf0
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
  8f:	e8 4c 23 e2 00       	call   0xe223e0
  94:	4c 8b e0             	mov    %rax,%r12
  97:	48 85 c0             	test   %rax,%rax
  9a:	74 ba                	je     0x56
  9c:	49 8d 84 24 88 00 00 	lea    0x88(%r12),%rax
  a3:	00 
  a4:	c7 00 01 00 00 00    	movl   $0x1,(%rax)
  aa:	49 8b fc             	mov    %r12,%rdi
  ad:	66 90                	xchg   %ax,%ax
  af:	e8 ec cc 8e ff       	call   0xffffffffff8ecda0
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
  fd:	8b 00                	mov    (%rax),%eax
  ff:	8b f0                	mov    %eax,%esi
 101:	49 8b fd             	mov    %r13,%rdi
 104:	66 66 90             	data16 xchg %ax,%ax
 107:	e8 14 84 ff ff       	call   0xffffffffffff8520
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
