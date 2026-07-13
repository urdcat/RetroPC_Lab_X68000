	.include doscall.mac

	.text
	.even
	.xdef _start

_start:
	pea.l message(pc)
	DOS _PRINT
	addq.l #4,sp
	clr.w -(sp)
	DOS _EXIT

message:
	.dc.b "HELLO, X68000 Z.",13,10,0
	.even
