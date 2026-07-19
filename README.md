# X68000 Z で何か作ろう

X68000 Z で実行する Human68k 向けソフトを、Windows 上でクロス開発するための作業場です。

最初の実機ターゲットは **X68000 Z の X68000 エミュレータモード**です。CPU の基準は最初から
**MC68000** に固定し、後にメガドライブへ移す際に使える68000中核ルーチン、固定小数点演算、素材原本、
ビルド規約を残します。X68000 用の `.X` 実行ファイルとメガドライブ用 ROM は別物なので、完成物や
ハードウェア制御まで共通化する方針ではありません。

## 方針

- X68000 側は [xdev68k](https://github.com/yosshin4004/xdev68k) の `run68`、`HAS060.X`、`HLK` を採用する。
- ソースとビルドは Motorola 記法のアセンブラを基準にする。ターゲット用Cコンパイラには依存しない。
- メガドライブ側も将来は68000アセンブラを基準にし、ROMヘッダ・VDP・音源の部分だけを別実装にする。
- CPU は MC68000 相当、性能予算はメガドライブを下限にして設計する。
- 素材は `assets/source/` を原本とし、X68000 とメガドライブにはそれぞれ専用の変換結果を作る。

## 現在地

MSYS2、xdev68k、run68、HAS060.X、HLK301.X の導入と、アセンブラ製 `HELLO.X` のホスト上での
ビルド・実行確認まで完了しています。次の動作サンプルとして、IOCS描画・非同期入力・衝突判定を持つ
`BREAKOUT.X`を追加しました。遊び方とXEiJでの実行手順は
[docs/SAMPLE_BREAKOUT.md](docs/SAMPLE_BREAKOUT.md)を参照してください。実機 X68000 Z への転送確認は
次の節目です。

現在の到達点、再現手順、未実装範囲、次の作業は [docs/HANDOFF.md](docs/HANDOFF.md) に集約しています。
環境・実機転送時の注意は [docs/ENVIRONMENT.md](docs/ENVIRONMENT.md)、調査根拠は
[docs/RESEARCH_2026-07-12.md](docs/RESEARCH_2026-07-12.md) を参照してください。

PC上の反復確認には、無償配布のX68000エミュレータ **XEiJ 0.26.07.08** を
`tools/third_party/xeij/` にローカル導入する。約790MBの第三者配布物はGit管理・製品配布から除外し、
欠けている環境では`./tools/Get-XEiJ.ps1`で公式配布物を検証付きで取得する。導入済みのOpenJDK 26以上が
あれば`./tools/Run-XEiJ.ps1`でMC68000 / 10MHzプロファイルを起動できる。ライセンスと起動方法は
[docs/EMULATOR_XEIJ.md](docs/EMULATOR_XEIJ.md) を参照する。

## 予定ディレクトリ

```text
src/core/                 # 将来両機種で共有する、ハード非依存の68000ルーチン
src/platform/x68k/        # Human68k / IOCS / X68000 の実装
src/platform/megadrive/   # SGDK / VDP / Z80 音源の実装
assets/source/            # 手作業する原本。各機種の完成データは置かない
assets/x68k/              # X68000 向け変換結果
assets/megadrive/         # メガドライブ向け変換結果
```

最初の題材は、入力・画面更新・音を一通り通せる小さな作品にする。`hello.s`に続いて、
`src/platform/x68k/breakout.s`を`HAS060 → HLK → .X`で組める段階まで進んでいます。
