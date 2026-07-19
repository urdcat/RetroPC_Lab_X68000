# CPU命令対応表

## CPU選択

`assemble INPUT -o OUTPUT --cpu CPU`の`CPU`には`68000`、`68010`、`68020`、`68030`を指定する。
省略時は`68000`である。API利用時は`BinaryAssembler.Assemble`の`CpuModel`引数で同じプロファイルを選ぶ。

各プロファイルは、そのCPUで実行できない命令・幅・実効アドレスをエラーにする。上位CPUを単純な
文字列許可リストとして扱わず、68020だけに存在するCALLM/RTMも68030では拒否する。

## MC68000

Motorola/NXP Programmer's Reference Manual Appendix A-3の整数・システム命令ファミリを実装する。

- ABCD、SBCD、NBCD
- ADD、ADDA、ADDI、ADDQ、ADDX
- SUB、SUBA、SUBI、SUBQ、SUBX
- AND、ANDI、OR、ORI、EOR、EORI
- ASL、ASR、LSL、LSR、ROL、ROR、ROXL、ROXR
- BCHG、BCLR、BSET、BTST
- Bcc、DBcc、Scc
- CHK、CLR、CMP、CMPA、CMPI、CMPM
- DIVS、DIVU、MULS、MULUのワード形式
- EXG、EXT、ILLEGAL
- JMP、JSR、LEA、LINK、PEA、UNLK
- MOVE、MOVEA、MOVEM、MOVEP、MOVEQ、MOVE CCR/SR/USP
- NEG、NEGX、NOT、SWAP、TAS、TST
- NOP、RESET、RTE、RTR、RTS、STOP、TRAP、TRAPV

条件別名`BHS/BLO`、`DBRA/DBHS/DBLO`、`SHS/SLO`も受理する。

## MC68010

MC68000に加えて次を実装する。

- BKPT
- MOVE CCRから実効アドレス
- MOVEC
- MOVES
- RTD

## MC68020

MC68010に加えて次を実装する。

- BFCHG、BFCLR、BFEXTS、BFEXTU、BFFFO、BFINS、BFSET、BFTST
- CALLM、RTM
- CAS、CAS2
- CHK.L、CHK2、CMP2
- DIVS.L、DIVU.L、DIVSL.L、DIVUL.L
- MULS.L、MULU.Lの単一レジスタ／レジスタ対形式
- EXTB.L、LINK.L、PACK、UNPK、TRAPcc
- Bcc.L
- TSTのアドレスレジスタ、PC相対、即値形式

68020拡張実効アドレスとして、`Xn.W/L*1/2/4/8`、16/32ビットベース変位、
`([bd,An,Xn],od)`プリインデックス、`([bd,An],Xn,od)`ポストインデックスを実装する。
ベース／インデックス抑止とヌル変位もフル拡張ワードで表現できる。

## MC68030

68020からCALLM/RTMを除くCPU本体命令に加え、MC68030内蔵MMU命令を実装する。

- PFLUSHA
- PFLUSH `FC,MASK[,<ea>]`
- PLOADR、PLOADW
- PMOVE、PMOVEFD: TC、SRP、CRP、TT0、TT1、MMUSR
- PTESTR、PTESTW

ファンクションコードは`#0`～`#7`、`d0`～`d7`、`sfc`、`dfc`を受理する。

## 対象外

`--cpu`はCPU本体を選ぶ。68881/68882は外付けコプロセッサであり、68030 CPUの必須命令ではないため、
浮動小数点命令と汎用`cp*`コプロセッサ命令はこの版に含めない。将来追加する場合は`--fpu`のような
独立プロファイルでCPUと組み合わせ、68881/68882の差と不在時の誤生成を検出する。

MC68040/MC68060も単純な上位互換ではないため、`68030`へ混在させず独立CPUプロファイルとして追加する。

## 検証

`selftest`には次の固定バイト列を組み込む。

- MC68000の全命令ファミリ: 298バイト
- MC68010追加命令: 38バイト
- MC68020追加命令: 156バイト
- MC68020拡張実効アドレス: 42バイト
- MC68030 MMU命令: 82バイト
- 下位CPUでの上位命令拒否、68030でのCALLM拒否

これらの期待値はHAS060.Xで同じソースをアセンブルし、命令列をバイト単位で照合して確定した。
