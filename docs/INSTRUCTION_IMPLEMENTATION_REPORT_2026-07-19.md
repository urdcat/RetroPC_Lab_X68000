# m68kasm命令実装レポート

日付: 2026-07-19
対象: m68kasm 0.8.0-preview.1 → 0.9.0-preview.1
状態: 正規ソース、自己テスト、公開仕様、win-x64配布物へ反映済み

## 修正要求

ゲーム内連携で次の不足が確認された。

- `MULU/MULS/DIVU/DIVS`の不足
- `ASR.W #8,D0`を含むシフト／ローテート形式の不足
- `CMP`ファミリと合法オペランドの実装確認
- MC68000命令表に対する残りの未実装命令
- X68030向けに、アセンブルスイッチでMC68030まで選択する機能

個別命令をゲーム側で回避せず、汎用アセンブラのソースを修正する。

## 実装結果

`--cpu 68000|68010|68020|68030`を追加した。省略時は`68000`である。

- MC68000: Programmer's Reference Manual Appendix A-3の全命令ファミリ
- MC68010: BKPT、MOVE CCRから、MOVEC、MOVES、RTD
- MC68020: ビットフィールド、CAS/CAS2、CHK2/CMP2、ロング乗除算、PACK/UNPK、
  TRAPcc、LINK.L、ロング分岐、拡張TST
- MC68020実効アドレス: スケール、16/32ビットベース変位、フル拡張ワード、
  プリ／ポストインデックスメモリ間接、ベース／インデックス抑止
- MC68030: PFLUSHA/PFLUSH、PLOADR/PLOADW、PMOVE/PMOVEFD、PTESTR/PTESTW

MC68020だけに存在するCALLM/RTMは`--cpu 68030`で拒否する。68881/68882 FPUは
MC68030 CPU本体とは別のコプロセッサなので、このCPUスイッチへ暗黙に混在させない。

## ソース変更

- `src/M68kAsm/CpuModel.cs`: CPUモデルとコマンドライン値
- `src/M68kAsm/Mc68000InstructionEncoder.cs`: MC68000全命令ファミリ
- `src/M68kAsm/Post68000InstructionEncoder.cs`: MC68010/68020/68030命令
- `src/M68kAsm/BinaryAssembler.cs`: CPUコンテキスト、ロング分岐、68020実効アドレス
- `src/M68kAsm/LanguageParser.cs`: ビットフィールドと複文の波括弧を区別
- `src/M68kAsm/InstructionSetProfileSelfTest.cs`: CPU別固定バイト列と拒否テスト
- `src/M68kAsm/Program.cs`: `--cpu`のCLIと、`check`での機械語生成検査

## 独立検証

HAS060.Xをオラクルとして、同じ命令列を個別にアセンブルし、次の固定バイト列を完全一致させた。

| 範囲 | 検証バイト数 |
| --- | ---: |
| MC68000全命令ファミリ | 298 |
| MC68010追加命令 | 38 |
| MC68020追加命令 | 156 |
| MC68020拡張実効アドレス | 42 |
| MC68030 MMU命令 | 82 |

これらを`m68kasm selftest`へ埋め込んだ。さらに、下位CPUでの上位命令拒否、
MC68030でのCALLM拒否、MC68000での拡張TST拒否を確認する。

## アセンブラ配布物への反映要求

1. 上記ソースを正規の`tools/m68kasm/src/M68kAsm/`へ反映する。
2. バージョンを`0.9.0-preview.1`へ更新する。
3. `dotnet build`と`selftest`を成功させる。
4. `Pack.ps1 -Runtime win-x64`で自己完結パッケージを再生成する。
5. 利用側は古い`bin/m68kasm.exe`だけを上書きせず、パッケージ単位で更新する。
6. メガドライブでは`--cpu 68000`または既定値を使い、X68030コードだけ`--cpu 68030`を指定する。

CPU別の公開仕様は`docs/INSTRUCTION_SUPPORT.md`を正本とする。

## 完了判定

上記要求は正規ソースへ反映した。Releaseビルドはwarning 0、error 0で成功し、
正規ソースと自己完結配布EXEの両方でCPU別`selftest`がPASSした。
配布マニフェストは`MC68000`、`MC68010`、`MC68020`、`MC68030`を列挙し、
既定CPUを`MC68000`としている。
従来は構文解析だけだった`check`も、`assemble`と同じCPU・ベースアドレスで機械語生成まで
検査し、CPU境界や不正オペランドを出力前に検出する。
