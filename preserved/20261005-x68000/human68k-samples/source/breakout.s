	.include doscall.mac
	.include iocscall.mac

BOARD_LEFT	.equ 10
BOARD_RIGHT	.equ 69
BOARD_TOP	.equ 3
BOARD_BOTTOM	.equ 28

BRICK_X	.equ 15
BRICK_Y	.equ 5
BRICK_COLS	.equ 10
BRICK_ROWS	.equ 5
BRICK_COUNT	.equ BRICK_COLS*BRICK_ROWS

PADDLE_Y	.equ 26
PADDLE_WIDTH	.equ 9
PADDLE_MIN_X	.equ BOARD_LEFT+2
PADDLE_MAX_X	.equ BOARD_RIGHT-PADDLE_WIDTH-1

	.text
	.even
	.xdef _start

_start:
	bsr initialize_game
	bsr draw_screen

game_loop:
	bsr poll_input
	tst.b quit_requested
	bne exit_game

	bsr erase_ball
	bsr update_ball
	tst.b game_result
	bne game_finished

	bsr draw_ball
	bsr frame_delay
	bra game_loop

game_finished:
	bsr draw_ball
	cmpi.b #1,game_result
	beq show_win

	move.w #25,d1
	move.w #15,d2
	lea.l lose_message(pc),a1
	bsr draw_text
	bra wait_after_game

show_win:
	move.w #24,d1
	move.w #15,d2
	lea.l win_message(pc),a1
	bsr draw_text

wait_after_game:
	bsr wait_for_key

exit_game:
	IOCS _B_CURON
	move.w #0,d1
	move.w #30,d2
	IOCS _B_LOCATE
	clr.w -(sp)
	DOS _EXIT

; -----------------------------------------------------------------------------
; Game setup

initialize_game:
	lea.l brick_state(pc),a0
	moveq.l #BRICK_COUNT-1,d0
initialize_bricks:
	move.b #1,(a0)+
	dbra d0,initialize_bricks

	move.w #35,paddle_x
	move.w #40,ball_x
	move.w #18,ball_y
	move.w #1,ball_dx
	move.w #-1,ball_dy
	move.w #BRICK_COUNT,bricks_left
	clr.b game_result
	clr.b quit_requested
	rts

draw_screen:
	moveq.l #2,d1
	IOCS _B_CLR_ST
	IOCS _B_CUROFF

	move.w #2,d1
	move.w #0,d2
	lea.l title_text(pc),a1
	bsr draw_text

	move.w #BOARD_LEFT,d1
	move.w #BOARD_TOP,d2
	move.w #'-',d3
	move.w #BOARD_RIGHT-BOARD_LEFT+1,d4
	bsr draw_repeat

	move.w #BOARD_TOP+1,d6
draw_side_walls:
	move.w #BOARD_LEFT,d1
	move.w d6,d2
	move.w #'|',d3
	bsr put_at
	move.w #BOARD_RIGHT,d1
	move.w d6,d2
	move.w #'|',d3
	bsr put_at
	addq.w #1,d6
	cmpi.w #BOARD_BOTTOM,d6
	ble draw_side_walls

	bsr draw_all_bricks
	bsr draw_paddle
	bsr draw_ball
	bsr draw_bricks_left
	rts

draw_all_bricks:
	moveq.l #0,d6
draw_brick_row:
	moveq.l #0,d5
draw_brick_column:
	move.w d5,d1
	move.w d1,d0
	lsl.w #2,d1
	add.w d0,d1
	addi.w #BRICK_X,d1
	move.w d6,d2
	addi.w #BRICK_Y,d2
	lea.l brick_text(pc),a1
	bsr draw_text
	addq.w #1,d5
	cmpi.w #BRICK_COLS,d5
	blt draw_brick_column
	addq.w #1,d6
	cmpi.w #BRICK_ROWS,d6
	blt draw_brick_row
	rts

; -----------------------------------------------------------------------------
; Input and paddle

poll_input:
	move.w #$00ff,-(sp)
	DOS _INPOUT
	addq.l #2,sp
	tst.b d0
	beq poll_input_done
	cmpi.b #27,d0
	beq request_quit
	ori.b #$20,d0
	cmpi.b #'a',d0
	beq move_paddle_left
	cmpi.b #'d',d0
	beq move_paddle_right
poll_input_done:
	rts

request_quit:
	move.b #1,quit_requested
	rts

move_paddle_left:
	move.w paddle_x(pc),d0
	cmpi.w #PADDLE_MIN_X,d0
	ble poll_input_done
	bsr erase_paddle
	subq.w #2,d0
	cmpi.w #PADDLE_MIN_X,d0
	bge store_paddle_x
	move.w #PADDLE_MIN_X,d0
	bra store_paddle_x

move_paddle_right:
	move.w paddle_x(pc),d0
	cmpi.w #PADDLE_MAX_X,d0
	bge poll_input_done
	bsr erase_paddle
	addq.w #2,d0
	cmpi.w #PADDLE_MAX_X,d0
	ble store_paddle_x
	move.w #PADDLE_MAX_X,d0

store_paddle_x:
	move.w d0,paddle_x
	bsr draw_paddle
	rts

erase_paddle:
	movem.l d0-d4,-(sp)
	move.w paddle_x(pc),d1
	move.w #PADDLE_Y,d2
	move.w #' ',d3
	move.w #PADDLE_WIDTH,d4
	bsr draw_repeat
	movem.l (sp)+,d0-d4
	rts

draw_paddle:
	move.w paddle_x(pc),d1
	move.w #PADDLE_Y,d2
	move.w #'=',d3
	move.w #PADDLE_WIDTH,d4
	bsr draw_repeat
	rts

; -----------------------------------------------------------------------------
; Ball movement and collisions

erase_ball:
	move.w ball_x(pc),d1
	move.w ball_y(pc),d2
	move.w #' ',d3
	bsr put_at
	rts

draw_ball:
	move.w ball_x(pc),d1
	move.w ball_y(pc),d2
	move.w #'O',d3
	bsr put_at
	rts

update_ball:
	move.w ball_x(pc),d5
	add.w ball_dx(pc),d5
	move.w ball_y(pc),d6
	add.w ball_dy(pc),d6

	cmpi.w #BOARD_LEFT+1,d5
	bgt check_right_wall
	neg.w ball_dx
	move.w #BOARD_LEFT+2,d5
	bra check_top_wall

check_right_wall:
	cmpi.w #BOARD_RIGHT-1,d5
	blt check_top_wall
	neg.w ball_dx
	move.w #BOARD_RIGHT-2,d5

check_top_wall:
	cmpi.w #BOARD_TOP+1,d6
	bgt check_brick_collision
	neg.w ball_dy
	move.w #BOARD_TOP+1,d6

check_brick_collision:
	cmpi.w #BRICK_Y,d6
	blt check_paddle_collision
	cmpi.w #BRICK_Y+BRICK_ROWS-1,d6
	bgt check_paddle_collision

	moveq.l #0,d0
	move.w d5,d0
	subi.w #BRICK_X,d0
	bcs check_paddle_collision
	cmpi.w #BRICK_COLS*5-1,d0
	bhi check_paddle_collision

	divu.w #5,d0
	move.l d0,d3
	swap d3
	cmpi.w #3,d3
	bhi check_paddle_collision
	andi.l #$0000ffff,d0
	move.w d0,d4

	move.w d6,d1
	subi.w #BRICK_Y,d1
	move.w d1,d2
	lsl.w #3,d1
	lsl.w #1,d2
	add.w d2,d1
	add.w d4,d1

	lea.l brick_state(pc),a0
	tst.b (a0,d1.w)
	beq check_paddle_collision
	clr.b (a0,d1.w)
	subq.w #1,bricks_left

	move.w d4,d1
	move.w d1,d0
	lsl.w #2,d1
	add.w d0,d1
	addi.w #BRICK_X,d1
	move.w d6,d2
	move.w #' ',d3
	move.w #4,d4
	bsr draw_repeat
	bsr draw_bricks_left

	neg.w ball_dy
	move.w ball_y(pc),d6
	tst.w bricks_left
	bne check_paddle_collision
	move.b #1,game_result

check_paddle_collision:
	tst.w ball_dy
	bmi check_ball_lost
	cmpi.w #PADDLE_Y,d6
	blt check_ball_lost
	move.w paddle_x(pc),d0
	cmp.w d0,d5
	blt check_ball_lost
	addi.w #PADDLE_WIDTH-1,d0
	cmp.w d0,d5
	bgt check_ball_lost
	move.w #-1,ball_dy
	move.w #PADDLE_Y-1,d6

	move.w paddle_x(pc),d0
	addi.w #PADDLE_WIDTH/2,d0
	cmp.w d0,d5
	beq check_ball_lost
	blt paddle_bounce_left
	move.w #1,ball_dx
	bra check_ball_lost
paddle_bounce_left:
	move.w #-1,ball_dx

check_ball_lost:
	cmpi.w #BOARD_BOTTOM,d6
	blt store_ball_position
	move.b #2,game_result
	move.w #BOARD_BOTTOM-1,d6

store_ball_position:
	move.w d5,ball_x
	move.w d6,ball_y
	rts

; -----------------------------------------------------------------------------
; Text output helpers

draw_bricks_left:
	moveq.l #0,d0
	move.w bricks_left(pc),d0
	divu.w #10,d0
	move.l d0,d4

	move.w #48,d1
	move.w #0,d2
	move.w d4,d3
	addi.w #'0',d3
	bsr put_at

	swap d4
	move.w #49,d1
	move.w #0,d2
	move.w d4,d3
	addi.w #'0',d3
	bsr put_at
	rts

draw_text:
	IOCS _B_LOCATE
	IOCS _B_PRINT
	rts

put_at:
	IOCS _B_LOCATE
	move.w d3,d1
	IOCS _B_PUTC
	rts

draw_repeat:
	movem.l d0-d2/d5-d6,-(sp)
	move.w d1,d5
	move.w d4,d6
	subq.w #1,d6
draw_repeat_loop:
	move.w d5,d1
	bsr put_at
	addq.w #1,d5
	dbra d6,draw_repeat_loop
	movem.l (sp)+,d0-d2/d5-d6
	rts

frame_delay:
	; MC68000 10MHzで目視して操作できる速さに合わせる。
	moveq.l #15,d6
frame_delay_outer:
	move.w #45000,d7
frame_delay_inner:
	dbra d7,frame_delay_inner
	dbra d6,frame_delay_outer
	rts

wait_for_key:
	move.w #$00ff,-(sp)
	DOS _INPOUT
	addq.l #2,sp
	tst.b d0
	beq wait_for_key
	rts

; -----------------------------------------------------------------------------
; Data

title_text:
	.dc.b "X68000 BREAKOUT  A/D: MOVE  ESC: QUIT  LEFT: ",0
brick_text:
	.dc.b "####",0
win_message:
	.dc.b " YOU CLEARED IT!  PRESS A KEY ",0
lose_message:
	.dc.b " GAME OVER  PRESS A KEY ",0
	.even

paddle_x:
	.dc.w 35
ball_x:
	.dc.w 40
ball_y:
	.dc.w 18
ball_dx:
	.dc.w 1
ball_dy:
	.dc.w -1
bricks_left:
	.dc.w BRICK_COUNT
game_result:
	.dc.b 0
quit_requested:
	.dc.b 0
brick_state:
	.ds.b BRICK_COUNT
	.even
