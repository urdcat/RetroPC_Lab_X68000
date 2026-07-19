# ブロック崩しサンプル

更新: 2026-07-13

`src/platform/x68k/breakout.s`は、MC68000アセンブリだけで書いたHuman68k向けの小さな
ブロック崩しである。X68000標準のIOCSでテキスト画面を描画し、Human68k DOSコールで
キーボードを非同期に読む。XEiJ固有APIへは依存しない。

## 内容

- 10列×5段、50個のブロック
- ボールと左右・天井・ブロック・パドルの衝突判定
- 残りブロック数の表示
- 全消去時のクリア表示、落球時のゲームオーバー表示
- `A` / `D`でパドルを移動、`ESC`で終了

初版はアセンブラ、Human68k Xfile、IOCS、入力、ゲームループを一度に実機相当環境へ通すための
テキスト画面版である。グラフィック画面、スプライト、FM/ADPCM音源は次の段階で追加する。

## ビルド

MSYS2 MinGW 64-bitシェルで次を実行する。

```bash
cd /d/work/X68000で何か作ろう/src/platform/x68k
make clean all
```

`build/HELLO.X`と`build/BREAKOUT.X`が生成される。

## 動作確認

2026-07-13にXEiJ 0.26.07.08、MC68000、10MHz、Human68k 3.02のHFS起動で確認した。

- `BREAKOUT.X`が起動し、50個のブロック、ボール、パドル、残数を表示する。
- ブロック衝突で表示が消え、残数が50から49へ減る。
- `A`でパドルが左、`D`で右へ移動する。
- 落球時に`GAME OVER`を表示し、キー入力後にHuman68kへ戻る。
- ゲーム中の`ESC`でHuman68kプロンプトへ戻る。

XEiJのGUIは検証後も利用者が操作できる状態で残す。自動試験専用のヘッドレス化はまだ行っていない。

## XEiJで実行

最初の一度だけ、MSYS2へXDF展開用のmtoolsを導入する。

```bash
pacman -S --needed mingw-w64-x86_64-mtools
```

PowerShellで、公式配布の`HUMAN302.XDF`からGit管理外のHFS起動ディレクトリを作り、
`BREAKOUT.X`を配置する。

```powershell
.\tools\Prepare-XEiJBoot.ps1
.\tools\Run-XEiJ.ps1 -BootDirectory .\tools\third_party\xeij\XEiJ-0.26.07.08\xeij_boot
```

Human68kのプロンプトが表示されたら、次を入力する。

```text
BREAKOUT.X
```

ビルド後は`Prepare-XEiJBoot.ps1`を再実行すると、最新の`.X`だけを同じ起動ディレクトリへ
上書きできる。別のプログラムを試す場合は`-Program`で指定する。

```powershell
.\tools\Prepare-XEiJBoot.ps1 -Program .\src\platform\x68k\build\HELLO.X
```
