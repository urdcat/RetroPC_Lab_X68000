# 引継ぎ資料

更新: 2026-07-13

## このリポジトリの目的

X68000 Z（X68000エミュレータモード）向け Human68k ソフトを、Windowsから開発する作業場である。
CPUコアはMC68000に限定し、将来メガドライブへ移せるゲームロジック・固定小数点演算・素材原本・
ビルド規約を残す。X68000の`.X`とメガドライブROM、各機種の入出力・描画・音源制御は共通化しない。

商用利用を前提に、既存アセンブラのライセンスへ依存しない独自クロスアセンブラを並行して育てる。
ツール名`m68kasm`は仮称で、最終製品名ではない。

## 現在の到達点

| 項目 | 状態 | 根拠・場所 |
| --- | --- | --- |
| X68000向け既存ツール | 導入済み | MSYS2内の`~/xdev68k`。`run68`、`HAS060.X`、`HLK301.X`を利用する。 |
| 最小`.X` | ホストでビルド・実行確認済み | `src/platform/x68k/hello.s` → `build/HELLO.X`。文字列を表示してDOS終了する。 |
| 実機確認 | 未実施 | X68000 Zへ`HELLO.X`を転送して、起動・表示・終了を確認する。 |
| `m68kasm` | 構文解析のみ実装済み | `tools/m68kasm/`。構造体、手続き、ローカル、代数記法、同一行複数文を検査する。 |
| `m68kasm`の機械語/Xfile出力 | 未実装 | パーサーが通っても68000命令・`.X`は生成されない。 |
| メガドライブ用プロファイル | 未実装 | CPUコア共有の方針だけを固定済み。 |

## 固定した設計判断

- X68000の既存ビルドは`HAS060 → HLK → Human68k Xfile (.X)`を使う。
- Cコンパイラに依存せず、Motorola記法の68000アセンブラを基準にする。
- `HAS060`/`HLK`は既存出力と比較する互換性オラクルとして保持する。独自ツールが未対応の構文を、
  既存アセンブラへ黙って委譲しない。
- `vasm`は機能的には有力だが、X68000向け商用利用の許諾が明確になるまで採用しない。
- `m68kasm`の代数記法ではデータレジスタの幅を必ず明記する。例: `d0.b`、`d0.w`、`d0.l`。
  裸の`d0`はエラー。同一式内のデータレジスタ幅も一致させる。
- `;`はコメントではなく同一行の文区切り、`//`はコメントである。
- `[手続き名`から`]`で閉じるとRTSを自動出力する。`]/`はRTSを出さない。ローカル変数がある場合の
  `link`/`unlk`は仕様として決めたが、まだコード生成していない。

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
cd /c/Users/urdca/OneDrive/Documents/X68000で何か作ろう/src/platform/x68k
make clean
make
```

成果物は`src/platform/x68k/build/HELLO.X`。`build/`は生成物なのでコミットしない。
`run68`には長い日本語を含むホストパスの制限があるため、ホスト上で実行確認を再現する際は
`HELLO.X`を`/tmp`など短いASCIIパスにコピーしてから実行する。

### 実機確認の次の一手

1. `HELLO.X`を、所持している転送手段でHuman68kから読める書込み可能な媒体へコピーする。
2. X68000 Zで`HELLO.X`を起動する。
3. 表示とDOSへの終了を確認する。
4. 結果（使用媒体・ファームウェア・表示・失敗時ログ）をこのファイルの「実機検証記録」へ追記する。

ゲーム専用SDカードは開発用に使わない。媒体要件とHDS/XDFの注意は
[ENVIRONMENT.md](ENVIRONMENT.md)を優先する。

## 次に実装する範囲

優先順位は次の通り。

1. `m68kasm`にローカル変数レイアウトを追加し、`link a6,#-size`/`unlk a6`を含む中間表現または
   展開リストを検証可能にする。
2. `moveq`、`move`、`addq`、`add`、`lea`、`pea`、`link`、`unlk`、`rts`など、最小のMC68000命令を
   バイト列へ符号化する。オペコードごとのゴールデンテストを作る。
3. 単一モジュール・単一コードセクションに限ったHuman68k Xfile直接出力を実装し、`HAS060`/`HLK`生成物と
   エミュレータ動作で比較する。
4. ラベル、定数、再配置、複数モジュール、レジスタ契約検査を段階的に追加する。

この順番を崩して全68000命令やメガドライブ固有処理を先に広げない。現在のパーサーはプロトタイプであり、
構文受理をアセンブル成功と表現しない。

## 主要な場所

```text
docs/ASSEMBLER_FOUNDATION.md   独自ツール採用の理由と段階計画
docs/ASSEMBLER_LANGUAGE.md     入力言語の仕様草案
docs/ENVIRONMENT.md            MSYS2/xdev68kと実機転送の手順
src/platform/x68k/hello.s      HAS060/HLKでビルドする最小プログラム
src/platform/x68k/Makefile     既存ツールチェーン用のビルド定義
tools/m68kasm/                 独自アセンブラ（現在はパーサーのみ）
```

## 実機検証記録

- 2026-07-13: 未実施。次回は`HELLO.X`の起動確認から開始する。
