# m68kasm (実験中)

MC68000向けの商用利用可能な開発基盤を目指す、ホスト用クロスアセンブラです。
現時点は入力言語の構文検証段階です。X68000 Xfileの直接出力は次段階で実装します。

```powershell
dotnet run --project tools/m68kasm/src/M68kAsm -- selftest
dotnet run --project tools/m68kasm/src/M68kAsm -- check tools/m68kasm/examples/language_features.m68
```

構文を受理できても、まだ68000命令や `.X` を生成したことにはなりません。対応外の命令をHAS060へ
黙って渡すことはしません。
詳細な目標と段階は [ASSEMBLER_FOUNDATION.md](../../docs/ASSEMBLER_FOUNDATION.md) を参照してください。
