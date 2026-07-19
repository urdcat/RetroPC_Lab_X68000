# m68kasm

MC68000、MC68010、MC68020、MC68030向けのホスト用クロスアセンブラです。
CPUプロファイルごとの命令・実効アドレス検証を行い、ヘッダーなしのビッグエンディアン機械語を出力します。
X68000 Xfileの直接出力は今後の段階で実装します。

```powershell
dotnet run --project tools/m68kasm/src/M68kAsm -- selftest
dotnet run --project tools/m68kasm/src/M68kAsm -- check tools/m68kasm/examples/language_features.m68 --cpu 68000
dotnet run --project tools/m68kasm/src/M68kAsm -- assemble tools/m68kasm/examples/language_features.m68 -o work/language_features.bin --cpu 68000
```

別プロジェクトへ持ち込む自己完結型パッケージは次のコマンドで生成します。

```powershell
.\tools\m68kasm\Pack.ps1 -Runtime win-x64
```

`tools/m68kasm/dist/`に展開済みディレクトリ、ZIP、SHA-256ファイルを生成します。
パッケージはターゲット非依存で、X68000・メガドライブ固有の素材や外部ツールを含みません。

## CPUプロファイル

`assemble`では`--cpu 68000|68010|68020|68030`を指定できます。省略時は互換性のため`68000`です。

```powershell
m68kasm assemble game.m68 -o game.bin --cpu 68030
```

- `68000`: MC68000 Programmer's Reference Manualの整数・システム命令一式
- `68010`: BKPT、MOVE CCRから、MOVEC、MOVES、RTD
- `68020`: ビットフィールド、CAS/CAS2、CHK2/CMP2、ロング乗除算、PACK/UNPK、TRAPcc、ロング分岐、拡張実効アドレス
- `68030`: 68020の継続命令と、内蔵MMUのPFLUSH/PLOAD/PMOVE/PTEST。68020専用のCALLM/RTMは拒否

68881/68882の浮動小数点命令はCPU本体とは別のコプロセッサプロファイルであり、この版の
`--cpu 68030`には含めません。対応表と形式は[INSTRUCTION_SUPPORT.md](../../docs/INSTRUCTION_SUPPORT.md)を参照してください。

`check`も構文解析だけでなく、指定CPUで2パスの機械語生成まで検査する。出力ファイルは作らない。
`assemble`と同じ`--cpu`および`--base-address`を指定できる。

## 現在の機械語出力範囲

- データレジスタ `d0.b`～`d7.l`
- `#10`、`#$ff`、`#0xff`、`#%1010`形式の即値
- 手続き内ローカル変数。必要な `LINK A6` / `UNLK A6` は自動出力
- `=`、`+=`、`-=`と、代入右辺の単一の `+` / `-`
- `]`による自動`RTS`と、`]/`による`RTS`抑止
- `defmacro NAME[(ARGS)] = { ... }`と引数なし／引数付き文マクロ
- `{ ... }`の字句ラベルスコープ。マクロ内ラベルは呼び出しごとに独立
- 相対パス`include`、循環検出、include内マクロ／構造体
- 定数式による`ifasm (式) { ... } else { ... }`
- `NAME equ 式`形式の定数ラベル。前方参照、定数間の式、循環／重複検出
- 括弧、算術、シフト、比較、ビット、論理演算を含む64ビット式
- `struct base:a0 vector { x.l, y.l, z.l }`と`vector,x`形式のロード／ストア
- 2パスのグローバル／手続きローカルラベルと、CPUに応じた`.s`／`.w`／`.l`条件分岐
- MC68000の全命令ファミリと、68010・68020・68030のCPU本体拡張命令
- MC68020以降のスケール付きインデックス、ワード／ロングベース変位、プリ／ポストインデックスメモリ間接
- MC68030内蔵MMUのファンクションコード、マスク、レベル、MMUレジスタ転送
- `MOVEQ`、`ADDQ/SUBQ`と、小さい代数即値のクイック命令化
- `dc.b`／`dc.w`／`dc.l`、`defb`／`defw`／`defl`／`defs`
- `align 境界[,詰め値]`と`even`
- `--base-address`による絶対シンボルアドレス指定

出力はヘッダのない68kビッグエンディアン機械語です。手続きはソース順に並びます。
ローカル変数を使う手続きではA6をフレームポインタとして予約します。

外部シンボル、オブジェクトリンク、Xfile、コマンドラインからの定数定義はまだ未対応です。
CPUプロファイル外の命令や不正なオペランドを黙って別命令へ変換せず、機械語出力時にエラーにします。
詳細な目標と段階は [ASSEMBLER_FOUNDATION.md](../../docs/ASSEMBLER_FOUNDATION.md) を参照してください。
