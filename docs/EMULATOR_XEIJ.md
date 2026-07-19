# XEiJ エミュレータの扱い

更新: 2026-07-13

## 採用物

- 名称: XEiJ (X68000 Emulator in Java)
- 版: 0.26.07.08
- 作者: Makoto Kamada / STUDIO KAMADA
- 公式配布元: <https://stdkmd.net/xeij/>
- 公式ライセンス: <https://stdkmd.net/xeij/license.htm>
- 配布物のSHA-256:
  `3EA0A1EE273CD6A4BF5D8AF7275AD7400AF61C64DCA0049F55A9841F8933BF3B`
- ローカル展開先: `tools/third_party/xeij/XEiJ-0.26.07.08/`

公式配布物を改変せずにローカルへ展開する。展開先には`XEiJ.jar`、再コンパイル用のJavaソース、
`data/license_XEiJ.txt`を含む。無償公開のHuman68k 3.02システムディスク`HUMAN302.XDF`も
公式配布物の一部として含まれる。

配布物は展開後に約790MB（2,990ファイル）となるためGit管理しない。代わりに
`tools/Get-XEiJ.ps1`が公式URL・固定版・SHA-256を検証して取得する。これは、リポジトリを
肥大化させず、開発環境を同じ内容で復元するための措置である。

## ライセンス上の境界

XEiJおよび派生物を再配布する際は、次を守る。

1. 無償で配布する。
2. 再コンパイルに必要なソースコードを同梱する。
3. XEiJのライセンス本文を添付する。

このリポジトリはXEiJを再配布せず、利用者が公式配布物を直接取得する。ローカルに取得したXEiJは
ソース・ライセンスと一体で保持する。ただしXEiJは**開発支援ツール**であり、完成した商用作品の
配布物へ含めない。製品配布物には
ゲームの`.X`またはROMと必要な利用者向けファイルだけを入れる。

Human68k、IPLROM、フォントなどはそれぞれの権利者に帰属する。公式配布物の範囲外のROM、
ゲーム、私有ディスクイメージをこのリポジトリへ追加しない。

## 開発専用の運用

XEiJはこの作業環境でのビルド確認・デバッグ・将来の自動テストにのみ使用する。XEiJへAPIや
自動操作のための改造を加えた場合も、まずはローカルの開発専用とし、完成したゲームの配布物には
一切含めない。

API改造の際も、利用者がXEiJを直接操作できるGUIモードを残す。ヘッドレスの自動化モードを追加する
場合は、別のユーザーコンソールから同じエミュレータへ接続・観察・介入できるようにする。詳細な設計方針は
[EMULATOR_API_DIRECTION.md](EMULATOR_API_DIRECTION.md)に固定する。

改造版を外部へ配布する必要が生じた時点で、配布形態を個別に確認し、XEiJの元ライセンスに従って
無償配布、再コンパイルに必要なソースコードの同梱、ライセンス本文の添付を必須とする。

## 起動

XEiJ 0.26.07.08はOpenJDK 26以上を要求する。Java本体はリポジトリへ同梱しない。
初回またはクリーンな環境では、リポジトリのルートからXEiJを取得する。

```powershell
.\tools\Get-XEiJ.ps1
```

その後、OpenJDKを導入して`java -version`が26以上を示す状態で起動する。

```powershell
.\tools\Run-XEiJ.ps1
```

起動前の依存関係だけを確認する場合は、次を実行する。

```powershell
.\tools\Run-XEiJ.ps1 -Check
```

このWindows環境にはEclipse Temurin OpenJDK 26.0.1を導入済みである。インストーラーが設定する
システムPATHが既存のターミナルへ即時反映されない場合にも、`Run-XEiJ.ps1`は`JAVA_HOME`と
`C:\Program Files\Eclipse Adoptium`を探索してJavaを見つける。

スクリプトは、X68000 EXPERT相当・MC68000・10MHz・2MBメモリで、同梱の`HUMAN302.XDF`から
起動する。終了時に設定を保存しないので、プロジェクトの基準状態を汚さない。

## ビルド成果物を見せる方法

まず既存のアセンブラで成果物を作る。

```bash
cd src/platform/x68k
make
```

XEiJ起動後にHFSメニューの「Human68kのドライブに割り当てるディレクトリを開く」から
`src/platform/x68k/build/`を割り当てる。ディスクイメージから起動したHuman68kでHFSを継続利用するには、
`CONFIG.SYS`に次を追加して再起動する。

```text
device=[rom$00e9f020]
```

これはXEiJ公式のホストファイルシステム説明に基づく。初回は書込みを行わず、`HELLO.X`の表示と
終了を確認する。

ディスクイメージ側の`CONFIG.SYS`を変更せず、生成物とHuman68kを同じHFSから起動する方法もある。
MSYS2の`mingw-w64-x86_64-mtools`を導入後、次を実行する。

```powershell
.\tools\Prepare-XEiJBoot.ps1
.\tools\Run-XEiJ.ps1 -BootDirectory .\tools\third_party\xeij\XEiJ-0.26.07.08\xeij_boot
```

`Prepare-XEiJBoot.ps1`は公式配布の`HUMAN302.XDF`をGit管理外の`xeij_boot/`へ展開し、既定では
`build/BREAKOUT.X`を配置する。XEiJのプロンプトで`BREAKOUT.X`を実行する。ゲーム固有の説明は
[SAMPLE_BREAKOUT.md](SAMPLE_BREAKOUT.md)を参照する。
