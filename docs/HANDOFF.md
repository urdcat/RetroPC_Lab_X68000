# 引継ぎ資料

更新: 2026-07-19

## このリポジトリの目的

X68000 Z（X68000エミュレータモード）向け Human68k ソフトを、Windowsから開発する作業場である。
既存サンプルのCPUコアはMC68000に保ち、将来メガドライブへ移せるゲームロジック・固定小数点演算・
素材原本・ビルド規約を残す。アセンブラはX68030向け開発のためMC68030まで選択可能にする。
X68000の`.X`とメガドライブROM、各機種の入出力・描画・音源制御は共通化しない。

商用利用を前提に、既存アセンブラのライセンスへ依存しない独自クロスアセンブラを並行して育てる。
ツール名`m68kasm`は仮称で、最終製品名ではない。

## 現在の到達点

| 項目 | 状態 | 根拠・場所 |
| --- | --- | --- |
| X68000向け既存ツール | 導入済み | MSYS2内の`~/xdev68k`。`run68`、`HAS060.X`、`HLK301.X`を利用する。 |
| 最小`.X` | ホストでビルド・実行確認済み | `src/platform/x68k/hello.s` → `build/HELLO.X`。文字列を表示してDOS終了する。 |
| ブロック崩しサンプル | XEiJで動作確認済み | `src/platform/x68k/breakout.s` → `build/BREAKOUT.X`。MC68000 / 10MHzで描画、ブロック衝突、A/D移動、GAME OVER、ESC終了を確認。 |
| PCエミュレータ | ローカル導入・起動条件検証済み | XEiJ 0.26.07.08を`tools/third_party/xeij/`に展開。約790MBのためGit除外。Eclipse Temurin OpenJDK 26.0.1を導入し、`Run-XEiJ.ps1 -Check`で検出済み。 |
| 実機確認 | 未実施 | X68000 Zへ`HELLO.X`を転送して、起動・表示・終了を確認する。 |
| `m68kasm` | 0.9.0-preview.1実装済み | `--cpu 68000/68010/68020/68030`、2パス、ラベル、マクロ、include、式、align、構造体を実装。 |
| `m68kasm`の機械語出力 | CPUプロファイル検証済み | MC68000全命令、68010/68020拡張、68020フル実効アドレス、68030内蔵MMUを実装。HAS060.Xとの固定バイト列照合を自己テスト化。 |
| `m68kasm`のXfile出力 | 未実装 | 現在はヘッダーなし68kビッグエンディアンバイナリのみ。 |
| メガドライブ用プロファイル | 未実装 | CPUコア共有の方針だけを固定済み。 |

## 固定した設計判断

- X68000の既存ビルドは`HAS060 → HLK → Human68k Xfile (.X)`を使う。
- Cコンパイラに依存せず、Motorola記法の68000アセンブラを基準にする。
- `HAS060`/`HLK`は既存出力と比較する互換性オラクルとして保持する。独自ツールが未対応の構文を、
  既存アセンブラへ黙って委譲しない。
- `vasm`は機能的には有力だが、X68000向け商用利用の許諾が明確になるまで採用しない。
- XEiJはローカルの開発専用とする。API・自動操作向けの改造を行ってもゲーム製品へ同梱せず、
  改造版を外部配布する必要が出たときは元ライセンスの無償配布・ソース同梱・ライセンス添付を守る。
- XEiJのAPI化ではGUIモードを残す。ヘッドレス自動化モードには、同じエミュレータへ接続する
  別ユーザーコンソールを用意する。実装方針は`docs/EMULATOR_API_DIRECTION.md`を参照する。
- `m68kasm`の代数記法ではデータレジスタの幅を必ず明記する。例: `d0.b`、`d0.w`、`d0.l`。
  裸の`d0`はエラー。同一式内のデータレジスタ幅も一致させる。
- `;`はコメントではなく同一行の文区切り、`//`はコメントである。
- `[手続き名`から`]`で閉じるとRTSを自動出力する。`]/`はRTSを出さない。ローカル変数がある場合の
  `link`/`unlk`は自動生成する。
- `defmacro NAME[(ARGS)] = { ... }`を文マクロとし、展開ごとに`{ ... }`の字句ラベルスコープを作る。
- マクロ内部の前方ラベルは通常ラベルを使う。マクロ展開ごとの複文スコープで同名ラベルを分離するため、
  専用の`adapt`構文は設けない。
- `include`はinclude元に対する相対パスで解決し、循環をエラーにする。
- `NAME equ 式`はグローバル定数ラベルとする。前方参照を許可し、未定義・循環・重複・通常シンボルとの
  名前競合をエラーにする。定数は代数即値、RAW式、データ、`align`、シフト回数、`ifasm`で共通利用する。
- `align 境界[,詰め値]`は明示的にパディングし、命令の奇数配置を暗黙補正しない。
- `struct base:a0 vector { x.l,y.l,z.l }`は`vector,x`形式の代数ロード／ストアへ展開する。
- `--cpu`の省略時はMC68000とし、上位命令を下位CPUで受理しない。CALLM/RTMはMC68020だけで受理する。
- MC68020/68030ではロング乗除算、ロング分岐、スケール／フル拡張実効アドレスを受理する。
- MC68030プロファイルは内蔵MMUを含むが、68881/68882 FPUはCPUと別の将来プロファイルとする。
- MC68040/MC68060は命令削除や内蔵FPU/MMU差があるため、68030の単純上位として扱わない。

仕様の原典は [ASSEMBLER_FOUNDATION.md](ASSEMBLER_FOUNDATION.md) と
[ASSEMBLER_LANGUAGE.md](ASSEMBLER_LANGUAGE.md) である。仕様を変える場合は、先にこの2ファイルと
パーサーの自己テストを同じ変更で更新する。

## 再現手順

### ホスト側の構文検証

Windows PowerShellで、リポジトリのルートから実行する。

```powershell
dotnet build tools/m68kasm/src/M68kAsm/M68kAsm.csproj --nologo
dotnet run --project tools/m68kasm/src/M68kAsm/M68kAsm.csproj --no-build -- selftest
dotnet run --project tools/m68kasm/src/M68kAsm/M68kAsm.csproj --no-build -- check tools/m68kasm/examples/language_features.m68
dotnet run --project tools/m68kasm/src/M68kAsm/M68kAsm.csproj --no-build -- assemble tools/m68kasm/examples/structured_macros.m68 -o work/structured_macros.bin
```

期待する最後の確認結果は次の形である。

```text
struct Actor: 4 field(s)
procedure update_actor: 2 local(s), 3 statement(s), no RTS
procedure finish_frame: 0 local(s), 1 statement(s), RTS
```

### X68000向けの既存ビルド

MSYS2 **MinGW 64-bit** シェルで実行する。`XDEV68K_DIR`未設定なら、先に
`docs/ENVIRONMENT.md`の環境変数を設定する。

```bash
cd /d/work/X68000で何か作ろう/src/platform/x68k
make clean
make
```

成果物は`src/platform/x68k/build/HELLO.X`と`BREAKOUT.X`。`build/`は生成物なのでコミットしない。
`run68`には長い日本語を含むホストパスの制限があるため、ホスト上で実行確認を再現する際は
`HELLO.X`を`/tmp`など短いASCIIパスにコピーしてから実行する。

### 実機確認の次の一手

1. `HELLO.X`を、所持している転送手段でHuman68kから読める書込み可能な媒体へコピーする。
2. X68000 Zで`HELLO.X`を起動する。
3. 表示とDOSへの終了を確認する。
4. 結果（使用媒体・ファームウェア・表示・失敗時ログ）をこのファイルの「実機検証記録」へ追記する。

ゲーム専用SDカードは開発用に使わない。媒体要件とHDS/XDFの注意は
[ENVIRONMENT.md](ENVIRONMENT.md)を優先する。

## XEiJでの反復確認

`tools/Get-XEiJ.ps1`でXEiJがローカル導入されていることを確認し、`tools/Run-XEiJ.ps1`で、
MC68000 / 10MHz / Human68k 3.02の起動を再現する。依存関係だけを確認する場合は
`tools/Run-XEiJ.ps1 -Check`を使う。XEiJが起動できたら、
Human68kを立ち上げた状態でHFSメニューから`src/platform/x68k/build/`を割り当て、生成した
`HELLO.X`を確認する。HFSをディスクイメージ起動中に恒常的に使うには、XEiJ公式資料に従って
`CONFIG.SYS`へ`device=[rom$00e9f020]`を加える。

XEiJは開発時の確認専用であり、製品の`.X`やメガドライブROM、商用配布パッケージへ同梱しない。
詳細なライセンス条件は[EMULATOR_XEIJ.md](EMULATOR_XEIJ.md)を参照する。

`BREAKOUT.X`の再現手順と検証記録は[SAMPLE_BREAKOUT.md](SAMPLE_BREAKOUT.md)を参照する。

## 次に実装する範囲

優先順位は次の通り。

1. マクロ／includeの元ソース位置追跡と、`ifasm`を含むコマンドライン定数定義を追加する。
2. code/data/bss、外部シンボル、再配置可能オブジェクト、リンカ、マップを段階的に追加する。
3. X68000側で必要ならHuman68k Xfile出力を別レイヤーとして実装する。メガドライブのROMヘッダーや
   ベクターテーブルは別プロジェクトに置き、汎用アセンブラへ混ぜない。
4. 実需が確定した場合だけ68881/68882、MC68040、MC68060を独立プロファイルで追加する。

## 主要な場所

```text
docs/ASSEMBLER_FOUNDATION.md   独自ツール採用の理由と段階計画
docs/ASSEMBLER_LANGUAGE.md     入力言語の仕様草案
docs/INSTRUCTION_SUPPORT.md    CPU別命令・実効アドレス・検証範囲
docs/ENVIRONMENT.md            MSYS2/xdev68kと実機転送の手順
src/platform/x68k/hello.s      HAS060/HLKでビルドする最小プログラム
src/platform/x68k/breakout.s   IOCS描画・入力・衝突判定を通すブロック崩し
src/platform/x68k/Makefile     既存ツールチェーン用のビルド定義
tools/m68kasm/                 独自MC68000クロスアセンブラ
tools/m68kasm/examples/structured_macros.m68
                               マクロ、include、スコープ、式、align、base構造体の統合例
```

## 実機検証記録

- 2026-07-13: XEiJ 0.26.07.08のMC68000 / 10MHz / Human68k 3.02で`BREAKOUT.X`を確認。
  HFSから起動し、描画、ブロック衝突、A/D移動、落球、ESC終了が動作した。X68000 Z実機は未実施。
